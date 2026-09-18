using System.Text.Json;

namespace FixFinder.Core.Engine;

/// <summary>The one <c>JsonSerializerOptions</c> used everywhere FixFinder reads or writes JSON.</summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new() { WriteIndented = true };
}
