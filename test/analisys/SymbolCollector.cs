using System;
using System.Collections.Generic;

/// <summary>
/// Обходит всё дерево и заполняет SymbolTable:
///   — объявления типов, классов, перечислений
///   — переменные (глобальные, локальные, поля)
///   — константы
///   — функции, процедуры, операторы с их параметрами
///   — метки
///
/// Для каждой функции/класса создаётся вложенный Scope,
/// что образует дерево областей видимости, отражающее структуру программы.
/// </summary>
public class SymbolCollector
{
    private readonly TreeSitterParser _parser;
    private readonly string _source;
    private readonly SymbolTable _table;

    // Текущий активный скоуп — меняется при входе/выходе из функций, классов и т.д.
    private Scope _current;

    public SymbolCollector(TreeSitterParser parser, string source, SymbolTable table)
    {
        _parser = parser;
        _source = source;
        _table = table;
        _current = table.Root;
    }

    // ── Публичный вход ───────────────────────────────────────────────────────

    public void Collect()
    {
        var root = _parser.GetRootNode();
        Visit(root);
    }

    // ── Диспетчер ────────────────────────────────────────────────────────────

    private void Visit(TSNode node)
    {
        if (node.id == IntPtr.Zero) return;

        switch (_parser.GetNodeType(node))
        {
            // Модули
            case "program": VisitModule(node, SymbolKind.Program); return;
            case "unit": VisitModule(node, SymbolKind.Unit); return;
            case "namespaceUnit": VisitModule(node, SymbolKind.Namespace); return;
            case "library": VisitModule(node, SymbolKind.Library); return;

            // Тела секций — просто спускаемся
            case "interface":
            case "implementation":
            case "initialization":
            case "finalization":
            case "shortProgram":
                VisitChildren(node);
                return;

            // Секция объявления типов
            case "declTypes":
                VisitChildren(node);
                return;

            // Одно объявление типа:  TFoo = class/record/enum/auto class/...
            case "declType":
                VisitDeclType(node);
                return;

            // Секции var / const / label
            case "declVars":
            case "declConsts":
                VisitChildren(node);
                return;

            case "declVar": VisitDeclVar(node); return;
            case "declConst": VisitDeclConst(node); return;
            case "declLabels":
                VisitDeclLabels(node);
                return;

            // Функции и процедуры
            case "defProc": VisitDefProc(node); return;
            case "declProc":
                // forward-декларация — регистрируем имя, но без тела
                VisitDeclProcHeader(node);
                return;

            // Внутри блока операторов — var x := ... / var x: T := ...
            case "varAssignDef":
            case "varDef":
                VisitVarStatement(node);
                return;

            // Всё остальное — просто спускаемся
            default:
                VisitChildren(node);
                return;
        }
    }

    private void VisitChildren(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            Visit(_parser.GetChild(node, i));
    }

    // ── Модуль верхнего уровня ───────────────────────────────────────────────

    private void VisitModule(TSNode node, SymbolKind kind)
    {
        // Имя модуля: первый именованный ребёнок-идентификатор
        string name = FindModuleName(node);
        var sym = Define(name, kind, node);

        // Создаём скоуп модуля — в нём будет всё содержимое
        var moduleScope = new Scope(name, _current);
        sym.InnerScope = moduleScope;

        PushScope(moduleScope);
        VisitChildren(node);
        PopScope();
    }

    private string FindModuleName(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            var t = _parser.GetNodeType(child);
            if (t == "moduleName" || t == "identifier")
                return _parser.GetNodeText(child, _source);
        }
        return "<anonymous>";
    }

    // ── Объявление типа: type TFoo = ... ────────────────────────────────────

    private void VisitDeclType(TSNode node)
    {
        // Ищем поле 'name'
        string name = ExtractFieldText(node, "name");
        if (string.IsNullOrEmpty(name)) { VisitChildren(node); return; }

        // Определяем конкретный вид по правой части
        TSNode typeNode = FindFieldNode(node, "type");
        SymbolKind kind = ClassifyDeclType(typeNode);

        var sym = Define(name, kind, node);

        // Для классов/record/интерфейсов/автоклассов — создаём вложенный скоуп
        if (kind is SymbolKind.Class or SymbolKind.Record or SymbolKind.Interface
                 or SymbolKind.AutoClass)
        {
            var classScope = new Scope(name, _current);
            sym.InnerScope = classScope;
            PushScope(classScope);
            CollectClassMembers(typeNode);
            PopScope();
        }
        else if (kind == SymbolKind.Enum)
        {
            // Перечисление: значения добавляем в текущий скоуп (Pascal-семантика)
            CollectEnumValues(typeNode);
        }
    }

    private SymbolKind ClassifyDeclType(TSNode typeNode)
    {
        if (typeNode.id == IntPtr.Zero) return SymbolKind.TypeAlias;
        switch (_parser.GetNodeType(typeNode))
        {
            case "declClass": return SymbolKind.Class;
            case "declIntf": return SymbolKind.Interface;
            case "declEnum": return SymbolKind.Enum;
            case "declAutoClass": return SymbolKind.AutoClass;
            default:
                // Может быть обёртка — проверяем тип внутри
                uint n = _parser.GetChildCount(typeNode);
                for (uint i = 0; i < n; i++)
                {
                    var ch = _parser.GetChild(typeNode, i);
                    var k = ClassifyDeclType(ch);
                    if (k != SymbolKind.TypeAlias) return k;
                }
                return SymbolKind.TypeAlias;
        }
    }

    // Обход полей и методов внутри declClass / declIntf
    private void CollectClassMembers(TSNode classNode)
    {
        if (classNode.id == IntPtr.Zero) return;
        uint n = _parser.GetChildCount(classNode);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(classNode, i);
            switch (_parser.GetNodeType(child))
            {
                case "declField":
                    VisitDeclField(child);
                    break;
                case "declProp":
                    VisitDeclProp(child);
                    break;
                case "declTypes":
                case "declVars":
                case "declConsts":
                    VisitChildren(child);
                    break;
                case "defProc":
                    VisitDefProc(child);
                    break;
                case "declProc":
                    VisitDeclProcHeader(child);
                    break;
                case "declSection": // visibility section (public/private/…)
                    CollectClassMembers(child);
                    break;
                default:
                    break;
            }
        }
    }

    // Значения перечисления
    private void CollectEnumValues(TSNode enumNode)
    {
        if (enumNode.id == IntPtr.Zero) return;
        uint n = _parser.GetChildCount(enumNode);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(enumNode, i);
            if (_parser.GetNodeType(child) == "declEnumValue")
            {
                string valName = ExtractFieldText(child, "name");
                if (!string.IsNullOrEmpty(valName))
                    Define(valName, SymbolKind.EnumValue, child);
            }
        }
    }

    // ── Поле класса: name: type ──────────────────────────────────────────────

    private void VisitDeclField(TSNode node)
    {
        // Поле может объявлять несколько имён: a, b, c: integer
        string typeName = ExtractFieldText(node, "type");
        ForEachNameInField(node, "name", name =>
        {
            var sym = Define(name, SymbolKind.Field, node);
            sym.TypeName = typeName;
        });
    }

    // ── Свойство: property X: T ──────────────────────────────────────────────

    private void VisitDeclProp(TSNode node)
    {
        string name = ExtractFieldText(node, "name");
        string typeName = ExtractFieldText(node, "type");
        if (string.IsNullOrEmpty(name)) return;
        var sym = Define(name, SymbolKind.Property, node);
        sym.TypeName = typeName;
    }

    // ── Объявление переменных: var a, b: integer ─────────────────────────────

    private void VisitDeclVar(TSNode node)
    {
        string typeName = ExtractFieldText(node, "type");
        ForEachNameInField(node, "name", name =>
        {
            var sym = Define(name, SymbolKind.Variable, node);
            sym.TypeName = typeName;
        });
    }

    // ── Константа: const C = ... ─────────────────────────────────────────────

    private void VisitDeclConst(TSNode node)
    {
        string name = ExtractFieldText(node, "name");
        string typeName = ExtractFieldText(node, "type"); // typed const
        if (string.IsNullOrEmpty(name)) return;
        var sym = Define(name, SymbolKind.Constant, node);
        sym.TypeName = typeName.Length > 0 ? typeName : null;
    }

    // ── Метки ────────────────────────────────────────────────────────────────

    private void VisitDeclLabels(TSNode node)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            if (_parser.GetNodeType(child) == "declLabel")
            {
                string name = _parser.GetNodeText(child, _source).Trim();
                if (!string.IsNullOrEmpty(name))
                    Define(name, SymbolKind.Label, child);
            }
        }
    }

    // ── var x := ... / var x: T внутри операторов ───────────────────────────

    private void VisitVarStatement(TSNode node)
    {
        // varAssignDef: kVar identifier [: type]
        // varDef:       kVar identifier :  type
        string typeName = ExtractFieldText(node, "type");
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            if (_parser.GetNodeType(child) == "identifier")
            {
                string name = _parser.GetNodeText(child, _source).Trim();
                var sym = Define(name, SymbolKind.Variable, child);
                sym.TypeName = typeName.Length > 0 ? typeName : null;
            }
        }
    }

    // ── Полное определение функции/процедуры: header + body ──────────────────

    private void VisitDefProc(TSNode node)
    {
        // Находим заголовок (declProc внутри defProc)
        TSNode header = FindFieldNode(node, "header");
        if (header.id == IntPtr.Zero)
            header = FindChildByType(node, "declProc");

        (string procName, SymbolKind kind, string retType, List<(string, string)> parms)
            = ExtractProcHeader(header.id != IntPtr.Zero ? header : node);

        if (string.IsNullOrEmpty(procName)) { VisitChildren(node); return; }

        var sym = Define(procName, kind, node);
        sym.TypeName = retType.Length > 0 ? retType : null;

        // Скоуп функции: параметры + локальные переменные
        var procScope = new Scope(procName, _current);
        sym.InnerScope = procScope;

        PushScope(procScope);

        // Добавляем параметры
        foreach (var (pName, pType) in parms)
        {
            var pSym = Define(pName, SymbolKind.Parameter, node);
            pSym.TypeName = pType.Length > 0 ? pType : null;
        }

        // Обходим тело (локальные объявления + операторы)
        VisitChildren(node);

        PopScope();
    }

    // forward-объявление — только регистрируем имя, без тела
    private void VisitDeclProcHeader(TSNode node)
    {
        (string procName, SymbolKind kind, string retType, _)
            = ExtractProcHeader(node);
        if (string.IsNullOrEmpty(procName)) return;
        var sym = Define(procName, kind, node);
        sym.TypeName = retType.Length > 0 ? retType : null;
    }

    // ── Извлечение заголовка процедуры ───────────────────────────────────────

    private (string name, SymbolKind kind, string retType, List<(string name, string type)> parms)
        ExtractProcHeader(TSNode node)
    {
        string procName = "";
        SymbolKind kind = SymbolKind.Procedure;
        string retType = "";
        var parms = new List<(string, string)>();

        if (node.id == IntPtr.Zero)
            return (procName, kind, retType, parms);

        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            var childType = _parser.GetNodeType(child);

            switch (childType)
            {
                // Ключевые слова — определяем вид
                case "kFunction": kind = SymbolKind.Function; break;
                case "kProcedure": kind = SymbolKind.Procedure; break;
                case "kConstructor": kind = SymbolKind.Constructor; break;
                case "kDestructor": kind = SymbolKind.Destructor; break;
                case "kOperator": kind = SymbolKind.Operator; break;

                // Имя функции (поле 'name')
                case "identifier":
                case "genericDot":
                case "genericTpl":
                case "extensionMethodName":
                    if (procName == "")
                        procName = _parser.GetNodeText(child, _source).Trim();
                    break;

                // Параметры
                case "declArgs":
                    parms = ExtractDeclArgs(child);
                    break;

                // Возвращаемый тип — после ':'
                case "typeref":
                    retType = _parser.GetNodeText(child, _source).Trim();
                    break;
            }
        }

        // Если имя — поле 'name' (явно)
        string fieldName = ExtractFieldText(node, "name");
        if (!string.IsNullOrEmpty(fieldName))
            procName = fieldName;

        return (procName, kind, retType, parms);
    }

    private List<(string name, string type)> ExtractDeclArgs(TSNode argsNode)
    {
        var result = new List<(string, string)>();
        uint n = _parser.GetChildCount(argsNode);
        for (uint i = 0; i < n; i++)
        {
            var arg = _parser.GetChild(argsNode, i);
            if (_parser.GetNodeType(arg) == "declArg")
                result.AddRange(ExtractSingleArg(arg));
        }
        return result;
    }

    private List<(string, string)> ExtractSingleArg(TSNode argNode)
    {
        var result = new List<(string, string)>();
        string typeName = ExtractFieldText(argNode, "type");

        // Собираем все identifier-ы в поле 'name'
        uint n = _parser.GetChildCount(argNode);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(argNode, i);
            var t = _parser.GetNodeType(child);
            if (t == "identifier")
            {
                string name = _parser.GetNodeText(child, _source).Trim();
                // Пропускаем ключевые слова var/const/out/params — они тоже identifier-образные
                if (!IsModifierKeyword(name))
                    result.Add((name, typeName));
            }
        }
        return result;
    }

    private static readonly HashSet<string> _modifierKws = new(StringComparer.OrdinalIgnoreCase)
    {
        "var", "const", "out", "constref", "params"
    };
    private static bool IsModifierKeyword(string s) => _modifierKws.Contains(s);

    // ── Вспомогательные методы ───────────────────────────────────────────────

    /// <summary>
    /// Добавить символ в текущий скоуп.
    /// При дублировании — добавляет диагностику с позицией повторного объявления.
    /// </summary>
    private Symbol Define(string name, SymbolKind kind, TSNode node)
    {
        int start = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int end = (int)TreeSitterNative.csharp_ts_node_end_byte(node);
        var sym = new Symbol(name, kind, _current, start, end);

        if (!_current.TryDefine(sym, out var existing))
        {
            // Позиция указывает на повторное объявление (не на первое)
            // чтобы пользователь видел подчёркивание именно на дубликате
            _table.Diagnostics.Add(new SymbolDiagnostic(
                message: $"'{name}' уже объявлен в этой области видимости",
                startByte: start,
                endByte: end,
                severity: 2   // Warning
            ));
        }
        return sym;
    }

    private void PushScope(Scope scope) => _current = scope;
    private void PopScope() => _current = _current.Parent ?? _table.Root;

    /// <summary>
    /// Извлечь текстовое содержимое первого потомка с заданным полевым именем (field name).
    /// tree-sitter field names — это имена вроде 'name', 'type', 'body'.
    /// Мы ищем их перебором потомков по типу узла (упрощённо).
    /// </summary>
    private string ExtractFieldText(TSNode node, string fieldHint)
    {
        // Прямой обход: ищем первый подходящий по смыслу дочерний узел.
        // Стратегия: тип узла содержит подсказку (typeref, identifier для 'name', etc.)
        uint n = _parser.GetChildCount(node);

        if (fieldHint == "name")
        {
            // Берём первый identifier (имя объекта)
            for (uint i = 0; i < n; i++)
            {
                var child = _parser.GetChild(node, i);
                var ct = _parser.GetNodeType(child);
                if (ct == "identifier" || ct == "genericTpl" || ct == "genericDot"
                    || ct == "extensionMethodName")
                    return _parser.GetNodeText(child, _source).Trim();
            }
        }
        else if (fieldHint == "type")
        {
            // Берём typeref / type / identifier после ':'
            bool seenColon = false;
            for (uint i = 0; i < n; i++)
            {
                var child = _parser.GetChild(node, i);
                var ct = _parser.GetNodeType(child);
                var txt = _parser.GetNodeText(child, _source).Trim();
                if (txt == ":") { seenColon = true; continue; }
                if (seenColon && (ct.StartsWith("typeref") || ct == "type"
                    || ct == "identifier" || ct == "declSequence"
                    || ct == "typerefFunc" || ct == "typerefNullable"))
                    return _parser.GetNodeText(child, _source).Trim();
            }
        }

        return "";
    }

    /// <summary>
    /// Найти дочерний узел по полевому имени — ищем TSNode с нужным типом.
    /// </summary>
    private TSNode FindFieldNode(TSNode node, string fieldHint)
    {
        uint n = _parser.GetChildCount(node);
        if (fieldHint == "type")
        {
            bool seenColon = false;
            for (uint i = 0; i < n; i++)
            {
                var child = _parser.GetChild(node, i);
                var ct = _parser.GetNodeType(child);
                var txt = _parser.GetNodeText(child, _source).Trim();
                if (txt == "=" || txt == ":") { seenColon = true; continue; }
                if (seenColon && (ct.StartsWith("decl") || ct.StartsWith("typeref")
                    || ct == "type" || ct == "identifier"))
                    return child;
            }
        }
        if (fieldHint == "header")
        {
            for (uint i = 0; i < n; i++)
            {
                var child = _parser.GetChild(node, i);
                if (_parser.GetNodeType(child) == "declProc")
                    return child;
            }
        }
        return default;
    }

    private TSNode FindChildByType(TSNode node, string typeName)
    {
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            if (_parser.GetNodeType(child) == typeName)
                return child;
        }
        return default;
    }

    /// <summary>
    /// Вызвать action для каждого имени из поля 'name' (которое может быть списком: a, b, c).
    /// </summary>
    private void ForEachNameInField(TSNode node, string fieldHint, Action<string> action)
    {
        // declVar / declField содержат delimited1(identifier) в поле 'name'
        // Идём по всем identifier-ам до первого ':'
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
        {
            var child = _parser.GetChild(node, i);
            var ct = _parser.GetNodeType(child);
            var txt = _parser.GetNodeText(child, _source).Trim();
            if (txt == ":" || txt == "=" || txt == ";") break;
            if (ct == "identifier")
                action(txt);
        }
    }
}