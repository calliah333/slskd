namespace slskd.Tests.Unit.Common
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using ChannelReader = slskd.Shares.ChannelReader<int>;
    using Xunit;

    public class ChannelReaderTests
    {
        [Fact]
        public async Task Producer_Blocked_On_Full_Channel_Is_Released_When_All_Readers_Fail()
        {
            var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
            var gate = new SemaphoreSlim(0);
            var taken = new CountdownEvent(2);
            var expected = new InvalidOperationException("boom");

            var readers = Enumerable.Range(0, 2).Select(i => new ChannelReader(channel, handler: item =>
            {
                taken.Signal();
                gate.Wait();
                throw expected;
            })).ToList();

            var producer = Task.Run(async () =>
            {
                foreach (var i in Enumerable.Range(0, 100))
                {
                    await channel.Writer.WriteAsync(i);
                }
            });

            // both readers are busy and the channel is full; the producer is blocked
            Assert.True(taken.Wait(TimeSpan.FromSeconds(5)));
            await Task.Delay(100);
            Assert.False(producer.IsCompleted);

            gate.Release(2);

            var closed = await Assert.ThrowsAsync<ChannelClosedException>(() => producer.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(expected, closed.InnerException);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(readers.Select(r => r.Completed)).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(expected, ex);
        }

        [Fact]
        public async Task Completes_Successfully_When_Channel_Is_Drained()
        {
            var channel = Channel.CreateBounded<int>(1);
            var count = 0;

            var reader = new ChannelReader(channel, handler: _ => Interlocked.Increment(ref count));

            foreach (var i in Enumerable.Range(0, 10))
            {
                await channel.Writer.WriteAsync(i);
            }

            channel.Writer.Complete();

            await reader.Completed.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(10, count);
        }
    }
}
