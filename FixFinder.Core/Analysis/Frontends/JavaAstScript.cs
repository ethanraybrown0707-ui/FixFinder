namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// Compiled once and run with the program's own JDK: parses each file with javac's public tree API and writes the trees
/// as JSON. Every getter of a com.sun.source.tree interface becomes a field, so the dump follows javac exactly.
/// </summary>
internal static class JavaAstScript
{
    public const string ClassName = "FixFinderAst";

    public const string Source = """
        import com.sun.source.tree.*;
        import com.sun.source.util.*;
        import java.lang.reflect.Method;
        import java.nio.charset.StandardCharsets;
        import java.nio.file.*;
        import java.util.*;
        import javax.lang.model.element.Name;
        import javax.tools.*;

        public class FixFinderAst {
            private final CompilationUnitTree unit;
            private final SourcePositions positions;
            private final StringBuilder json;

            private FixFinderAst(CompilationUnitTree unit, SourcePositions positions, StringBuilder json) {
                this.unit = unit;
                this.positions = positions;
                this.json = json;
            }

            public static void main(String[] args) throws Exception {
                JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
                StandardJavaFileManager fileManager = compiler.getStandardFileManager(null, null, StandardCharsets.UTF_8);
                StringBuilder json = new StringBuilder("{\"files\":[");

                for (int i = 1; i < args.length; i++) {
                    if (i > 1) json.append(',');
                    json.append("{\"path\":").append(quote(args[i]));

                    DiagnosticCollector<JavaFileObject> diagnostics = new DiagnosticCollector<>();
                    JavacTask task = (JavacTask) compiler.getTask(null, fileManager, diagnostics, List.of("-proc:none"), null,
                        fileManager.getJavaFileObjects(args[i]));
                    Iterable<? extends CompilationUnitTree> units = task.parse();

                    List<String> problems = new ArrayList<>();
                    for (Diagnostic<? extends JavaFileObject> diagnostic : diagnostics.getDiagnostics())
                        if (diagnostic.getKind() == Diagnostic.Kind.ERROR)
                            problems.add("line " + diagnostic.getLineNumber() + ": " + diagnostic.getMessage(Locale.ENGLISH));

                    if (!problems.isEmpty()) {
                        json.append(",\"problem\":").append(quote(String.join("; ", problems)));
                    } else {
                        for (CompilationUnitTree unit : units) {
                            json.append(",\"tree\":");
                            new FixFinderAst(unit, Trees.instance(task).getSourcePositions(), json).write(unit);
                        }
                    }
                    json.append('}');
                }

                json.append("]}");
                Files.writeString(Paths.get(args[0]), json.toString(), StandardCharsets.UTF_8);
            }

            private void write(Object value) {
                if (value == null) { json.append("null"); return; }
                if (value instanceof Tree tree) { writeTree(tree); return; }
                if (value instanceof Collection<?> items) {
                    json.append('[');
                    boolean first = true;
                    for (Object item : items) {
                        if (!first) json.append(',');
                        first = false;
                        write(item);
                    }
                    json.append(']');
                    return;
                }
                if (value instanceof Boolean || value instanceof Integer || value instanceof Long) { json.append(value); return; }
                if (value instanceof Float || value instanceof Double) {
                    double number = ((Number) value).doubleValue();
                    if (Double.isFinite(number)) json.append(number); else json.append(quote(value.toString()));
                    return;
                }
                if (value instanceof Character character) { json.append("{\"char\":").append((int) character.charValue()).append('}'); return; }
                if (value instanceof Name || value instanceof String || value instanceof Enum<?>) { json.append(quote(value.toString())); return; }
                json.append(quote(value.toString()));
            }

            private void writeTree(Tree tree) {
                json.append("{\"kind\":").append(quote(tree.getKind().name()));

                long start = positions.getStartPosition(unit, tree);
                long end = positions.getEndPosition(unit, tree);
                LineMap lines = unit.getLineMap();
                if (start >= 0) {
                    json.append(",\"line\":").append(lines.getLineNumber(start)).append(",\"column\":").append(lines.getColumnNumber(start) - 1);
                }
                if (end >= 0) {
                    json.append(",\"endLine\":").append(lines.getLineNumber(end)).append(",\"endColumn\":").append(lines.getColumnNumber(end) - 1);
                }

                Set<String> seen = new HashSet<>();
                for (Class<?> type : interfacesOf(tree.getClass())) {
                    if (!type.getPackageName().equals("com.sun.source.tree")) continue;
                    for (Method method : type.getMethods()) {
                        if (method.getParameterCount() != 0 || method.getDeclaringClass() == Object.class) continue;
                        String name = method.getName();
                        if (name.equals("getKind") || name.equals("accept") || name.equals("getLineMap") || name.equals("getSourceFile")
                            || name.equals("getDocComments") || name.equals("getPackage")) continue;
                        String field = name.startsWith("get") ? name.substring(3) : name.startsWith("is") ? name.substring(2) : null;
                        if (field == null || field.isEmpty() || !seen.add(field)) continue;
                        field = Character.toLowerCase(field.charAt(0)) + field.substring(1);

                        Object value;
                        try { value = method.invoke(tree); } catch (Exception ignored) { continue; }
                        json.append(',').append(quote(field)).append(':');
                        write(value);
                    }
                }

                json.append('}');
            }

            private static List<Class<?>> interfacesOf(Class<?> type) {
                List<Class<?>> found = new ArrayList<>();
                Deque<Class<?>> pending = new ArrayDeque<>();
                for (Class<?> c = type; c != null; c = c.getSuperclass()) pending.addAll(Arrays.asList(c.getInterfaces()));
                while (!pending.isEmpty()) {
                    Class<?> next = pending.pop();
                    if (found.contains(next)) continue;
                    found.add(next);
                    pending.addAll(Arrays.asList(next.getInterfaces()));
                }
                return found;
            }

            private static String quote(String text) {
                StringBuilder quoted = new StringBuilder("\"");
                for (char c : text.toCharArray()) {
                    switch (c) {
                        case '"' -> quoted.append("\\\"");
                        case '\\' -> quoted.append("\\\\");
                        case '\n' -> quoted.append("\\n");
                        case '\r' -> quoted.append("\\r");
                        case '\t' -> quoted.append("\\t");
                        default -> {
                            if (c < 0x20) quoted.append(String.format("\\u%04x", (int) c)); else quoted.append(c);
                        }
                    }
                }
                return quoted.append('"').toString();
            }
        }
        """;
}
