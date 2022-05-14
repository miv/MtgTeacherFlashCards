namespace MtgTeacher.Cli;


// from stackoverflow
class AsyncRateLimitedSemaphore
{
    private readonly int maxCount;
    private readonly TimeSpan resetTimeSpan;

    private readonly SemaphoreSlim semaphore;
    private long nextResetTimeTicks;

    private readonly object resetTimeLock = new();

    public AsyncRateLimitedSemaphore(int maxCount, TimeSpan resetTimeSpan)
    {
        this.maxCount = maxCount;
        this.resetTimeSpan = resetTimeSpan;

        this.semaphore = new SemaphoreSlim(maxCount, maxCount);
        this.nextResetTimeTicks = (DateTimeOffset.UtcNow + this.resetTimeSpan).UtcTicks;
    }

    private void TryResetSemaphore()
    {
        // quick exit if before the reset time, no need to lock
        if (!(DateTimeOffset.UtcNow.UtcTicks > Interlocked.Read(ref this.nextResetTimeTicks)))
        {
            return;
        }

        // take a lock so only one reset can happen per period
        lock (this.resetTimeLock)
        {
            var currentTime = DateTimeOffset.UtcNow;
            // need to check again in case a reset has already happened in this period
            if (currentTime.UtcTicks > Interlocked.Read(ref this.nextResetTimeTicks))
            {
                var releaseCount = this.maxCount - this.semaphore.CurrentCount;
                if (releaseCount > 0)
                {
                    this.semaphore.Release(releaseCount);
                    var newResetTimeTicks = (currentTime + this.resetTimeSpan).UtcTicks;
                    Interlocked.Exchange(ref this.nextResetTimeTicks, newResetTimeTicks);
                }
            }
        }
    }

    public async Task WaitAsync()
    {
        // attempt a reset in case it's been some time since the last wait
        TryResetSemaphore();

        var semaphoreTask = this.semaphore.WaitAsync();

        // if there are no slots, need to keep trying to reset until one opens up
        while (!semaphoreTask.IsCompleted)
        {
            var ticks = Interlocked.Read(ref this.nextResetTimeTicks);
            var nextResetTime = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            var delayTime = nextResetTime - DateTimeOffset.UtcNow;

            // delay until the next reset period
            // can't delay a negative time so if it's already passed just continue with a completed task
            var delayTask = delayTime >= TimeSpan.Zero ? Task.Delay(delayTime) : Task.CompletedTask;

            await Task.WhenAny(semaphoreTask, delayTask);

            TryResetSemaphore();
        }
    }
}