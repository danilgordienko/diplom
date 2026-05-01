using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Фасад анализатора символов.
/// Запускает оба прохода (SymbolCollector + ReferenceCollector)
/// и предоставляет два метода запроса:
///   FindDefinition — где объявлен символ под курсором
///   FindReferences — все вхождения символа
///
/// Позже сюда можно добавить LSP-режим без изменения этого класса.
/// </summary>
public class SymbolAnalyzer
{
    private readonly TreeSitterParser _parser;

    public SymbolAnalyzer(TreeSitterParser parser)
    {
        _parser = parser;
    }

    // ── Запуск анализа ───────────────────────────────────────────────────────

    /// <summary>
    /// Парсит исходник и строит таблицу символов + индекс вхождений.
    /// Возвращает готовый результат анализа.
    /// </summary>
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
    /// Найти объявление символа по имени.
    /// Возвращает null если символ не найден.
    /// </summary>
    public static DefinitionResult? FindDefinitionByName(AnalysisResult result, string name)
    {
        // Ищем в корневом скоупе и всех вложенных
        Symbol? sym = FindSymbolByName(result.Table.Root, name);
        if (sym == null) return null;

        var (line, col) = ByteToLineCol(result.Source, sym.StartByte);
        string preview = ExtractLine(result.Source, sym.StartByte);

        return new DefinitionResult(sym, line, col, preview);
    }

    /// <summary>
    /// Найти объявление символа по байтовой позиции (позиция курсора).
    /// </summary>
    public static DefinitionResult? FindDefinitionAtByte(AnalysisResult result, int bytePos)
    {
        Symbol? sym = result.Index.FindSymbolAtByte(bytePos);

        // Если не нашли в индексе вхождений — может курсор стоит прямо на объявлении
        if (sym == null)
            sym = FindSymbolAtDeclaration(result.Table.Root, bytePos);

        if (sym == null) return null;

        var (line, col) = ByteToLineCol(result.Source, sym.StartByte);
        string preview = ExtractLine(result.Source, sym.StartByte);

        return new DefinitionResult(sym, line, col, preview);
    }

    /// <summary>
    /// Все вхождения символа по имени.
    /// </summary>
    public static ReferencesResult FindReferencesByName(AnalysisResult result, string name)
    {
        Symbol? sym = FindSymbolByName(result.Table.Root, name);
        if (sym == null)
            return new ReferencesResult(null, Array.Empty<Reference>());

        var refs = result.Index.GetReferences(sym);
        return new ReferencesResult(sym, refs);
    }

    // ── Вывод в консоль ──────────────────────────────────────────────────────

    public static void PrintDefinition(DefinitionResult? def)
    {
        Console.WriteLine();
        if (def == null)
        {
            Console.WriteLine("  Символ не найден.");
            return;
        }

        Console.WriteLine($"  Объявление:  {def.Symbol.Kind} '{def.Symbol.Name}'");
        if (def.Symbol.TypeName != null)
            Console.WriteLine($"  Тип:         {def.Symbol.TypeName}");
        Console.WriteLine($"  Позиция:     строка {def.Line}, столбец {def.Column}");
        Console.WriteLine($"  Байт:        {def.Symbol.StartByte}–{def.Symbol.EndByte}");
        Console.WriteLine($"  Код:         {def.Preview}");
    }

    public static void PrintReferences(ReferencesResult refs)
    {
        Console.WriteLine();
        if (refs.Symbol == null)
        {
            Console.WriteLine("  Символ не найден.");
            return;
        }

        Console.WriteLine($"  Символ '{refs.Symbol.Name}' ({refs.Symbol.Kind})");
        Console.WriteLine($"  Вхождений: {refs.References.Count}");
        Console.WriteLine();

        if (refs.References.Count == 0)
        {
            Console.WriteLine("  (вхождений не найдено)");
            return;
        }

        foreach (var r in refs.References.OrderBy(r => r.StartByte))
            Console.WriteLine($"    строка {r.Line,4}:{r.Column,-3}  {r.LinePreview}");
    }

    // ── Поиск по дереву скоупов ──────────────────────────────────────────────

    private static Symbol? FindSymbolByName(Scope scope, string name)
    {
        // Сначала в текущем скоупе
        var sym = scope.LookupLocal(name);
        if (sym != null) return sym;

        // Потом рекурсивно в дочерних
        foreach (var child in scope.Children)
        {
            sym = FindSymbolByName(child, name);
            if (sym != null) return sym;
        }
        return null;
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

    // ── Утилиты позиционирования ─────────────────────────────────────────────

    public static (int line, int col) ByteToLineCol(string source, int bytePos)
    {
        if (bytePos <= 0) return (1, 1);
        int line = 1, col = 1;
        int end = Math.Min(bytePos, source.Length);
        for (int i = 0; i < end; i++)
        {
            if (source[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return (line, col);
    }

    public static int LineColToByte(string source, int targetLine, int targetCol)
    {
        int line = 1, col = 1;
        for (int i = 0; i < source.Length; i++)
        {
            if (line == targetLine && col == targetCol) return i;
            if (source[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return source.Length;
    }

    private static string ExtractLine(string source, int bytePos)
    {
        if (source.Length == 0) return "";
        int pos = Math.Min(bytePos, source.Length - 1);
        int start = pos;
        while (start > 0 && source[start - 1] != '\n') start--;
        int end = pos;
        while (end < source.Length && source[end] != '\n' && source[end] != '\r') end++;
        return source.Substring(start, end - start).Trim();
    }
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