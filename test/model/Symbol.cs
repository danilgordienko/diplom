using System.Collections.Generic;

/// <summary>
/// Вид символа — что именно объявлено.
/// </summary>
public enum SymbolKind
{
    // Модули
    Program,
    Unit,
    Namespace,
    Library,

    // Типы
    TypeAlias,      // type TFoo = ...
    Class,          // type TFoo = class ... end
    Record,         // type TFoo = record ... end
    Interface,      // type TFoo = interface ... end
    Enum,           // type TFoo = (A, B, C)
    EnumValue,      // A, B, C внутри enum
    AutoClass,      // type TFoo = auto class(X: T)

    // Процедуры и функции
    Procedure,
    Function,
    Constructor,
    Destructor,
    Operator,

    // Переменные и поля
    Variable,       // var x: T  /  var x := ...
    Parameter,      // параметр функции
    Field,          // поле класса/record
    Constant,       // const C = ...
    Property,       // property X: T

    // Прочее
    Label,
}

/// <summary>
/// Один символ в таблице: имя + откуда взят + к какому скоупу принадлежит.
/// </summary>
public class Symbol
{
    /// <summary>Имя как написано в исходнике.</summary>
    public string Name { get; }

    /// <summary>Вид символа.</summary>
    public SymbolKind Kind { get; }

    /// <summary>Скоуп, в котором символ объявлен.</summary>
    public Scope DeclaringScope { get; }

    /// <summary>Байтовый диапазон объявления в исходнике.</summary>
    public int StartByte { get; }
    public int EndByte { get; }

    /// <summary>
    /// Тип — строка из исходника (например "integer", "TFoo", "array of integer").
    /// null если тип выводится или не применим.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Для функций/процедур — вложенный скоуп с параметрами и локальными переменными.
    /// Для классов — скоуп с полями и методами.
    /// </summary>
    public Scope? InnerScope { get; set; }

    /// <summary>Сколько раз на этот символ сослались (для анализа неиспользуемых).</summary>
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
        TypeName != null
            ? $"{Kind} {Name}: {TypeName}"
            : $"{Kind} {Name}";
}

/// <summary>
/// Скоуп (область видимости): содержит символы и ссылку на родительский скоуп.
/// Образует дерево: unit → функция → вложенная функция → ...
/// </summary>
public class Scope
{
    private static int _nextId = 0;

    /// <summary>Уникальный идентификатор для отладки.</summary>
    public int Id { get; } = _nextId++;

    /// <summary>Человекочитаемое имя скоупа (имя функции, "program", etc.).</summary>
    public string Name { get; }

    /// <summary>Родительский скоуп. null только у корневого.</summary>
    public Scope? Parent { get; }

    /// <summary>Дочерние скоупы (тела функций, классы).</summary>
    public List<Scope> Children { get; } = new();

    // Символы этого скоупа — по имени в нижнем регистре (Pascal case-insensitive).
    private readonly Dictionary<string, Symbol> _symbols = new();

    public Scope(string name, Scope? parent)
    {
        Name = name;
        Parent = parent;
        parent?.Children.Add(this);
    }

    /// <summary>
    /// Добавить символ в этот скоуп.
    /// Если имя уже занято — возвращает false (дублирование объявления).
    /// </summary>
    public bool TryDefine(Symbol symbol, out Symbol? existing)
    {
        string key = symbol.Name.ToLowerInvariant();
        if (_symbols.TryGetValue(key, out existing))
            return false;

        _symbols[key] = symbol;
        existing = null;
        return true;
    }

    /// <summary>
    /// Поиск символа по имени только в этом скоупе (без подъёма вверх).
    /// </summary>
    public Symbol? LookupLocal(string name)
    {
        _symbols.TryGetValue(name.ToLowerInvariant(), out var sym);
        return sym;
    }

    /// <summary>
    /// Поиск символа по имени: сначала в этом скоупе, потом в родительских.
    /// Возвращает null если не найден нигде.
    /// </summary>
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
    public IEnumerable<Symbol> Symbols => _symbols.Values;

    public override string ToString() => $"Scope#{Id}({Name})";
}