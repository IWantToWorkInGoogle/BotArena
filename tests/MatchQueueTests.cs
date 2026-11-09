using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BotArena.Tests;

public class MatchQueueTests
{
    [Fact]
    public void CancelledWaiterFreesCapacityAndUpdatesPositions()
    {
        using var queue = new MatchQueue(NullLogger<MatchQueue>.Instance);
        using var cancellation = new CancellationTokenSource();
        var request = new MatchRequest("function tick() { return {}; }", "defensive");
        var cancelled = Assert.IsType<MatchJob>(queue.Enqueue(request, cancellation.Token));
        var next = Assert.IsType<MatchJob>(queue.Enqueue(request));
        while (queue.Enqueue(request) is not null)
        {
        }

        Assert.Equal(2, queue.GetStatus(next.Id)?.Position);
        cancellation.Cancel();
        queue.CancelWaiting(cancelled);

        Assert.True(cancelled.Completion.Task.IsCanceled);
        Assert.Null(queue.GetStatus(cancelled.Id));
        Assert.Equal(1, queue.GetStatus(next.Id)?.Position);
        Assert.NotNull(queue.Enqueue(request));
        Assert.Null(queue.Enqueue(request));
    }
}
