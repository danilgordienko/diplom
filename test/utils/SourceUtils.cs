using System;

/// <summary>
/// Утилиты для работы с байтовыми позициями в исходном коде.
/// </summary>
public static class SourceUtils
{
    /// <summary>
    /// Конвертирует байтовую позицию в номер строки и столбца (1-based).
    /// </summary>
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

    /// <summary>
    /// Конвертирует номер строки и столбца (1-based) в байтовую позицию.
    /// </summary>
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

    /// <summary>
    /// Вырезает строку кода, на которой находится байт bytePos.
    /// </summary>
    public static string ExtractLine(string source, int bytePos)
    {
        if (source.Length == 0) return "";
        int pos = Math.Min(bytePos, source.Length - 1);
        int start = pos;
        while (start > 0 && source[start - 1] != '\n') start--;
        int end = pos;
        while (end < source.Length && source[end] != '\n' && source[end] != '\r') end++;
        return source.Substring(start, end - start).Trim();
    }

    /// <summary>
    /// Конвертирует байтовую позицию в LSP-позицию (0-based).
    /// </summary>
    public static (int line, int character) ToLspPosition(string source, int bytePos)
    {
        var (line, col) = ByteToLineCol(source, bytePos);
        return (line - 1, col - 1);
    }

    /// <summary>
    /// Конвертирует LSP-позицию (0-based) в байтовую позицию.
    /// </summary>
    public static int FromLspPosition(string source, int lspLine, int lspCharacter)
        => LineColToByte(source, lspLine + 1, lspCharacter + 1);
}