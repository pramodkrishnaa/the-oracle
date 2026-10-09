using System;
using FluentAssertions;
using Oracle.Core;
using Xunit;

namespace Oracle.Core.Tests;

public class OracleEngineTests
{
    [Fact]
    public void Divine_WithSeededRandom_ReturnsDeterministicProphecy()
    {
        const string question = "Will it rain tomorrow?";
        var mood = OracleMood.Balanced;
        var a = new OracleEngine(new Random(42)).Divine(question, mood);
        var b = new OracleEngine(new Random(42)).Divine(question, mood);
        a.Should().Be(b);
    }

    [Fact]
    public void Divine_WithEmptyQuestion_Throws()
    {
        var engine = new OracleEngine();
        Action act = () => engine.Divine(string.Empty, OracleMood.Balanced);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MoodBias_OptimisticYieldsMoreAffirmativeThanGrumpy()
    {
        const string question = "Will this work?";
        const int draws = 1000;
        var seed = 12345;
        var optEngine = new OracleEngine(new Random(seed));
        var grimEngine = new OracleEngine(new Random(seed));
        int optAffirmative = 0;
        int grimAffirmative = 0;
        for (int i = 0; i < draws; i++)
        {
            var opt = optEngine.Divine(question, OracleMood.Optimistic);
            if (opt.Kind == ProphecyKind.Affirmative) optAffirmative++;
            var grim = grimEngine.Divine(question, OracleMood.Grumpy);
            if (grim.Kind == ProphecyKind.Affirmative) grimAffirmative++;
        }
        // Optimistic mood should produce strictly more affirmative prophecies than grumpy mood.
        optAffirmative.Should().BeGreaterThan(grimAffirmative);
    }
}
