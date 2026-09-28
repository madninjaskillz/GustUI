using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GustUI.Managers;
using Xunit;

namespace GustUI.Tests
{
    /// <summary>
    /// Telemetry keeps each thread's totals apart and merges them when a
    /// reader asks (ezmuze #444). Before that every Begin/End added into one
    /// shared dictionary under one lock, and the audio engine's million probe
    /// pairs per 20 s of audio fought the UI thread's own for it: rendering ran
    /// 33-51% slower with telemetry on. These pin that moving the adding-up
    /// changed nothing a reader sees.
    ///
    /// One collection, not parallel: Telemetry is static.
    /// </summary>
    [Collection("Telemetry")]
    public class TelemetryTests : IDisposable
    {
        private readonly bool wasEnabled = Telemetry.Enabled;

        public TelemetryTests()
        {
            Telemetry.Enabled = true;
            Telemetry.Reset();
        }

        public void Dispose()
        {
            Telemetry.Reset();
            Telemetry.Enabled = wasEnabled;
        }

        private static TagStats? Stats(string tag)
        {
            foreach (Telemetry.TagStats s in Telemetry.GetAllStats())
            {
                if (s.Tag == tag)
                {
                    return new TagStats(s.Calls, s.TotalMs);
                }
            }

            return null;
        }

        private readonly record struct TagStats(long Calls, double TotalMs);

        private static void Spin(double ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalMilliseconds < ms)
            {
            }
        }

        /// <summary>Calls recorded on several threads at once add up exactly --
        /// none lost to the merge, none counted twice.</summary>
        [Fact]
        public void Calls_from_several_threads_add_up_exactly()
        {
            const int Threads = 4;
            const int PerThread = 20_000;
            var threads = Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < PerThread; i++)
                {
                    Telemetry.Begin("T.Outer");
                    Telemetry.Begin("T.Inner");
                    Telemetry.End("T.Inner");
                    Telemetry.End("T.Outer");
                }
            })).ToList();

            // A reader draining at the same time must not lose or double
            // anything either.
            int stop = 0;
            var reader = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    Telemetry.EndFrame();
                    _ = Telemetry.GetAllStats();
                }
            });

            reader.Start();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            Volatile.Write(ref stop, 1);
            reader.Join();

            Assert.Equal(Threads * PerThread, Stats("T.Outer")!.Value.Calls);
            Assert.Equal(Threads * PerThread, Stats("T.Inner")!.Value.Calls);
        }

        /// <summary>Self time still excludes a nested child: the parent is
        /// charged for its own work, not the child's.</summary>
        [Fact]
        public void A_parent_is_not_charged_for_its_child()
        {
            Telemetry.Begin("P");
            Spin(10);
            Telemetry.Begin("C");
            Spin(30);
            Telemetry.End("C");
            Telemetry.End("P");

            double parent = Stats("P")!.Value.TotalMs;
            double child = Stats("C")!.Value.TotalMs;
            Assert.InRange(child, 30, 200);
            Assert.InRange(parent, 10, 25);
        }

        /// <summary>A thread that has finished still counts: its totals are
        /// merged the next time anybody reads.</summary>
        [Fact]
        public void A_finished_thread_still_counts()
        {
            var worker = new Thread(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using (Telemetry.Scope("Gone"))
                    {
                    }
                }
            });
            worker.Start();
            worker.Join();

            Assert.Equal(100, Stats("Gone")!.Value.Calls);
        }

        /// <summary>Reset clears what other threads have recorded but nobody
        /// has read yet, not only the merged totals.</summary>
        [Fact]
        public void Reset_clears_totals_other_threads_have_not_handed_over()
        {
            var ready = new ManualResetEventSlim();
            var finish = new ManualResetEventSlim();
            var worker = new Thread(() =>
            {
                using (Telemetry.Scope("Pending"))
                {
                }

                ready.Set();
                finish.Wait();
            });
            worker.Start();
            ready.Wait();

            Telemetry.Reset();
            finish.Set();
            worker.Join();

            Assert.Null(Stats("Pending"));
        }

        /// <summary>The per-frame graph sample includes time spent on other
        /// threads, as it did when everything went into one accumulator.</summary>
        [Fact]
        public void A_frame_sample_includes_other_threads_time()
        {
            var worker = new Thread(() =>
            {
                using (Telemetry.Scope("Elsewhere"))
                {
                    Spin(20);
                }
            });
            worker.Start();
            worker.Join();

            Telemetry.EndFrame();
            (double _, float totalMs) = Telemetry.RecentSamples(60).First();
            Assert.True(totalMs >= 20, $"frame sample was {totalMs} ms");
        }
    }

    [CollectionDefinition("Telemetry", DisableParallelization = true)]
    public class TelemetryCollection
    {
    }
}
