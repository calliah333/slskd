namespace slskd.Tests.Unit.Common
{
    using System.Collections.Generic;
    using Xunit;

    public class ManagedStateTests
    {
        [Fact]
        public void SetValue_Notifies_Listeners_With_Chained_Previous_And_Current_Values()
        {
            var state = new ManagedState<ShareState>();
            var initial = state.CurrentValue;
            var changes = new List<(ShareState Previous, ShareState Current)>();

            using var subscription = state.OnChange(change => changes.Add(change));

            var first = state.SetValue(s => s with { Files = 1 });
            var second = state.SetValue(s => s with { Files = 2 });

            Assert.Equal(2, changes.Count);
            Assert.Same(initial, changes[0].Previous);
            Assert.Same(first, changes[0].Current);
            Assert.Same(first, changes[1].Previous);
            Assert.Same(second, changes[1].Current);
            Assert.Equal(1, changes[1].Previous.Files);
            Assert.Equal(2, state.CurrentValue.Files);
        }

        [Fact]
        public void Snapshot_Is_Unaffected_By_Later_Changes()
        {
            var state = new ManagedState<ShareState>();
            state.SetValue(s => s with { Files = 1 });

            var snapshot = state.Snapshot;
            state.SetValue(s => s with { Files = 2 });

            Assert.Equal(1, snapshot.Value.Files);
        }

        [Fact]
        public void Disposed_Listener_Is_Not_Notified()
        {
            var state = new ManagedState<ShareState>();
            var calls = 0;

            var subscription = state.OnChange(_ => calls++);
            state.SetValue(s => s with { Files = 1 });
            subscription.Dispose();
            state.SetValue(s => s with { Files = 2 });

            Assert.Equal(1, calls);
        }
    }
}
