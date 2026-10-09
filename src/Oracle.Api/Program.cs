using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Oracle.Api;
using Oracle.Api.Observability;
using Oracle.Core;
using Oracle.Infrastructure;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services.Configure<OracleOptions>(builder.Configuration.GetSection("Oracle"));

builder.Services.AddSingleton<DivinationChannel>();
builder.Services.AddSingleton<IOracleEngine, OracleEngine>();
builder.Services.AddHostedService<DivinationWorker>();

builder.Services.AddDbContext<OracleDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("OracleDb") ?? "Data Source=oracle.db"));

builder.Services.AddSignalR();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OracleDbContext>("db");

// Fixed-window: 5 asks / 10s per connection/IP (plan §10).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("global", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromSeconds(10);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
});

builder.Services.AddOpenTelemetry()
    .WithTracing(trace => trace
        .AddSource(OracleTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(OracleTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

// Render assigns the listen port via $PORT at runtime; it isn't known at build time.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// Zero manual DB setup on a clean clone.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OracleDbContext>();
    await db.Database.MigrateAsync();
}

// ---------- Middleware ----------
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapHub<OracleHub>("/hub/oracle");

app.MapGet("/api/v1/mood", (IOptionsMonitor<OracleOptions> options) =>
    Results.Ok(new { mood = options.CurrentValue.Mood }));

app.MapGet("/api/v1/history", async (OracleDbContext db, int take) =>
{
    var entries = await db.ProphecyLogEntries
        .OrderByDescending(e => e.Id)
        .Take(Math.Clamp(take <= 0 ? 20 : take, 1, 100))
        .Select(e => new { e.Question, e.Answer, Kind = e.Kind.ToString(), e.AskedAt })
        .ToListAsync();
    return Results.Ok(entries);
});

// REST fallback: exercises the same engine/mood/cosmic-latency logic as the hub, but computes
// synchronously since there's no SignalR connection to push a result back to later.
app.MapPost("/api/v1/ask", async (
    AskRequest body,
    IOracleEngine engine,
    IOptionsMonitor<OracleOptions> options,
    OracleDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Question) || body.Question.Length > 280)
        return Results.BadRequest(new { error = "The cosmos requires an actual question, no more than 280 characters." });

    var current = options.CurrentValue;
    var mood = current.ResolveMood();
    var prophecy = engine.Divine(body.Question, mood);

    var latencyMs = Random.Shared.Next(current.CosmicLatencyMinMs, current.CosmicLatencyMaxMs);
    using var activity = OracleTelemetry.ActivitySource.StartActivity("oracle.divine");
    await Task.Delay(latencyMs, ct);

    db.ProphecyLogEntries.Add(new ProphecyLogEntry
    {
        CorrelationId = Guid.NewGuid(),
        ConnectionId = string.Empty,
        Question = body.Question,
        Answer = prophecy.Text,
        Kind = prophecy.Kind,
        AskedAt = DateTimeOffset.UtcNow,
        Latency = TimeSpan.FromMilliseconds(latencyMs)
    });
    await db.SaveChangesAsync(ct);

    OracleTelemetry.PropheciesRevealed.Add(1, new KeyValuePair<string, object?>("kind", prophecy.Kind.ToString()));
    OracleTelemetry.CosmicLatency.Record(latencyMs);

    return Results.Accepted(value: new { prophecy.Text, Kind = prophecy.Kind.ToString(), cosmicLatencyMs = latencyMs });
}).RequireRateLimiting("global");

app.Run();

internal sealed record AskRequest(string Question);
