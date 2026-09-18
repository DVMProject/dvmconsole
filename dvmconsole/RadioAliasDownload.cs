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
*/

using System.IO;
using System.Runtime.CompilerServices;
using fnecore;

namespace dvmconsole
{
    internal static class RadioAliasDownload
    {
        private static readonly ConditionalWeakTable<FnePeer, SemaphoreSlim> downloadGates = new();

        /// <summary>
        /// Awaits FNECore's alias event; all wire encoding and reassembly remain in FNECore.
        /// </summary>
        public static async Task<byte[]> RequestRadioAliasesAsync(this FnePeer peer,
            CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(peer);
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan duration = timeout ?? TimeSpan.FromSeconds(30);
            if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            SemaphoreSlim gate = downloadGates.GetValue(peer, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
                throw new InvalidOperationException("A radio alias download is already in progress.");

            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            object responseLock = new();
            uint streamId = 0;
            void OnAliases(object sender, RadioAliasSyncEvent e)
            {
                lock (responseLock)
                {
                    if (e.PeerId == peer.PeerId && e.StreamId == streamId && e.Data != null && e.Length == e.Data.Length)
                        completion.TrySetResult(e.Data);
                }
            }
            void OnDisconnected(uint peerId)
            {
                if (peerId == peer.PeerId)
                    completion.TrySetException(new IOException("Peer disconnected during radio alias sync."));
            }
            void OnFailure(object sender, RadioAliasSyncFailedEvent e)
            {
                lock (responseLock)
                {
                    if (e.PeerId == peer.PeerId && e.StreamId == streamId)
                        completion.TrySetException(e.Error);
                }
            }

            try
            {
                peer.RadioAliasSync += OnAliases;
                peer.RadioAliasSyncFailed += OnFailure;
                peer.PeerDisconnected += OnDisconnected;
                lock (responseLock)
                {
                    if (!peer.IsStarted || peer.Information.State != ConnectionState.RUNNING)
                        throw new InvalidOperationException("The peer must be connected before requesting radio aliases.");
                    // Subscribe before sending; a loopback response can arrive before Send returns its stream ID.
                    streamId = peer.SendMasterRadioAliasSync();
                }
                return await completion.Task.WaitAsync(duration, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("Radio alias sync timed out. Check FNE support and metadata port connectivity.");
            }
            finally
            {
                peer.RadioAliasSync -= OnAliases;
                peer.RadioAliasSyncFailed -= OnFailure;
                peer.PeerDisconnected -= OnDisconnected;
                if (streamId != 0)
                    peer.CancelRadioAliasSync(streamId);
                gate.Release();
            }
        }
    }
}
