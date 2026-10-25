namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>Consumes an ordered queue cooperatively, with a count cap and a monotonic elapsed-time budget.</summary>
    public static class BudgetedQueue
    {
        private static readonly Func<long> ReadTimestamp = Stopwatch.GetTimestamp;

        /// <summary>
        /// Returns attempted items. The clock uses Stopwatch ticks; indivisible work may exceed the budget.
        /// Cancellation preserves the current queue head, or replacement work supplied by a new generation.
        /// </summary>
        public static int Process<T, TState>(
            Queue<T> pending,
            TState state,
            int maximumCount,
            double budgetMilliseconds,
            Action<T, TState> process,
            Func<TState, bool> canContinue,
            Func<long> timestamp = null
        )
        {
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }
            if (canContinue == null)
            {
                throw new ArgumentNullException(nameof(canContinue));
            }
            if (maximumCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            }
            if (double.IsNaN(budgetMilliseconds) || budgetMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
            }
            timestamp ??= ReadTimestamp;
            long started = timestamp();
            int processed = 0;
            while (
                processed < maximumCount
                && 0 < pending.Count
                && canContinue(state)
                && (processed == 0 || (timestamp() - started) * 1000d / Stopwatch.Frequency < budgetMilliseconds)
            )
            {
                process(pending.Peek(), state);
                processed++;
                if (!canContinue(state))
                {
                    break;
                }
                pending.Dequeue();
            }
            return processed;
        }
    }
}
