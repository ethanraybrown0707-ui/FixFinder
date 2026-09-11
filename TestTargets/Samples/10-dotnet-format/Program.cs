// A crash in a different language, so the same window can be seen doing the same job on .NET.
//
// FormatException from int.Parse is the .NET equivalent of the everyday Python mistakes above:
// enormously common, thoroughly written about, and thrown from inside the framework rather than
// from this file - so the deepest frames belong to System.Private.CoreLib and the culprit has to
// be picked out from further up.
//
// Built with a portable PDB kept beside the assembly, which is how FixFinder locates the source
// without being told where it is.

var settings = new Dictionary<string, string>
{
    ["retries"] = "3",
    ["timeout"] = "30s",        // the bug: a unit crept into a field meant to hold a number
};

Console.WriteLine("reading settings...");

Console.WriteLine($"retries: {ReadNumber(settings, "retries")}");
Console.WriteLine($"timeout: {ReadNumber(settings, "timeout")}");

static int ReadNumber(Dictionary<string, string> settings, string key)
{
    var raw = settings[key];
    return int.Parse(raw);
}
