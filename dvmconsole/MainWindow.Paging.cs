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

using fnecore;
using fnecore.P25;

namespace dvmconsole
{
    public partial class MainWindow
    {
        private const string PAGE_ALERT_OUTPUT_KEY = "__dvmconsole_page_alert";
        private static readonly byte[] PAGE_ALERT_PCM = BuildPageAlert();

        private static byte[] BuildPageAlert()
        {
            byte[] chime = BuildFneConnectionChime(new double[] { 480, 640, 480, 640 });
            const int repeats = 3;
            const int gapBytes = 8000; // Half a second of 8 kHz, 16-bit mono silence.
            byte[] pcm = new byte[chime.Length * repeats + gapBytes * (repeats - 1)];
            for (int i = 0; i < repeats; i++)
                Buffer.BlockCopy(chime, 0, pcm, i * (chime.Length + gapBytes), chime.Length);
            return pcm;
        }

        /// <summary>
        /// Receives a P25 call alert using the originating peer's configured RID,
        /// independently of channel selection. Notification audio is local only.
        /// </summary>
        public void P25CallAlertReceived(PeerSystem peer, P25DataReceivedEvent e, DateTime receivedAt)
        {
            // FNECore supplies the network source/destination IDs; other TSBKs are not pages.
            if (peer == null || e == null || e.DUID != P25DUID.TSDU || e.Data == null ||
                e.Data.Length < 24 || e.Data[4] != P25Defines.TSBK_IOSP_CALL_ALRT ||
                e.Data[23] < 24 + P25Defines.P25_TSDU_FRAME_LENGTH_BYTES || e.Data[23] > e.Data.Length ||
                e.SrcId == 0 || e.SrcId > 0xFFFFFF || e.DstId == 0 || e.DstId > 0xFFFFFF ||
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                FneConnectionEntry entry = GetFneConnectionEntry(peer.ConfiguredSystemName);
                if (isReloadingResources || !IsCurrentFneConnectionEntry(entry) ||
                    !ReferenceEquals(entry.Peer, peer) || !entry.DesiredStarted ||
                    !uint.TryParse(entry.SystemConfig.Rid, out uint consoleRid) || consoleRid != e.DstId)
                    return;

                string alias = TryResolveSubscriberAlias(entry.SystemConfig, (int)e.SrcId);
                if (callHistoryWindow == null)
                    return;

                // Acknowledge only this page, including audio still queued for device startup.
                string outputKey = PAGE_ALERT_OUTPUT_KEY + "_" + Guid.NewGuid().ToString("N");
                var cancellation = new CancellationTokenSource();
                callHistoryWindow.AddPage(entry.SystemName, (int)e.SrcId, alias, receivedAt, () =>
                {
                    cancellation.Cancel();
                    try { audioManager.StopOneShot(outputKey); }
                    finally { cancellation.Dispose(); }
                });
                Log.WriteLine($"({entry.SystemName}) Page received from RID {e.SrcId} to Console RID {consoleRid}.");
                try
                {
                    audioManager.PlayOneShot(outputKey, PAGE_ALERT_PCM, cancellation.Token);
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"Failed to play received page alert: {ex.Message}");
                }
            }));
        }
    }
}
