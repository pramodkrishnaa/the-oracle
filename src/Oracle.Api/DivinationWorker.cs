using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oracle.Api.Observability;
using Oracle.Core;
using Oracle.Infrastructure;

namespace Oracle.Api;

/// <summary>
/// Singleton BackgroundService that reads requests off the channel, applies mood + artificial
/// "cosmic latency" from config, persists the result, and pushes it back over SignalR.
/// OracleDbContext is scoped, so a fresh scope is resolved per message via IServiceScopeFactory --
/// the classic captive-dependency trap, handled correctly here.
/// </summary>
public sealed class DivinationWorker : BackgroundService
{
    private readonly DivinationChannel _channel;
    private readonly IOracleEngine _engine;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<OracleHub> _hubContext;
    private readonly IOptionsMonitor<OracleOptions> _options;

    public DivinationWorker(
        DivinationChannel channel,
        IOracleEngine engine,
        IServiceScopeFactory scopeFactory,
        IHubContext<OracleHub> hubContext,
        IOptionsMonitor<OracleOptions> options)
    {
        _channel = channel;
        _engine = engine;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            using var activity = OracleTelemetry.ActivitySource.StartActivity("oracle.divine");

            var options = _options.CurrentValue;
            var mood = options.ResolveMood();
            var prophecy = _engine.Divine(request.Question, mood);

            var latencyMs = Random.Shared.Next(options.CosmicLatencyMinMs, options.CosmicLatencyMaxMs);
            await Task.Delay(latencyMs, stoppingToken);
            var cosmicLatency = TimeSpan.FromMilliseconds(latencyMs);

            activity?.SetTag("question.length", request.Question.Length);
            activity?.SetTag("prophecy.kind", prophecy.Kind.ToString());
            activity?.SetTag("cosmic.latency_ms", latencyMs);

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OracleDbContext>();
                db.ProphecyLogEntries.Add(new ProphecyLogEntry
                {
                    CorrelationId = request.CorrelationId,
                    ConnectionId = request.ConnectionId,
                    Question = request.Question,
                    Answer = prophecy.Text,
                    Kind = prophecy.Kind,
                    AskedAt = request.AskedAt,
                    Latency = cosmicLatency
                });
                await db.SaveChangesAsync(stoppingToken);
            }

            await _hubContext.Clients.Client(request.ConnectionId).SendAsync("ProphecyRevealed", new
            {
                correlationId = request.CorrelationId,
                text = prophecy.Text,
                kind = prophecy.Kind.ToString(),
                cosmicLatencyMs = latencyMs
            }, stoppingToken);

            OracleTelemetry.PropheciesRevealed.Add(1, new KeyValuePair<string, object?>("kind", prophecy.Kind.ToString()));
            OracleTelemetry.CosmicLatency.Record(latencyMs);
        }
    }
}
