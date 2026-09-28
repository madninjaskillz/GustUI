using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GustUI.Managers
{
    /// <summary>
    /// Ad-hoc, tag-based CPU profiler — the "which section of code is
    /// actually expensive" complement to <see cref="FrameProfiler"/>'s fixed
    /// 8-bucket phase timing. Call sites tag arbitrary sections with a plain
    /// string (<c>using (Telemetry.Scope("Sequencer.DrawBlocks")) { ... }</c>)
    /// instead of pre-registering an enum value, so instrumentation can be
    /// added or deepened incrementally wherever a hotspot turns up — the
    /// intended workflow: enable, watch the overlay/summary for the worst
    /// tag, wrap a nested Scope inside it to split it further, repeat.
    ///
    /// Tracks EXCLUSIVE ("self") time per tag via a simple call stack: a
    /// parent scope's own recorded time excludes whatever a nested child
    /// scope accounted for, so "which tag has the most CPU" isn't dominated
    /// by whichever outermost scope happens to wrap everything else.
    ///
    /// Disabled, every call is a single bool check — safe to leave
    /// instrumentation compiled into shipping code.
    /// </summary>
    public static class Telemetry
    {
        public struct TagStats
        {
            public string Tag;
            public long TotalTicks;
            public long Calls;
            public double TotalMs => TotalTicks * 1000.0 / Stopwatch.Frequency;
            public double AvgMs => Calls > 0 ? TotalMs / Calls : 0;
        }

        private struct StackFrame
        {
            public string Tag;
            public long ResumedAt;
        }

        private struct Sample
        {
            public double TimeSeconds;
            public float TotalMs;
        }

        public static bool Enabled;

        /// <summary>Whether the on-screen graph+table overlay should draw
        /// (DrawManager.DrawLoop reads this). Independent of Enabled so a
        /// caller could keep collecting without showing the HUD, though the
        /// normal use is to flip both together.</summary>
        public static bool OverlayVisible;

        /// <summary>Where <see cref="PrintSummary"/> writes; defaults to
        /// Console.WriteLine (visible in the browser console under WASM),
        /// matching FrameProfiler's own ReportSink convention.</summary>
        public static Action<string> ReportSink = s => Console.WriteLine(s);

        // Per-thread call stack (2026-08-13, fixed after a real crash): the
        // audio path calls Begin/End from more than one thread — the main
        // thread's DynamicSoundEffectInstance.BufferNeeded pump AND the
        // background Stretch-clip pump (an async loop resuming on a
        // threadpool thread post-await, no SynchronizationContext in this
        // console-style Game). A single SHARED stack doesn't even have
        // coherent semantics across two genuinely concurrent call
        // sequences — whose "current top frame" is it? — and in practice
        // corrupted (a torn List<T> read/write from the concurrent
        // Add/RemoveAt below) the instant per-node audio instrumentation
        // pushed call frequency high enough to make the race actually land:
        // System.ArgumentNullException: Value cannot be null (Parameter
        // 'key') out of Dictionary.TryGetValue, from a stack entry's Tag
        // reading back null after concurrent mutation. Each thread now gets
        // its own nesting stack (correct self-time per thread); the shared
        // totals/history below are still cross-thread aggregates, so THEY
        // stay lock-protected.
        [ThreadStatic]
        private static List<StackFrame>? threadStack;

        private static List<StackFrame> Stack => threadStack ??= new List<StackFrame>();

        private static readonly object syncRoot = new object();

        // PER-THREAD TOTALS (ezmuze #444). Every Begin/End used to add into
        // the shared totals below under syncRoot, two or three times a pair.
        // The audio engine makes a pair per graph node per 64-frame tick --
        // about a million per 20 s of audio -- and with the UI instrumenting
        // on the game thread at the same time, the two fought over that one
        // lock: rendering took 33-51% longer (measured 2026-09-28, shipped
        // demos, telemetry on). Now each thread adds into its OWN
        // accumulator under its own lock, which only a reader ever contends
        // for, about once a frame. Readers (EndFrame, GetAllStats, Reset)
        // merge every thread's accumulator into the shared totals under
        // syncRoot. Lock order is always syncRoot, then a thread's Gate --
        // the hot path takes only its own Gate, never syncRoot.
        private sealed class ThreadTotals
        {
            public readonly object Gate = new object();
            public readonly Dictionary<string, (long ticks, long calls)> Totals = new();
            public long FrameTicks;

            // Weak, so a finished thread can be merged one last time and
            // dropped rather than kept alive by the registry.
            public readonly WeakReference<System.Threading.Thread> Owner =
                new WeakReference<System.Threading.Thread>(System.Threading.Thread.CurrentThread);
        }

        [ThreadStatic]
        private static ThreadTotals threadTotals;

        /// <summary>Every thread that has recorded anything. Guarded by
        /// syncRoot.</summary>
        private static readonly List<ThreadTotals> allThreadTotals = new();

        private static ThreadTotals MyTotals
        {
            get
            {
                ThreadTotals mine = threadTotals;
                if (mine == null)
                {
                    mine = new ThreadTotals();
                    threadTotals = mine;
                    lock (syncRoot)
                    {
                        allThreadTotals.Add(mine);
                    }
                }

                return mine;
            }
        }

        private static readonly Dictionary<string, (long ticks, long calls)> totals = new();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static long frameTicksAccum;

        private const int HistoryCapacity = 4096; // far more than 10s needs at any real frame rate
        private static readonly Sample[] history = new Sample[HistoryCapacity];
        private static int historyHead;
        private static int historyCount;

        public static void Begin(string tag)
        {
            if (!Enabled)
            {
                return;
            }

            List<StackFrame> stack = Stack;
            long now = Stopwatch.GetTimestamp();
            if (stack.Count > 0)
            {
                StackFrame top = stack[stack.Count - 1];
                Accumulate(top.Tag, now - top.ResumedAt, 0);
            }

            stack.Add(new StackFrame { Tag = tag, ResumedAt = now });
            Accumulate(tag, 0, 1);
        }

        public static void End(string tag)
        {
            if (!Enabled)
            {
                return;
            }

            List<StackFrame> stack = Stack;
            if (stack.Count == 0)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            StackFrame top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            Accumulate(top.Tag, now - top.ResumedAt, 0);

            if (stack.Count > 0)
            {
                StackFrame parent = stack[stack.Count - 1];
                parent.ResumedAt = now;
                stack[stack.Count - 1] = parent;
            }
        }

        /// <summary>Disposable scope — <c>using (Telemetry.Scope("tag")) { ... }</c>.
        /// A readonly struct, so this allocates nothing on the heap even
        /// with Enabled true.</summary>
        public readonly struct ScopeHandle : IDisposable
        {
            private readonly string tag;

            internal ScopeHandle(string tag)
            {
                this.tag = tag;
                Begin(tag);
            }

            public void Dispose() => End(tag);
        }

        public static ScopeHandle Scope(string tag) => new ScopeHandle(tag);

        private static void Accumulate(string tag, long addTicks, long addCalls)
        {
            ThreadTotals mine = MyTotals;
            lock (mine.Gate)
            {
                AddTo(mine.Totals, tag, addTicks, addCalls);
                if (addTicks > 0)
                {
                    mine.FrameTicks += addTicks;
                }
            }
        }

        private static void AddTo(Dictionary<string, (long ticks, long calls)> into, string tag, long addTicks, long addCalls)
        {
            if (into.TryGetValue(tag, out (long ticks, long calls) t))
            {
                into[tag] = (t.ticks + addTicks, t.calls + addCalls);
            }
            else
            {
                into[tag] = (addTicks, addCalls);
            }
        }

        /// <summary>Folds every thread's accumulator into the shared totals
        /// and frame accumulator, and empties them. Caller holds syncRoot. A
        /// thread that has finished is merged one last time and dropped.
        /// Its Dictionary keeps its capacity across the Clear, so a thread
        /// that records the same tags every frame allocates nothing
        /// here.</summary>
        private static void DrainThreadTotalsLocked()
        {
            for (int i = allThreadTotals.Count - 1; i >= 0; i--)
            {
                ThreadTotals t = allThreadTotals[i];
                lock (t.Gate)
                {
                    foreach (KeyValuePair<string, (long ticks, long calls)> kv in t.Totals)
                    {
                        AddTo(totals, kv.Key, kv.Value.ticks, kv.Value.calls);
                    }

                    t.Totals.Clear();
                    frameTicksAccum += t.FrameTicks;
                    t.FrameTicks = 0;
                }

                if (!t.Owner.TryGetTarget(out System.Threading.Thread owner) || !owner.IsAlive)
                {
                    allThreadTotals.RemoveAt(i);
                }
            }
        }

        /// <summary>Call once per presented frame (after Draw) — records
        /// this frame's total instrumented CPU time as one graph sample and
        /// resets the per-frame accumulator. Any tag still open on the stack
        /// at this point (an unmatched Begin — a bug in instrumentation, not
        /// something to paper over) keeps accruing into the NEXT frame's
        /// sample until it's eventually End()'d.</summary>
        public static void EndFrame()
        {
            if (!Enabled)
            {
                return;
            }

            double now = clock.Elapsed.TotalSeconds;
            lock (syncRoot)
            {
                DrainThreadTotalsLocked();
                float ms = (float)(frameTicksAccum * 1000.0 / Stopwatch.Frequency);
                history[historyHead] = new Sample { TimeSeconds = now, TotalMs = ms };
                historyHead = (historyHead + 1) % HistoryCapacity;
                historyCount = Math.Min(historyCount + 1, HistoryCapacity);
                frameTicksAccum = 0;
            }
        }

        /// <summary>Samples from newest to oldest within the last
        /// <paramref name="windowSeconds"/> — caller reverses if it wants
        /// oldest-first for left-to-right graphing.</summary>
        public static IEnumerable<(double TimeSeconds, float TotalMs)> RecentSamples(double windowSeconds)
        {
            // Snapshot the header under the lock, then read the (otherwise
            // single-writer — only EndFrame, always called from the same
            // render thread this is read from) history array without
            // holding the lock across the yield boundary: a lock held while
            // the caller controls how fast it drains the enumerator is a
            // latent contention/deadlock risk against the readers that
            // drain every thread's totals under it.
            int head, n;
            lock (syncRoot)
            {
                head = historyHead;
                n = historyCount;
            }

            double now = clock.Elapsed.TotalSeconds;
            double cutoff = now - windowSeconds;
            for (int i = 0; i < n; i++)
            {
                int idx = (head - 1 - i + HistoryCapacity) % HistoryCapacity;
                Sample s = history[idx];
                if (s.TimeSeconds < cutoff)
                {
                    yield break;
                }

                yield return (s.TimeSeconds, s.TotalMs);
            }
        }

        /// <summary>Snapshot of every tag seen since the last <see cref="Reset"/>.</summary>
        public static List<TagStats> GetAllStats()
        {
            lock (syncRoot)
            {
                DrainThreadTotalsLocked();
                var list = new List<TagStats>(totals.Count);
                foreach (KeyValuePair<string, (long ticks, long calls)> kv in totals)
                {
                    list.Add(new TagStats { Tag = kv.Key, TotalTicks = kv.Value.ticks, Calls = kv.Value.calls });
                }

                return list;
            }
        }

        /// <summary>Clears all accumulated tag totals and graph history —
        /// call when starting a fresh measurement session (e.g. the View
        /// menu toggle turning telemetry back on). Only clears THIS
        /// thread's call stack (see <see cref="threadStack"/>'s own doc) —
        /// any other thread's in-flight Begin/End pairs finish naturally
        /// against the fresh totals, which is fine (a few stale-looking
        /// entries in the first post-reset report at worst).</summary>
        public static void Reset()
        {
            lock (syncRoot)
            {
                foreach (ThreadTotals t in allThreadTotals)
                {
                    lock (t.Gate)
                    {
                        t.Totals.Clear();
                        t.FrameTicks = 0;
                    }
                }

                totals.Clear();
                historyHead = 0;
                historyCount = 0;
                frameTicksAccum = 0;
            }

            Stack.Clear();
        }

        /// <summary>The "when we stop running, spit out a summary" report:
        /// top tags by total self-time, by average self-time-per-call, and
        /// by call count — the three angles the workflow wants for deciding
        /// where to add finer-grained tags next.</summary>
        public static string BuildSummary(int topN = 8)
        {
            List<TagStats> stats = GetAllStats();
            if (stats.Count == 0)
            {
                return "[telemetry] no data collected";
            }

            var byTotal = new List<TagStats>(stats);
            byTotal.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
            var byAvg = new List<TagStats>(stats);
            byAvg.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            var byCalls = new List<TagStats>(stats);
            byCalls.Sort((a, b) => b.Calls.CompareTo(a.Calls));

            var sb = new StringBuilder(512);
            sb.Append("[telemetry] summary — ").Append(stats.Count).Append(" tags\n");
            sb.Append("  top total CPU:\n");
            AppendTop(sb, byTotal, topN, s => $"{s.TotalMs:0.0}ms total, {s.Calls} calls, {s.AvgMs:0.000}ms avg");
            sb.Append("  top average CPU/call:\n");
            AppendTop(sb, byAvg, topN, s => $"{s.AvgMs:0.000}ms avg, {s.Calls} calls, {s.TotalMs:0.0}ms total");
            sb.Append("  most calls:\n");
            AppendTop(sb, byCalls, topN, s => $"{s.Calls} calls, {s.TotalMs:0.0}ms total, {s.AvgMs:0.000}ms avg");
            return sb.ToString();
        }

        private static void AppendTop(StringBuilder sb, List<TagStats> sorted, int topN, Func<TagStats, string> detail)
        {
            int count = Math.Min(topN, sorted.Count);
            for (int i = 0; i < count; i++)
            {
                TagStats s = sorted[i];
                sb.Append("    ").Append(i + 1).Append(". ").Append(s.Tag).Append(" — ").Append(detail(s)).Append('\n');
            }
        }

        public static void PrintSummary(int topN = 8)
        {
            ReportSink?.Invoke(BuildSummary(topN));
        }
    }
}
