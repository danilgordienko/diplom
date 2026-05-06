using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Поиск объявления и вхождений символа по всему проекту.
///
/// Алгоритм:
///   1. Собираем все .pas файлы в папке (рекурсивно).
///   2. Каждый файл парсим и запускаем SymbolCollector + ReferenceCollector.
///   3. Собираем ВСЕ объявления символа с заданным именем (во всех файлах).
///   4. Собираем все вхождения из всех файлов.
///   5. Выводим результат в консоль.
/// </summary>
public class ProjectAnalyzer
{
    private readonly TreeSitterParser _parser;

    public ProjectAnalyzer(TreeSitterParser parser)
    {
        _parser = parser;
    }

    // ── Главный метод ────────────────────────────────────────────────────────

    public void FindSymbol(string symbolName, string projectPath)
    {
        var files = CollectFiles(projectPath);
        if (files == null) return;

        if (files.Count == 0)
        {
            Console.WriteLine("Нет .pas файлов в указанной папке.");
            return;
        }

        Console.WriteLine($"═══ Поиск символа '{symbolName}' ═══");
        Console.WriteLine($"    Проект: {projectPath}");
        Console.WriteLine($"    Файлов: {files.Count}");

        // Все объявления — теперь список, а не одно
        var allDefinitions = new List<(DefinitionResult def, string filePath)>();

        // Вхождения: (путь к файлу, список Reference)
        var allRefs = new List<(string filePath, List<Reference> refs)>();

        int done = 0;
        foreach (var filePath in files)
        {
            done++;
            if (files.Count > 5)
                Console.Write($"\r    Анализ [{done}/{files.Count}] {Path.GetFileName(filePath),-40}");

            string source;
            try { source = ReadFile(filePath); }
            catch { continue; }
            if (source.Length == 0) continue;

            AnalysisResult result;
            try
            {
                var symAnalyzer = new SymbolAnalyzer(_parser);
                result = symAnalyzer.Analyze(source);
            }
            catch { continue; }

            // Собираем ВСЕ объявления из этого файла
            var defs = SymbolAnalyzer.FindDefinitionsByName(result, symbolName);
            foreach (var def in defs)
                allDefinitions.Add((def, filePath));

            // Собираем вхождения из этого файла
            var refs = SymbolAnalyzer.FindReferencesByName(result, symbolName);
            if (refs.References.Count > 0)
                allRefs.Add((filePath, refs.References.ToList()));
        }

        if (files.Count > 5)
            Console.WriteLine();

        Console.WriteLine();

        PrintDefinitions(allDefinitions, projectPath);
        Console.WriteLine();
        PrintAllReferences(symbolName, allRefs, projectPath);
    }

    // ── Вывод объявлений ─────────────────────────────────────────────────────

    private static void PrintDefinitions(
        List<(DefinitionResult def, string filePath)> definitions,
        string projectPath)
    {
        Console.WriteLine($"  ┌─ Объявления ({definitions.Count})");

        if (definitions.Count == 0)
        {
            Console.WriteLine("  │  (не найдено)");
            Console.WriteLine("  └─");
            return;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            var (def, filePath) = definitions[i];
            bool last = i == definitions.Count - 1;
            string connector = last ? "  └─ " : "  ├─ ";
            string linePrefix = last ? "      " : "  │   ";

            string displayName;
            try { displayName = Path.GetRelativePath(projectPath, filePath); }
            catch { displayName = Path.GetFileName(filePath); }

            Console.WriteLine($"{connector}{displayName}");
            Console.WriteLine($"{linePrefix}  Вид:    {def.Symbol.Kind}");
            Console.WriteLine($"{linePrefix}  Скоуп:  {def.Symbol.DeclaringScope.Name}");
            Console.WriteLine($"{linePrefix}  Строка: {def.Line}, столбец {def.Column}");
            if (def.Symbol.TypeName != null)
                Console.WriteLine($"{linePrefix}  Тип:    {def.Symbol.TypeName}");
            Console.WriteLine($"{linePrefix}  Код:    {def.Preview}");
        }
    }

    // ── Вывод вхождений ──────────────────────────────────────────────────────

    private static void PrintAllReferences(string symbolName,
        List<(string filePath, List<Reference> refs)> allRefs,
        string projectPath)
    {
        int totalRefs = allRefs.Sum(x => x.refs.Count);
        int totalFiles = allRefs.Count;

        Console.WriteLine($"  ┌─ Вхождения  ({totalRefs} в {totalFiles} {FilesWord(totalFiles)})");

        if (totalRefs == 0)
        {
            Console.WriteLine("  │  (не найдено)");
            Console.WriteLine("  └─");
            return;
        }

        for (int fi = 0; fi < allRefs.Count; fi++)
        {
            var (filePath, refs) = allRefs[fi];
            bool lastFile = fi == allRefs.Count - 1;

            string displayName;
            try { displayName = Path.GetRelativePath(projectPath, filePath); }
            catch { displayName = Path.GetFileName(filePath); }

            string fileConnector = lastFile ? "  └─ " : "  ├─ ";
            string linePrefix = lastFile ? "      " : "  │   ";

            Console.WriteLine($"{fileConnector}{displayName}  ({refs.Count})");

            foreach (var r in refs.OrderBy(r => r.StartByte))
            {
                string highlighted = HighlightInLine(r.LinePreview, symbolName, r.Column);
                Console.WriteLine($"{linePrefix}  строка {r.Line,4}:{r.Column,-4} {highlighted}");
            }
        }
    }

    // ── Подсветка по точной позиции столбца ─────────────────────────────────

    private static string HighlightInLine(string line, string symbolName, int column)
    {
        // column — 1-based; в строке превью начало может быть обрезано (Trim),
        // поэтому сначала пробуем точную позицию, затем fallback на IndexOf.
        int idx = column - 1;
        if (idx >= 0 && idx + symbolName.Length <= line.Length &&
            string.Equals(line.Substring(idx, symbolName.Length), symbolName,
                          StringComparison.OrdinalIgnoreCase))
        {
            return line.Substring(0, idx)
                 + "►" + line.Substring(idx, symbolName.Length) + "◄"
                 + line.Substring(idx + symbolName.Length);
        }

        // Fallback — ищем первое вхождение (строка могла быть обрезана Trim'ом)
        idx = line.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return line;
        return line.Substring(0, idx)
             + "►" + line.Substring(idx, symbolName.Length) + "◄"
             + line.Substring(idx + symbolName.Length);
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private List<string>? CollectFiles(string projectPath)
    {
        if (File.Exists(projectPath) &&
            projectPath.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { projectPath };
        }

        if (Directory.Exists(projectPath))
        {
            return Directory.GetFiles(projectPath, "*.pas", SearchOption.AllDirectories)
                            .OrderBy(f => f)
                            .ToList();
        }

        Console.Error.WriteLine($"Не найден файл или папка: {projectPath}");
        return null;
    }

    private static string FilesWord(int n) =>
        n == 1 ? "файле" : "файлах";

    internal static string ReadFile(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length == 0) return "";
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            return Encoding.Unicode.GetString(raw).TrimStart('\uFEFF');
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(raw).TrimStart('\uFEFF');
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(raw); }
        catch { return Encoding.GetEncoding(1251).GetString(raw); }
    }
}