namespace BotArena;

internal sealed record MatchJobStatus(
    string State, int? Position = null, MatchResponse? Match = null, string? Error = null);

internal sealed class MatchJob(MatchRequest request, CancellationToken cancellation)
{
    public Guid Id { get; } = Guid.NewGuid();
    public MatchRequest Request { get; } = request;
    public CancellationToken Cancellation { get; } = cancellation;
    public TaskCompletionSource<MatchWorkerResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class MatchQueue(ILogger<MatchQueue> logger) : BackgroundService
{
    private const int Capacity = 16;
    private const int RetainedResults = 32;
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromMinutes(5);
    private readonly object gate = new();
    private readonly LinkedList<MatchJob> waiting = new();
    private readonly SemaphoreSlim available = new(0);
    private readonly Queue<(Guid Id, DateTimeOffset Expires)> completed = new();
    private readonly Dictionary<Guid, MatchWorkerResult> results = new();
    private MatchJob? running;

    public MatchJob? Enqueue(MatchRequest request, CancellationToken cancellation = default)
    {
        lock (gate)
        {
            if (waiting.Count >= Capacity) return null;
            var job = new MatchJob(request, cancellation);
            waiting.AddLast(job);
            available.Release();
            return job;
        }
    }

    public void CancelWaiting(MatchJob job)
    {
        lock (gate)
        {
            if (!waiting.Remove(job)) return;
            // The worker may have consumed this wakeup before it acquired the lock.
            available.Wait(0);
            job.Completion.TrySetCanceled(job.Cancellation);
        }
    }

    public MatchJobStatus? GetStatus(Guid id)
    {
        lock (gate)
        {
            TrimResults(DateTimeOffset.UtcNow);
            if (running?.Id == id) return new("running");
            var position = 0;
            foreach (var job in waiting)
            {
                if (job.Cancellation.IsCancellationRequested) continue;
                position++;
                if (job.Id == id) return new("queued", position);
            }

            return results.TryGetValue(id, out var result)
                ? new("completed", Match: result.Response, Error: result.Error)
                : null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await available.WaitAsync(stoppingToken);
            MatchJob job;
            lock (gate)
            {
                if (waiting.First is not { } next) continue;
                job = next.Value;
                waiting.RemoveFirst();
                running = job;
            }

            MatchWorkerResult result;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                result = await MatchWorker.RunAsync(job.Request, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                lock (gate) running = null;
                job.Completion.TrySetCanceled(cancellation.Token);
                continue;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Failed to run match job {JobId}", job.Id);
                result = new(null, "Не удалось выполнить расчёт матча.");
            }

            lock (gate)
            {
                var now = DateTimeOffset.UtcNow;
                results.Add(job.Id, result);
                completed.Enqueue((job.Id, now + ResultLifetime));
                TrimResults(now);
                running = null;
            }
            job.Completion.TrySetResult(result);
        }
    }

    private void TrimResults(DateTimeOffset now)
    {
        while (completed.TryPeek(out var entry) && (completed.Count > RetainedResults || entry.Expires <= now))
        {
            completed.Dequeue();
            results.Remove(entry.Id);
        }
    }
}
