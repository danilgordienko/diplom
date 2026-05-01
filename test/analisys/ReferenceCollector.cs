using System;
using System.Collections.Generic;

/// <summary>
/// Второй проход по AST.
/// Обходит все выражения и операторы, находит идентификаторы,
/// разрешает их в символы через Scope.Lookup() и записывает
/// вхождения в ReferenceIndex.
///
/// Важные правила:
///  — идентификаторы в позиции объявления (declVar, declField, …) пропускаем —
///    они уже есть в SymbolTable как определения
///  — правая часть точки (obj.Field) — это поле, не локальная переменная;
///    её мы тоже пропускаем (разрешение полей — отдельная задача)
///  — скоупы отслеживаем так же, как в SymbolCollector, чтобы Lookup
///    искал в правильной области видимости
/// </summary>
public class ReferenceCollector
{
    private readonly TreeSitterParser _parser;
    private readonly string _source;
    private readonly SymbolTable _table;
    private readonly ReferenceIndex _index;

    // Текущий скоуп — синхронизирован с деревом скоупов из первого прохода
    private Scope _current;

    public ReferenceCollector(TreeSitterParser parser, string source,
                               SymbolTable table, ReferenceIndex index)
    {
        _parser = parser;
        _source = source;
        _table = table;
        _index = index;
        _current = table.Root;
    }

    // ── Публичный вход ───────────────────────────────────────────────────────

    public void Collect()
    {
        var root = _parser.GetRootNode();
        Visit(root, inDeclPosition: false, afterDot: false);
    }

    // ── Диспетчер ────────────────────────────────────────────────────────────

    /// <param name="inDeclPosition">
    ///   true — мы внутри узла объявления (declVar, declField, …),
    ///   identifier здесь — имя объявляемого символа, не вхождение.
    /// </param>
    /// <param name="afterDot">
    ///   true — identifier является правой частью точки (obj.Member),
    ///   разрешать в текущем скоупе не нужно.
    /// </param>
    private void Visit(TSNode node, bool inDeclPosition, bool afterDot)
    {
        if (node.id == IntPtr.Zero) return;

        string type = _parser.GetNodeType(node);

        switch (type)
        {
            // ── Узлы, которые меняют скоуп ──────────────────────────────────

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

            // ── Узлы объявлений — имена здесь не являются вхождениями ───────

            case "declVar":
            case "declConst":
            case "declField":
            case "declProp":
            case "declLabel":
            case "declEnumValue":
            case "varAssignDef":
            case "varDef":
                // Тип (после ':') разрешать можно — это использование типа.
                // Имена (до ':') — объявление, пропускаем.
                VisitDeclNode(node);
                return;

            case "declArg":
                // Параметры функции — имена объявляются, тип — вхождение
                VisitDeclNode(node);
                return;

            case "declProc":
                // forward-декларация: имя функции — объявление, параметры — тоже
                // просто спускаемся не фиксируя ничего как вхождение
                VisitChildren(node, inDeclPosition: true, afterDot: false);
                return;

            // ── Доступ через точку: левая часть — ref, правая — пропуск ─────

            case "exprDot":
            case "exprNullDot":
            case "typerefDot":
            case "genericDot":
                VisitDotExpr(node);
                return;

            // ── Обычный идентификатор в выражении ───────────────────────────

            case "identifier":
                if (!inDeclPosition && !afterDot)
                    TryRecord(node);
                return;

            // ── Секции — спускаемся прозрачно ───────────────────────────────

            case "interface":
            case "implementation":
            case "initialization":
            case "finalization":
            case "shortProgram":
            case "bareProgram":
            case "simpleUnit":
            default:
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

    // ── Узел с переходом скоупа ──────────────────────────────────────────────

    private void VisitWithScope(TSNode node, string scopeName, bool inDeclPosition)
    {
        // Ищем вложенный скоуп, созданный SymbolCollector'ом
        Scope? inner = FindInnerScope(scopeName);
        if (inner != null) PushScope(inner);

        VisitChildren(node, inDeclPosition: false, afterDot: false);

        if (inner != null) PopScope();
    }

    // ── Объявление типа ──────────────────────────────────────────────────────

    private void VisitDeclType(TSNode node)
    {
        // Имя типа — объявление, не фиксируем.
        // Тело (правая часть) — спускаемся, возможно там есть скоуп класса.
        string name = ExtractFirstIdentifier(node);
        Scope? inner = !string.IsNullOrEmpty(name) ? FindInnerScope(name) : null;
        if (inner != null) PushScope(inner);

        VisitChildren(node, inDeclPosition: false, afterDot: false);

        if (inner != null) PopScope();
    }

    // ── Узел объявления: имена — пропуск, типы — вхождения ──────────────────

    private void VisitDeclNode(TSNode node)
    {
        // Идём по потомкам вручную:
        // identifier до ':' — имя объявляемого символа → пропускаем
        // всё после ':' (typeref и т.д.) → это использование типа → фиксируем
        bool seenColon = false;
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            string ct = _parser.GetNodeType(child);
            string txt = _parser.GetNodeText(child, _source).Trim();

            if (txt == ":") { seenColon = true; continue; }

            if (!seenColon)
            {
                // До двоеточия — имена объявлений; спускаемся с флагом inDecl
                Visit(child, inDeclPosition: true, afterDot: false);
            }
            else
            {
                // После двоеточия — тип; это вхождение типа
                Visit(child, inDeclPosition: false, afterDot: false);
            }
        }
    }

    // ── Доступ через точку ───────────────────────────────────────────────────

    private void VisitDotExpr(TSNode node)
    {
        // Структура: lhs  '.'  rhs
        // lhs — полноценное выражение, rhs — имя поля (не разрешаем в скоупе)
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
            // Левая часть — обычный ref; правая — afterDot=true
            Visit(child, inDeclPosition: false, afterDot: seenDot);
        }
    }

    // ── Запись вхождения ─────────────────────────────────────────────────────

    private void TryRecord(TSNode node)
    {
        string name = _parser.GetNodeText(node, _source).Trim();
        if (string.IsNullOrEmpty(name)) return;

        // Ищем символ в текущем скоупе и выше
        Symbol? sym = _current.Lookup(name);
        if (sym == null) return; // не объявлен — внешний идентификатор, пропускаем

        int start = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int end = (int)TreeSitterNative.csharp_ts_node_end_byte(node);

        // Пропускаем если это сама точка объявления символа
        if (start == sym.StartByte) return;

        _index.Add(new Reference(sym, start, end, _source));
    }

    // ── Поиск скоупа ─────────────────────────────────────────────────────────

    private Scope? FindInnerScope(string name)
    {
        // Ищем символ с таким именем в текущем скоупе
        Symbol? sym = _current.LookupLocal(name);
        return sym?.InnerScope;
    }

    private void PushScope(Scope scope) => _current = scope;
    private void PopScope() => _current = _current.Parent ?? _table.Root;

    // ── Вспомогательные ──────────────────────────────────────────────────────

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
        // defProc содержит declProc как header
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