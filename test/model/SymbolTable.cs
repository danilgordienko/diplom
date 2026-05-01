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

    /// <summary>Диагностики, обнаруженные во время сбора (дублирующие объявления и т.п.).</summary>
    public List<string> Diagnostics { get; } = new();

    // ── Печать ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Выводит всё дерево скоупов со всеми символами в консоль.
    /// </summary>
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
                Console.WriteLine($"    ⚠ {d}");
        }
        Console.WriteLine("══════════════════════════════════════════");
    }

    private static void PrintScope(Scope scope, string prefix, bool isLast)
    {
        string branch = isLast ? "└─ " : "├─ ";
        string cont = isLast ? "   " : "│  ";

        Console.WriteLine($"{prefix}{branch}[{scope.Name}]");

        string childPrefix = prefix + cont;

        // Символы этого скоупа
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

        // Дочерние скоупы (тела функций, классы)
        for (int i = 0; i < scope.Children.Count; i++)
            PrintScope(scope.Children[i], childPrefix, isLast: i == scope.Children.Count - 1);
    }
}