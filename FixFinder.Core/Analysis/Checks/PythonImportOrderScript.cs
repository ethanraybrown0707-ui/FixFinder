namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Run with the program's own Python: lists, for each file, the statements at its top level that a first import runs - each
/// import, and each name a line there gives a value to - with their lines, in order, as JSON.
/// </summary>
internal static class PythonImportOrderScript
{
    public const string Source = """
        import ast
        import json
        import sys


        def names_given(target):
            # The names an assignment gives values to: total, or each of first, second in first, second = pair.
            if isinstance(target, ast.Name):
                return [target.id]
            if isinstance(target, (ast.Tuple, ast.List)):
                return [name for element in target.elts for name in names_given(element)]
            if isinstance(target, ast.Starred):
                return names_given(target.value)
            return []


        def tests_for_main(test):
            # if __name__ == "__main__":
            return (isinstance(test, ast.Compare) and isinstance(test.left, ast.Name) and test.left.id == "__name__"
                    and len(test.ops) == 1 and isinstance(test.ops[0], ast.Eq) and len(test.comparators) == 1
                    and isinstance(test.comparators[0], ast.Constant) and test.comparators[0].value == "__main__")


        def every_name_given(statement):
            # Each name a statement inside a block at the top gives a value to, anywhere in it but its functions and classes:
            # whether the block runs depends on values, so the names count as given, never as missing.
            given = []
            for node in ast.walk(statement):
                if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                    given.append(node.name)
                elif isinstance(node, ast.Name) and isinstance(node.ctx, ast.Store):
                    given.append(node.id)
                elif isinstance(node, (ast.Import, ast.ImportFrom)):
                    given.extend((alias.asname or alias.name).split(".")[0] for alias in node.names)
            return given


        def top_level(body, only_when_run):
            statements = []
            for statement in body:
                line = statement.lineno
                if isinstance(statement, ast.Import):
                    for alias in statement.names:
                        statements.append({"line": line, "kind": "import", "module": alias.name, "level": 0,
                                           "names": [], "gives": [alias.asname or alias.name.split(".")[0]], "onlyWhenRun": only_when_run})
                elif isinstance(statement, ast.ImportFrom):
                    statements.append({"line": line, "kind": "from", "module": statement.module or "", "level": statement.level,
                                       "names": [alias.name for alias in statement.names],
                                       "gives": [alias.asname or alias.name for alias in statement.names], "onlyWhenRun": only_when_run})
                elif isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                    statements.append({"line": line, "kind": "gives", "module": "", "level": 0, "names": [],
                                       "gives": [statement.name], "onlyWhenRun": only_when_run})
                elif isinstance(statement, ast.Assign):
                    statements.append({"line": line, "kind": "gives", "module": "", "level": 0, "names": [],
                                       "gives": [name for target in statement.targets for name in names_given(target)], "onlyWhenRun": only_when_run})
                elif isinstance(statement, (ast.AnnAssign, ast.AugAssign)):
                    statements.append({"line": line, "kind": "gives", "module": "", "level": 0, "names": [],
                                       "gives": names_given(statement.target), "onlyWhenRun": only_when_run})
                elif isinstance(statement, ast.If) and tests_for_main(statement.test) and not only_when_run:
                    statements.extend(top_level(statement.body, True))
                    statements.append({"line": line, "kind": "gives", "module": "", "level": 0, "names": [],
                                       "gives": [name for part in statement.orelse for name in every_name_given(part)], "onlyWhenRun": False})
                elif isinstance(statement, ast.Try):
                    statements.extend(top_level(statement.body, only_when_run))
                    statements.append({"line": line, "kind": "gives", "module": "", "level": 0, "names": [],
                                       "gives": [name for part in statement.handlers + statement.orelse + statement.finalbody
                                                 for name in every_name_given(part)], "onlyWhenRun": only_when_run})
                else:
                    statements.append({"line": line, "kind": "gives", "module": "", "level": 0, "names": [],
                                       "gives": every_name_given(statement), "onlyWhenRun": only_when_run})
            return statements


        def main():
            output, listing = sys.argv[1], sys.argv[2]
            with open(listing, encoding="utf-8") as named:
                paths = [line.rstrip("\n") for line in named if line.strip()]
            files = []
            for path in paths:
                try:
                    with open(path, "rb") as source:
                        tree = ast.parse(source.read(), filename=path)
                    files.append({"path": path, "statements": top_level(tree.body, False)})
                except (SyntaxError, ValueError, OSError) as problem:
                    files.append({"path": path, "problem": str(problem)})
            with open(output, "w", encoding="utf-8") as written:
                json.dump({"files": files}, written)


        main()
        """;
}
