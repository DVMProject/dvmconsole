// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using System.Diagnostics;
using System.Threading.Channels;
using fnecore.NXDN;

namespace dvmconsole.NXDN;

/// <summary>
/// Owns one NXDN call, including bounded buffering, 80 ms packet pacing and release.
/// </summary>
internal sealed class NxdnTxCall : IDisposable
{
    private readonly object sync = new();
    private readonly Func<short[], byte[]> encode;
    private readonly Action<byte[], ushort> send;
    private readonly fnecore.FneSystemBase system;
    private readonly NXDNCallData call;
    private readonly NxdnAmbeCodec codec = new();
    private readonly byte[] voice = new byte[NXDNFrame.VoiceBytes];
    private int pendingWords;
    private readonly Channel<byte[]> packets = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource cancel = new();
    private bool ended;
    private bool disposed;

    public uint StreamId { get; }
    public Task Completion { get; }

    public NxdnTxCall(fnecore.FneSystemBase system, NXDNCallData call,
        Func<short[], byte[]> encode, Action<byte[], ushort> send)
    {
        if (call.TxStreamID == 0)
            throw new ArgumentOutOfRangeException(nameof(call));
        StreamId = call.TxStreamID;
        this.system = system;
        this.call = call;
        this.encode = encode;
        this.send = send;
        packets.Writer.TryWrite(system.CreateNXDNHeader(call));
        Completion = PumpAsync();
    }

    public void Process(short[] samples)
    {
        lock (sync)
        {
            if (ended || disposed)
                return;
            if (Completion.IsCompleted)
                throw new InvalidOperationException("The NXDN network sender has stopped.");
            if (samples.Length != 160)
                throw new ArgumentException("NXDN requires 160 PCM samples per codeword.", nameof(samples));
            AppendCodeword(samples);
        }
    }

    private void AppendCodeword(short[] samples)
    {
        byte[] word = encode(samples);
        if (word.Length != NXDNFrame.CodewordBytes)
            throw new InvalidOperationException("NXDN vocoder returned an invalid codeword.");
        word.CopyTo(voice, pendingWords * NXDNFrame.CodewordBytes);
        if (++pendingWords < 4)
            return;
        byte[] packet = system.CreateNXDNVoice(call, voice, codec);
        pendingWords = 0;
        if (!packets.Writer.TryWrite(packet))
        {
            ended = true;
            packets.Writer.TryComplete(new InvalidOperationException("NXDN TX audio exceeded its bounded packet buffer."));
            throw new InvalidOperationException("NXDN TX stopped rather than queue stale audio.");
        }
    }

    public Task End()
    {
        lock (sync)
        {
            if (!ended && !disposed)
            {
                try
                {
                    // Complete at most one partial 80 ms voice frame.
                    while (pendingWords > 0 && !ended)
                        AppendCodeword(new short[160]);
                }
                finally
                {
                    ended = true;
                    packets.Writer.TryComplete();
                }
            }
            return Completion;
        }
    }

    private async Task PumpAsync()
    {
        ushort sequence = 0;
        long lastSent = 0;
        bool started = false;
        try
        {
            await foreach (byte[] packet in packets.Reader.ReadAllAsync(cancel.Token).ConfigureAwait(false))
            {
                await WaitForPacketTimeAsync(lastSent, cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                send(packet, sequence);
                started = true;
                lastSent = Stopwatch.GetTimestamp();
                sequence = (ushort)((sequence + 1) % ushort.MaxValue);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally
        {
            if (started)
            {
                await WaitForPacketTimeAsync(lastSent).ConfigureAwait(false);
                send(system.CreateNXDNRelease(call), ushort.MaxValue);
            }
        }
    }

    private static async Task WaitForPacketTimeAsync(long lastSent, CancellationToken cancellationToken = default)
    {
        if (lastSent == 0)
            return;
        TimeSpan remaining = TimeSpan.FromMilliseconds(80) - Stopwatch.GetElapsedTime(lastSent);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
    }

    public void Abort()
    {
        lock (sync)
        {
            ended = true;
            pendingWords = 0;
            Array.Clear(voice);
            packets.Writer.TryComplete();
            cancel.Cancel();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            ended = true;
            disposed = true;
            packets.Writer.TryComplete();
            cancel.Cancel();
            if (Completion.IsCompleted)
            {
                call.Dispose();
                cancel.Dispose();
            }
            else
                _ = Completion.ContinueWith(_ => { call.Dispose(); cancel.Dispose(); }, TaskScheduler.Default);
        }
    }
}
