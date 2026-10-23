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
using System.Threading.Channels;
using fnecore.Analog;

namespace dvmconsole;

/// <summary>Spaces captured analog audio onto the network and drains it before release.</summary>
internal sealed class AnalogTxCall : IDisposable
{
    private const int MaxQueuedAudioMilliseconds = 250;
    private readonly object sync = new();
    private readonly uint sourceId;
    private readonly uint destinationId;
    private readonly Action<byte[], ushort> send;
    private readonly AnalogVoiceInversion inverter;
    private readonly Channel<(short[] Samples, long QueuedAt)> audio =
        Channel.CreateBounded<(short[] Samples, long QueuedAt)>(new BoundedChannelOptions(25)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly CancellationTokenSource cancel = new();
    private bool ended;
    private bool disposed;

    public uint StreamId { get; }
    public Task Completion { get; }

    public AnalogTxCall(uint sourceId, uint destinationId, uint streamId, int scramblerCode,
        Action<byte[], ushort> send)
    {
        this.sourceId = sourceId;
        this.destinationId = destinationId;
        StreamId = streamId;
        this.send = send;
        inverter = scramblerCode == 0 ? null : new AnalogVoiceInversion(scramblerCode);
        Completion = Task.Run(PumpAsync);
    }

    public void Process(short[] samples)
    {
        lock (sync)
        {
            if (ended || disposed)
                return;
            if (Completion.IsCompleted)
                throw new InvalidOperationException("The analog network sender has stopped.");
            if (samples.Length != AnalogVoicePacketCodec.SampleCount)
                throw new ArgumentException("Analog requires 160 PCM samples.", nameof(samples));
            // Prefer current speech over a full queue without ending the call.
            if (!audio.Writer.TryWrite((samples.ToArray(), Stopwatch.GetTimestamp())))
                throw new InvalidOperationException("The analog audio queue has closed.");
        }
    }

    public void End()
    {
        lock (sync)
        {
            ended = true;
            audio.Writer.TryComplete();
        }
    }

    public void Abort()
    {
        lock (sync)
        {
            if (disposed)
                return;
            End();
            cancel.Cancel();
        }
    }

    private async Task PumpAsync()
    {
        ushort sequence = 0;
        byte audioSequence = 0;
        bool started = false;
        bool failed = false;
        long due = 0;
        long interval = Stopwatch.Frequency / 50;
        try
        {
            await foreach (var frame in audio.Reader.ReadAllAsync(cancel.Token).ConfigureAwait(false))
            {
                await WaitUntilAsync(due, cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                // Check after waiting so a stalled sender cannot replay old speech on recovery.
                if (Stopwatch.GetElapsedTime(frame.QueuedAt).TotalMilliseconds > MaxQueuedAudioMilliseconds)
                    continue;
                short[] samples = frame.Samples;
                inverter?.Process(samples);
                byte[] packet = AnalogVoicePacketCodec.Encode(sourceId, destinationId, audioSequence,
                    started ? AudioFrameType.VOICE : AudioFrameType.VOICE_START, samples);
                send(packet, sequence);
                started = true;
                sequence = (ushort)((sequence + 1) % ushort.MaxValue);
                audioSequence = (byte)((audioSequence + 1) % 254);
                // Keep a monotonic schedule without accumulating timer rounding or bursting after a stall.
                long now = Stopwatch.GetTimestamp();
                due = due == 0 || now - due >= interval ? now + interval :
                    Math.Max(due + interval, now + interval / 2);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (started && !failed)
            {
                await WaitUntilAsync(due, CancellationToken.None).ConfigureAwait(false);
                send(AnalogVoicePacketCodec.Encode(sourceId, destinationId, audioSequence,
                    AudioFrameType.TERMINATOR, ReadOnlySpan<short>.Empty), ushort.MaxValue);
            }
        }
    }

    private static async Task WaitUntilAsync(long due, CancellationToken token)
    {
        while (due > Stopwatch.GetTimestamp())
        {
            double milliseconds = (due - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            await Task.Delay(Math.Max(1, (int)Math.Ceiling(milliseconds)), token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            Abort();
            disposed = true;
            _ = Completion.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
        }
    }
}
