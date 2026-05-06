using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Анализирует папку с .pas файлами.
/// Консоль: сводка по контекстам ошибок.
/// Файлы:   logs/errors_by_context.txt  — все ошибки сгруппированные по контексту
///          logs/errors_by_file.txt     — все ошибки сгруппированные по файлу
/// </summary>
public class GrammarAnalyzer
{
    private readonly TreeSitterParser _parser;

    public GrammarAnalyzer(TreeSitterParser parser) => _parser = parser;

    // ── Запуск по папке ───────────────────────────────────────────────────────

    public void Run(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"Папка не найдена: {folder}");
            return;
        }

        var files = Directory.GetFiles(folder, "*.pas", SearchOption.AllDirectories)
                             .OrderBy(f => f).ToList();

        if (files.Count == 0) { Console.Error.WriteLine("Нет .pas файлов."); return; }

        // Создаём папку для логов рядом с папкой samples
        string logsDir = Path.Combine(Path.GetDirectoryName(folder)!, "logs");
        Directory.CreateDirectory(logsDir);

        Console.WriteLine($"Файлов: {files.Count}  Папка: {folder}");
        Console.WriteLine($"Логи:   {logsDir}");
        Console.WriteLine();

        // Все ошибки: (файл, родитель, текст, кол-во узлов в ошибке)
        var allErrors = new List<(string file, string parent, string text, int nodes)>();
        int totalAll = 0, errorNodesAll = 0, filesWithErrors = 0;

        int done = 0;
        foreach (var path in files)
        {
            done++;
            Console.Write($"\r  [{done}/{files.Count}] {Path.GetFileName(path),-50}");

            string src;
            try { src = Decode(File.ReadAllBytes(path)); }
            catch { continue; }
            if (src.Length == 0) continue;

            try { _parser.Parse(src); }
            catch { continue; }

            var root = _parser.GetRootNode();
            var errs = new List<(string parent, string text, int nodes)>();
            int total = 0;
            Walk(root, src, null, errs, ref total);

            totalAll += total;
            if (errs.Count > 0)
            {
                filesWithErrors++;
                string fname = Path.GetFileName(path);
                foreach (var (parent, text, nodes) in errs)
                {
                    errorNodesAll += nodes;
                    allErrors.Add((fname, parent, text, nodes));
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine();

        // ── Консоль: сводка ───────────────────────────────────────────────────
        Console.WriteLine($"Узлов всего:       {totalAll}");
        Console.WriteLine($"Ошибочных узлов:   {errorNodesAll}  ({(totalAll > 0 ? 100.0 * errorNodesAll / totalAll : 0):F1}% дерева)");
        Console.WriteLine($"Файлов с ошибками: {filesWithErrors} из {files.Count}");
        Console.WriteLine();

        var byParent = allErrors
            .GroupBy(e => e.parent)
            .Select(g => new {
                Parent = g.Key,
                Nodes = g.Sum(e => e.nodes),
                FileCnt = g.Select(e => e.file).Distinct().Count(),
                Samples = g.Select(e => e.text).Where(t => t.Length > 0)
                            .Distinct().Take(3)
                            .Select(t => t.Length > 45 ? t[..45] + "…" : t).ToList()
            })
            .OrderByDescending(g => g.Nodes)
            .ToList();

        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        Console.WriteLine($"  {"Контекст (родитель)",-22} {"Узлов",9}  {"Файлов",7}  Примеры");
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        foreach (var g in byParent)
        {
            Console.WriteLine($"  {g.Parent,-22} {g.Nodes,9}  {g.FileCnt,7}");
            foreach (var s in g.Samples)
                Console.WriteLine($"  {"",22}            «{s}»");
        }
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ── Лог 1: по контексту ───────────────────────────────────────────────
        string byContextPath = Path.Combine(logsDir, "errors_by_context.txt");
        using (var w = new StreamWriter(byContextPath, false, Encoding.UTF8))
        {
            w.WriteLine($"Ошибки по контексту (родительскому узлу AST)");
            w.WriteLine($"Всего ошибочных узлов: {errorNodesAll} из {totalAll}");
            w.WriteLine($"Файлов с ошибками: {filesWithErrors} из {files.Count}");
            w.WriteLine();

            foreach (var g in byParent)
            {
                w.WriteLine($"════ [{g.Parent}]  узлов: {g.Nodes}  файлов: {g.FileCnt}");

                // Для каждого файла — его ошибки в этом контексте
                var byFile = allErrors
                    .Where(e => e.parent == g.Parent)
                    .GroupBy(e => e.file)
                    .OrderByDescending(fg => fg.Sum(e => e.nodes));

                foreach (var fg in byFile)
                {
                    w.WriteLine($"  {fg.Key}  ({fg.Sum(e => e.nodes)} узлов)");
                    foreach (var e in fg)
                    {
                        string t = e.text.Replace("\r", "");
                        if (t.Length > 120) t = t[..120] + "…";
                        w.WriteLine($"    «{t}»");
                    }
                }
                w.WriteLine();
            }
        }

        // ── Лог 2: по файлу ───────────────────────────────────────────────────
        string byFilePath = Path.Combine(logsDir, "errors_by_file.txt");
        using (var w = new StreamWriter(byFilePath, false, Encoding.UTF8))
        {
            w.WriteLine($"Ошибки по файлу");
            w.WriteLine($"Всего ошибочных узлов: {errorNodesAll} из {totalAll}");
            w.WriteLine();

            var byFile = allErrors
                .GroupBy(e => e.file)
                .OrderByDescending(g => g.Sum(e => e.nodes));

            foreach (var fg in byFile)
            {
                int fileNodes = fg.Sum(e => e.nodes);
                w.WriteLine($"════ {fg.Key}  ({fileNodes} ошибочных узлов)");

                foreach (var e in fg.OrderByDescending(x => x.nodes))
                {
                    string t = e.text.Replace("\r", "");
                    if (t.Length > 120) t = t[..120] + "…";
                    w.WriteLine($"  [{e.parent}]  {e.nodes} узлов  «{t}»");
                }
                w.WriteLine();
            }
        }

        Console.WriteLine($"Записано: {byContextPath}");
        Console.WriteLine($"Записано: {byFilePath}");
    }

    // ── Инспекция одного файла ────────────────────────────────────────────────

    public void InspectFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Файл не найден: {filePath}");
            return;
        }

        string src;
        try { src = Decode(File.ReadAllBytes(filePath)); }
        catch { Console.Error.WriteLine("Не удалось прочитать файл"); return; }

        Console.WriteLine();
        Console.WriteLine($"═══ {Path.GetFileName(filePath)} ═══");

        if (src.Length == 0) { Console.WriteLine("Пустой файл."); return; }

        _parser.Parse(src);
        var root = _parser.GetRootNode();

        var errs = new List<(string parent, string text, int nodes)>();
        int total = 0;
        Walk(root, src, null, errs, ref total);

        int errorTotal = errs.Sum(e => e.nodes);
        Console.WriteLine($"Узлов всего: {total}   Ошибочных: {errorTotal} ({(total > 0 ? 100.0 * errorTotal / total : 0):F1}%)");
        Console.WriteLine();

        if (errs.Count == 0) { Console.WriteLine("Ошибок нет."); return; }

        var groups = errs
            .GroupBy(e => e.parent)
            .Select(g => new {
                Parent = g.Key,
                Nodes = g.Sum(e => e.nodes),
                Samples = g.Select(e => e.text).Where(t => t.Length > 0)
                           .Distinct().Take(5).ToList()
            })
            .OrderByDescending(g => g.Nodes);

        foreach (var g in groups)
        {
            Console.WriteLine($"  [{g.Parent}]  {g.Nodes} ошибочных узлов");
            foreach (var s in g.Samples)
            {
                string t = s.Replace("\n", "↵").Replace("\r", "");
                if (t.Length > 70) t = t[..70] + "…";
                Console.WriteLine($"    «{t}»");
            }
            Console.WriteLine();
        }
    }

    // ── Обход дерева ──────────────────────────────────────────────────────────

    /// <summary>
    /// Выводит полное AST дерево файла с отступами.
    /// ERROR-узлы помечены «*** ERROR ***», MISSING — «*** MISSING ***».
    /// Показывает только именованные узлы (type != анонимный).
    /// Для листьев показывает текст.
    /// </summary>
    public void DumpTree(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Файл не найден: {filePath}");
            return;
        }

        string src;
        try { src = Decode(File.ReadAllBytes(filePath)); }
        catch { Console.Error.WriteLine("Не удалось прочитать файл"); return; }

        if (src.Length == 0) { Console.WriteLine("Пустой файл."); return; }

        _parser.Parse(src);
        var root = _parser.GetRootNode();
        DumpNode(root, src, 0);
    }

    private void DumpNode(TSNode node, string src, int depth)
    {
        if (node.id == IntPtr.Zero) return;

        string type = _parser.GetNodeType(node);
        uint childCount = _parser.GetChildCount(node);
        string indent = new string(' ', depth * 2);

        uint startByte = TreeSitterNative.csharp_ts_node_start_byte(node);
        uint endByte = TreeSitterNative.csharp_ts_node_end_byte(node);
        int startLine = GetLine(src, (int)startByte);

        if (type == "ERROR")
        {
            string errText = _parser.GetNodeText(node, src)
                .Replace("\r", "").Replace("\n", "↵");
            if (errText.Length > 80) errText = errText[..80] + "…";
            Console.WriteLine($"{indent}*** ERROR *** L{startLine} «{errText}»");
            return;
        }

        if (type == "MISSING")
        {
            Console.WriteLine($"{indent}*** MISSING {_parser.GetNodeType(node)} *** L{startLine}");
            return;
        }

        bool isNamed = _parser.IsNamedNode(node);
        if (!isNamed && childCount == 0)
            return; // пропускаем анонимные листья (скобки, ключевые слова)

        if (isNamed && childCount == 0)
        {
            // Лист — показываем текст
            string text = _parser.GetNodeText(node, src)
                .Replace("\r", "").Replace("\n", "↵");
            if (text.Length > 60) text = text[..60] + "…";
            Console.WriteLine($"{indent}{type} L{startLine} = «{text}»");
            return;
        }

        if (isNamed)
            Console.WriteLine($"{indent}{type} L{startLine}");

        for (uint i = 0; i < childCount; i++)
            DumpNode(_parser.GetChild(node, i), src, isNamed ? depth + 1 : depth);
    }

    private static int GetLine(string src, int byteOffset)
    {
        // Приблизительно — считаем символы = байты (для ASCII/UTF8)
        int pos = Math.Min(byteOffset, src.Length);
        int line = 1;
        for (int i = 0; i < pos; i++)
            if (src[i] == '\n') line++;
        return line;
    }

    private void Walk(TSNode node, string src, string? parentType,
                      List<(string parent, string text, int nodes)> errs,
                      ref int total)
    {
        if (node.id == IntPtr.Zero) return;
        total++;

        string type = _parser.GetNodeType(node);

        if (type == "ERROR")
        {
            string text = _parser.GetNodeText(node, src)
                .Replace("\r", "").Replace("\n", "↵");
            if (text.Length > 120) text = text[..120] + "…";

            int sub = 0;
            CountNodes(node, ref sub);
            errs.Add((parentType ?? "<root>", text, sub));
            return; // не спускаемся внутрь
        }

        if (type == "MISSING")
        {
            errs.Add((parentType ?? "<root>", "", 1));
            return;
        }

        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            Walk(_parser.GetChild(node, i), src, type, errs, ref total);
    }

    private void CountNodes(TSNode node, ref int count)
    {
        if (node.id == IntPtr.Zero) return;
        count++;
        uint n = _parser.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            CountNodes(_parser.GetChild(node, i), ref count);
    }

    // ── Кодировка ─────────────────────────────────────────────────────────────

    private static string Decode(byte[] raw)
    {
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