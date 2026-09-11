// A C# file that compiles and then throws, with a real stack trace and line numbers.
//
// KeyNotFoundException is the .NET equivalent of Python's KeyError, and it is thrown from
// inside the framework - so the deepest frames belong to System.Private.CoreLib and the
// culprit has to be picked out from further up.

var settings = new Dictionary<string, string>
{
    ["host"] = "localhost",
};

Console.WriteLine("reading settings");
Console.WriteLine($"host is {Read(settings, "host")}");
Console.WriteLine($"port is {Read(settings, "port")}");

static string Read(Dictionary<string, string> settings, string key)
{
    // The bug: nothing checks whether the key is there.
    return settings[key];
}
