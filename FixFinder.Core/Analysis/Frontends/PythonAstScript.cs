namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Run with the program's own Python: parses each file with <c>ast</c> and writes the trees as JSON.</summary>
internal static class PythonAstScript
{
    public const string Source = """
        import ast
        import json
        import math
        import sys


        def convert(node):
            if isinstance(node, ast.AST):
                result = {"_": type(node).__name__}
                for field, value in ast.iter_fields(node):
                    result[field] = convert(value)
                for position in ("lineno", "col_offset", "end_lineno", "end_col_offset"):
                    value = getattr(node, position, None)
                    if value is not None:
                        result[position] = value
                return result
            if isinstance(node, list):
                return [convert(item) for item in node]
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
                        tree = ast.parse(handle.read(), filename=path)
                    results.append({"path": path, "tree": convert(tree)})
                except SyntaxError as error:
                    results.append({"path": path, "problem": "line %s: %s" % (error.lineno, error.msg)})
                except (OSError, ValueError, RecursionError) as error:
                    results.append({"path": path, "problem": str(error)})
            with open(output, "w", encoding="utf-8") as handle:
                json.dump({"files": results}, handle)


        main()
        """;
}
