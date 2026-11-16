namespace slskd.Tests.Unit.Common
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public class CoalescerTests
    {
        [Fact]
        public async Task Delivers_First_Value_Immediately()
        {
            var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coalescer = new Coalescer<int>(TimeSpan.FromMinutes(1), value =>
            {
                delivered.TrySetResult(value);
                return Task.CompletedTask;
            });

            coalescer.Post(42);

            Assert.Equal(42, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task Coalesces_Burst_And_Always_Delivers_Last_Value()
        {
            var deliveries = new ConcurrentQueue<int>();
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var coalescer = new Coalescer<int>(TimeSpan.FromMilliseconds(50), async value =>
            {
                deliveries.Enqueue(value);

                // hold the first delivery until the whole burst has been posted
                if (value == 0)
                {
                    first.TrySetResult();
                    await release.Task;
                }

                if (value == 99)
                {
                    last.TrySetResult();
                }
            });

            coalescer.Post(0);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));

            for (int i = 1; i < 100; i++)
            {
                coalescer.Post(i);
            }

            release.SetResult();
            await last.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { 0, 99 }, deliveries.ToArray());
        }

        [Fact]
        public async Task Never_Delivers_Concurrently_And_Preserves_Order()
        {
            var active = 0;
            var maxActive = 0;
            var deliveries = new ConcurrentQueue<int>();
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var coalescer = new Coalescer<int>(TimeSpan.FromMilliseconds(1), async value =>
            {
                var now = Interlocked.Increment(ref active);
                InterlockedMax(ref maxActive, now);

                await Task.Delay(2);
                deliveries.Enqueue(value);

                Interlocked.Decrement(ref active);

                if (value == 49)
                {
                    done.TrySetResult();
                }
            });

            for (int i = 0; i < 50; i++)
            {
                coalescer.Post(i);
                await Task.Delay(1);
            }

            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var delivered = deliveries.ToArray();

            Assert.Equal(1, maxActive);
            Assert.Equal(delivered.OrderBy(v => v), delivered);
            Assert.Equal(49, delivered.Last());
        }

        [Fact]
        public async Task Continues_Delivering_After_Delivery_Throws()
        {
            var threw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var coalescer = new Coalescer<int>(TimeSpan.FromMilliseconds(1), value =>
            {
                if (value == 1)
                {
                    threw.TrySetResult();
                    throw new InvalidOperationException("boom");
                }

                delivered.TrySetResult(value);
                return Task.CompletedTask;
            });

            coalescer.Post(1);
            await threw.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coalescer.Post(2);

            Assert.Equal(2, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;

            while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
