namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Run with the program's own Python: parses each file with <c>ast</c> and writes the trees as JSON.</summary>
internal static class PythonAstScript
{
    public const string Source = """
        import ast
        import json
        import math
        import sys


        def dotnet_column(lines, number, offset):
            # ast counts a column in UTF-8 bytes and .NET in UTF-16 characters; the two differ as soon as a line holds
            # anything outside ASCII, and every quote of the code would then be cut in the wrong place.
            if 1 <= number <= len(lines):
                before = lines[number - 1][:offset].decode("utf-8", errors="replace")
                return len(before.encode("utf-16-le")) // 2
            return offset


        def convert(node, lines):
            if isinstance(node, ast.AST):
                result = {"_": type(node).__name__}
                for field, value in ast.iter_fields(node):
                    result[field] = convert(value, lines)
                for position in ("lineno", "col_offset", "end_lineno", "end_col_offset"):
                    value = getattr(node, position, None)
                    if value is not None:
                        result[position] = value
                for line_field, column_field in (("lineno", "col_offset"), ("end_lineno", "end_col_offset")):
                    if line_field in result and column_field in result:
                        result[column_field] = dotnet_column(lines, result[line_field], result[column_field])
                return result
            if isinstance(node, list):
                return [convert(item, lines) for item in node]
            if node is None or isinstance(node, (bool, str)):
                return node
            if isinstance(node, int):
                return node if -2 ** 63 <= node < 2 ** 63 else {"_": "BigInt", "text": str(node)}
            if isinstance(node, float):
                return node if math.isfinite(node) else {"_": "SpecialFloat", "text": repr(node)}
            if isinstance(node, bytes):
                return {"_": "Bytes", "text": node.decode("latin-1")}
            if node is Ellipsis:
                return {"_": "Ellipsis"}
            return {"_": "Unknown", "text": repr(node)}


        def main():
            output, files = sys.argv[1], sys.argv[2:]
            results = []
            for path in files:
                try:
                    with open(path, encoding="utf-8-sig") as handle:
                        source = handle.read()
                    tree = ast.parse(source, filename=path)
                    lines = [line.encode("utf-8") for line in source.split("\n")]
                    results.append({"path": path, "tree": convert(tree, lines)})
                except SyntaxError as error:
                    results.append({"path": path, "problem": "line %s: %s" % (error.lineno, error.msg)})
                except (OSError, ValueError, RecursionError) as error:
                    results.append({"path": path, "problem": str(error)})
            with open(output, "w", encoding="utf-8") as handle:
                json.dump({"files": results}, handle)


        main()
        """;
}
