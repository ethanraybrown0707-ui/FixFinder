// A C# file that does not compile.
//
// CS0103 is about as searchable as an error gets, and the .NET SDK builds and runs a loose .cs
// file in one command, so FixFinder sees the diagnostic without any project being set up.

var items = new[] { "one", "two", "three" };

Console.WriteLine("counting items");

// The bug: "total" was never declared.
foreach (var item in items)
{
    total += item.Length;
}

Console.WriteLine($"total length is {total}");
