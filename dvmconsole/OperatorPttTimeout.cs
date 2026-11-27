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

using System.Diagnostics;

namespace dvmconsole
{
    /// <summary>
    /// Limits one operator call using elapsed time rather than the system clock.
    /// </summary>
    internal sealed class OperatorPttTimeout
    {
        private readonly object sync = new object();
        private readonly long startedAt;
        private readonly int seconds;
        private bool stopped;
        private int lastWarningSecond = -1;

        internal uint StreamId { get; }

        internal OperatorPttTimeout(uint streamId, int seconds, long startedAt)
        {
            if (seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            StreamId = streamId;
            this.seconds = seconds;
            this.startedAt = startedAt;
        }

        internal bool IsExpired(long now)
        {
            return seconds > 0 && Stopwatch.GetElapsedTime(startedAt, now).TotalSeconds >= seconds;
        }

        /// <summary>
        /// Emits at most one warning per countdown second, without replaying missed beeps.
        /// </summary>
        internal bool TakeWarning(long now)
        {
            if (seconds == 0 || IsExpired(now))
                return false;
            int remaining = (int)Math.Ceiling(seconds - Stopwatch.GetElapsedTime(startedAt, now).TotalSeconds);
            if (remaining < 1 || remaining > 10 || remaining == lastWarningSecond)
                return false;
            lastWarningSecond = remaining;
            return true;
        }

        /// <summary>
        /// Serializes microphone frames and fences queued work when the call ends.
        /// </summary>
        internal void SendFrame(Action send)
        {
            lock (sync)
            {
                if (!stopped && !IsExpired(Stopwatch.GetTimestamp()))
                    send();
            }
        }

        internal void Stop()
        {
            lock (sync)
                stopped = true;
        }
    }
}
