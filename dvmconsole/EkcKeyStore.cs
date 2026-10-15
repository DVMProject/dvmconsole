// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 DVMProject Authors
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace dvmconsole
{
    /// <summary>
    /// Read-only local traffic keys from a KFDEKC encrypted container.
    /// </summary>
    internal sealed class EkcKeyStore : IDisposable
    {
        private const int MAX_CONTAINER_BYTES = 16 * 1024 * 1024;
        private readonly Dictionary<(byte Algorithm, ushort Id), byte[]> keys = new();

        public int Count => keys.Count;
        public int SkippedKeys { get; private set; }

        public bool TryGetKey(byte algorithm, ushort id, out byte[] material) =>
            keys.TryGetValue((algorithm, id), out material);

        /// <summary>
        /// Open only encrypted EKC files. Renaming a clear key file cannot enable the old format.
        /// </summary>
        public static EkcKeyStore Open(string path, string password)
        {
            if (!string.Equals(Path.GetExtension(path), ".ekc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Local keys require a KFDEKC .ekc container. Clear key files are no longer supported.");

            using var file = File.OpenRead(path);
            if (file.Length < 4 || file.Length > MAX_CONTAINER_BYTES)
                throw new InvalidDataException("Invalid EKC container size.");
            // KFDEKC prefixes the gzip payload with its uncompressed length.
            Span<byte> header = stackalloc byte[4];
            file.ReadExactly(header);
            int expandedLength = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (expandedLength < 1 || expandedLength > MAX_CONTAINER_BYTES)
                throw new InvalidDataException("Invalid EKC expanded size.");
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var expanded = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (expanded.Length + read > expandedLength)
                    throw new InvalidDataException("The EKC expanded size does not match its header.");
                expanded.Write(buffer, 0, read);
            }
            if (expanded.Length != expandedLength)
                throw new InvalidDataException("The EKC expanded size does not match its header.");
            XmlDocument outer = ReadXml(expanded.ToArray());
            XmlElement root = outer.DocumentElement;
            RequireVersion(root, "OuterContainer", 1);
            XmlNode derivation = root.SelectSingleNode("KeyDerivation");
            if (Value(derivation, "DerivationAlgorithm") != "PBKDF2" ||
                Value(derivation, "HashAlgorithm") != "SHA512" ||
                Number(derivation, "KeyLength") != 32)
                throw new InvalidDataException("Unsupported EKC password derivation.");

            int iterations = Number(derivation, "IterationCount");
            if (iterations < 1 || iterations > 2000000)
                throw new InvalidDataException("Unsupported EKC password iteration count.");
            byte[] salt = Convert.FromBase64String(Value(derivation, "Salt"));
            if (salt.Length < 16 || salt.Length > 1024)
                throw new InvalidDataException("Invalid EKC password salt.");

            var namespaces = new XmlNamespaceManager(outer.NameTable);
            namespaces.AddNamespace("enc", EncryptedXml.XmlEncNamespaceUrl);
            var element = root.SelectSingleNode("enc:EncryptedData", namespaces) as XmlElement;
            if (element == null)
                throw new InvalidDataException("The EKC container has no encrypted key data.");
            var encrypted = new EncryptedData();
            encrypted.LoadXml(element);
            if (encrypted.EncryptionMethod?.KeyAlgorithm != EncryptedXml.XmlEncAES256Url ||
                encrypted.CipherData.CipherValue == null || encrypted.CipherData.CipherValue.Length < 32)
                throw new InvalidDataException("Unsupported EKC encryption.");

            byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, 32);
            byte[] plaintext = null;
            try
            {
                using var aes = Aes.Create();
                aes.Key = derived;
                aes.Mode = CipherMode.CBC;
                XmlDocument inner;
                try
                {
                    plaintext = new EncryptedXml().DecryptData(encrypted, aes);
                    inner = ReadXml(plaintext);
                    if (inner.DocumentElement?.Name != "InnerContainer")
                        throw new CryptographicException();
                }
                catch (Exception ex) when (ex is CryptographicException || ex is XmlException)
                {
                    // XML parser errors can include decrypted text; never propagate them.
                    throw new CryptographicException("Unable to unlock the EKC container. Check the password and file.");
                }
                RequireVersion(inner.DocumentElement, "InnerContainer", 1, 2);
                return ReadKeys(inner.DocumentElement);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
                if (plaintext != null)
                    CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static EkcKeyStore ReadKeys(XmlElement root)
        {
            var result = new EkcKeyStore();
            try
            {
                XmlNode list = root.SelectSingleNode("Keys");
                if (list == null)
                    throw new InvalidDataException("The EKC container has no key list.");
                foreach (XmlNode entry in list.SelectNodes("KeyItem"))
                {
                    bool automatic = Value(entry, "KeyTypeAuto") == "true";
                    bool traffic = Value(entry, "KeyTypeTek") == "true";
                    bool wrapping = Value(entry, "KeyTypeKek") == "true";
                    int sln = Number(entry, "Sln");
                    if (automatic)
                        traffic = sln >= 0 && sln <= 61439;
                    if (!traffic || wrapping)
                    {
                        result.SkippedKeys++;
                        continue;
                    }

                    int algorithm = Number(entry, "AlgorithmId");
                    int length = algorithm switch { 0x01 => 2, 0x81 => 8, 0x84 => 32, 0xAA => 5, _ => 0 };
                    if (length == 0)
                    {
                        result.SkippedKeys++;
                        continue;
                    }
                    int id = Number(entry, "KeyId");
                    if (id < 1 || id > ushort.MaxValue)
                        throw new InvalidDataException("An EKC traffic key has an invalid key ID.");
                    string hex = Value(entry, "Key");
                    if (hex.Length != length * 2 || hex.Any(c => !Uri.IsHexDigit(c)))
                        throw new InvalidDataException("An EKC traffic key has invalid material or length.");

                    byte[] material = Convert.FromHexString(hex);
                    var index = ((byte)algorithm, (ushort)id);
                    if (result.keys.TryGetValue(index, out byte[] previous))
                    {
                        bool matches = CryptographicOperations.FixedTimeEquals(previous, material);
                        CryptographicOperations.ZeroMemory(material);
                        if (!matches)
                            throw new InvalidDataException("Conflicting EKC traffic keys share an algorithm and key ID. Use a container with one value per algorithm/key ID.");
                    }
                    else
                        result.keys.Add(index, material);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static XmlDocument ReadXml(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MAX_CONTAINER_BYTES
            });
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);
            return document;
        }

        private static string Value(XmlNode node, string field) =>
            node?.SelectSingleNode(field)?.InnerText ??
            throw new InvalidDataException("The EKC container is missing a required field.");

        private static int Number(XmlNode node, string field)
        {
            if (!int.TryParse(Value(node, field), out int value))
                throw new InvalidDataException("The EKC container has an invalid numeric field.");
            return value;
        }

        private static void RequireVersion(XmlElement element, string name, params int[] majors)
        {
            if (element?.Name != name || !Version.TryParse(element.GetAttribute("version"), out Version version) ||
                !majors.Contains(version.Major))
                throw new InvalidDataException("Unsupported EKC container version.");
        }

        public void Dispose()
        {
            foreach (byte[] material in keys.Values)
                CryptographicOperations.ZeroMemory(material);
            keys.Clear();
        }
    }
}

