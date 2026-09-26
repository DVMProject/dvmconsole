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
using fnecore.DMR;

namespace dvmconsole.DMR;

/// <summary>Queues audio; only the sender advances FNECore's call state.</summary>
internal sealed class DmrTxCall : IDisposable
{
    private readonly object sync = new();
    private readonly fnecore.FneSystemBase system;
    private readonly DMRCallData call;
    private readonly Func<short[], byte[]> encode;
    private readonly Action<byte[], ushort> send;
    private readonly Channel<byte[]> audio = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(12)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource cancel = new();
    private readonly byte[] partial = new byte[DMRFrame.VoiceBytes];
    private int used;
    private bool ended;
    private bool disposed;

    public uint StreamId => call.TxStreamID;
    public Task Completion { get; }

    public DmrTxCall(fnecore.FneSystemBase system, DMRCallData call,
        Func<short[], byte[]> encode, Action<byte[], ushort> send)
    {
        this.system = system ?? throw new ArgumentNullException(nameof(system));
        this.call = call ?? throw new ArgumentNullException(nameof(call));
        this.encode = encode ?? throw new ArgumentNullException(nameof(encode));
        this.send = send ?? throw new ArgumentNullException(nameof(send));
        Completion = Task.Run(PumpAsync);
    }

    public void Process(short[] samples)
    {
        lock (sync)
        {
            if (ended || disposed) return;
            if (Completion.IsCompleted) throw new InvalidOperationException("The DMR sender has stopped.");
            if (samples.Length != 160) throw new ArgumentException("DMR audio requires 160 samples.", nameof(samples));
            Append(encode(samples));
        }
    }

    private void Append(byte[] codeword)
    {
        if (codeword == null || codeword.Length != DMRFrame.CodewordBytes)
            throw new InvalidOperationException("The DMR vocoder returned an invalid codeword.");
        codeword.CopyTo(partial, used);
        used += codeword.Length;
        if (used != partial.Length) return;
        used = 0;
        if (!audio.Writer.TryWrite(partial.ToArray()))
            throw new InvalidOperationException("DMR stopped rather than transmit stale queued audio.");
    }

    public Task End()
    {
        lock (sync)
        {
            if (ended || disposed) return Completion;
            try
            {
                if (used != 0)
                {
                    byte[] silence = encode(new short[160]);
                    while (used != 0) Append(silence);
                }
            }
            finally
            {
                ended = true;
                audio.Writer.TryComplete();
            }
            return Completion;
        }
    }

    public void Abort()
    {
        lock (sync)
        {
            if (disposed) return;
            ended = true;
            used = 0;
            audio.Writer.TryComplete();
            cancel.Cancel();
        }
    }

    private async Task PumpAsync()
    {
        long lastSent = 0;
        ushort sequence = 0;
        bool started = false;
        bool failed = false;
        try
        {
            cancel.Token.ThrowIfCancellationRequested();
            send(system.CreateDMRHeader(call), sequence++);
            started = true;
            lastSent = Stopwatch.GetTimestamp();
            await foreach (byte[] voice in audio.Reader.ReadAllAsync(cancel.Token).ConfigureAwait(false))
            {
                await PaceAsync(lastSent, cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                send(system.CreateDMRVoice(call, voice), sequence);
                sequence = (ushort)((sequence + 1) % ushort.MaxValue);
                lastSent = Stopwatch.GetTimestamp();
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
            // Finish only what actually reached the wire, never the discarded audio queue.
            if (started && !failed)
            {
                int remaining = call.PendingVoiceBursts;
                for (int i = 0; i < remaining; i++)
                {
                    await PaceAsync(lastSent, CancellationToken.None).ConfigureAwait(false);
                    send(system.CreateDMRSilence(call), sequence);
                    sequence = (ushort)((sequence + 1) % ushort.MaxValue);
                    lastSent = Stopwatch.GetTimestamp();
                }
                await PaceAsync(lastSent, CancellationToken.None).ConfigureAwait(false);
                send(system.CreateDMRRelease(call), ushort.MaxValue);
            }
        }
    }

    private static async Task PaceAsync(long lastSent, CancellationToken token)
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(60) - Stopwatch.GetElapsedTime(lastSent);
        if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            Abort();
            disposed = true;
            _ = Completion.ContinueWith(_ =>
            {
                call.Dispose();
                cancel.Dispose();
            }, TaskScheduler.Default);
        }
    }
}
