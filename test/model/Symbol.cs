using System.Collections.Generic;

/// <summary>
/// Вид символа — что именно объявлено.
/// </summary>
public enum SymbolKind
{
    Program, Unit, Namespace, Library,
    TypeAlias, Class, Record, Interface, Enum, EnumValue, AutoClass,
    Procedure, Function, Constructor, Destructor, Operator,
    Variable, Parameter, Field, Constant, Property,
    Label,
}

/// <summary>
/// Один символ в таблице: имя + откуда взят + к какому скоупу принадлежит.
/// </summary>
public class Symbol
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public Scope DeclaringScope { get; }
    public int StartByte { get; }
    public int EndByte { get; }

    /// <summary>
    /// Тип — строка из исходника (например "integer", "TFoo").
    /// null если тип выводится или не применим.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Для функций/классов — вложенный скоуп с параметрами и локальными переменными.
    /// </summary>
    public Scope? InnerScope { get; set; }

    /// <summary>Сколько раз на этот символ сослались.</summary>
    public int UseCount { get; set; }

    public Symbol(string name, SymbolKind kind, Scope declaringScope, int startByte, int endByte)
    {
        Name = name;
        Kind = kind;
        DeclaringScope = declaringScope;
        StartByte = startByte;
        EndByte = endByte;
    }

    public override string ToString() =>
        TypeName != null ? $"{Kind} {Name}: {TypeName}" : $"{Kind} {Name}";
}

/// <summary>
/// Скоуп (область видимости): содержит символы и ссылку на родительский скоуп.
/// </summary>
public class Scope
{
    private static int _nextId = 0;
    public int Id { get; } = _nextId++;
    public string Name { get; }
    public Scope? Parent { get; }
    public List<Scope> Children { get; } = new();

    private readonly Dictionary<string, Symbol> _symbols = new();

    /// <summary>
    /// Все перегруженные символы — несколько функций/процедур с одним именем.
    /// Хранятся отдельно от _symbols (там лежит первый).
    /// </summary>
    private readonly Dictionary<string, List<Symbol>> _overloads = new();

    public Scope(string name, Scope? parent)
    {
        Name = name;
        Parent = parent;
        parent?.Children.Add(this);
    }

    /// <summary>
    /// Добавить символ в этот скоуп.
    /// Если имя уже занято — разрешает перегрузку для функций/процедур/конструкторов/операторов.
    /// </summary>
    public bool TryDefine(Symbol symbol, out Symbol? existing)
    {
        string key = symbol.Name.ToLowerInvariant();

        if (_symbols.TryGetValue(key, out existing))
        {
            if (IsOverloadable(existing.Kind) && IsOverloadable(symbol.Kind))
            {
                if (!_overloads.ContainsKey(key))
                    _overloads[key] = new List<Symbol> { existing };
                _overloads[key].Add(symbol);
                existing = null;
                return true;
            }
            return false;
        }

        _symbols[key] = symbol;
        existing = null;
        return true;
    }

    public Symbol? LookupLocal(string name)
    {
        _symbols.TryGetValue(name.ToLowerInvariant(), out var sym);
        return sym;
    }

    public Symbol? Lookup(string name)
    {
        string key = name.ToLowerInvariant();
        Scope? cur = this;
        while (cur != null)
        {
            if (cur._symbols.TryGetValue(key, out var sym))
                return sym;
            cur = cur.Parent;
        }
        return null;
    }

    /// <summary>Все символы этого скоупа (для итерации).</summary>
    public IEnumerable<Symbol> Symbols
    {
        get
        {
            foreach (var sym in _symbols.Values)
                yield return sym;
            foreach (var list in _overloads.Values)
                foreach (var sym in list)
                    if (!_symbols.ContainsValue(sym))
                        yield return sym;
        }
    }

    private static bool IsOverloadable(SymbolKind kind) =>
        kind is SymbolKind.Function
            or SymbolKind.Procedure
            or SymbolKind.Constructor
            or SymbolKind.Destructor
            or SymbolKind.Operator;

    public override string ToString() => $"Scope#{Id}({Name})";
}