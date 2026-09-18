using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>The .NET types a C# beginner reaches for, so their real members can be read by reflection.</summary>
/// <remarks>
/// FixFinder is itself a .NET program, so what <c>Console</c>, <c>string</c> or <c>List&lt;T&gt;</c>
/// really has is not a table somebody wrote - it is asked of the runtime. That is the C# counterpart
/// of reading a JDK class with javap.
/// </remarks>
internal static class CSharpTypes
{
    private static readonly Dictionary<string, Type> Known = new(StringComparer.Ordinal)
    {
        ["string"] = typeof(string), ["String"] = typeof(string), ["int"] = typeof(int), ["Int32"] = typeof(int),
        ["long"] = typeof(long), ["double"] = typeof(double), ["Double"] = typeof(double), ["float"] = typeof(float),
        ["decimal"] = typeof(decimal), ["bool"] = typeof(bool), ["Boolean"] = typeof(bool), ["char"] = typeof(char),
        ["Char"] = typeof(char), ["object"] = typeof(object), ["Object"] = typeof(object),
        ["List"] = typeof(List<>), ["Dictionary"] = typeof(Dictionary<,>), ["HashSet"] = typeof(HashSet<>),
        ["Queue"] = typeof(Queue<>), ["Stack"] = typeof(Stack<>), ["LinkedList"] = typeof(LinkedList<>),
        ["SortedDictionary"] = typeof(SortedDictionary<,>), ["Console"] = typeof(Console), ["Math"] = typeof(Math),
        ["DateTime"] = typeof(DateTime), ["TimeSpan"] = typeof(TimeSpan), ["Convert"] = typeof(Convert),
        ["Array"] = typeof(Array), ["StringBuilder"] = typeof(System.Text.StringBuilder), ["File"] = typeof(File),
        ["Path"] = typeof(Path), ["Directory"] = typeof(Directory), ["Random"] = typeof(Random), ["Guid"] = typeof(Guid),
        ["Environment"] = typeof(Environment), ["Task"] = typeof(Task), ["Thread"] = typeof(Thread),
        ["Regex"] = typeof(Regex), ["Enumerable"] = typeof(Enumerable), ["Enum"] = typeof(Enum),
    };

    public static IEnumerable<string> Names => Known.Keys;

    public static Type? Resolve(string name)
    {
        var bare = name.Trim();
        if (bare.EndsWith("[]", StringComparison.Ordinal)) return typeof(Array);

        var generic = bare.IndexOf('<');
        if (generic >= 0) bare = bare[..generic];

        var dot = bare.LastIndexOf('.');
        if (dot >= 0) bare = bare[(dot + 1)..];

        return Known.GetValueOrDefault(bare);
    }

    public static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(Array) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>Public member names, and whether each is called with brackets.</summary>
    public static Dictionary<string, bool> Members(Type type, bool isStatic)
    {
        var members = new Dictionary<string, bool>(StringComparer.Ordinal);
        var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        foreach (var member in type.GetMembers(flags))
        {
            switch (member)
            {
                case MethodInfo { IsSpecialName: true }:
                    continue;
                case MethodInfo:
                    members.TryAdd(member.Name, true);
                    break;
                case PropertyInfo or FieldInfo:
                    members[member.Name] = false;
                    break;
            }
        }

        // An instance collection also has everything LINQ adds to it, which implicit usings bring in.
        if (!isStatic && IsCollection(type))
            foreach (var method in typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static))
                members.TryAdd(method.Name, true);

        return members;
    }
}
