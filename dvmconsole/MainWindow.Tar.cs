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

using System.Windows;
using System.Windows.Controls;

using dvmconsole.Controls;
using fnecore.DMR;
using fnecore.P25;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private TarViewerWindow tarViewerWindow;

        private void TarConfiguration_Click(object sender, RoutedEventArgs e)
        {
            TarConfigurationWindow window = new TarConfigurationWindow(settingsManager, Codeplug?.Zones, OnTarConfigurationSaved)
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void TarViewer_Click(object sender, RoutedEventArgs e)
        {
            if (tarViewerWindow == null || !tarViewerWindow.IsLoaded)
            {
                tarViewerWindow = new TarViewerWindow(tarManager)
                {
                    Owner = this
                };
                tarViewerWindow.Closed += (_, _) => tarViewerWindow = null;
            }

            tarViewerWindow.RefreshView();
            if (tarViewerWindow.Visibility == Visibility.Visible)
            {
                tarViewerWindow.Activate();
                return;
            }

            tarViewerWindow.Show();
            tarViewerWindow.Activate();
        }

        private void OnTarConfigurationSaved()
        {
            UpdateTarIndicators();
            tarManager.RunRetentionMaintenanceAsync();
            tarViewerWindow?.RefreshView();
        }

        private void UpdateTarIndicators()
        {
            foreach (Canvas canvas in GetAllCanvases())
            {
                foreach (ChannelBox channel in canvas.Children.OfType<ChannelBox>())
                {
                    string systemName = NormalizeChannelSystemName(channel.SystemName);
                    bool enabled = channel.SystemName != PLAYBACKSYS &&
                        channel.ChannelName != PLAYBACKCHNAME &&
                        channel.DstId != PLAYBACKTG &&
                        tarManager.IsChannelEnabled(systemName, channel.DstId, channel.ChannelName);
                    channel.SetTarRecordingIndicator(enabled);
                }
            }

            if (playbackChannelBox != null)
                playbackChannelBox.SetTarRecordingIndicator(false);
        }

        private void BeginTarRxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            uint subscriberId,
            string subscriberAlias,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime packetTime)
        {
            tarManager.StartRxRecording(
                system,
                channel,
                streamId,
                subscriberId,
                subscriberAlias,
                isEncrypted,
                encryptionAlgorithm,
                encryptionKeyId,
                packetTime);
        }

        private void AppendTarRxAudio(string systemName, string talkgroupId, uint streamId, byte[] pcmData)
        {
            tarManager.AppendRxAudio(systemName, talkgroupId, streamId, pcmData);
        }

        private void EnsureTarRxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            ChannelBox channelBox,
            uint peerId,
            uint streamId,
            uint subscriberId,
            string subscriberAlias,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime packetTime,
            string reason)
        {
            if (system == null || channel == null || channelBox == null || streamId == 0)
                return;

            if (channelBox.RxStreamId != streamId)
            {
                channelBox.IsReceiving = true;
                channelBox.PeerId = peerId;
                channelBox.RxStreamId = streamId;
                channelBox.IsReceivingEncrypted = isEncrypted;
                channelBox.LastPktTime = packetTime;
            }

            if (tarManager.HasRxRecording(system.Name, channel.Tgid, streamId))
                return;

            TarChannelConfig config = tarManager.GetChannelConfig(system, channel);
            if (!config.Enabled || config.IgnoredSubscriberIds.Contains(subscriberId))
                return;

            Log.WriteLine($"TAR RX late-start requested for {system.Name} TG {channel.Tgid} RID {subscriberId} stream {streamId} ({reason}).");
            BeginTarRxRecording(
                system,
                channel,
                streamId,
                subscriberId,
                subscriberAlias,
                isEncrypted,
                encryptionAlgorithm,
                encryptionKeyId,
                packetTime);
        }

        private void EndTarRxRecording(
            Codeplug.System system,
            Codeplug.Channel channel,
            uint streamId,
            uint subscriberId,
            string subscriberAlias,
            bool isEncrypted,
            string encryptionAlgorithm,
            ushort? encryptionKeyId,
            DateTime packetTime)
        {
            tarManager.StopRxRecording(
                system,
                channel,
                streamId,
                subscriberId,
                subscriberAlias,
                isEncrypted,
                encryptionAlgorithm,
                encryptionKeyId,
                packetTime);
        }

        private void EndTarRxRecordingFromChannelState(
            Codeplug.System system,
            Codeplug.Channel channel,
            ChannelBox channelBox,
            SlotStatus slotStatus,
            DateTime packetTime)
        {
            if (system == null || channel == null || channelBox == null || channelBox.RxStreamId == 0)
                return;

            uint subscriberId = slotStatus?.RxRFS ?? 0;
            string subscriberAlias = subscriberId > 0
                ? TryResolveSubscriberAlias(system, (int)subscriberId)
                : string.Empty;

            bool isEncrypted = channelBox.IsReceivingEncrypted;
            string encryptionAlgorithm = string.Empty;
            ushort? encryptionKeyId = null;

            switch (channel.GetChannelMode())
            {
                case Codeplug.ChannelMode.P25:
                    isEncrypted = channelBox.algId != P25Defines.P25_ALGO_UNENCRYPT;
                    encryptionAlgorithm = DescribeP25EncryptionAlgorithm(channelBox.algId);
                    encryptionKeyId = channelBox.kId > 0 ? channelBox.kId : null;
                    break;

                case Codeplug.ChannelMode.DMR:
                    PrivacyLC privacy = slotStatus?.DMR_RxPILC;
                    isEncrypted = privacy != null && privacy.AlgId != 0;
                    encryptionAlgorithm = DescribeDmrEncryptionAlgorithm(privacy?.AlgId ?? 0);
                    encryptionKeyId = NormalizeEncryptionKeyId(privacy?.KId ?? 0);
                    break;
            }

            EndTarRxRecording(
                system,
                channel,
                channelBox.RxStreamId,
                subscriberId,
                subscriberAlias,
                isEncrypted,
                encryptionAlgorithm,
                encryptionKeyId,
                packetTime);
        }

        private void BeginTarTxRecording(ChannelBox channelBox, Codeplug.System system, Codeplug.Channel channel, uint streamId)
        {
            if (channelBox == null || system == null || channel == null || streamId == 0)
                return;

            AddConsoleTxHistoryEntry(channelBox, system, channel, streamId);

            bool isEncrypted = channelBox.IsTxEncrypted;
            string algorithm = DescribeTxEncryptionAlgorithm(channel);
            ushort? keyId = isEncrypted && channel.GetKeyId() > 0 ? channel.GetKeyId() : null;

            tarManager.StartTxRecording(system, channel, streamId, isEncrypted, algorithm, keyId, DateTime.UtcNow);
        }

        private void AppendTarTxAudio(string systemName, string talkgroupId, uint streamId, byte[] pcmData)
        {
            tarManager.AppendTxAudio(systemName, talkgroupId, streamId, pcmData);
        }

        private void EndTarTxRecording(ChannelBox channelBox, Codeplug.System system, Codeplug.Channel channel)
        {
            if (channelBox == null || system == null || channel == null || channelBox.TxStreamId == 0)
                return;

            ClearConsoleTxHistoryEntry(channelBox, system, channel);

            bool isEncrypted = channelBox.IsTxEncrypted;
            string algorithm = DescribeTxEncryptionAlgorithm(channel);
            ushort? keyId = isEncrypted && channel.GetKeyId() > 0 ? channel.GetKeyId() : null;

            tarManager.StopTxRecording(system, channel, channelBox.TxStreamId, isEncrypted, algorithm, keyId, DateTime.UtcNow);
        }

        private void AddConsoleTxHistoryEntry(ChannelBox channelBox, Codeplug.System system, Codeplug.Channel channel, uint streamId)
        {
            if (callHistoryWindow == null || !TryResolveConsoleTxHistoryIds(system, channel, out int sourceId, out int destinationId))
                return;

            callHistoryWindow.AddConsoleTransmission(
                channel.Name ?? channelBox.ChannelName ?? string.Empty,
                sourceId,
                destinationId,
                ResolveConsoleHistoryDisplayName(system),
                DateTime.Now.ToString("HH:mm:ss"),
                streamId);
        }

        private void ClearConsoleTxHistoryEntry(ChannelBox channelBox, Codeplug.System system, Codeplug.Channel channel)
        {
            if (callHistoryWindow == null || channelBox == null || channelBox.TxStreamId == 0)
                return;

            if (!TryResolveConsoleTxHistoryIds(system, channel, out int sourceId, out _))
                return;

            callHistoryWindow.ConsoleTransmissionEnded(
                channel.Name ?? channelBox.ChannelName ?? string.Empty,
                sourceId,
                channelBox.TxStreamId);
        }

        private static bool TryResolveConsoleTxHistoryIds(Codeplug.System system, Codeplug.Channel channel, out int sourceId, out int destinationId)
        {
            sourceId = 0;
            destinationId = 0;

            if (!uint.TryParse(system?.Rid, out uint parsedSourceId) || parsedSourceId > int.MaxValue)
                return false;

            if (!uint.TryParse(channel?.Tgid, out uint parsedDestinationId) || parsedDestinationId > int.MaxValue)
                return false;

            sourceId = (int)parsedSourceId;
            destinationId = (int)parsedDestinationId;
            return true;
        }

        private static string ResolveConsoleHistoryDisplayName(Codeplug.System system)
        {
            if (!string.IsNullOrWhiteSpace(system?.Identity))
                return system.Identity.Trim();

            return system?.Name?.Trim() ?? string.Empty;
        }

        private static string TryResolveSubscriberAlias(Codeplug.System system, int subscriberId)
        {
            try
            {
                return AliasTools.GetAliasByRid(system?.RidAlias, subscriberId);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsConsoleSourceRid(Codeplug.System system, uint sourceId)
        {
            return uint.TryParse(system?.Rid, out uint consoleRid) && consoleRid == sourceId;
        }

        private static string DescribeTxEncryptionAlgorithm(Codeplug.Channel channel)
        {
            if (channel == null || channel.GetAlgoId() == P25Defines.P25_ALGO_UNENCRYPT || channel.GetKeyId() == 0)
                return string.Empty;

            return DescribeP25EncryptionAlgorithm(channel.GetAlgoId());
        }

        private static string DescribeP25EncryptionAlgorithm(byte algorithmId)
        {
            return algorithmId switch
            {
                P25Defines.P25_ALGO_AES => "AES",
                P25Defines.P25_ALGO_DES => "DES-OFB",
                P25Defines.P25_ALGO_ARC4 => "ARC4",
                _ => algorithmId == P25Defines.P25_ALGO_UNENCRYPT ? string.Empty : $"0x{algorithmId:X2}"
            };
        }

        private static string DescribeDmrEncryptionAlgorithm(byte algorithmId)
        {
            if (algorithmId == 0)
                return string.Empty;

            return $"0x{algorithmId:X2}";
        }

        private static ushort? NormalizeEncryptionKeyId(uint keyId)
        {
            return keyId > 0 && keyId <= ushort.MaxValue ? (ushort)keyId : null;
        }
    }
}
