string?[] names = { "Ada", "Bob", null };
foreach (var name in names)
{
    Console.WriteLine(name!.ToUpper());
}
