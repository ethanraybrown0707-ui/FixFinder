namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// Built once with the program's own Go and run on its files: parses each with go/parser and writes the syntax trees as
/// JSON. Every exported field of a go/ast node becomes a field, found by reflection, so the dump follows go/ast exactly.
/// Positions become 1-based lines and 0-based columns counted the way .NET counts characters.
/// </summary>
internal static class GoAstScript
{
    public const string Source = """
        package main

        import (
        	"bufio"
        	"encoding/json"
        	"go/ast"
        	"go/parser"
        	"go/token"
        	"os"
        	"reflect"
        	"strconv"
        	"unicode/utf8"
        )

        var (
        	fset   = token.NewFileSet()
        	out    *bufio.Writer
        	source []byte
        	starts []int
        	posType   = reflect.TypeOf(token.Pos(0))
        	tokenType = reflect.TypeOf(token.Token(0))
        	skipped   = map[string]bool{"Obj": true, "Scope": true, "Doc": true, "Comment": true, "Comments": true,
        		"Imports": true, "Unresolved": true, "GoVersion": true}
        )

        func main() {
        	file, err := os.Create(os.Args[1])
        	if err != nil {
        		os.Exit(2)
        	}
        	out = bufio.NewWriter(file)
        	out.WriteString(`{"files":[`)
        	for i, path := range os.Args[2:] {
        		if i > 0 {
        			out.WriteByte(',')
        		}
        		out.WriteString(`{"path":`)
        		text(path)
        		source, err = os.ReadFile(path)
        		if err == nil {
        			var tree *ast.File
        			tree, err = parser.ParseFile(fset, path, source, parser.SkipObjectResolution)
        			if err == nil {
        				starts = []int{0}
        				for at, b := range source {
        					if b == '\n' {
        						starts = append(starts, at+1)
        					}
        				}
        				out.WriteString(`,"tree":`)
        				value(reflect.ValueOf(tree))
        			}
        		}
        		if err != nil {
        			out.WriteString(`,"problem":`)
        			text(err.Error())
        		}
        		out.WriteByte('}')
        	}
        	out.WriteString("]}")
        	out.Flush()
        	file.Close()
        }

        func text(s string) {
        	encoded, _ := json.Marshal(s)
        	out.Write(encoded)
        }

        // where writes a position as a line and the number of UTF-16 units before it on that line.
        func where(key string, pos token.Pos) {
        	if !pos.IsValid() {
        		return
        	}
        	p := fset.Position(pos)
        	column := 0
        	if p.Line >= 1 && p.Line <= len(starts) {
        		line := source[starts[p.Line-1]:]
        		for rest := line[:p.Column-1]; len(rest) > 0; {
        			r, size := utf8.DecodeRune(rest)
        			if r >= 0x10000 {
        				column += 2
        			} else {
        				column++
        			}
        			rest = rest[size:]
        		}
        	}
        	out.WriteString(`,"` + key + `":[` + strconv.Itoa(p.Line) + `,` + strconv.Itoa(column) + `]`)
        }

        func value(v reflect.Value) {
        	switch v.Kind() {
        	case reflect.Interface, reflect.Pointer:
        		if v.IsNil() {
        			out.WriteString("null")
        		} else if v.Kind() == reflect.Pointer && v.Elem().Kind() == reflect.Struct {
        			node(v)
        		} else {
        			value(v.Elem())
        		}
        	case reflect.Slice:
        		out.WriteByte('[')
        		for i := 0; i < v.Len(); i++ {
        			if i > 0 {
        				out.WriteByte(',')
        			}
        			value(v.Index(i))
        		}
        		out.WriteByte(']')
        	case reflect.String:
        		text(v.String())
        	case reflect.Bool:
        		out.WriteString(strconv.FormatBool(v.Bool()))
        	case reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64:
        		out.WriteString(strconv.FormatInt(v.Int(), 10))
        	case reflect.Uint, reflect.Uint8, reflect.Uint16, reflect.Uint32, reflect.Uint64:
        		out.WriteString(strconv.FormatUint(v.Uint(), 10))
        	default:
        		out.WriteString("null")
        	}
        }

        func node(pointer reflect.Value) {
        	v := pointer.Elem()
        	t := v.Type()
        	out.WriteString(`{"k":`)
        	text(t.Name())
        	if n, ok := pointer.Interface().(ast.Node); ok {
        		where("at", n.Pos())
        		where("to", n.End())
        	}
        	for i := 0; i < t.NumField(); i++ {
        		field := t.Field(i)
        		if !field.IsExported() || skipped[field.Name] {
        			continue
        		}
        		switch field.Type {
        		case posType:
        			if field.Name == "Ellipsis" {
        				out.WriteString(`,"Ellipsis":` + strconv.FormatBool(token.Pos(v.Field(i).Int()).IsValid()))
        			}
        			continue
        		case tokenType:
        			out.WriteString(`,"` + field.Name + `":`)
        			text(token.Token(v.Field(i).Int()).String())
        			continue
        		}
        		out.WriteString(`,"` + field.Name + `":`)
        		value(v.Field(i))
        	}
        	out.WriteByte('}')
        }
        """;
}
