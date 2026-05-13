using System;
using System.Collections.Generic;

/// <summary>
/// Таблица символов всей программы.
/// Хранит дерево скоупов и список диагностик, обнаруженных при сборе.
/// </summary>
public class SymbolTable
{
    /// <summary>Корневой скоуп — содержит символы верхнего уровня (unit, types, vars…).</summary>
    public Scope Root { get; } = new Scope("<root>", parent: null);

    /// <summary>Структурированные диагностики с позицией в байтах.</summary>
    public List<SymbolDiagnostic> Diagnostics { get; } = new();

    // ── Печать ──────────────────────────────────────────────────────────────

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine("  Symbol Table");
        Console.WriteLine("══════════════════════════════════════════");
        PrintScope(Root, "", isLast: true);

        if (Diagnostics.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  Диагностики ({Diagnostics.Count}):");
            foreach (var d in Diagnostics)
                Console.WriteLine($"    ⚠ {d.Message}");
        }
        Console.WriteLine("══════════════════════════════════════════");
    }

    private static void PrintScope(Scope scope, string prefix, bool isLast)
    {
        string branch = isLast ? "└─ " : "├─ ";
        string cont = isLast ? "   " : "│  ";

        Console.WriteLine($"{prefix}{branch}[{scope.Name}]");

        string childPrefix = prefix + cont;

        var symbols = new List<Symbol>(scope.Symbols);
        symbols.Sort((a, b) => a.StartByte.CompareTo(b.StartByte));

        for (int i = 0; i < symbols.Count; i++)
        {
            var sym = symbols[i];
            bool last = (i == symbols.Count - 1) && scope.Children.Count == 0;
            string sb = last ? "└─ " : "├─ ";

            string typeStr = sym.TypeName != null ? $": {sym.TypeName}" : "";
            string innerStr = sym.InnerScope != null ? $"  →  {sym.InnerScope}" : "";

            Console.WriteLine($"{childPrefix}{sb}{sym.Kind,-12} {sym.Name}{typeStr}{innerStr}");
        }

        for (int i = 0; i < scope.Children.Count; i++)
            PrintScope(scope.Children[i], childPrefix, isLast: i == scope.Children.Count - 1);
    }
}

/// <summary>
/// Диагностика с байтовой позицией — для корректного отображения в редакторе.
/// </summary>
public class SymbolDiagnostic
{
    /// <summary>Сообщение об ошибке.</summary>
    public string Message { get; }

    /// <summary>Байтовая позиция начала проблемного идентификатора.</summary>
    public int StartByte { get; }

    /// <summary>Байтовая позиция конца проблемного идентификатора.</summary>
    public int EndByte { get; }

    /// <summary>Серьёзность: 1=Error, 2=Warning.</summary>
    public int Severity { get; }

    public SymbolDiagnostic(string message, int startByte, int endByte, int severity = 2)
    {
        Message = message;
        StartByte = startByte;
        EndByte = endByte;
        Severity = severity;
    }
}