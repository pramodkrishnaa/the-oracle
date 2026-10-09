using System.Collections.Immutable;
using FluentAssertions;
using Oracle.Core;
using Xunit;

namespace Oracle.Generators.Tests;

public class ProphecyCatalogGeneratorTests
{
    [Fact]
    public void GeneratedCatalog_ContainsAll20Prophecies_AndAllKinds()
    {
        // The source generator has already run at compile time, so ProphecyCatalog.All should be populated.
        var all = ProphecyCatalog.All;
        all.Should().HaveCount(20, "the original prophecies.txt defines exactly 20 entries");
        var kinds = all.Select(p => p.Kind).Distinct().ToImmutableArray();
        kinds.Should().Contain(ProphecyKind.Affirmative);
        kinds.Should().Contain(ProphecyKind.NonCommittal);
        kinds.Should().Contain(ProphecyKind.Negative);
    }
}
