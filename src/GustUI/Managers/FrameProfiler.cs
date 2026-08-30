using System;
using System.Diagnostics;
using System.Text;

namespace GustUI.Managers
{
    /// <summary>
    /// Lightweight always-compiled frame profiler. Disabled it costs one
    /// branch per probe; enabled it accumulates Stopwatch ticks into fixed
    /// buckets plus a few per-frame counters (sprite draws, batch flushes,
    /// managed allocation delta, GC collection counts) and emits a one-line
    /// report through <see cref="ReportSink"/> every
    /// <see cref="ReportIntervalSeconds"/>. The host game calls
    /// <see cref="Begin"/>/<see cref="End"/> around its own phases and
    /// <see cref="FrameEnd"/> once per presented frame; GustUI's own managers
    /// probe their internals with the same API.
    /// </summary>
    public static class FrameProfiler
    {
        public enum Bucket
        {
            UpdateTotal = 0,
            UpdateInput = 1,
            UpdateTree = 2,
            UpdateApp = 3,
            DrawTotal = 4,
            DrawFontCache = 5,
            DrawRoot = 6,
            DrawDebug = 7,
        }

        private const int BucketCount = 8;

        private static readonly string[] BucketNames =
        {
            "up.total", "up.input", "up.tree", "up.app",
            "dr.total", "dr.fontcache", "dr.root", "dr.debug",
        };

        public static bool Enabled;

        /// <summary>Where reports go; defaults to Console.WriteLine (visible in the browser console under WASM).</summary>
        public static Action<string> ReportSink = s => Console.WriteLine(s);

        public static double ReportIntervalSeconds = 5.0;

        private static readonly long[] starts = new long[BucketCount];
        private static readonly long[] frameTicks = new long[BucketCount];
        private static readonly long[] sumTicks = new long[BucketCount];
        private static readonly double[] lastReportMs = new double[BucketCount];

        // per-frame counters
        private static int batchFlushes;

        /// <summary>DrawIndexedPrimitives calls -- one per GeometryBatch
        /// segment. Replaced the sprite/string draw counters (2026-08-30),
        /// which had been reporting 0 since SpriteBatch was retired. This is
        /// the number that says how well the frame batched: flushes tell you
        /// how often the batch was forced out, segments how many draw calls
        /// it took when it went.</summary>
        private static int segments;
        private static int elementsDrawn;
        private static int elementsUpdated;

        private static long sumSegments, sumFlushes, sumElementsDrawn, sumElementsUpdated;

        private static int frames;
        private static long worstFrameTicks;
        private static long intervalStartTicks;
        private static long lastFrameEndTicks;
        private static long allocAtFrameStart;
        private static long sumAllocBytes;
        private static int gc0Start, gc1Start, gc2Start;
        private static bool intervalOpen;

        /// <summary>Last full report line, for surfacing in a UI overlay if wanted.</summary>
        public static string LastReport { get; private set; } = "";

        public static void Begin(Bucket bucket)
        {
            if (Enabled)
            {
                starts[(int)bucket] = Stopwatch.GetTimestamp();
            }
        }

        public static void End(Bucket bucket)
        {
            if (Enabled)
            {
                frameTicks[(int)bucket] += Stopwatch.GetTimestamp() - starts[(int)bucket];
            }
        }

        public static void CountFlush() { if (Enabled) { batchFlushes++; } }

        public static void CountSegments(int n) { if (Enabled) { segments += n; } }
        public static void CountElementDraw() { if (Enabled) { elementsDrawn++; } }
        public static void CountElementUpdate() { if (Enabled) { elementsUpdated++; } }

        /// <summary>Call once per presented frame, after Draw.</summary>
        public static void FrameEnd()
        {
            if (!Enabled)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();

            if (!intervalOpen)
            {
                intervalOpen = true;
                intervalStartTicks = now;
                lastFrameEndTicks = now;
                gc0Start = GC.CollectionCount(0);
                gc1Start = GC.CollectionCount(1);
                gc2Start = GC.CollectionCount(2);
                allocAtFrameStart = SafeAllocatedBytes();
                ResetFrame();
                return;
            }

            frames++;

            long frameCost = frameTicks[(int)Bucket.UpdateTotal] + frameTicks[(int)Bucket.DrawTotal];
            if (frameCost > worstFrameTicks)
            {
                worstFrameTicks = frameCost;
            }

            for (int i = 0; i < BucketCount; i++)
            {
                sumTicks[i] += frameTicks[i];
            }

            sumSegments += segments;
            sumFlushes += batchFlushes;
            sumElementsDrawn += elementsDrawn;
            sumElementsUpdated += elementsUpdated;

            long allocNow = SafeAllocatedBytes();
            if (allocNow > allocAtFrameStart)
            {
                sumAllocBytes += allocNow - allocAtFrameStart;
            }

            allocAtFrameStart = allocNow;
            lastFrameEndTicks = now;
            ResetFrame();

            double intervalMs = TicksToMs(now - intervalStartTicks);
            if (intervalMs >= ReportIntervalSeconds * 1000.0 && frames > 0)
            {
                Report(intervalMs);
                frames = 0;
                worstFrameTicks = 0;
                Array.Clear(sumTicks, 0, BucketCount);
                sumSegments = sumFlushes = sumElementsDrawn = sumElementsUpdated = 0;
                sumAllocBytes = 0;
                intervalStartTicks = now;
                gc0Start = GC.CollectionCount(0);
                gc1Start = GC.CollectionCount(1);
                gc2Start = GC.CollectionCount(2);
            }
        }

        private static void ResetFrame()
        {
            Array.Clear(frameTicks, 0, BucketCount);
            segments = 0;
            batchFlushes = 0;
            elementsDrawn = 0;
            elementsUpdated = 0;
        }

        private static void Report(double intervalMs)
        {
            double f = frames;
            var sb = new StringBuilder(256);
            sb.Append("[perf] fps ").Append((frames * 1000.0 / intervalMs).ToString("0.0"));
            sb.Append(" | ms/f:");
            for (int i = 0; i < BucketCount; i++)
            {
                double avg = TicksToMs(sumTicks[i]) / f;
                lastReportMs[i] = avg;
                sb.Append(' ').Append(BucketNames[i]).Append('=').Append(avg.ToString("0.00"));
            }

            sb.Append(" | worst ").Append(TicksToMs(worstFrameTicks).ToString("0.0"));
            sb.Append(" | segs ").Append((sumSegments / f).ToString("0"));
            sb.Append(" flushes ").Append((sumFlushes / f).ToString("0"));
            sb.Append(" | elems d").Append((sumElementsDrawn / f).ToString("0"));
            sb.Append("/u").Append((sumElementsUpdated / f).ToString("0"));
            sb.Append(" | alloc ").Append((sumAllocBytes / f / 1024.0).ToString("0.0")).Append(" KB/f");
            sb.Append(" | GC ").Append(GC.CollectionCount(0) - gc0Start);
            sb.Append('/').Append(GC.CollectionCount(1) - gc1Start);
            sb.Append('/').Append(GC.CollectionCount(2) - gc2Start);

            LastReport = sb.ToString();
            ReportSink?.Invoke(LastReport);
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static long SafeAllocatedBytes()
        {
            try
            {
                return GC.GetAllocatedBytesForCurrentThread();
            }
            catch
            {
                return 0;
            }
        }
    }
}
