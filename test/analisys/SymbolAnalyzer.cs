using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Фасад анализатора символов.
/// Запускает оба прохода (SymbolCollector + ReferenceCollector)
/// и предоставляет методы запроса.
/// </summary>
public class SymbolAnalyzer
{
    private readonly TreeSitterParser _parser;

    public SymbolAnalyzer(TreeSitterParser parser)
    {
        _parser = parser;
    }

    public AnalysisResult Analyze(string source)
    {
        _parser.Parse(source);

        var table = new SymbolTable();
        new SymbolCollector(_parser, source, table).Collect();

        var index = new ReferenceIndex();
        var refCollector = new ReferenceCollector(_parser, source, table, index);
        refCollector.Collect();

        return new AnalysisResult(source, table, index, refCollector.Unresolved);
    }

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

    public static DefinitionResult? FindDefinitionAtByte(AnalysisResult result, int bytePos)
    {
        Symbol? sym = result.Index.FindSymbolAtByte(bytePos)
                   ?? FindSymbolAtDeclaration(result.Table.Root, bytePos);

        if (sym == null) return null;

        var (line, col) = SourceUtils.ByteToLineCol(result.Source, sym.StartByte);
        string preview = SourceUtils.ExtractLine(result.Source, sym.StartByte);
        return new DefinitionResult(sym, line, col, preview);
    }

    public static ReferencesResult FindReferencesByName(AnalysisResult result, string name)
    {
        var found = new List<Symbol>();
        CollectSymbolsByName(result.Table.Root, name, found);

        if (found.Count == 0)
            return new ReferencesResult(null, Array.Empty<Reference>());

        var allRefs = found
            .SelectMany(sym => result.Index.GetReferences(sym))
            .OrderBy(r => r.StartByte)
            .ToList();

        return new ReferencesResult(found[0], allRefs);
    }

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
            if (sym.StartByte <= bytePos && bytePos < sym.EndByte)
                return sym;

        foreach (var child in scope.Children)
        {
            var found = FindSymbolAtDeclaration(child, bytePos);
            if (found != null) return found;
        }
        return null;
    }
}

public class AnalysisResult
{
    public string Source { get; }
    public SymbolTable Table { get; }
    public ReferenceIndex Index { get; }
    public IReadOnlyList<UnresolvedIdentifier> Unresolved { get; }

    public AnalysisResult(string source, SymbolTable table, ReferenceIndex index,
                          List<UnresolvedIdentifier> unresolved)
    {
        Source = source;
        Table = table;
        Index = index;
        Unresolved = unresolved;
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