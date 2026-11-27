// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dvmconsole
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TarRecordingDirection
    {
        RX,
        TX
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TarRecordingSourceType
    {
        InboundRadio,
        ConsoleTx
    }

    /// <summary>
    /// Persisted TAR configuration for a channel/resource.
    /// </summary>
    public class TarChannelConfig
    {
        public bool Enabled { get; set; } = false;
        public int RetentionDays { get; set; } = 7;
        public List<uint> IgnoredSubscriberIds { get; set; } = new List<uint>();
    }

    /// <summary>
    /// Metadata written alongside each TAR recording.
    /// </summary>
    public class TarRecordingMetadata
    {
        public int SchemaVersion { get; set; } = 1;
        public string RecordingId { get; set; } = Guid.NewGuid().ToString("N");
        public TarRecordingDirection Direction { get; set; }
        public TarRecordingSourceType RecordingSourceType { get; set; }
        public string Protocol { get; set; } = string.Empty;

        public DateTime UtcStartTime { get; set; }
        public DateTime UtcEndTime { get; set; }
        public long DurationMs { get; set; }

        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int SampleRate { get; set; } = 8000;
        public int BitsPerSample { get; set; } = 16;
        public int ChannelCount { get; set; } = 1;

        public string SystemName { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public uint? TalkgroupId { get; set; }
        public string TalkgroupName { get; set; } = string.Empty;
        public uint? SubscriberId { get; set; }
        public string SubscriberAlias { get; set; } = string.Empty;

        public uint? ConsoleId { get; set; }
        public string ConsoleName { get; set; } = string.Empty;

        public bool IsEncrypted { get; set; }
        public string EncryptionAlgorithm { get; set; } = string.Empty;
        public ushort? EncryptionKeyId { get; set; }

        public uint? StreamId { get; set; }
        public int? RetentionDaysAtRecordTime { get; set; }
        public int TrimLeadMs { get; set; }
        public int TrimTailMs { get; set; }
    }
}
