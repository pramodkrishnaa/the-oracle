using Oracle.Core;

namespace Oracle.Api;

/// <summary>
/// Binds the "Oracle" section of configuration. The mood/latency knobs are the deliberate
/// "hardware calibration" surface for the demo -- tunable without a redeploy via env vars
/// (e.g. Oracle__Mood=Grumpy), never client-supplied.
/// </summary>
public sealed class OracleOptions
{
    public string Mood { get; set; } = "Balanced";
    public int CosmicLatencyMinMs { get; set; } = 600;
    public int CosmicLatencyMaxMs { get; set; } = 1800;

    public OracleMood ResolveMood() => Mood switch
    {
        "Optimistic" => OracleMood.Optimistic,
        "Grumpy" => OracleMood.Grumpy,
        _ => OracleMood.Balanced
    };
}
