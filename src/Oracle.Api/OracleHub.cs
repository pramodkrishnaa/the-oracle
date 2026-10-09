using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Oracle.Core;

namespace Oracle.Api;

/// <summary>
/// The hub does no divination -- it validates, rate-limits, and enqueues. The
/// <see cref="DivinationWorker"/> does the real work and pushes the answer back via
/// "ProphecyRevealed". Decoupling ingest from processing through the channel is the point.
/// </summary>
public sealed class OracleHub : Hub
{
    private const int MaxAsksPerWindow = 5;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> AskTimestamps = new();

    private readonly DivinationChannel _channel;

    public OracleHub(DivinationChannel channel) => _channel = channel;

    public async Task Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 280)
        {
            await Clients.Caller.SendAsync("Rejected", new { reason = "The cosmos requires an actual question, no more than 280 characters." });
            return;
        }

        if (IsRateLimited(Context.ConnectionId))
        {
            await Clients.Caller.SendAsync("Rejected", new { reason = "The cosmos can only be consulted so often." });
            return;
        }

        var request = new DivinationRequest(Guid.NewGuid(), Context.ConnectionId, question, DateTimeOffset.UtcNow);
        if (!_channel.Writer.TryWrite(request))
        {
            await Clients.Caller.SendAsync("Rejected", new { reason = "The cosmos is busy. Try again in a moment." });
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        AskTimestamps.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    // ponytail: a dictionary + lock does the job; reach for IHubFilter only if a second hub needs the same policy.
    private static bool IsRateLimited(string connectionId)
    {
        var timestamps = AskTimestamps.GetOrAdd(connectionId, _ => new Queue<DateTimeOffset>());
        lock (timestamps)
        {
            var now = DateTimeOffset.UtcNow;
            while (timestamps.Count > 0 && now - timestamps.Peek() > Window)
                timestamps.Dequeue();

            if (timestamps.Count >= MaxAsksPerWindow)
                return true;

            timestamps.Enqueue(now);
            return false;
        }
    }
}
