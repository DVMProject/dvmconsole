// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, K7CBL
*
*/

using System.Globalization;
using System.IO;
using System.Text;

using NAudio.Wave;
using Newtonsoft.Json;

namespace dvmconsole
{
    /// <summary>
    /// Coordinates TAR session lifecycle, persistence, and retention cleanup.
    /// </summary>
    public sealed class TarManager
    {
        private const int SampleRate = 8000;
        private const int BitsPerSample = 16;
        private const int ChannelCount = 1;
        private const short SilenceThreshold = 400;
        private const int WindowSamples = 160; // 20ms at 8kHz
        private const int TrimPaddingMs = 120;

        private readonly SettingsManager settingsManager;
        private readonly object syncRoot = new object();
        private readonly Dictionary<string, TarActiveSession> activeSessions = new Dictionary<string, TarActiveSession>(StringComparer.OrdinalIgnoreCase);

        private sealed class TarActiveSession
        {
            public string SessionKey { get; init; } = string.Empty;
            public string ChannelName { get; init; } = string.Empty;
            public TarRecordingMetadata Metadata { get; init; }
            public MemoryStream PcmBuffer { get; } = new MemoryStream();
            public object SyncRoot { get; } = new object();
        }

        public TarManager(SettingsManager settingsManager)
        {
            this.settingsManager = settingsManager;
        }

        public string GetConfiguredRecordingRoot()
        {
            return string.IsNullOrWhiteSpace(settingsManager.TarRecordingsRootPath)
                ? SettingsManager.DefaultTarRecordingsPath
                : settingsManager.TarRecordingsRootPath.Trim();
        }

        public Dictionary<string, TarChannelConfig> GetChannelConfigs()
        {
            return settingsManager.GetTarChannelConfigs();
        }

        public TarChannelConfig GetChannelConfig(string talkgroupId, string legacyChannelName = null)
        {
            return settingsManager.GetTarChannelConfig(talkgroupId, legacyChannelName);
        }

        public bool IsChannelEnabled(string talkgroupId, string legacyChannelName = null)
        {
            return GetChannelConfig(talkgroupId, legacyChannelName).Enabled;
        }

        public void StartRxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            uint subscriberId,
            string subscriberAlias,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime startTimeUtc)
        {
            if (!TryCreateRxSession(system, channel, streamId, subscriberId, subscriberAlias, isEncrypted, encryptionAlgorithm, encryptionKeyId, startTimeUtc))
                return;
        }

        public void AppendRxAudio(string systemName, string talkgroupId, uint streamId, byte[] pcmData)
        {
            AppendAudio(BuildRxSessionKey(systemName, talkgroupId, streamId), pcmData);
        }

        public void StopRxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            uint subscriberId,
            string subscriberAlias,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime endTimeUtc)
        {
            TarActiveSession session = RemoveSession(BuildRxSessionKey(system?.Name, channel?.Tgid, streamId));
            if (session == null)
                return;

            session.Metadata.SubscriberId = subscriberId;
            if (!string.IsNullOrWhiteSpace(subscriberAlias))
                session.Metadata.SubscriberAlias = subscriberAlias.Trim();
            UpdateEncryptionMetadata(session.Metadata, isEncrypted, encryptionAlgorithm, encryptionKeyId);
            FinalizeSessionAsync(session, endTimeUtc);
        }

        public void StartTxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime startTimeUtc)
        {
            if (system == null || channel == null || streamId == 0)
                return;

            TarChannelConfig config = GetChannelConfig(channel.Tgid, channel.Name);
            if (!config.Enabled)
                return;

            if (!TryEnsureRecordingRoot(GetConfiguredRecordingRoot(), out _, out _))
                return;

            string sessionKey = BuildTxSessionKey(system.Name, channel.Tgid, streamId);
            lock (syncRoot)
            {
                if (activeSessions.ContainsKey(sessionKey))
                    return;

                uint? consoleId = TryParseUInt(system.Rid);
                TarRecordingMetadata metadata = new TarRecordingMetadata
                {
                    Direction = TarRecordingDirection.TX,
                    RecordingSourceType = TarRecordingSourceType.ConsoleTx,
                    Protocol = (channel.Mode ?? string.Empty).ToUpperInvariant(),
                    UtcStartTime = NormalizeUtc(startTimeUtc),
                    SystemName = system.Name ?? string.Empty,
                    ChannelName = channel.Name ?? string.Empty,
                    TalkgroupId = TryParseUInt(channel.Tgid),
                    TalkgroupName = channel.Name ?? string.Empty,
                    SubscriberId = consoleId,
                    SubscriberAlias = ResolveConsoleDisplayName(system),
                    ConsoleId = consoleId,
                    ConsoleName = ResolveConsoleDisplayName(system),
                    StreamId = streamId,
                    RetentionDaysAtRecordTime = config.RetentionDays > 0 ? config.RetentionDays : null
                };

                UpdateEncryptionMetadata(metadata, isEncrypted, encryptionAlgorithm, encryptionKeyId);

                activeSessions[sessionKey] = new TarActiveSession
                {
                    SessionKey = sessionKey,
                    ChannelName = channel.Name ?? string.Empty,
                    Metadata = metadata
                };
            }
        }

        public void AppendTxAudio(string systemName, string talkgroupId, uint streamId, byte[] pcmData)
        {
            AppendAudio(BuildTxSessionKey(systemName, talkgroupId, streamId), pcmData);
        }

        public void StopTxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime endTimeUtc)
        {
            TarActiveSession session = RemoveSession(BuildTxSessionKey(system?.Name, channel?.Tgid, streamId));
            if (session == null)
                return;

            if (system != null)
            {
                session.Metadata.ConsoleId = TryParseUInt(system.Rid);
                session.Metadata.ConsoleName = ResolveConsoleDisplayName(system);
                session.Metadata.SubscriberId = session.Metadata.ConsoleId;
                session.Metadata.SubscriberAlias = session.Metadata.ConsoleName;
            }

            UpdateEncryptionMetadata(session.Metadata, isEncrypted, encryptionAlgorithm, encryptionKeyId);
            FinalizeSessionAsync(session, endTimeUtc);
        }

        public void StopAllSessions()
        {
            List<TarActiveSession> sessions;
            lock (syncRoot)
            {
                sessions = activeSessions.Values.ToList();
                activeSessions.Clear();
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (TarActiveSession session in sessions)
                FinalizeSession(session, nowUtc);
        }

        public void RunRetentionMaintenanceAsync()
        {
            Task.Run(RunRetentionMaintenance);
        }

        public void RunRetentionMaintenance()
        {
            if (!TryEnsureRecordingRoot(GetConfiguredRecordingRoot(), out string rootPath, out _))
                return;

            Dictionary<string, TarChannelConfig> configs = GetChannelConfigs();
            if (configs.Count == 0)
                return;

            IEnumerable<string> metadataFiles;
            try
            {
                metadataFiles = Directory.EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"TAR retention scan skipped: {ex.Message}");
                return;
            }

            foreach (string metadataFile in metadataFiles)
            {
                TarRecordingMetadata metadata = TryReadMetadata(metadataFile);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.ChannelName))
                    continue;

                string configKey = metadata.TalkgroupId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configKey) || !configs.TryGetValue(configKey, out TarChannelConfig config))
                    continue;

                if (config.RetentionDays <= 0)
                    continue;

                if (metadata.UtcEndTime >= DateTime.UtcNow.AddDays(-config.RetentionDays))
                    continue;

                DeleteRecording(metadataFile, metadata.FilePath);
            }
        }

        public List<TarRecordingMetadata> LoadRecordings()
        {
            List<TarRecordingMetadata> recordings = new List<TarRecordingMetadata>();
            if (!TryEnsureRecordingRoot(GetConfiguredRecordingRoot(), out string rootPath, out _))
                return recordings;

            IEnumerable<string> metadataFiles;
            try
            {
                metadataFiles = Directory.EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Unable to scan TAR recordings: {ex.Message}");
                return recordings;
            }

            foreach (string metadataFile in metadataFiles)
            {
                TarRecordingMetadata metadata = TryReadMetadata(metadataFile);
                if (metadata == null)
                    continue;
                if (string.IsNullOrWhiteSpace(metadata.FilePath) || !File.Exists(metadata.FilePath))
                    continue;

                recordings.Add(metadata);
            }

            return recordings
                .OrderByDescending(recording => recording.UtcStartTime)
                .ThenBy(recording => recording.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void DeleteRecording(TarRecordingMetadata metadata)
        {
            if (metadata == null)
                return;

            string sidecarPath = GetSidecarPath(metadata.FilePath);
            DeleteRecording(sidecarPath, metadata.FilePath);
        }

        public static bool TryEnsureRecordingRoot(string rootPath, out string normalizedPath, out string errorMessage)
        {
            normalizedPath = string.IsNullOrWhiteSpace(rootPath)
                ? SettingsManager.DefaultTarRecordingsPath
                : rootPath.Trim();
            errorMessage = string.Empty;

            try
            {
                Directory.CreateDirectory(normalizedPath);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private bool TryCreateRxSession(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            uint subscriberId,
            string subscriberAlias,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime startTimeUtc)
        {
            if (system == null || channel == null || streamId == 0)
                return false;

            TarChannelConfig config = GetChannelConfig(channel.Tgid, channel.Name);
            if (!config.Enabled)
                return false;

            if (config.IgnoredSubscriberIds.Contains(subscriberId))
                return false;

            if (!TryEnsureRecordingRoot(GetConfiguredRecordingRoot(), out _, out _))
                return false;

            string sessionKey = BuildRxSessionKey(system.Name, channel.Tgid, streamId);
            lock (syncRoot)
            {
                if (activeSessions.ContainsKey(sessionKey))
                    return false;

                TarRecordingMetadata metadata = new TarRecordingMetadata
                {
                    Direction = TarRecordingDirection.RX,
                    RecordingSourceType = TarRecordingSourceType.InboundRadio,
                    Protocol = (channel.Mode ?? string.Empty).ToUpperInvariant(),
                    UtcStartTime = NormalizeUtc(startTimeUtc),
                    SystemName = system.Name ?? string.Empty,
                    ChannelName = channel.Name ?? string.Empty,
                    TalkgroupId = TryParseUInt(channel.Tgid),
                    TalkgroupName = channel.Name ?? string.Empty,
                    SubscriberId = subscriberId,
                    SubscriberAlias = subscriberAlias?.Trim() ?? string.Empty,
                    ConsoleId = TryParseUInt(system.Rid),
                    ConsoleName = ResolveConsoleDisplayName(system),
                    StreamId = streamId,
                    RetentionDaysAtRecordTime = config.RetentionDays > 0 ? config.RetentionDays : null
                };

                UpdateEncryptionMetadata(metadata, isEncrypted, encryptionAlgorithm, encryptionKeyId);

                activeSessions[sessionKey] = new TarActiveSession
                {
                    SessionKey = sessionKey,
                    ChannelName = channel.Name ?? string.Empty,
                    Metadata = metadata
                };
            }

            return true;
        }

        private void AppendAudio(string sessionKey, byte[] pcmData)
        {
            if (string.IsNullOrWhiteSpace(sessionKey) || pcmData == null || pcmData.Length == 0)
                return;

            TarActiveSession session;
            lock (syncRoot)
            {
                if (!activeSessions.TryGetValue(sessionKey, out session))
                    return;
            }

            lock (session.SyncRoot)
            {
                session.PcmBuffer.Write(pcmData, 0, pcmData.Length);
            }
        }

        private TarActiveSession RemoveSession(string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return null;

            lock (syncRoot)
            {
                if (!activeSessions.TryGetValue(sessionKey, out TarActiveSession session))
                    return null;

                activeSessions.Remove(sessionKey);
                return session;
            }
        }

        private void FinalizeSessionAsync(TarActiveSession session, DateTime endTimeUtc)
        {
            Task.Run(() => FinalizeSession(session, endTimeUtc));
        }

        private void FinalizeSession(TarActiveSession session, DateTime endTimeUtc)
        {
            if (session?.Metadata == null)
                return;

            try
            {
                if (!TryEnsureRecordingRoot(GetConfiguredRecordingRoot(), out string rootPath, out string error))
                {
                    Log.WriteWarning($"TAR recording skipped; recording root invalid: {error}");
                    return;
                }

                byte[] pcmBytes;
                lock (session.SyncRoot)
                    pcmBytes = session.PcmBuffer.ToArray();

                if (pcmBytes.Length == 0)
                    return;

                DateTime normalizedEndUtc = NormalizeUtc(endTimeUtc);
                TarTrimResult trimResult = TrimSilence(pcmBytes);
                if (trimResult.AudioBytes.Length == 0)
                    return;

                string dayFolder = Path.Combine(rootPath, session.Metadata.UtcStartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                string talkgroupFolder = Path.Combine(dayFolder, BuildTalkgroupFolderName(session.Metadata));
                string hourFolder = Path.Combine(talkgroupFolder, session.Metadata.UtcStartTime.ToString("HH", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(hourFolder);

                string fileBaseName = BuildRecordingBaseFileName(session.Metadata);
                string wavPath = Path.Combine(hourFolder, fileBaseName + ".wav");
                string metadataPath = Path.Combine(hourFolder, fileBaseName + ".json");

                using (WaveFileWriter writer = new WaveFileWriter(wavPath, new WaveFormat(SampleRate, BitsPerSample, ChannelCount)))
                    writer.Write(trimResult.AudioBytes, 0, trimResult.AudioBytes.Length);

                FileInfo fileInfo = new FileInfo(wavPath);
                session.Metadata.UtcEndTime = normalizedEndUtc;
                session.Metadata.DurationMs = Math.Max(0, (long)Math.Round(trimResult.AudioBytes.Length / 16.0));
                session.Metadata.FilePath = wavPath;
                session.Metadata.FileName = Path.GetFileName(wavPath);
                session.Metadata.FileSizeBytes = fileInfo.Exists ? fileInfo.Length : trimResult.AudioBytes.Length;
                session.Metadata.SampleRate = SampleRate;
                session.Metadata.BitsPerSample = BitsPerSample;
                session.Metadata.ChannelCount = ChannelCount;
                session.Metadata.TrimLeadMs = trimResult.TrimLeadMs;
                session.Metadata.TrimTailMs = trimResult.TrimTailMs;

                string json = JsonConvert.SerializeObject(session.Metadata, Formatting.Indented);
                File.WriteAllText(metadataPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"TAR finalize failed for {session.ChannelName}: {ex.Message}");
                Log.StackTrace(ex, false);
            }
            finally
            {
                session.PcmBuffer.Dispose();
            }
        }

        private static TarRecordingMetadata TryReadMetadata(string metadataFile)
        {
            try
            {
                string json = File.ReadAllText(metadataFile);
                TarRecordingMetadata metadata = JsonConvert.DeserializeObject<TarRecordingMetadata>(json);
                if (metadata == null)
                    return null;
                if (string.IsNullOrWhiteSpace(metadata.FilePath))
                    metadata.FilePath = Path.ChangeExtension(metadataFile, ".wav");
                if (string.IsNullOrWhiteSpace(metadata.FileName))
                    metadata.FileName = Path.GetFileName(metadata.FilePath);
                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private static void DeleteRecording(string metadataPath, string wavPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(metadataPath) && File.Exists(metadataPath))
                    File.Delete(metadataPath);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Unable to delete TAR metadata '{metadataPath}': {ex.Message}");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath))
                    File.Delete(wavPath);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"Unable to delete TAR recording '{wavPath}': {ex.Message}");
            }
        }

        private static string BuildRxSessionKey(string systemName, string talkgroupId, uint streamId)
        {
            return $"RX|{systemName?.Trim()}|{talkgroupId?.Trim()}|{streamId}";
        }

        private static string BuildTxSessionKey(string systemName, string talkgroupId, uint streamId)
        {
            return $"TX|{systemName?.Trim()}|{talkgroupId?.Trim()}|{streamId}";
        }

        private static string ResolveConsoleDisplayName(Codeplug.System system)
        {
            if (!string.IsNullOrWhiteSpace(system?.Identity))
                return system.Identity.Trim();

            return system?.Name?.Trim() ?? string.Empty;
        }

        private static uint? TryParseUInt(string value)
        {
            return uint.TryParse(value, out uint parsed) ? parsed : null;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return value;
            if (value.Kind == DateTimeKind.Local)
                return value.ToUniversalTime();

            return DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        }

        private static string BuildRecordingBaseFileName(TarRecordingMetadata metadata)
        {
            string timestamp = metadata.UtcStartTime.ToString("yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture);
            string system = SanitizeSegment(metadata.SystemName, 18);
            string channel = SanitizeSegment(metadata.ChannelName, 18);
            string tg = metadata.TalkgroupId.HasValue ? metadata.TalkgroupId.Value.ToString(CultureInfo.InvariantCulture) : "TG0";
            string src = metadata.SubscriberId.HasValue ? metadata.SubscriberId.Value.ToString(CultureInfo.InvariantCulture) : "SRC0";
            string shortId = string.IsNullOrWhiteSpace(metadata.RecordingId)
                ? Guid.NewGuid().ToString("N")[..8]
                : metadata.RecordingId[..Math.Min(8, metadata.RecordingId.Length)];

            return $"{timestamp}_{metadata.Direction}_{system}_{channel}_TG{tg}_SRC{src}_{shortId}";
        }

        private static string BuildTalkgroupFolderName(TarRecordingMetadata metadata)
        {
            string talkgroupName = SanitizeSegment(metadata?.TalkgroupName, 48);
            string talkgroupId = metadata?.TalkgroupId?.ToString(CultureInfo.InvariantCulture) ?? "0";
            return $"{talkgroupName}_TG{talkgroupId}";
        }

        private static string SanitizeSegment(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            StringBuilder builder = new StringBuilder();
            foreach (char character in value.Trim())
            {
                if (Path.GetInvalidFileNameChars().Contains(character))
                    continue;

                if (char.IsLetterOrDigit(character))
                    builder.Append(character);
                else
                    builder.Append('_');
            }

            string sanitized = builder.ToString().Trim('_');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "unknown";

            if (sanitized.Length > maxLength)
                sanitized = sanitized[..maxLength];

            return sanitized;
        }

        private static string GetSidecarPath(string wavPath)
        {
            return string.IsNullOrWhiteSpace(wavPath)
                ? string.Empty
                : Path.ChangeExtension(wavPath, ".json");
        }

        private static void UpdateEncryptionMetadata(TarRecordingMetadata metadata, bool isEncrypted, string encryptionAlgorithm, ushort? encryptionKeyId)
        {
            metadata.IsEncrypted = isEncrypted;
            metadata.EncryptionAlgorithm = isEncrypted
                ? (string.IsNullOrWhiteSpace(encryptionAlgorithm) ? "Unknown" : encryptionAlgorithm.Trim())
                : string.Empty;
            metadata.EncryptionKeyId = isEncrypted && encryptionKeyId.GetValueOrDefault() > 0
                ? encryptionKeyId
                : null;
        }

        private sealed class TarTrimResult
        {
            public byte[] AudioBytes { get; init; } = Array.Empty<byte>();
            public int TrimLeadMs { get; init; }
            public int TrimTailMs { get; init; }
        }

        private static TarTrimResult TrimSilence(byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length < 2)
            {
                return new TarTrimResult
                {
                    AudioBytes = Array.Empty<byte>(),
                    TrimLeadMs = 0,
                    TrimTailMs = 0
                };
            }

            int totalSamples = pcmBytes.Length / 2;
            int firstActiveSample = FindFirstActiveSample(pcmBytes, totalSamples);
            int lastActiveSample = FindLastActiveSample(pcmBytes, totalSamples);

            if (firstActiveSample < 0 || lastActiveSample < firstActiveSample)
            {
                return new TarTrimResult
                {
                    AudioBytes = pcmBytes.ToArray(),
                    TrimLeadMs = 0,
                    TrimTailMs = 0
                };
            }

            int paddingSamples = (SampleRate * TrimPaddingMs) / 1000;
            int startSample = Math.Max(0, firstActiveSample - paddingSamples);
            int endSample = Math.Min(totalSamples - 1, lastActiveSample + paddingSamples);

            int startByte = startSample * 2;
            int byteLength = ((endSample - startSample) + 1) * 2;
            byte[] trimmedBytes = new byte[byteLength];
            Buffer.BlockCopy(pcmBytes, startByte, trimmedBytes, 0, byteLength);

            int trimLeadMs = (int)Math.Round(startSample * 1000.0 / SampleRate);
            int trimTailSamples = Math.Max(0, totalSamples - endSample - 1);
            int trimTailMs = (int)Math.Round(trimTailSamples * 1000.0 / SampleRate);

            return new TarTrimResult
            {
                AudioBytes = trimmedBytes,
                TrimLeadMs = trimLeadMs,
                TrimTailMs = trimTailMs
            };
        }

        private static int FindFirstActiveSample(byte[] pcmBytes, int totalSamples)
        {
            for (int sampleIndex = 0; sampleIndex < totalSamples; sampleIndex += WindowSamples)
            {
                int samplesToCheck = Math.Min(WindowSamples, totalSamples - sampleIndex);
                if (WindowHasActivity(pcmBytes, sampleIndex, samplesToCheck))
                    return sampleIndex;
            }

            return -1;
        }

        private static int FindLastActiveSample(byte[] pcmBytes, int totalSamples)
        {
            for (int sampleIndex = Math.Max(0, totalSamples - WindowSamples); sampleIndex >= 0; sampleIndex -= WindowSamples)
            {
                int samplesToCheck = Math.Min(WindowSamples, totalSamples - sampleIndex);
                if (WindowHasActivity(pcmBytes, sampleIndex, samplesToCheck))
                    return sampleIndex + samplesToCheck - 1;
            }

            return -1;
        }

        private static bool WindowHasActivity(byte[] pcmBytes, int startSample, int sampleCount)
        {
            int byteIndex = startSample * 2;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(pcmBytes, byteIndex + (i * 2));
                if (Math.Abs(sample) >= SilenceThreshold)
                    return true;
            }

            return false;
        }
    }
}
