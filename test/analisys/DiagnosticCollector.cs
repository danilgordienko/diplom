using System;
using System.Collections.Generic;

/// <summary>
/// Собирает диагностики (ошибки) из результатов анализа:
///   1. Синтаксические ошибки — узлы ERROR и MISSING в AST (tree-sitter)
///   2. Семантические ошибки — дублирование объявлений (из SymbolTable.Diagnostics)
///
/// Особенности:
///   — Многострочные ERROR-узлы обрезаются до первой строки, чтобы не заливать
///     красным весь файл при одной сломанной конструкции.
///   — MISSING-узлы показывают что именно ожидалось (если tree-sitter даёт подсказку).
///   — Максимум 50 диагностик на файл, чтобы не перегружать редактор.
/// </summary>
public class DiagnosticCollector
{
    private readonly TreeSitterParser _parser;

    // Ограничение чтобы не перегружать редактор при файлах с множеством ошибок
    private const int MaxDiagnosticsPerFile = 50;

    public DiagnosticCollector(TreeSitterParser parser)
    {
        _parser = parser;
    }

    /// <summary>
    /// Собирает все диагностики для данного исходника.
    /// Вызывать ПОСЛЕ SymbolAnalyzer.Analyze() — парсер уже содержит дерево.
    /// </summary>
    public List<LspDiagnostic> Collect(string source, AnalysisResult analysis)
    {
        var diagnostics = new List<LspDiagnostic>();

        // 1. Синтаксические ошибки из AST
        var root = _parser.GetRootNode();
        CollectSyntaxErrors(root, source, diagnostics);

        // 2. Дублирование объявлений из таблицы символов — теперь с позицией
        foreach (var d in analysis.Table.Diagnostics)
        {
            if (diagnostics.Count >= MaxDiagnosticsPerFile) break;

            var (sl, sc) = SourceUtils.ToLspPosition(source, d.StartByte);
            var (el, ec) = SourceUtils.ToLspPosition(source, d.EndByte);

            // Если диапазон пустой — показываем хотя бы один символ
            if (sl == el && sc == ec) ec = sc + 1;

            diagnostics.Add(new LspDiagnostic(
                startLine: sl, startChar: sc,
                endLine: el, endChar: ec,
                message: d.Message,
                severity: d.Severity
            ));
        }

        // 3. Неразрешённые идентификаторы — переменные/функции не найдены в скоупе
        foreach (var u in analysis.Unresolved)
        {
            if (diagnostics.Count >= MaxDiagnosticsPerFile) break;

            var (sl, sc) = SourceUtils.ToLspPosition(source, u.StartByte);
            var (el, ec) = SourceUtils.ToLspPosition(source, u.EndByte);

            diagnostics.Add(new LspDiagnostic(
                startLine: sl, startChar: sc,
                endLine: el, endChar: ec,
                message: $"Необъявленный идентификатор '{u.Name}'",
                severity: 2  // Warning (не Error — может быть из uses)
            ));
        }

        return diagnostics;
    }

    /// <summary>
    /// Рекурсивно обходит AST и собирает узлы ERROR и MISSING.
    /// </summary>
    private void CollectSyntaxErrors(TSNode node, string source, List<LspDiagnostic> diagnostics)
    {
        if (node.id == IntPtr.Zero) return;
        if (diagnostics.Count >= MaxDiagnosticsPerFile) return;

        string type = _parser.GetNodeType(node);

        if (type == "ERROR")
        {
            AddErrorDiagnostic(node, source, diagnostics);
            return; // Не спускаемся внутрь ERROR — всё поддерево ошибочно
        }

        if (type == "MISSING")
        {
            AddMissingDiagnostic(node, source, diagnostics);
            return;
        }

        // Рекурсивно обходим дочерние узлы
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            CollectSyntaxErrors(_parser.GetChild(node, i), source, diagnostics);
    }

    /// <summary>
    /// Добавляет диагностику для ERROR-узла.
    /// Ключевая особенность: ограничиваем подсветку ОДНОЙ строкой.
    /// Если tree-sitter создал огромный ERROR (например весь остаток файла),
    /// подсвечиваем только первую строку где началась ошибка.
    /// </summary>
    private void AddErrorDiagnostic(TSNode node, string source, List<LspDiagnostic> diagnostics)
    {
        int startByte = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int endByte = (int)TreeSitterNative.csharp_ts_node_end_byte(node);

        var (sl, sc) = SourceUtils.ToLspPosition(source, startByte);

        // Ограничиваем конец ошибки концом первой строки
        int endOfFirstLine = FindEndOfLine(source, startByte);
        int clampedEnd = Math.Min(endByte, endOfFirstLine);

        var (el, ec) = SourceUtils.ToLspPosition(source, clampedEnd);

        // Если после обрезки диапазон пустой — расширяем на 1 символ
        if (sl == el && sc == ec)
        {
            ec = sc + 1;
        }

        // Текст ошибки — только первая строка
        string preview = ExtractFirstLine(source, startByte);
        if (preview.Length > 60) preview = preview[..60] + "…";

        // Если ошибка многострочная — добавляем подсказку
        var (originalEndLine, _) = SourceUtils.ToLspPosition(source, endByte);
        string multilineHint = originalEndLine > sl
            ? $" (ошибка затрагивает {originalEndLine - sl + 1} строк)"
            : "";

        diagnostics.Add(new LspDiagnostic(
            startLine: sl, startChar: sc,
            endLine: el, endChar: ec,
            message: $"Синтаксическая ошибка: «{preview}»{multilineHint}",
            severity: 1  // Error
        ));
    }

    /// <summary>
    /// Добавляет диагностику для MISSING-узла (tree-sitter ожидал элемент).
    /// </summary>
    private void AddMissingDiagnostic(TSNode node, string source, List<LspDiagnostic> diagnostics)
    {
        int startByte = (int)TreeSitterNative.csharp_ts_node_start_byte(node);
        int endByte = (int)TreeSitterNative.csharp_ts_node_end_byte(node);

        // MISSING-узлы имеют нулевую длину — расширяем на 1 символ для видимости
        if (endByte <= startByte) endByte = startByte + 1;

        var (sl, sc) = SourceUtils.ToLspPosition(source, startByte);
        var (el, ec) = SourceUtils.ToLspPosition(source, endByte);

        // Пробуем понять что именно ожидалось — tree-sitter иногда хранит
        // ожидаемый тип узла в самом MISSING-узле
        // Для пользователя показываем контекст — что стоит рядом
        string context = ExtractFirstLine(source, startByte);
        if (context.Length > 40) context = context[..40] + "…";

        diagnostics.Add(new LspDiagnostic(
            startLine: sl, startChar: sc,
            endLine: el, endChar: ec,
            message: $"Ожидается элемент языка (рядом с «{context}»)",
            severity: 1  // Error
        ));
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    /// <summary>
    /// Находит позицию конца строки (до \n или конца файла) начиная с bytePos.
    /// </summary>
    private static int FindEndOfLine(string source, int bytePos)
    {
        int pos = bytePos;
        while (pos < source.Length && source[pos] != '\n' && source[pos] != '\r')
            pos++;
        return pos;
    }

    /// <summary>
    /// Извлекает текст от bytePos до конца строки.
    /// </summary>
    private static string ExtractFirstLine(string source, int bytePos)
    {
        if (bytePos >= source.Length) return "";
        int end = FindEndOfLine(source, bytePos);
        return source.Substring(bytePos, end - bytePos).Trim();
    }
}

/// <summary>
/// Одна диагностика в формате LSP.
/// severity: 1 = Error, 2 = Warning, 3 = Information, 4 = Hint.
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