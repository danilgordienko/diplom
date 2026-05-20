using System;
using System.Collections.Generic;

/// <summary>
/// Собирает диагностики (ошибки) из результатов анализа:
///   1. Синтаксические ошибки — узлы ERROR и MISSING в AST (tree-sitter)
///   2. Дублирование объявлений (из SymbolTable.Diagnostics)
///   3. Необъявленные идентификаторы — Hint (серый шрифт)
/// </summary>
public class DiagnosticCollector
{
    private readonly TreeSitterParser _parser;
    private const int MaxDiagnosticsPerFile = 50;

    public DiagnosticCollector(TreeSitterParser parser)
    {
        _parser = parser;
    }

    public List<LspDiagnostic> Collect(string source, AnalysisResult analysis)
    {
        var diagnostics = new List<LspDiagnostic>();

        var root = _parser.GetRootNode();
        CollectSyntaxErrors(root, source, diagnostics);

        foreach (var d in analysis.Table.Diagnostics)
        {
            if (diagnostics.Count >= MaxDiagnosticsPerFile) break;

            var (sl, sc) = SourceUtils.ToLspPosition(source, d.StartByte);
            var (el, ec) = SourceUtils.ToLspPosition(source, d.EndByte);
            if (sl == el && sc == ec) ec = sc + 1;

            diagnostics.Add(new LspDiagnostic(sl, sc, el, ec, d.Message, d.Severity));
        }

        foreach (var u in analysis.Unresolved)
        {
            if (diagnostics.Count >= MaxDiagnosticsPerFile) break;

            var (sl, sc) = SourceUtils.ToLspPosition(source, u.StartByte);
            var (el, ec) = SourceUtils.ToLspPosition(source, u.EndByte);

            diagnostics.Add(new LspDiagnostic(
                sl, sc, el, ec,
                $"Необъявленный идентификатор '{u.Name}'",
                severity: 4   // Hint — серый шрифт, ненавязчиво
            ));
        }

        return diagnostics;
    }

    private void CollectSyntaxErrors(TSNode node, string source, List<LspDiagnostic> diagnostics)
    {
        if (node.id == IntPtr.Zero) return;
        if (diagnostics.Count >= MaxDiagnosticsPerFile) return;

        string type = _parser.GetNodeType(node);

        if (type == "ERROR")
        {
            AddErrorDiagnostic(node, source, diagnostics);
            return;
        }

        if (type == "MISSING")
        {
            AddMissingDiagnostic(node, source, diagnostics);
            return;
        }

        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            CollectSyntaxErrors(_parser.GetChild(node, i), source, diagnostics);
    }

    /// <summary>
    /// ERROR-узел ограничивается одной строкой чтобы не заливать красным весь файл.
    /// </summary>
    private void AddErrorDiagnostic(TSNode node, string source, List<LspDiagnostic> diagnostics)
    {
        int startByte = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int endByte = (int)TreeSitterNative.csharp_ts_node_end_byte(node);

        var (sl, sc) = SourceUtils.ToLspPosition(source, startByte);

        int endOfFirstLine = FindEndOfLine(source, startByte);
        int clampedEnd = Math.Min(endByte, endOfFirstLine);
        var (el, ec) = SourceUtils.ToLspPosition(source, clampedEnd);

        if (sl == el && sc == ec) ec = sc + 1;

        string preview = ExtractFirstLine(source, startByte);
        if (preview.Length > 60) preview = preview[..60] + "…";

        var (originalEndLine, _) = SourceUtils.ToLspPosition(source, endByte);
        string multilineHint = originalEndLine > sl
            ? $" (ошибка затрагивает {originalEndLine - sl + 1} строк)"
            : "";

        diagnostics.Add(new LspDiagnostic(
            sl, sc, el, ec,
            $"Синтаксическая ошибка: «{preview}»{multilineHint}",
            severity: 1
        ));
    }

    private void AddMissingDiagnostic(TSNode node, string source, List<LspDiagnostic> diagnostics)
    {
        int startByte = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int endByte = (int)TreeSitterNative.csharp_ts_node_end_byte(node);
        if (endByte <= startByte) endByte = startByte + 1;

        var (sl, sc) = SourceUtils.ToLspPosition(source, startByte);
        var (el, ec) = SourceUtils.ToLspPosition(source, endByte);

        string context = ExtractFirstLine(source, startByte);
        if (context.Length > 40) context = context[..40] + "…";

        diagnostics.Add(new LspDiagnostic(
            sl, sc, el, ec,
            $"Ожидается элемент языка (рядом с «{context}»)",
            severity: 1
        ));
    }

    private static int FindEndOfLine(string source, int bytePos)
    {
        int pos = bytePos;
        while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r')
            pos++;
        return pos;
    }

    private static string ExtractFirstLine(string source, int bytePos)
    {
        if (bytePos >= source.Length) return "";
        int end = FindEndOfLine(source, bytePos);
        return source.Substring(bytePos, end - bytePos).Trim();
    }
}

/// <summary>
/// Одна диагностика в формате LSP.
/// severity: 1=Error, 2=Warning, 3=Information, 4=Hint
/// </summary>
public class LspDiagnostic
{
    public int StartLine { get; }
    public int StartChar { get; }
    public int EndLine { get; }
    public int EndChar { get; }
    public string Message { get; }
    public int Severity { get; }

    public LspDiagnostic(int startLine, int startChar, int endLine, int endChar,
                         string message, int severity)
    {
        StartLine = startLine;
        StartChar = startChar;
        EndLine = endLine;
        EndChar = endChar;
        Message = message;
        Severity = severity;
    }
}