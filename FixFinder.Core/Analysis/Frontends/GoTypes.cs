using System.Text.Json;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>What sort of value a Go type holds, as far as nil and zero values go.</summary>
internal enum GoKind { Other, Whole, Real, Text, Truth, Pointer, Interface, Function, Slice, Array, Map, Channel, Struct }

/// <summary>
/// The types a Go package declares, across all its files - its structs, interfaces and other named types - and how a
/// Go type reads in the IR's terms. A pointer is transparent: FixFinder follows whether a value can be nil, not where it lives.
/// </summary>
internal sealed class GoTypes
{
    private readonly Dictionary<string, JsonElement> _named = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceSpan> _declaredAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GoKind> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public void Collect(string file, JsonElement tree)
    {
        foreach (var declaration in Json.List(tree, "Decls"))
        {
            if (Json.Kind(declaration) != "GenDecl" || Json.Text(declaration, "Tok") != "type") continue;

            foreach (var spec in Json.List(declaration, "Specs"))
            {
                if (Json.Prop(spec, "Name") is not { } name || Json.Text(name, "Name") is not { } typeName || Json.Prop(spec, "Type") is not { } type) continue;

                _named[typeName] = type;
                _declaredAt[typeName] = Json.Span(file, spec);
                _files[typeName] = file;
                if (Json.Kind(type) != "StructType") continue;

                foreach (var field in Json.List(Json.Prop(type, "Fields"), "List"))
                {
                    var kind = KindOf(Json.Prop(field, "Type"));
                    foreach (var fieldName in Json.List(field, "Names").Select(n => Json.Text(n, "Name")).OfType<string>())
                        _fields[fieldName] = _fields.TryGetValue(fieldName, out var seen) && seen != kind ? GoKind.Other : kind;
                }
            }
        }
    }

    public bool IsType(string name) => _named.ContainsKey(name);

    public GoKind FieldKind(string name) => _fields.GetValueOrDefault(name, GoKind.Other);

    public GoKind KindOf(JsonElement? type, int depth = 0)
    {
        if (type is not { } t || depth > 12) return GoKind.Other;

        return Json.Kind(t) switch
        {
            "StarExpr" => GoKind.Pointer,
            "ArrayType" => Json.Prop(t, "Len") is null ? GoKind.Slice : GoKind.Array,
            "Ellipsis" => GoKind.Slice,
            "MapType" => GoKind.Map,
            "ChanType" => GoKind.Channel,
            "FuncType" => GoKind.Function,
            "InterfaceType" => GoKind.Interface,
            "StructType" => GoKind.Struct,
            "ParenExpr" => KindOf(Json.Prop(t, "X"), depth + 1),
            "IndexExpr" or "IndexListExpr" => KindOf(Json.Prop(t, "X"), depth + 1),
            "Ident" => Json.Text(t, "Name") switch
            {
                "int" or "int8" or "int16" or "int32" or "int64" or "uint" or "uint8" or "uint16" or "uint32" or "uint64" or "uintptr" or "byte" or "rune" => GoKind.Whole,
                "float32" or "float64" => GoKind.Real,
                "string" => GoKind.Text,
                "bool" => GoKind.Truth,
                "error" or "any" => GoKind.Interface,
                { } name when _named.TryGetValue(name, out var underlying) => KindOf(underlying, depth + 1),
                _ => GoKind.Other,
            },
            _ => GoKind.Other,
        };
    }

    /// <summary>The IR's name for a Go type: the basic types as the evaluator knows them, collections by their shape.</summary>
    public IrType TypeOf(JsonElement? type, int depth = 0)
    {
        if (type is not { } t || depth > 12) return IrType.Unknown;

        switch (Json.Kind(t))
        {
            case "StarExpr":
                return TypeOf(Json.Prop(t, "X"), depth + 1);
            case "ArrayType":
                return IrType.Named(Json.Prop(t, "Len") is null ? "list" : "array", TypeOf(Json.Prop(t, "Elt"), depth + 1));
            case "Ellipsis":
                return IrType.Named("list", TypeOf(Json.Prop(t, "Elt"), depth + 1));
            case "MapType":
                return IrType.Named("dict", TypeOf(Json.Prop(t, "Key"), depth + 1), TypeOf(Json.Prop(t, "Value"), depth + 1));
            case "ChanType":
                return IrType.Named("chan");
            case "FuncType":
                return IrType.Named("func");
            case "ParenExpr" or "IndexExpr" or "IndexListExpr":
                return TypeOf(Json.Prop(t, "X"), depth + 1);
            case "SelectorExpr":
                return Json.Prop(t, "Sel") is { } selected && Json.Text(selected, "Name") is { } imported ? IrType.Named(imported) : IrType.Unknown;
            case "Ident":
                var name = Json.Text(t, "Name") ?? "?";
                return name switch
                {
                    "int" or "int64" => IrType.Named("long"),
                    "int32" or "rune" => IrType.Named("int"),
                    "int16" => IrType.Named("short"),
                    "int8" => IrType.Named("sbyte"),
                    "uint8" or "byte" => IrType.Named("byte"),
                    "uint16" => IrType.Named("ushort"),
                    "uint32" => IrType.Named("uint"),
                    "uint" or "uint64" or "uintptr" => IrType.Named("ulong"),
                    "float32" or "float64" => IrType.Named("double"),
                    "string" => IrType.Named("string"),
                    "bool" => IrType.Named("bool"),
                    _ when _named.TryGetValue(name, out var underlying) && KindOf(underlying) is GoKind.Whole or GoKind.Real or GoKind.Text or GoKind.Truth =>
                        TypeOf(underlying, depth + 1),
                    _ => IrType.Named(name),
                };
            default:
                return IrType.Unknown;
        }
    }

    /// <summary>A class for every struct, and for every other named type with methods, holding its fields and methods.</summary>
    public IReadOnlyList<IrClass> Classes(IReadOnlyList<IrFunction> methods)
    {
        var owners = _named.Where(n => Json.Kind(n.Value) == "StructType").Select(n => n.Key)
            .Concat(methods.Select(m => m.Owner).OfType<string>())
            .Distinct(StringComparer.Ordinal);

        return owners.Select(owner =>
        {
            var fields = new List<IrField>();
            var bases = new List<string>();

            if (_named.TryGetValue(owner, out var type) && Json.Kind(type) == "StructType")
            {
                foreach (var field in Json.List(Json.Prop(type, "Fields"), "List"))
                {
                    var fieldType = TypeOf(Json.Prop(field, "Type"));
                    var names = Json.List(field, "Names").ToList();
                    if (names.Count == 0) bases.Add(fieldType.Name);

                    foreach (var name in names)
                        if (Json.Text(name, "Name") is { } fieldName) fields.Add(new IrField(Json.Span(_files.GetValueOrDefault(owner, ""), name), fieldName, fieldType, null, false));
                }
            }

            var span = methods.FirstOrDefault(m => m.Owner == owner)?.Span ?? _declaredAt.GetValueOrDefault(owner) ?? SourceSpan.None;
            return new IrClass(span, owner, bases, fields, methods.Where(m => m.Owner == owner).ToList());
        }).ToList();
    }
}

/// <summary>Reading the JSON the Go helper writes.</summary>
internal static class Json
{
    public static string? Kind(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("k", out var kind) ? kind.GetString() : null;

    public static JsonElement? Prop(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    public static string? Text(JsonElement? element, string name) => Prop(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static bool Flag(JsonElement? element, string name) => Prop(element, name) is { ValueKind: JsonValueKind.True };

    public static IEnumerable<JsonElement> List(JsonElement? element, string name) =>
        Prop(element, name) is { ValueKind: JsonValueKind.Array } items ? items.EnumerateArray() : [];

    public static SourceSpan Span(string file, JsonElement element)
    {
        var (line, column) = Position(Prop(element, "at"));
        var (endLine, endColumn) = Position(Prop(element, "to"));
        return new SourceSpan(file, line, column, endLine, endColumn);
    }

    private static (int Line, int Column) Position(JsonElement? position) =>
        position is { ValueKind: JsonValueKind.Array } p && p.GetArrayLength() == 2 ? (p[0].GetInt32(), p[1].GetInt32()) : (0, 0);
}
