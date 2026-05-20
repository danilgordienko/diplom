using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// LSP-сервер для Pascal/PascalABC.NET на основе Language Server Protocol 3.17.
///
/// Транспорт: stdio (stdin → запросы, stdout → ответы).
/// Кодировка:  UTF-8, заголовок "Content-Length: N\r\n\r\n".
///
/// Реализованные методы:
///   initialize               — handshake; запоминаем rootPath, загружаем все .pas файлы проекта
///   initialized              — уведомление (ответа нет)
///   shutdown / exit          — корректное завершение
///   textDocument/didOpen     — клиент открыл файл (перезаписывает версию с диска)
///   textDocument/didChange   — клиент прислал новую версию файла
///   textDocument/didClose    — клиент закрыл файл (возвращаем версию с диска)
///   textDocument/definition  — все объявления символа под курсором (по всему проекту)
///   textDocument/references  — все вхождения символа (по всему проекту)
///   workspace/symbol         — поиск символа по имени во всём проекте 
///
/// Диагностики:
///   При didOpen и didChange сервер анализирует файл и отправляет
///   textDocument/publishDiagnostics с синтаксическими ошибками (из tree-sitter AST)
///   и семантическими предупреждениями (дублирование объявлений).
///
/// Логика "по всему проекту":
///   При initialize сервер читает rootPath и загружает все .pas файлы в _documents.
///   textDocument/definition и textDocument/references ищут сначала имя символа
///   в текущем файле, затем ищут объявления и вхождения во ВСЕХ файлах проекта.
///
/// ВСЕ логи пишутся в stderr, чтобы не засорять stdout (LSP-канал).
/// </summary>
public class LspServer
{
    private readonly TreeSitterParser _parser;

    // Путь к корневой папке проекта (из initialize → rootUri/rootPath)
    private string? _rootPath;

    // Все документы проекта: uri → текст файла
    // Заполняется при initialize из файловой системы,
    // перезаписывается при didOpen/didChange редактором.
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

    // Кэш анализа: uri → AnalysisResult. Сбрасывается при изменении файла.
    private readonly Dictionary<string, AnalysisResult> _cache = new(StringComparer.Ordinal);

    private bool _shutdownRequested = false;

    // Ссылка на stdout — нужна для отправки уведомлений (diagnostics)
    private BinaryWriter? _stdout;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public LspServer(TreeSitterParser parser)
    {
        _parser = parser;
    }

    // ── Главный цикл ─────────────────────────────────────────────────────────

    public void Run()
    {
        Log("LSP сервер запущен, ожидаю сообщения...");

        using var stdin = new BinaryReader(Console.OpenStandardInput());
        _stdout = new BinaryWriter(Console.OpenStandardOutput());

        while (true)
        {
            try
            {
                string? message = ReadMessage(stdin);
                if (message == null) break;

                Log($"← {Truncate(message, 300)}");

                string? response = HandleMessage(message);
                if (response != null)
                {
                    Log($"→ {Truncate(response, 300)}");
                    WriteMessage(_stdout, response);
                }

                if (_shutdownRequested && message.Contains("\"method\":\"exit\""))
                    break;
            }
            catch (EndOfStreamException) { break; }
            catch (Exception ex) { Log($"ОШИБКА в цикле: {ex.Message}"); }
        }

        _stdout.Dispose();
        Log("LSP сервер завершён.");
    }

    // ── Транспорт ─────────────────────────────────────────────────────────────

    private static string? ReadMessage(BinaryReader reader)
    {
        int contentLength = -1;

        while (true)
        {
            var lineBuilder = new StringBuilder();
            while (true)
            {
                int b = reader.BaseStream.ReadByte();
                if (b == -1) return null;
                if (b == '\r')
                {
                    int next = reader.BaseStream.ReadByte();
                    if (next == '\n') break;
                    lineBuilder.Append((char)b);
                    if (next != -1) lineBuilder.Append((char)next);
                }
                else if (b == '\n') break;
                else lineBuilder.Append((char)b);
            }

            string line = lineBuilder.ToString();
            if (line.Length == 0) break;

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                if (int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out int len))
                    contentLength = len;
        }

        if (contentLength <= 0)
            throw new InvalidDataException($"Неверный Content-Length: {contentLength}");

        byte[] body = reader.ReadBytes(contentLength);
        return body.Length != contentLength ? null : Encoding.UTF8.GetString(body);
    }

    private static void WriteMessage(BinaryWriter writer, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        writer.Write(header);
        writer.Write(body);
        writer.Flush();
    }

    // ── Диспетчер ────────────────────────────────────────────────────────────

    private string? HandleMessage(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return ErrorResponse(null, -32700, "Parse error"); }

        if (root == null) return ErrorResponse(null, -32700, "Parse error");

        var id = root["id"];
        var method = root["method"]?.GetValue<string>();

        if (method == null)
            return ErrorResponse(id, -32600, "Invalid Request: missing method");

        try
        {
            return method switch
            {
                "initialize" => HandleInitialize(id, root["params"]),
                "initialized" => null,
                "shutdown" => HandleShutdown(id),
                "exit" => null,
                "textDocument/didOpen" => HandleDidOpen(root["params"]),
                "textDocument/didChange" => HandleDidChange(root["params"]),
                "textDocument/didClose" => HandleDidClose(root["params"]),
                "textDocument/definition" => HandleDefinition(id, root["params"]),
                "textDocument/references" => HandleReferences(id, root["params"]),
                "workspace/symbol" => HandleWorkspaceSymbol(id, root["params"]),
                "pascal/findSymbol" => HandleFindSymbol(id, root["params"]),
                _ => id != null ? ErrorResponse(id, -32601, $"Method not found: {method}") : null,
            };
        }
        catch (Exception ex)
        {
            Log($"ОШИБКА в '{method}': {ex}");
            return id != null ? ErrorResponse(id, -32603, ex.Message) : null;
        }
    }

    // ── initialize ────────────────────────────────────────────────────────────

    private string HandleInitialize(JsonNode? id, JsonNode? p)
    {
        // Извлекаем корневую папку проекта
        // LSP передаёт rootUri (предпочтительно) или устаревший rootPath
        string? rootUri = p?["rootUri"]?.GetValue<string>();
        string? rootPath = p?["rootPath"]?.GetValue<string>();

        _rootPath = rootUri != null ? UriToPath(rootUri) : rootPath;

        if (_rootPath != null && Directory.Exists(_rootPath))
        {
            Log($"Корень проекта: {_rootPath}");
            LoadWorkspaceFiles(_rootPath);
        }
        else
        {
            Log("rootPath не передан или папка не существует — работаем только с открытыми файлами.");
        }

        var capabilities = new
        {
            textDocumentSync = new { openClose = true, change = 1 },
            definitionProvider = true,
            referencesProvider = true,
            workspaceSymbolProvider = true,
        };

        return SuccessResponse(id, new
        {
            capabilities,
            serverInfo = new { name = "pascal-lsp", version = "1.0.0" },
        });
    }

    // ── Загрузка всех .pas файлов проекта ────────────────────────────────────

    private void LoadWorkspaceFiles(string rootPath)
    {
        var files = Directory.GetFiles(rootPath, "*.pas", SearchOption.AllDirectories);
        int loaded = 0;

        foreach (var filePath in files)
        {
            try
            {
                string text = ReadFile(filePath);
                if (text.Length == 0) continue;

                string uri = PathToUri(filePath);
                // Не перезаписываем то, что уже прислал редактор через didOpen
                if (!_documents.ContainsKey(uri))
                    _documents[uri] = text;

                loaded++;
            }
            catch (Exception ex)
            {
                Log($"Не удалось прочитать {filePath}: {ex.Message}");
            }
        }

        Log($"Загружено файлов проекта: {loaded} из {files.Length}");
    }

    // ── shutdown ──────────────────────────────────────────────────────────────

    private string HandleShutdown(JsonNode? id)
    {
        _shutdownRequested = true;
        return SuccessResponse(id, null);
    }

    // ── didOpen / didChange / didClose ────────────────────────────────────────

    private string? HandleDidOpen(JsonNode? p)
    {
        string? uri = p?["textDocument"]?["uri"]?.GetValue<string>();
        string? text = p?["textDocument"]?["text"]?.GetValue<string>();
        if (uri == null || text == null) return null;

        _documents[uri] = text;
        _cache.Remove(uri);
        Log($"didOpen: {uri}");

        // Отправляем диагностики для этого файла
        PublishDiagnostics(uri, text);

        return null;
    }

    private string? HandleDidChange(JsonNode? p)
    {
        string? uri = p?["textDocument"]?["uri"]?.GetValue<string>();
        string? text = p?["contentChanges"]?[0]?["text"]?.GetValue<string>();
        if (uri == null || text == null) return null;

        _documents[uri] = text;
        _cache.Remove(uri);
        Log($"didChange: {uri}");

        // Обновляем диагностики при каждом изменении
        PublishDiagnostics(uri, text);

        return null;
    }

    private string? HandleDidClose(JsonNode? p)
    {
        string? uri = p?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri == null) return null;

        // При закрытии очищаем диагностики для файла
        ClearDiagnostics(uri);

        _cache.Remove(uri);
        string? filePath = UriToPath(uri);
        if (filePath != null && File.Exists(filePath))
        {
            try
            {
                _documents[uri] = ReadFile(filePath);
                Log($"didClose (восстановлен с диска): {uri}");
                return null;
            }
            catch { }
        }

        _documents.Remove(uri);
        Log($"didClose (удалён из кэша): {uri}");
        return null;
    }

    // ── Диагностики ──────────────────────────────────────────────────────────

    /// <summary>
    /// Анализирует файл и отправляет диагностики клиенту.
    /// Вызывается при didOpen и didChange.
    /// </summary>
    private void PublishDiagnostics(string uri, string source)
    {
        try
        {
            var analysis = GetOrAnalyze(uri, source);

            var collector = new DiagnosticCollector(_parser);
            var diagnostics = collector.Collect(source, analysis);

            Log($"diagnostics: {uri} → {diagnostics.Count} ошибок");

            // Формируем массив LSP Diagnostic
            var lspDiags = diagnostics.Select(d => new
            {
                range = new
                {
                    start = new { line = d.StartLine, character = d.StartChar },
                    end = new { line = d.EndLine, character = d.EndChar },
                },
                severity = d.Severity,
                source = "pascal-lsp",
                message = d.Message,
            }).ToArray();

            // Отправляем уведомление textDocument/publishDiagnostics
            SendNotification("textDocument/publishDiagnostics", new
            {
                uri,
                diagnostics = lspDiags,
            });
        }
        catch (Exception ex)
        {
            Log($"ОШИБКА при сборе диагностик: {ex.Message}");
        }
    }

    /// <summary>
    /// Очищает диагностики при закрытии файла.
    /// </summary>
    private void ClearDiagnostics(string uri)
    {
        SendNotification("textDocument/publishDiagnostics", new
        {
            uri,
            diagnostics = Array.Empty<object>(),
        });
    }

    /// <summary>
    /// Отправляет уведомление (notification) клиенту — без id, без ответа.
    /// </summary>
    private void SendNotification(string method, object @params)
    {
        if (_stdout == null) return;

        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = JsonSerializer.SerializeToNode(@params, _jsonOpts),
        };

        string json = obj.ToJsonString(_jsonOpts);
        Log($"→ notify: {Truncate(json, 200)}");
        WriteMessage(_stdout, json);
    }

    // ── textDocument/definition ───────────────────────────────────────────────

    /// <summary>
    /// Шаг 1: по позиции курсора определяем имя символа в текущем файле.
    /// Шаг 2: ищем объявления с этим именем во ВСЕХ файлах проекта.
    /// </summary>
    private string HandleDefinition(JsonNode? id, JsonNode? p)
    {
        var (uri, source, lspLine, lspChar) = ExtractPositionParams(p);
        if (uri == null) return SuccessResponse(id, null);

        var currentResult = GetOrAnalyze(uri, source!);
        int bytePos = SourceUtils.FromLspPosition(source!, lspLine, lspChar);
        var defAtPos = SymbolAnalyzer.FindDefinitionAtByte(currentResult, bytePos);

        if (defAtPos == null) return SuccessResponse(id, null);

        string symbolName = defAtPos.Symbol.Name;
        Log($"definition: ищу '{symbolName}' в {_documents.Count} файлах");

        var locations = FindDefinitionsAcrossProject(symbolName);

        object? result = locations.Count switch
        {
            0 => null,
            1 => (object)locations[0],
            _ => (object)locations,
        };

        return SuccessResponse(id, result);
    }

    // ── textDocument/references ───────────────────────────────────────────────

    /// <summary>
    /// Шаг 1: по позиции курсора определяем имя символа.
    /// Шаг 2: собираем объявления (если includeDeclaration) и вхождения
    ///         из ВСЕХ файлов проекта.
    /// </summary>
    private string HandleReferences(JsonNode? id, JsonNode? p)
    {
        var (uri, source, lspLine, lspChar) = ExtractPositionParams(p);
        if (uri == null) return SuccessResponse(id, Array.Empty<object>());

        bool includeDecl = p?["context"]?["includeDeclaration"]?.GetValue<bool>() ?? false;

        var currentResult = GetOrAnalyze(uri, source!);
        int bytePos = SourceUtils.FromLspPosition(source!, lspLine, lspChar);
        var defAtPos = SymbolAnalyzer.FindDefinitionAtByte(currentResult, bytePos);

        if (defAtPos == null) return SuccessResponse(id, Array.Empty<object>());

        string symbolName = defAtPos.Symbol.Name;
        Log($"references: ищу '{symbolName}' в {_documents.Count} файлах");

        var locations = new List<object>();

        if (includeDecl)
            locations.AddRange(FindDefinitionsAcrossProject(symbolName));

        locations.AddRange(FindReferencesAcrossProject(symbolName));

        return SuccessResponse(id, locations);
    }

    // ── workspace/symbol ──────────────────────────────────────────────────────

    /// <summary>
    /// workspace/symbol — стандартный LSP-метод для навигации.
    /// Ищет по подстроке (по спеку LSP так и должно быть — это поиск для
    /// быстрой навигации, не точный поиск переменной).
    /// Для точного поиска используй pascal/findSymbol.
    /// </summary>
    private string HandleWorkspaceSymbol(JsonNode? id, JsonNode? p)
    {
        string query = p?["query"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(query))
            return SuccessResponse(id, Array.Empty<object>());

        Log($"workspace/symbol (подстрока): '{query}' в {_documents.Count} файлах");

        var results = new List<object>();
        foreach (var (uri, source) in _documents)
        {
            AnalysisResult analysis;
            try { analysis = GetOrAnalyze(uri, source); }
            catch { continue; }

            // Contains — поиск по подстроке, это поведение по LSP-спеку
            CollectMatchingSymbols(analysis.Table.Root, query,
                exactMatch: false, source, uri, results);
        }

        Log($"workspace/symbol: найдено {results.Count} символов");
        return SuccessResponse(id, results);
    }

    /// <summary>
    /// pascal/findSymbol — кастомный метод (расширение LSP).
    /// Точный поиск переменной по имени во всём проекте.
    /// Возвращает: объявления (definitions) + все вхождения (references).
    /// Параметры: { "name": "ИМЯ_ПЕРЕМЕННОЙ" }
    /// Это прямой аналог режима --find через протокол LSP.
    /// </summary>
    private string HandleFindSymbol(JsonNode? id, JsonNode? p)
    {
        string name = p?["name"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return ErrorResponse(id, -32602, "Параметр 'name' обязателен");

        Log($"pascal/findSymbol (точный): '{name}' в {_documents.Count} файлах");

        // Объявления — где переменная описана
        var definitions = new List<object>();
        // Вхождения — где переменная используется
        var references = new List<object>();

        foreach (var (uri, source) in _documents)
        {
            AnalysisResult analysis;
            try { analysis = GetOrAnalyze(uri, source); }
            catch { continue; }

            // Объявления с точным именем
            var defs = SymbolAnalyzer.FindDefinitionsByName(analysis, name);
            foreach (var def in defs)
            {
                definitions.Add(new
                {
                    uri,
                    range = MakeRange(source, def.Symbol.StartByte, def.Symbol.EndByte),
                    kind = def.Symbol.Kind.ToString(),
                    scope = def.Symbol.DeclaringScope.Name,
                    typeName = def.Symbol.TypeName,
                });
            }

            // Вхождения (использования)
            var refs = SymbolAnalyzer.FindReferencesByName(analysis, name);
            foreach (var r in refs.References)
            {
                references.Add(new
                {
                    uri,
                    range = MakeRange(source, r.StartByte, r.EndByte),
                });
            }
        }

        Log($"pascal/findSymbol: объявлений={definitions.Count}, вхождений={references.Count}");

        return SuccessResponse(id, new
        {
            name,
            definitions,
            references,
            totalFiles = _documents.Count,
        });
    }

    // ── Поиск по всему проекту ────────────────────────────────────────────────

    private List<object> FindDefinitionsAcrossProject(string symbolName)
    {
        var locations = new List<object>();
        foreach (var (uri, source) in _documents)
        {
            AnalysisResult analysis;
            try { analysis = GetOrAnalyze(uri, source); }
            catch { continue; }

            var defs = SymbolAnalyzer.FindDefinitionsByName(analysis, symbolName);
            foreach (var def in defs)
                locations.Add(new { uri, range = MakeRange(source, def.Symbol.StartByte, def.Symbol.EndByte) });
        }
        return locations;
    }

    private List<object> FindReferencesAcrossProject(string symbolName)
    {
        var locations = new List<object>();
        foreach (var (uri, source) in _documents)
        {
            AnalysisResult analysis;
            try { analysis = GetOrAnalyze(uri, source); }
            catch { continue; }

            var refs = SymbolAnalyzer.FindReferencesByName(analysis, symbolName);
            foreach (var r in refs.References)
                locations.Add(new { uri, range = MakeRange(source, r.StartByte, r.EndByte) });
        }
        return locations;
    }

    private static void CollectMatchingSymbols(
        Scope scope, string query, bool exactMatch, string source, string uri, List<object> results)
    {
        foreach (var sym in scope.Symbols)
        {
            bool match = exactMatch
                ? sym.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
                : sym.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (match)
            {
                results.Add(new
                {
                    name = sym.Name,
                    kind = SymbolKindToLsp(sym.Kind),
                    location = new { uri, range = MakeRange(source, sym.StartByte, sym.EndByte) },
                    containerName = sym.DeclaringScope.Name,
                });
            }
        }
        foreach (var child in scope.Children)
            CollectMatchingSymbols(child, query, exactMatch, source, uri, results);
    }

    // ── Утилиты ───────────────────────────────────────────────────────────────

    // ── Утилиты ───────────────────────────────────────────────────────────────

    private AnalysisResult GetOrAnalyze(string uri, string source)
    {
        if (_cache.TryGetValue(uri, out var cached)) return cached;
        var result = new SymbolAnalyzer(_parser).Analyze(source);
        _cache[uri] = result;
        return result;
    }

    private (string? uri, string? source, int lspLine, int lspChar)
        ExtractPositionParams(JsonNode? p)
    {
        string? uri = p?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri == null) return (null, null, 0, 0);

        if (!_documents.TryGetValue(uri, out string? source))
        {
            Log($"Документ не найден: {uri}");
            return (null, null, 0, 0);
        }

        int line = p?["position"]?["line"]?.GetValue<int>() ?? 0;
        int ch = p?["position"]?["character"]?.GetValue<int>() ?? 0;
        return (uri, source, line, ch);
    }

    private static object MakeRange(string source, int startByte, int endByte)
    {
        var (sl, sc) = SourceUtils.ToLspPosition(source, startByte);
        var (el, ec) = SourceUtils.ToLspPosition(source, endByte);
        return new
        {
            start = new { line = sl, character = sc },
            end = new { line = el, character = ec },
        };
    }

    private static int SymbolKindToLsp(SymbolKind kind) => kind switch
    {
        SymbolKind.Program or SymbolKind.Unit or
        SymbolKind.Namespace or SymbolKind.Library => 2,   // Module
        SymbolKind.Class => 5,   // Class
        SymbolKind.Record => 23,  // Struct
        SymbolKind.Interface => 11,  // Interface
        SymbolKind.Enum => 10,  // Enum
        SymbolKind.EnumValue => 22,  // EnumMember
        SymbolKind.Function or SymbolKind.Procedure => 12,  // Function
        SymbolKind.Constructor => 9,   // Constructor
        SymbolKind.Operator => 25,  // Operator
        SymbolKind.Field => 8,   // Field
        SymbolKind.Constant => 14,  // Constant
        SymbolKind.Property => 7,   // Property
        SymbolKind.TypeAlias => 26,  // TypeParameter
        _ => 13,  // Variable
    };

    // ── Конвертация URI ↔ путь ────────────────────────────────────────────────

    private static string? UriToPath(string uri)
    {
        try { return Uri.UnescapeDataString(new Uri(uri).LocalPath); }
        catch { return null; }
    }

    private static string PathToUri(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/')) normalized = '/' + normalized;
        return "file://" + Uri.EscapeUriString(normalized);
    }

    // ── Чтение файлов с определением кодировки ───────────────────────────────

    /// <summary>
    /// Читает файл с автоматическим определением кодировки (UTF-8/16, Windows-1251).
    /// </summary>
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

    // ── JSON-RPC ──────────────────────────────────────────────────────────────

    private static string SuccessResponse(JsonNode? id, object? result)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? JsonValue.Create(id.GetValue<int>()) : JsonValue.Create((int?)null),
            ["result"] = result != null
                            ? JsonSerializer.SerializeToNode(result, _jsonOpts)
                            : JsonValue.Create((string?)null),
        };
        return obj.ToJsonString(_jsonOpts);
    }

    private static string ErrorResponse(JsonNode? id, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? JsonValue.Create(id.GetValue<int>()) : JsonValue.Create((int?)null),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
        return obj.ToJsonString(_jsonOpts);
    }

    // ── Лог ──────────────────────────────────────────────────────────────────

    private static void Log(string msg)
        => Console.Error.WriteLine($"[LSP] {DateTime.Now:HH:mm:ss.fff}  {msg}");

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";
}