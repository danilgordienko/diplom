using System;
using System.Collections.Generic;

/// <summary>
/// Второй проход по AST.
/// Обходит все выражения и операторы, находит идентификаторы,
/// разрешает их в символы через Scope.Lookup() и записывает
/// вхождения в ReferenceIndex.
///
/// Неразрешённые идентификаторы (не встроенные и не for-переменные)
/// записываются в список Unresolved для диагностик.
/// </summary>
public class ReferenceCollector
{
    private readonly TreeSitterParser _parser;
    private readonly string _source;
    private readonly SymbolTable _table;
    private readonly ReferenceIndex _index;
    private Scope _current;

    /// <summary>
    /// Неразрешённые идентификаторы — не найдены ни в скоупе, ни среди встроенных.
    /// </summary>
    public List<UnresolvedIdentifier> Unresolved { get; } = new();

    /// <summary>
    /// Имена, неявно объявленные в for-циклах (for i := ...).
    /// </summary>
    private readonly HashSet<string> _forVars = new(StringComparer.OrdinalIgnoreCase);

    public ReferenceCollector(TreeSitterParser parser, string source,
                               SymbolTable table, ReferenceIndex index)
    {
        _parser = parser;
        _source = source;
        _table = table;
        _index = index;
        _current = table.Root;
    }

    public void Collect()
    {
        var root = _parser.GetRootNode();
        Visit(root, inDeclPosition: false, afterDot: false);
    }

    private void Visit(TSNode node, bool inDeclPosition, bool afterDot)
    {
        if (node.id == IntPtr.Zero) return;

        string type = _parser.GetNodeType(node);

        switch (type)
        {
            case "program":
            case "unit":
            case "namespaceUnit":
            case "library":
                VisitWithScope(node, FindModuleName(node), inDeclPosition);
                return;

            case "defProc":
            case "defProcShort":
                VisitWithScope(node, FindProcName(node), inDeclPosition);
                return;

            case "declType":
                VisitDeclType(node);
                return;

            case "declVar":
            case "declConst":
            case "declField":
            case "declProp":
            case "declLabel":
            case "declEnumValue":
            case "varAssignDef":
            case "varDef":
            case "declArg":
                VisitDeclNode(node);
                return;

            case "declProc":
                VisitChildren(node, inDeclPosition: true, afterDot: false);
                return;

            case "exprDot":
            case "exprNullDot":
            case "typerefDot":
            case "genericDot":
                VisitDotExpr(node);
                return;

            case "identifier":
                if (!inDeclPosition && !afterDot)
                    TryRecord(node);
                return;

            default:
                if (IsForLikeNode(type))
                {
                    VisitForNode(node);
                    return;
                }
                VisitChildren(node, inDeclPosition: false, afterDot: false);
                return;
        }
    }

    private void VisitChildren(TSNode node, bool inDeclPosition, bool afterDot)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            Visit(_parser.GetChild(node, i), inDeclPosition, afterDot);
    }

    private void VisitWithScope(TSNode node, string scopeName, bool inDeclPosition)
    {
        Scope? inner = FindInnerScope(scopeName);
        if (inner != null) PushScope(inner);
        VisitChildren(node, inDeclPosition: false, afterDot: false);
        if (inner != null) PopScope();
    }

    private void VisitDeclType(TSNode node)
    {
        string name = ExtractFirstIdentifier(node);
        Scope? inner = !string.IsNullOrEmpty(name) ? FindInnerScope(name) : null;
        if (inner != null) PushScope(inner);
        VisitChildren(node, inDeclPosition: false, afterDot: false);
        if (inner != null) PopScope();
    }

    private void VisitDeclNode(TSNode node)
    {
        bool seenColon = false;
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            string txt = _parser.GetNodeText(child, _source).Trim();
            if (txt == ":") { seenColon = true; continue; }
            Visit(child, inDeclPosition: !seenColon, afterDot: false);
        }
    }

    private void VisitDotExpr(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        bool seenDot = false;
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            string txt = _parser.GetNodeText(child, _source).Trim();
            if (txt == "." || txt == "?.")
            {
                seenDot = true;
                continue;
            }
            Visit(child, inDeclPosition: false, afterDot: seenDot);
        }
    }

    /// <summary>
    /// Определяет, является ли тип узла for-подобной конструкцией.
    /// Разные грамматики tree-sitter используют разные имена:
    /// stmtFor, stmtForeach, stmtForIn, forStatement, и т.д.
    /// Ловим все вариации.
    /// </summary>
    private static bool IsForLikeNode(string type)
    {
        return type.StartsWith("stmtFor", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("for", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Обработка for-узла: первый identifier — переменная цикла,
    /// запоминаем её как неявно объявленную.
    /// </summary>
    private void VisitForNode(TSNode node)
    {
        bool foundLoopVar = false;
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            string ct = _parser.GetNodeType(child);

            if (!foundLoopVar && ct == "identifier")
            {
                string name = _parser.GetNodeText(child, _source).Trim();
                if (!string.IsNullOrEmpty(name))
                    _forVars.Add(name);
                foundLoopVar = true;
                continue;
            }

            Visit(child, inDeclPosition: false, afterDot: false);
        }
    }

    private void TryRecord(TSNode node)
    {
        string name = _parser.GetNodeText(node, _source).Trim();
        if (string.IsNullOrEmpty(name)) return;

        Symbol? sym = _current.Lookup(name);

        if (sym == null)
        {
            if (!IsBuiltinIdentifier(name) && !_forVars.Contains(name))
            {
                int start = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
                int end = (int)TreeSitterNative.csharp_ts_node_end_byte(node);
                Unresolved.Add(new UnresolvedIdentifier(name, start, end));
            }
            return;
        }

        int startByte = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int endByte = (int)TreeSitterNative.csharp_ts_node_end_byte(node);

        if (startByte == sym.StartByte) return;

        _index.Add(new Reference(sym, startByte, endByte, _source));
    }

    private static bool IsBuiltinIdentifier(string name)
    {
        return _builtins.Contains(name.ToLowerInvariant());
    }

    private static readonly HashSet<string> _builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ввод-вывод
        "write", "writeln", "print", "println", "readln", "read", "readkey",
        "writef", "writelnf", "printf", "printlnf", "formatstr",

        // Файлы
        "assign", "reset", "rewrite", "close", "eof", "eoln", "append",
        "fileexists", "deletefile", "rename",

        // Математика
        "abs", "sqr", "sqrt", "sin", "cos", "tan", "arctan", "exp", "ln", "log",
        "log2", "log10", "power", "round", "trunc", "ceil", "floor", "frac", "int",
        "max", "min", "random", "randomize", "odd", "succ", "pred", "sign", "pi",

        // Строки
        "length", "copy", "delete", "insert", "pos", "concat", "uppercase", "lowercase",
        "trim", "trimleft", "trimright", "chr", "ord", "strtoint", "strtofloat",
        "inttostr", "floattostr", "format", "setlength", "stringofchar",

        // Типы
        "integer", "real", "double", "single", "string", "boolean", "char", "byte",
        "shortint", "smallint", "word", "longword", "longint", "int64", "uint64",
        "cardinal", "extended", "biginteger",

        // Системные типы
        "object", "tobject", "tlist", "tpoint",
        "array", "set", "file", "text", "pointer",

        // Логические
        "true", "false", "nil", "maxint",

        // Системные функции
        "inc", "dec", "new", "dispose", "sizeof", "typeof", "default",
        "high", "low", "assigned", "freemem", "getmem",
        "halt", "exit", "break", "continue", "assert", "raise",

        // Контейнеры и функциональный стиль
        "range", "arr", "lst", "seq", "dict", "hset",
        "sort", "sorted", "reverse", "reversed",
        "zip", "enumerate",
        "toarray", "tolist",
        "where", "select", "aggregate", "takewhile", "skipwhile",
        "take", "skip", "first", "last", "count", "sum", "average",
        "any", "all", "contains", "distinct", "orderby", "orderbydescending",
        "foreach", "map", "filter", "reduce", "flatmap",

        // Графика
        "setwindowsize", "setwindowtitle", "clearwindow",
        "setpencolor", "setpenwidth", "setbrushcolor",
        "line", "circle", "ellipse", "rectangle", "fillrect",
        "drawcircle", "fillcircle", "drawrectangle", "fillrectangle",
        "moveto", "lineto", "textout", "floodfill",
        "redcolor", "greencolor", "bluecolor", "clred", "clgreen", "clblue",
        "clblack", "clwhite", "clyellow", "clgray",
        "rgb", "sleep", "milliseconds",

        // Исключения
        "exception",

        // PABCSystem
        "swap", "val", "str",
        "readinteger", "readreal", "readstring",
        "readlninteger", "readlnreal", "readlnstring",
        "readarrayinteger", "readarrayreal",
        "arrfill", "arrgen", "arrrandom", "arrandominteger", "arrrandomreal",
        "matrrandom", "matrrandominteger", "matrrandomreal",
        "seqrandom", "seqrandominteger", "seqrandomreal",
        "include", "exclude",

        // Ключевые слова которые tree-sitter может считать identifier
        "self", "result", "inherited",
    };

    private Scope? FindInnerScope(string name)
    {
        Symbol? sym = _current.LookupLocal(name);
        return sym?.InnerScope;
    }

    private void PushScope(Scope scope) => _current = scope;
    private void PopScope() => _current = _current.Parent ?? _table.Root;

    private string FindModuleName(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            var t = _parser.GetNodeType(child);
            if (t == "moduleName" || t == "identifier")
                return _parser.GetNodeText(child, _source).Trim();
        }
        return "";
    }

    private string FindProcName(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            if (_parser.GetNodeType(child) == "declProc")
                return ExtractFirstIdentifier(child);
        }
        return ExtractFirstIdentifier(node);
    }

    private string ExtractFirstIdentifier(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            var ct = _parser.GetNodeType(child);
            if (ct == "identifier" || ct == "genericTpl" || ct == "genericDot")
                return _parser.GetNodeText(child, _source).Trim();
        }
        return "";
    }
}

/// <summary>
/// Неразрешённый идентификатор — не найден ни в скоупе, ни среди встроенных.
/// </summary>
public class UnresolvedIdentifier
{
    public string Name { get; }
    public int StartByte { get; }
    public int EndByte { get; }

    public UnresolvedIdentifier(string name, int startByte, int endByte)
    {
        Name = name;
        StartByte = startByte;
        EndByte = endByte;
    }
}