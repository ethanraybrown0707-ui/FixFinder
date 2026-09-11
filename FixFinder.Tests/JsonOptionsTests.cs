using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Tests;

/// <summary>
/// Placeholder coverage that keeps the test project honest from M0 onwards: it proves the
/// Core reference resolves and that the shared serializer options are actually shared.
/// The real suites - parsers, fingerprinting, diff parsing, patch application - land in M2.
/// </summary>
public class JsonOptionsTests
{
    [Fact]
    public void Default_IsASingleSharedInstance()
    {
        Assert.Same(JsonOptions.Default, JsonOptions.Default);
    }

    [Fact]
    public void Default_WritesIndentedJson()
    {
        var json = JsonSerializer.Serialize(new { name = "FixFinder", milestone = 0 }, JsonOptions.Default);

        Assert.Contains("\n", json);
    }
}
