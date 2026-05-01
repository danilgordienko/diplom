using System.Collections.Generic;

/// <summary>
/// Одно вхождение символа в исходном коде — не объявление, а использование.
/// </summary>
public class Reference
{
    /// <summary>Символ, на который ссылаются.</summary>
    public Symbol Symbol { get; }

    /// <summary>Байтовая позиция начала идентификатора в исходнике.</summary>
    public int StartByte { get; }

    /// <summary>Байтовая позиция конца идентификатора.</summary>
    public int EndByte { get; }

    /// <summary>Строка исходника для отображения в консоли (одна строка кода).</summary>
    public string LinePreview { get; }

    /// <summary>Номер строки (1-based) — вычисляется из байта.</summary>
    public int Line { get; }

    /// <summary>Номер столбца (1-based) — вычисляется из байта.</summary>
    public int Column { get; }

    public Reference(Symbol symbol, int startByte, int endByte, string source)
    {
        Symbol = symbol;
        StartByte = startByte;
        EndByte = endByte;

        // Вычисляем строку и столбец
        (Line, Column) = ByteToLineCol(source, startByte);

        // Вырезаем строку кода для предпросмотра
        LinePreview = ExtractLine(source, startByte);
    }

    // ── Вспомогательные ─────────────────────────────────────────────────────

    private static (int line, int col) ByteToLineCol(string source, int bytePos)
    {
        if (bytePos <= 0) return (1, 1);
        int line = 1, col = 1;
        int end = System.Math.Min(bytePos, source.Length);
        for (int i = 0; i < end; i++)
        {
            if (source[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return (line, col);
    }

    private static string ExtractLine(string source, int bytePos)
    {
        if (source.Length == 0) return "";
        int pos = System.Math.Min(bytePos, source.Length - 1);

        // Идём влево до начала строки
        int start = pos;
        while (start > 0 && source[start - 1] != '\n') start--;

        // Идём вправо до конца строки
        int end = pos;
        while (end < source.Length && source[end] != '\n' && source[end] != '\r') end++;

        return source.Substring(start, end - start).Trim();
    }

    public override string ToString() =>
        $"{Line}:{Column}  {LinePreview}";
}

/// <summary>
/// Индекс всех вхождений: Symbol → список Reference.
/// Также позволяет найти символ по байтовой позиции курсора.
/// </summary>
public class ReferenceIndex
{
    // Вхождения по символу
    private readonly Dictionary<Symbol, List<Reference>> _refs = new();

    // Все вхождения отсортированные по байту — для быстрого поиска по позиции
    private readonly List<Reference> _all = new();
    private bool _sorted = false;

    // ── Наполнение ───────────────────────────────────────────────────────────

    public void Add(Reference r)
    {
        if (!_refs.TryGetValue(r.Symbol, out var list))
        {
            list = new List<Reference>();
            _refs[r.Symbol] = list;
        }
        list.Add(r);
        _all.Add(r);
        _sorted = false;
    }

    // ── Запросы ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Найти символ, чьё вхождение содержит данный байт.
    /// Используется для запроса "что стоит под курсором".
    /// </summary>
    public Symbol? FindSymbolAtByte(int bytePos)
    {
        EnsureSorted();
        foreach (var r in _all)
        {
            if (r.StartByte <= bytePos && bytePos < r.EndByte)
                return r.Symbol;
        }
        return null;
    }

    /// <summary>
    /// Все вхождения данного символа (кроме самого объявления).
    /// </summary>
    public IReadOnlyList<Reference> GetReferences(Symbol symbol)
    {
        _refs.TryGetValue(symbol, out var list);
        return list ?? (IReadOnlyList<Reference>)System.Array.Empty<Reference>();
    }

    /// <summary>Все вхождения всех символов.</summary>
    public IReadOnlyList<Reference> All => _all;

    // ── Приватное ────────────────────────────────────────────────────────────

    private void EnsureSorted()
    {
        if (_sorted) return;
        _all.Sort((a, b) => a.StartByte.CompareTo(b.StartByte));
        _sorted = true;
    }
}