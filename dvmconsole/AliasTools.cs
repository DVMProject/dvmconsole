// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using System.IO;
using System.Globalization;
using System.Text;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    public class RadioAlias
    {
        /// <summary>
        /// 
        /// </summary>
        public string Alias { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int Rid { get; set; }
    } //public class RadioAlias

    /// <summary>
    /// 
    /// </summary>
    public static class AliasTools
    {
        /*
        ** Methods
        */

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static List<RadioAlias> LoadAliases(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Alias file not found.", filePath);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlText = File.ReadAllText(filePath);
            return deserializer.Deserialize<List<RadioAlias>>(yamlText);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="aliases"></param>
        /// <param name="rid"></param>
        /// <returns></returns>
        public static string GetAliasByRid(List<RadioAlias> aliases, int rid)
        {
            if (aliases == null || aliases.Count == 0)
                return string.Empty;

            var match = aliases.FirstOrDefault(a => a.Rid == rid);
            return match?.Alias ?? string.Empty;
        }

        /// <summary>
        /// Parses the FNE's RID,alias, file format into a complete lookup snapshot.
        /// </summary>
        public static IReadOnlyDictionary<int, string> ParseNetworkAliases(byte[] data)
        {
            if (data == null || data.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Invalid radio alias file size.");

            var aliases = new Dictionary<int, string>();
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                string[] fields = line.Split(',');
                if (fields.Length < 2 || !int.TryParse(fields[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int rid) ||
                    rid <= 0 || rid > 0xFFFFFF || fields.Skip(2).Any(field => !string.IsNullOrWhiteSpace(field)) ||
                    fields[1].Any(char.IsControl))
                    throw new InvalidDataException($"Invalid radio alias entry at line {lineNumber}.");

                string alias = fields[1].Trim();
                if (alias.Length > 0)
                    aliases[rid] = alias;
                else
                    aliases.Remove(rid);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(aliases);
        }

        /// <summary>
        /// Resolves this system's downloaded aliases first, with its local YAML as fallback.
        /// </summary>
        public static string ResolveAlias(Codeplug.System system, int rid)
        {
            if (system?.SyncRadioAliases == true && system.NetworkRidAliases?.TryGetValue(rid, out string alias) == true)
                return alias;
            return GetAliasByRid(system?.RidAlias, rid);
        }
    } //public static class AliasTools
} // namespace DVMConsole
