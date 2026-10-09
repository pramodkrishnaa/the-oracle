using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Oracle.Core;

public sealed class OracleEngine : IOracleEngine
{
    private readonly Random _random;

    public OracleEngine() : this(Random.Shared) { }

    public OracleEngine(Random random) => _random = random ?? throw new ArgumentNullException(nameof(random));

    public Prophecy Divine(string question, OracleMood mood)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question cannot be empty.", nameof(question));

        // Get the full catalog (generated or fallback static implementation)
        var all = ProphecyCatalog.All;

        // Build a weighted list based on the mood
        var weighted = new List<Prophecy>();
        foreach (var p in all)
        {
            int weight = p.Kind switch
            {
                ProphecyKind.Affirmative => (int)Math.Max(1, mood.AffirmativeWeight),
                ProphecyKind.NonCommittal => (int)Math.Max(1, mood.NonCommittalWeight),
                ProphecyKind.Negative => (int)Math.Max(1, mood.NegativeWeight),
                _ => 1
            };
            for (int i = 0; i < weight; i++)
                weighted.Add(p);
        }

        // Pick a random prophecy from the weighted list
        var chosen = weighted[_random.Next(weighted.Count)];
        return chosen;
    }
}
