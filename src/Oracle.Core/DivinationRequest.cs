using System;

namespace Oracle.Core;

public sealed record DivinationRequest(
    Guid CorrelationId,
    string ConnectionId,
    string Question,
    DateTimeOffset AskedAt);
