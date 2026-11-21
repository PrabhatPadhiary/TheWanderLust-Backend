namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Wakes NotificationDigestWorker when a change is queued, so it doesn't
    /// have to poll the database (Neon sleeps when idle and bills while
    /// awake; polling every few seconds would keep it awake all day).
    /// </summary>
    public class NotificationQueueSignal
    {
        private readonly SemaphoreSlim _signal = new(0, 1);

        /// <summary>Something was queued: have a look.</summary>
        public void Wake()
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { /* already awake */ }
        }

        /// <summary>Wait until woken or the timeout passes (true when woken).</summary>
        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
    }
}
