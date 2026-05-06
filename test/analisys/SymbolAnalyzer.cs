using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Фасад анализатора символов.
/// Запускает оба прохода (SymbolCollector + ReferenceCollector)
/// и предоставляет методы запроса:
///   FindDefinitions     — все места, где объявлен символ с данным именем
///   FindDefinitionAtByte — объявление символа под курсором
///   FindReferences      — все вхождения символа
/// </summary>
public class SymbolAnalyzer
{
    private readonly TreeSitterParser _parser;

    public SymbolAnalyzer(TreeSitterParser parser)
    {
        _parser = parser;
    }

    // ── Запуск анализа ───────────────────────────────────────────────────────

    public AnalysisResult Analyze(string source)
    {
        _parser.Parse(source);

        var table = new SymbolTable();
        var collector = new SymbolCollector(_parser, source, table);
        collector.Collect();

        var index = new ReferenceIndex();
        var refCollector = new ReferenceCollector(_parser, source, table, index);
        refCollector.Collect();

        return new AnalysisResult(source, table, index);
    }

    // ── Запросы ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Найти ВСЕ объявления символа по имени во всём дереве скоупов.
    /// Один и тот же идентификатор может быть объявлен в разных скоупах
    /// (например, поле класса и локальная переменная с одинаковым именем).
    /// </summary>
    public static List<DefinitionResult> FindDefinitionsByName(AnalysisResult result, string name)
    {
        var found = new List<Symbol>();
        CollectSymbolsByName(result.Table.Root, name, found);

        return found.Select(sym =>
        {
            var (line, col) = SourceUtils.ByteToLineCol(result.Source, sym.StartByte);
            string preview = SourceUtils.ExtractLine(result.Source, sym.StartByte);
            return new DefinitionResult(sym, line, col, preview);
        }).ToList();
    }

    /// <summary>
    /// Найти объявление символа по байтовой позиции курсора.
    /// Сначала ищем в индексе вхождений, потом — прямо на объявлении.
    /// </summary>
    public static DefinitionResult? FindDefinitionAtByte(AnalysisResult result, int bytePos)
    {
        Symbol? sym = result.Index.FindSymbolAtByte(bytePos);

        if (sym == null)
            sym = FindSymbolAtDeclaration(result.Table.Root, bytePos);

        if (sym == null) return null;

        var (line, col) = SourceUtils.ByteToLineCol(result.Source, sym.StartByte);
        string preview = SourceUtils.ExtractLine(result.Source, sym.StartByte);

        return new DefinitionResult(sym, line, col, preview);
    }

    /// <summary>
    /// Все вхождения символа по имени (из первого найденного объявления).
    /// </summary>
    public static ReferencesResult FindReferencesByName(AnalysisResult result, string name)
    {
        var found = new List<Symbol>();
        CollectSymbolsByName(result.Table.Root, name, found);

        if (found.Count == 0)
            return new ReferencesResult(null, Array.Empty<Reference>());

        // Агрегируем вхождения всех символов с данным именем
        var allRefs = found
            .SelectMany(sym => result.Index.GetReferences(sym))
            .OrderBy(r => r.StartByte)
            .ToList();

        return new ReferencesResult(found[0], allRefs);
    }

    // ── Вывод в консоль ──────────────────────────────────────────────────────

    public static void PrintDefinitions(List<DefinitionResult> defs)
    {
        Console.Error.WriteLine();
        if (defs.Count == 0)
        {
            Console.Error.WriteLine("  Символ не найден.");
            return;
        }

        foreach (var def in defs)
        {
            Console.Error.WriteLine($"  Объявление:  {def.Symbol.Kind} '{def.Symbol.Name}'");
            if (def.Symbol.TypeName != null)
                Console.Error.WriteLine($"  Тип:         {def.Symbol.TypeName}");
            Console.Error.WriteLine($"  Скоуп:       {def.Symbol.DeclaringScope.Name}");
            Console.Error.WriteLine($"  Позиция:     строка {def.Line}, столбец {def.Column}");
            Console.Error.WriteLine($"  Код:         {def.Preview}");
            Console.Error.WriteLine();
        }
    }

    public static void PrintReferences(ReferencesResult refs)
    {
        Console.Error.WriteLine();
        if (refs.Symbol == null)
        {
            Console.Error.WriteLine("  Символ не найден.");
            return;
        }

        Console.Error.WriteLine($"  Символ '{refs.Symbol.Name}' ({refs.Symbol.Kind})");
        Console.Error.WriteLine($"  Вхождений: {refs.References.Count}");
        Console.Error.WriteLine();

        if (refs.References.Count == 0)
        {
            Console.Error.WriteLine("  (вхождений не найдено)");
            return;
        }

        foreach (var r in refs.References.OrderBy(r => r.StartByte))
            Console.Error.WriteLine($"    строка {r.Line,4}:{r.Column,-3}  {r.LinePreview}");
    }

    // ── Поиск по дереву скоупов ──────────────────────────────────────────────

    /// <summary>
    /// Рекурсивно собирает все символы с данным именем из всего дерева скоупов.
    /// </summary>
    private static void CollectSymbolsByName(Scope scope, string name, List<Symbol> result)
    {
        var sym = scope.LookupLocal(name);
        if (sym != null) result.Add(sym);

        foreach (var child in scope.Children)
            CollectSymbolsByName(child, name, result);
    }

    private static Symbol? FindSymbolAtDeclaration(Scope scope, int bytePos)
    {
        foreach (var sym in scope.Symbols)
        {
            if (sym.StartByte <= bytePos && bytePos < sym.EndByte)
                return sym;
        }
        foreach (var child in scope.Children)
        {
            var found = FindSymbolAtDeclaration(child, bytePos);
            if (found != null) return found;
        }
        return null;
    }

    // ── Утилиты позиционирования (делегируем в SourceUtils) ──────────────────

    public static (int line, int col) ByteToLineCol(string source, int bytePos)
        => SourceUtils.ByteToLineCol(source, bytePos);

    public static int LineColToByte(string source, int targetLine, int targetCol)
        => SourceUtils.LineColToByte(source, targetLine, targetCol);
}

// ── Результаты запросов ──────────────────────────────────────────────────────

public class AnalysisResult
{
    public string Source { get; }
    public SymbolTable Table { get; }
    public ReferenceIndex Index { get; }

    public AnalysisResult(string source, SymbolTable table, ReferenceIndex index)
    {
        Source = source;
        Table = table;
        Index = index;
    }
}

public class DefinitionResult
{
    public Symbol Symbol { get; }
    public int Line { get; }
    public int Column { get; }
    public string Preview { get; }

    public DefinitionResult(Symbol symbol, int line, int col, string preview)
    {
        Symbol = symbol;
        Line = line;
        Column = col;
        Preview = preview;
    }
}

public class ReferencesResult
{
    public Symbol? Symbol { get; }
    public IReadOnlyList<Reference> References { get; }

    public ReferencesResult(Symbol? symbol, IReadOnlyList<Reference> refs)
    {
        Symbol = symbol;
        References = refs;
    }
}