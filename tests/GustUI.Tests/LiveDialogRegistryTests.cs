using System;
using System.Runtime.CompilerServices;
using GustUI.Managers;

namespace GustUI.Tests
{
    /// <summary>
    /// The window registry must not be what keeps a window alive (ezmuze
    /// #604). Every ModalWindowElement joins it when constructed, and until
    /// this change only Kill() took it out again, so a window built and never
    /// shown, or dropped from the tree without Kill(), stayed reachable for
    /// the life of the process, along with everything its content could
    /// reach. The pattern explorer builds its window on every song open, so
    /// every song the app had ever opened stayed in memory.
    /// </summary>
    public class LiveDialogRegistryTests
    {
        private sealed class Window
        {
            // Something worth leaking, standing in for a whole song.
            public byte[] Payload = new byte[1024];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AddAndForget(WeakRegistry<Window> registry)
        {
            var window = new Window();
            registry.Add(window);
            return new WeakReference(window);
        }

        private static void Collect()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        [Fact]
        public void AnObjectNobodyElseHoldsIsCollected()
        {
            var registry = new WeakRegistry<Window>();
            WeakReference window = AddAndForget(registry);

            Collect();

            Assert.False(window.IsAlive, "the registry kept an unreferenced object alive");
            Assert.Empty(registry.Alive());
        }

        [Fact]
        public void TheRegistryStaysTheSizeOfTheLiveSet()
        {
            var registry = new WeakRegistry<Window>();
            for (int i = 0; i < 500; i++)
            {
                AddAndForget(registry);
            }

            Collect();
            var held = new Window();
            registry.Add(held); // pruning happens on the way in

            Assert.Equal(1, registry.EntryCount);
            GC.KeepAlive(held);
        }

        [Fact]
        public void HeldObjectsStayListedInTheOrderTheyJoined()
        {
            var registry = new WeakRegistry<Window>();
            var first = new Window();
            var second = new Window();
            registry.Add(first);
            AddAndForget(registry);
            registry.Add(second);

            Collect();

            Assert.Equal(new[] { first, second }, registry.Alive());
        }

        [Fact]
        public void RemoveTakesOnlyThatObject()
        {
            var registry = new WeakRegistry<Window>();
            var keep = new Window();
            var gone = new Window();
            registry.Add(keep);
            registry.Add(gone);

            registry.Remove(gone);

            Assert.Equal(new[] { keep }, registry.Alive());
        }
    }
}
