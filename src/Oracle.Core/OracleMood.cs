namespace Oracle.Core;

public sealed record OracleMood(double AffirmativeWeight, double NonCommittalWeight, double NegativeWeight)
{
    public static readonly OracleMood Balanced = new(1, 1, 1);
    public static readonly OracleMood Optimistic = new(3, 1, 1);
    public static readonly OracleMood Grumpy = new(1, 1, 3);
}
