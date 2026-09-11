using System.Text.Json;

namespace FixFinder.Core.Engine;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used everywhere FixFinder reads or
/// writes JSON - the HTTP response cache, the backup manifest, the test fixtures and the
/// eventual CLI report.
/// </summary>
/// <remarks>
/// One shared static instance rather than a <c>new()</c> per call site, because
/// <see cref="JsonSerializerOptions"/> caches its reflection metadata internally: constructing a
/// fresh one per serialization re-does that work every time and is a well-known way to make
/// JSON handling quietly slow.
/// <para>
/// Indented on purpose. Every file FixFinder writes is something you may need to read while
/// working out what the tool did - a cached API response, or the manifest naming the files it
/// backed up before patching - and none of them is large enough for the extra bytes to matter.
/// </para>
/// </remarks>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new() { WriteIndented = true };
}
