using System;

namespace Oracle.Core;

public abstract record DivinationResult
{
    public sealed record Revealed(Guid CorrelationId, Prophecy Prophecy, TimeSpan CosmicLatency) : DivinationResult;

    public sealed record Rejected(Guid CorrelationId, string Reason) : DivinationResult;
}
