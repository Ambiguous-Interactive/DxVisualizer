namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class BudgetedQueueTests
    {
        private static Queue<int> CreateQueue()
        {
            Queue<int> queue = new();
            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(i);
            }
            return queue;
        }

        [TestCase(4000, 1000, 4)]
        [TestCase(4, 10, 1)]
        [TestCase(0, 0, 1)]
        public void ShouldYieldAtElapsedBudgetAndPreservePendingOrder(double budget, double itemMilliseconds, int expected)
        {
            ClockState state = new();
            Queue<int> queue = CreateQueue();
            long itemTicks = (long)(itemMilliseconds * Stopwatch.Frequency / 1000);
            int processed = BudgetedQueue.Process(
                queue,
                state,
                10,
                budget,
                (value, clock) =>
                {
                    clock.Values.Add(value);
                    clock.Ticks += itemTicks;
                },
                static _ => true,
                () => state.Ticks
            );
            Assert.That(processed, Is.EqualTo(expected));
            Assert.That(state.Values.Count, Is.EqualTo(expected));
            Assert.That(queue.Count, Is.EqualTo(10 - expected));
            Assert.That(queue.Peek(), Is.EqualTo(expected));
            for (int i = 0; i < state.Values.Count; i++)
            {
                Assert.That(state.Values[i], Is.EqualTo(i));
            }
        }

        [TestCase(0)]
        [TestCase(3)]
        [TestCase(10)]
        public void ShouldRespectCountCapWhenClockDoesNotAdvance(int maximum)
        {
            ClockState state = new();
            Queue<int> queue = CreateQueue();
            Assert.That(
                BudgetedQueue.Process(queue, state, maximum, 4, static (value, clock) => clock.Values.Add(value), static _ => true, () => state.Ticks),
                Is.EqualTo(maximum)
            );
            if (maximum < 10)
            {
                Assert.That(queue.Peek(), Is.EqualTo(maximum));
            }
            else
            {
                Assert.That(queue.Count, Is.Zero);
            }
        }

        [Test]
        public void ShouldLeaveReplacementWorkQueuedWhenGenerationChangesDuringProcessing()
        {
            Queue<int> queue = CreateQueue();
            ClockState state = new();
            int processed = BudgetedQueue.Process(
                queue,
                state,
                10,
                4,
                (value, clock) =>
                {
                    clock.Values.Add(value);
                    clock.Generation++;
                    queue.Clear();
                    queue.Enqueue(99);
                },
                static clock => clock.Generation == 0,
                () => state.Ticks
            );
            Assert.That(processed, Is.EqualTo(1));
            Assert.That(state.Values, Is.EqualTo(new[] { 0 }));
            Assert.That(queue.ToArray(), Is.EqualTo(new[] { 99 }));
            Assert.That(BudgetedQueue.Process(queue, state, 10, 4, static (_, _) => Assert.Fail("Cancelled work must not run"), static clock => clock.Generation == 0), Is.Zero);
            Assert.That(queue.Peek(), Is.EqualTo(99));
        }

        [Test]
        public void ShouldKeepInFlightItemQueuedWhenProcessingPauses()
        {
            Queue<int> queue = CreateQueue();
            ClockState state = new();
            int attempted = BudgetedQueue.Process(
                queue,
                state,
                10,
                4,
                static (value, clock) =>
                {
                    clock.Values.Add(value);
                    clock.Paused = true;
                },
                static clock => !clock.Paused,
                () => state.Ticks
            );
            Assert.That(attempted, Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(10));
            Assert.That(queue.Peek(), Is.Zero, "Resume retries the item interrupted by suspension");
        }

        [Test]
        public void ShouldRejectInvalidBudgetsWithoutConsumingPendingWork()
        {
            Queue<int> queue = CreateQueue();
            foreach (double budget in new[] { -1d, double.NaN })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => BudgetedQueue.Process(queue, 0, 10, budget, static (_, _) => { }, static _ => true));
            }
            Assert.That(queue.Count, Is.EqualTo(10));
        }

        private sealed class ClockState
        {
            public List<int> Values { get; } = new();
            public long Ticks { get; set; }
            public int Generation { get; set; }
            public bool Paused { get; set; }
        }
    }
}
