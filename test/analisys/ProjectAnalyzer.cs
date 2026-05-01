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
///   3. Ищем объявление символа с заданным именем во всех файлах.
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
        // Определяем что передали — файл или папку
        List<string> files;
        if (File.Exists(projectPath) && projectPath.EndsWith(".pas", StringComparison.OrdinalIgnoreCase))
        {
            files = new List<string> { projectPath };
        }
        else if (Directory.Exists(projectPath))
        {
            files = Directory.GetFiles(projectPath, "*.pas", SearchOption.AllDirectories)
                             .OrderBy(f => f)
                             .ToList();
        }
        else
        {
            Console.Error.WriteLine($"Не найден файл или папка: {projectPath}");
            return;
        }

        if (files.Count == 0)
        {
            Console.WriteLine("Нет .pas файлов в указанной папке.");
            return;
        }

        Console.WriteLine($"═══ Поиск символа '{symbolName}' ═══");
        Console.WriteLine($"    Проект: {projectPath}");
        Console.WriteLine($"    Файлов: {files.Count}");

        // Результаты со всех файлов
        DefinitionResult? definition = null;
        string? definitionFile = null;

        // Список вхождений: (короткое имя файла, список Reference)
        var allRefs = new List<(string fileName, List<Reference> refs)>();

        int done = 0;
        foreach (var filePath in files)
        {
            done++;
            // Показываем прогресс только если файлов много
            if (files.Count > 5)
                Console.Write($"\r    Анализ [{done}/{files.Count}] {Path.GetFileName(filePath),-40}");

            string source;
            try { source = ReadFile(filePath); }
            catch { continue; }
            if (source.Length == 0) continue;

            // Парсим и собираем символы
            AnalysisResult result;
            try
            {
                var symAnalyzer = new SymbolAnalyzer(_parser);
                result = symAnalyzer.Analyze(source);
            }
            catch { continue; }

            // Ищем объявление в этом файле (берём первое найденное)
            if (definition == null)
            {
                var def = SymbolAnalyzer.FindDefinitionByName(result, symbolName);
                if (def != null)
                {
                    definition = def;
                    definitionFile = filePath;
                }
            }

            // Собираем вхождения из этого файла
            var refs = SymbolAnalyzer.FindReferencesByName(result, symbolName);
            if (refs.References.Count > 0)
            {
                allRefs.Add((filePath, refs.References.ToList()));
            }
        }

        if (files.Count > 5)
            Console.WriteLine(); // сброс строки прогресса

        Console.WriteLine();

        // ── Вывод результатов ────────────────────────────────────────────────

        PrintDefinition(definition, definitionFile);
        Console.WriteLine();
        PrintAllReferences(symbolName, allRefs, projectPath);
    }

    // ── Вывод объявления ─────────────────────────────────────────────────────

    private static void PrintDefinition(DefinitionResult? def, string? filePath)
    {
        Console.WriteLine("  ┌─ Объявление");
        if (def == null || filePath == null)
        {
            Console.WriteLine("  │  (не найдено)");
            Console.WriteLine("  └─");
            return;
        }

        string shortName = Path.GetFileName(filePath);
        Console.WriteLine($"  │  Файл:    {shortName}");
        Console.WriteLine($"  │  Строка:  {def.Line}, столбец {def.Column}");
        Console.WriteLine($"  │  Вид:     {def.Symbol.Kind}");
        if (def.Symbol.TypeName != null)
            Console.WriteLine($"  │  Тип:     {def.Symbol.TypeName}");
        Console.WriteLine($"  │  Код:     {def.Preview}");
        Console.WriteLine("  └─");
    }

    // ── Вывод вхождений ──────────────────────────────────────────────────────

    private static void PrintAllReferences(string symbolName,
        List<(string fileName, List<Reference> refs)> allRefs,
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

            // Имя файла относительно папки проекта
            string displayName;
            try { displayName = Path.GetRelativePath(projectPath, filePath); }
            catch { displayName = Path.GetFileName(filePath); }

            string fileConnector = lastFile ? "  └─ " : "  ├─ ";
            string linePrefix = lastFile ? "      " : "  │   ";

            Console.WriteLine($"{fileConnector}{displayName}  ({refs.Count})");

            foreach (var r in refs.OrderBy(r => r.StartByte))
            {
                // Подсвечиваем имя символа в строке кода
                string highlighted = HighlightInLine(r.LinePreview, symbolName);
                Console.WriteLine($"{linePrefix}  строка {r.Line,4}:{r.Column,-4} {highlighted}");
            }
        }
    }

    // ── Подсветка имени в строке (символы >>> <<< вокруг имени) ─────────────

    private static string HighlightInLine(string line, string symbolName)
    {
        int idx = line.IndexOf(symbolName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return line;
        return line.Substring(0, idx)
             + "►" + line.Substring(idx, symbolName.Length) + "◄"
             + line.Substring(idx + symbolName.Length);
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private static string FilesWord(int n) =>
        n == 1 ? "файле" : n is 2 or 3 or 4 ? "файлах" : "файлах";

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