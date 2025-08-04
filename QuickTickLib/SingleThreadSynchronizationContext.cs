using System.Collections.Concurrent;

public class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
    private readonly Thread workerThread;
    private readonly bool skipMissedIntervals;
    private volatile bool running = true;

    /// <summary>
    /// Number of ticks that were discarded because a new one was already queued.
    /// </summary>
    public long SkippedIntervalCount { get; private set; }

    /// <summary>
    /// Create a single-threaded synchronization context.
    /// </summary>
    /// <param name="skipMissedTicks">If true, only the most recent tick is processed when the queue backs up.</param>
    /// <param name="priority">The priority of the dedicated worker thread.</param>
    public SingleThreadSynchronizationContext(bool skipMissedTicks = false, ThreadPriority priority = ThreadPriority.Normal)
    {
        this.skipMissedIntervals = skipMissedTicks;

        workerThread = new Thread(Run)
        {
            IsBackground = true,
            Name = "QuickTickTimerContext",
            Priority = priority
        };

        workerThread.Start();
    }

    private void Run()
    {
        SetSynchronizationContext(this);

        while (running)
        {
            try
            {
                // Wait for at least one callback
                if (!queue.TryTake(out var workItem, Timeout.Infinite))
                {
                    continue;
                }                

                // If skipping is enabled, drain queue and only keep the latest
                if (skipMissedIntervals && queue.Count > 0)
                {
                    while (queue.TryTake(out var nextItem))
                    {
                        workItem = nextItem;
                        SkippedIntervalCount++;
                    }
                }

                var (callback, state) = workItem;
                callback(state);
            }
            catch (InvalidOperationException)
            {
                // Queue completed
                break;
            }
        }
    }

    /// <summary>
    /// Posts work to be executed on the dedicated thread.
    /// </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        if (running && !queue.IsAddingCompleted)
        {
            try 
            { 
                queue.Add((d, state)); 
            }
            catch (InvalidOperationException) 
            {
                // Queue completed
            }
        }
    }

    /// <summary>
    /// Dispose and stop the worker thread.
    /// </summary>
    public void Dispose()
    {
        running = false;
        queue.CompleteAdding();
    }
}
