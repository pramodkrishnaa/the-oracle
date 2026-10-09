using Microsoft.EntityFrameworkCore;
using Oracle.Core;

namespace Oracle.Infrastructure;

/// <summary>
/// EF Core DbContext for persisting divination requests and their outcomes.
/// </summary>
public sealed class OracleDbContext : DbContext
{
    public OracleDbContext(DbContextOptions<OracleDbContext> options) : base(options) { }

    public DbSet<ProphecyLogEntry> ProphecyLogEntries => Set<ProphecyLogEntry>();
}

/// <summary>
/// Represents a single divination record stored in the database.
/// </summary>
public sealed class ProphecyLogEntry
{
    public int Id { get; set; }
    public Guid CorrelationId { get; init; }
    public string ConnectionId { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public ProphecyKind Kind { get; init; }
    public DateTimeOffset AskedAt { get; init; }
    public TimeSpan Latency { get; init; }
}
