using System.Threading.Channels;
using Oracle.Core;

namespace Oracle.Api;

/// <summary>
/// Bounded channel carrying raw <see cref="DivinationRequest"/>s from the hub (producer) to the
/// <see cref="DivinationWorker"/> (consumer). Bounded so a flooded cosmos pushes back instead of
/// growing memory without limit; <see cref="ChannelWriter{T}.TryWrite"/> returns false immediately
/// when full, which the hub turns into a "the cosmos is busy" rejection.
/// </summary>
public sealed class DivinationChannel
{
    private readonly Channel<DivinationRequest> _channel;

    public DivinationChannel()
    {
        _channel = Channel.CreateBounded<DivinationRequest>(new BoundedChannelOptions(capacity: 50)
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelWriter<DivinationRequest> Writer => _channel.Writer;
    public ChannelReader<DivinationRequest> Reader => _channel.Reader;
}
