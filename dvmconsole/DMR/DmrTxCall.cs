// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using System.Diagnostics;
using System.Threading.Channels;
using fnecore.DMR;

namespace dvmconsole.DMR;

/// <summary>Owns one paced DMR call while FNECore owns its protocol state.</summary>
internal sealed class DmrTxCall : IDisposable
{
    private readonly object sync = new();
    private readonly Func<short[], byte[]> encode;
    private readonly Action<byte[], ushort> send;
    private readonly DmrVoiceEncoder encoder;
    private readonly Channel<DmrOutboundPacket> packets = Channel.CreateBounded<DmrOutboundPacket>(
        new BoundedChannelOptions(12)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly CancellationTokenSource cancel = new();
    private bool ended;
    private bool disposed;

    public DmrTxCall(uint sourceId, uint destinationId, byte slot, uint streamId,
        Func<short[], byte[]> encode, Action<byte[], ushort> send, DmrPrivacyOptions privacy = null)
    {
        if (streamId == 0)
            throw new ArgumentOutOfRangeException(nameof(streamId));
        StreamId = streamId;
        this.encode = encode ?? throw new ArgumentNullException(nameof(encode));
        this.send = send ?? throw new ArgumentNullException(nameof(send));
        encoder = new DmrVoiceEncoder(sourceId, destinationId, slot, privacy,
            privacy == null ? null : new DmrAmbeCodec());
        foreach (DmrOutboundPacket packet in encoder.CreateCallStartPackets())
            packets.Writer.TryWrite(packet);
        Completion = PumpAsync();
    }

    public uint StreamId { get; }
    public Task Completion { get; }

    public void Process(short[] samples)
    {
        lock (sync)
        {
            if (ended || disposed)
                return;
            if (Completion.IsCompleted)
                throw new InvalidOperationException("The DMR network sender has stopped.");
            if (samples.Length != 160)
                throw new ArgumentException("DMR requires 160 PCM samples per codeword.", nameof(samples));
            DmrOutboundPacket? packet = encoder.ProcessCodeword(encode(samples));
            if (packet.HasValue && !packets.Writer.TryWrite(packet.Value))
                throw new InvalidOperationException("DMR TX stopped rather than queue stale audio.");
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
                    byte[] silence = encode(new short[160]);
                    foreach (DmrOutboundPacket packet in encoder.Complete(silence))
                    {
                        if (!packets.Writer.TryWrite(packet))
                            throw new InvalidOperationException("DMR TX completion exceeded its bounded packet buffer.");
                    }
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

    public void Abort()
    {
        lock (sync)
        {
            if (ended || disposed)
                return;
            ended = true;
            encoder.DiscardPending();
            packets.Writer.TryComplete();
            cancel.Cancel();
        }
    }

    private async Task PumpAsync()
    {
        long lastSent = 0;
        try
        {
            await foreach (DmrOutboundPacket packet in packets.Reader.ReadAllAsync(cancel.Token).ConfigureAwait(false))
            {
                await WaitForPacketTimeAsync(lastSent, cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                send(packet.Payload, packet.Sequence);
                lastSent = Stopwatch.GetTimestamp();
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    }

    private static async Task WaitForPacketTimeAsync(long lastSent, CancellationToken cancellationToken)
    {
        if (lastSent == 0)
            return;
        TimeSpan remaining = TimeSpan.FromMilliseconds(60) - Stopwatch.GetElapsedTime(lastSent);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
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
            encoder.Dispose();
            if (Completion.IsCompleted)
                cancel.Dispose();
            else
                _ = Completion.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
        }
    }
}
