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
                    Codeplug.Channel cpgChannel = Codeplug?.GetChannelByName(channel.ChannelName);
                    bool enabled = channel.SystemName != PLAYBACKSYS &&
                        channel.ChannelName != PLAYBACKCHNAME &&
                        channel.DstId != PLAYBACKTG &&
                        cpgChannel != null &&
                        tarManager.IsChannelEnabled(cpgChannel.System, cpgChannel.Tgid, cpgChannel.Name);
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

        private void BeginTarTxRecording(ChannelBox channelBox, Codeplug.System system, Codeplug.Channel channel, uint streamId)
        {
            if (channelBox == null || system == null || channel == null || streamId == 0)
                return;

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

            bool isEncrypted = channelBox.IsTxEncrypted;
            string algorithm = DescribeTxEncryptionAlgorithm(channel);
            ushort? keyId = isEncrypted && channel.GetKeyId() > 0 ? channel.GetKeyId() : null;

            tarManager.StopTxRecording(system, channel, channelBox.TxStreamId, isEncrypted, algorithm, keyId, DateTime.UtcNow);
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
