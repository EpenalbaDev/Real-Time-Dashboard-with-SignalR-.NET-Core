using System.Threading.Channels;
using RealTimeDashboard.Data.Entities;

namespace RealTimeDashboard.Services;

public sealed class TransactionChannel
{
    private readonly Channel<TransactionEntity> _channel;

    public TransactionChannel()
    {
        _channel = Channel.CreateBounded<TransactionEntity>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ChannelWriter<TransactionEntity> Writer => _channel.Writer;
    public ChannelReader<TransactionEntity> Reader => _channel.Reader;
}
