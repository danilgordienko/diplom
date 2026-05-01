using System;
using System.Collections.Generic;

/// <summary>
/// Набор тестов грамматики PascalABC.NET.
/// Запускается через GrammarTests.RunAll(parser).
/// </summary>
public static class GrammarTests
{
    // ── Вывод дерева ────────────────────────────────────────────────────────

    public static void PrintTree(TreeSitterParser p, TSNode node, string code,
                                 string prefix = "", bool isLast = true)
    {
        var type = p.GetNodeType(node);
        var text = p.GetNodeText(node, code).Replace("\n", "↵");
        var isErr = type == "ERROR" || type == "MISSING";
        var marker = isErr ? " ⚠" : "";
        var branch = isLast ? "└─ " : "├─ ";
        var cont = isLast ? "   " : "│  ";
        var childCount = p.GetChildCount(node);
        var label = childCount == 0
            ? $"{type}{marker} \"{text}\""
            : $"{type}{marker}";

        Console.WriteLine($"{prefix}{branch}{label}");

        for (uint i = 0; i < childCount; i++)
            PrintTree(p, p.GetChild(node, i), code, prefix + cont, i == childCount - 1);
    }

    // ── Сбор ошибок ─────────────────────────────────────────────────────────

    static void CollectErrors(TreeSitterParser p, TSNode node, string code,
                              List<string> errors)
    {
        var type = p.GetNodeType(node);
        if (type == "ERROR" || type == "MISSING")
        {
            var text = p.GetNodeText(node, code).Replace("\n", "↵");
            errors.Add($"{type}: \"{text}\"");
        }
        uint n = p.GetChildCount(node);
        for (uint i = 0; i < n; i++)
            CollectErrors(p, p.GetChild(node, i), code, errors);
    }

    // ── Запуск одного теста ──────────────────────────────────────────────────

    static void Run(TreeSitterParser p, string name, string code, bool expectErrors = false)
    {
        Console.WriteLine();
        Console.WriteLine($"┌─ {name}");
        Console.WriteLine($"│  {code.Replace("\n", "↵")}");
        Console.WriteLine("│");

        p.Parse(code);
        var root = p.GetRootNode();
        var errors = new List<string>();
        CollectErrors(p, root, code, errors);

        PrintTree(p, root, code, "│  ");

        Console.WriteLine("│");
        if (errors.Count == 0)
        {
            Console.WriteLine($"└─ ✓  нет ошибок");
        }
        else
        {
            // Если ошибки ожидались — помечаем иначе
            var sign = expectErrors ? "⚠ (ожидаемые)" : "✗";
            Console.WriteLine($"└─ {sign}  ошибок: {errors.Count}");
            foreach (var e in errors)
                Console.WriteLine($"      {e}");
        }
    }

    // ── Все тесты ────────────────────────────────────────────────────────────

    public static void RunAll(TreeSitterParser p)
    {
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine("  Тесты грамматики PascalABC.NET — Уровень 1");
        Console.WriteLine("════════════════════════════════════════════════════════");

        // ── Тест на устойчивость к ошибкам ──────────────────────────────────
        // Tree-sitter должен восстановиться после ошибки и продолжить разбор.
        // Ожидаем ERROR-узлы, но дерево не должно быть пустым.
        Run(p, "ERR. Устойчивость к ошибке — невалидная конструкция посреди кода",
            "program Test; begin x := ???; writeln(x) end.",
            expectErrors: true);

        Run(p, "ERR-b. Ошибка в середине — корректный код до и после",
            "program Test; var n: integer; begin n := 10; n := @@@; writeln(n) end.",
            expectErrors: true);

        // ── УРОВЕНЬ 1: Токены и ключевые слова ──────────────────────────────

        Run(p, "1. Оператор ** (степень)",
            "program Test; begin x := 2 ** 10 end.");

        Run(p, "2. Оператор ?? (null-coalescing)",
            "program Test; begin x := a ?? b end.");

        Run(p, "3. Оператор ?. (null-safe dot)",
            "program Test; begin x := obj?.Name end.");

        Run(p, "4. Оператор ?[ (null-safe subscript)",
            "program Test; begin x := arr?[0] end.");

        Run(p, "5. foreach x in arr do",
            "program Test; begin foreach x in arr do writeln(x) end.");

        Run(p, "5a. foreach var x in arr do",
            "program Test; begin foreach var x in arr do writeln(x) end.");

        Run(p, "5b. foreach var x in arr index i do",
            "program Test; begin foreach var x in arr index i do writeln(i) end.");

        Run(p, "5c. foreach x in arr index i do",
            "program Test; begin foreach x in arr index i do writeln(i) end.");

        Run(p, "37. new TPoint(1, 2)",
            "program Test; begin p := new TPoint(1, 2) end.");

        Run(p, "37a. new TObject()",
            "program Test; begin obj := new TObject() end.");

        Run(p, "37b. new integer[10]",
            "program Test; begin arr := new integer[10] end.");

        Run(p, "37c. new integer[3, 4]",
            "program Test; begin m := new integer[3, 4] end.");

        Run(p, "38. var f: integer->integer",
            "program Test; var f: integer->integer; begin end.");

        Run(p, "38a. var f: integer->integer->boolean",
            "program Test; var f: integer->integer->boolean; begin end.");

        Run(p, "38b. procedure с параметром-функцией",
            "procedure Apply(f: integer->integer; x: integer); begin end;");

        Run(p, "39. exit",
            "procedure Foo; begin if x < 0 then exit end;");

        Run(p, "39a. exit(value)",
            "function Foo: integer; begin exit(42) end;");

        Run(p, "40. 1..10 как выражение",
            "program Test; begin x := 1..10 end.");

        Run(p, "40a. (1..10).Sum()",
            "program Test; begin s := (1..10).Sum() end.");

        Run(p, "41. for var i in 1..10 do",
            "program Test; begin for var i in 1..10 do writeln(i) end.");

        Run(p, "41a. for i in arr do",
            "program Test; begin for i in arr do writeln(i) end.");

        Run(p, "42. a[1..10]",
            "program Test; begin x := a[1..10] end.");

        Run(p, "43. case по строкам",
            "program Test; begin case s of 'hello': writeln('hi'); 'bye': writeln('bye') end end.");

        Run(p, "44. set of string",
            "program Test; var s: set of string; begin end.");

        Run(p, "45. m[:2, 1:3] многомерный срез",
            "program Test; begin x := m[:2, 1:3] end.");

        Run(p, "45a. m[0, 1:3]",
            "program Test; begin x := m[0, 1:3] end.");

        Run(p, "46. auto class(X, Y: real)",
            "type TPoint = auto class(X, Y: real);");

        Run(p, "46a. auto class(Name: string; Age: integer)",
            "type TPerson = auto class(Name: string; Age: integer);");

        Run(p, "47. function double(x) := x * 2",
            "function double(x: integer): integer := x * 2;");

        Run(p, "47a. function Max(a,b) := ...",
            "function Max(a, b: integer): integer := if a > b then a else b;");

        Run(p, "48. function integer.IsEven",
            "function integer.IsEven: boolean := self mod 2 = 0;");

        Run(p, "48a. function string.Repeat(n)",
            "function string.Repeat(n: integer): string; begin end;");

        Run(p, "6. loop N do (литерал)",
            "program Test; begin loop 5 do writeln('hi') end.");

        Run(p, "6a. loop expr do",
            "program Test; begin loop n * 2 do writeln('hi') end.");

        Run(p, "6b. loop N do begin ... end",
            "program Test; begin loop 3 do begin writeln('a'); writeln('b') end end.");

        Run(p, "21. Составное присваивание +=",
            "program Test; begin x += 1 end.");

        Run(p, "21a. Составное присваивание -=",
            "program Test; begin x -= 1 end.");

        Run(p, "21b. Составное присваивание *=",
            "program Test; begin x *= 2 end.");

        Run(p, "21c. Составное присваивание /=",
            "program Test; begin x /= 2 end.");

        Run(p, "7. yield expr",
            "function Seq: sequence of integer; begin yield 1 end;");

        Run(p, "8. match x with 1: ... end",
            "program Test; begin match x with 1: writeln('one'); 2: writeln('two') end end.");

        Run(p, "8a. match с wildcard _",
            "program Test; begin match x with 1: writeln('one'); _: writeln('other') end end.");

        Run(p, "8b. match с else",
            "program Test; begin match x with 1: writeln('one') else: writeln('other') end end.");

        Run(p, "8c. match с when guard",
            "program Test; begin match x with n when n > 0: writeln('pos'); _: writeln('neg') end end.");

        Run(p, "8e. match с паттерном коллекции",
            "program Test; begin match arr with [1,2]: writeln('two'); _: writeln('other') end end.");

        Run(p, "8f. match с деструктором",
            "program Test; begin match shape with Circle(r): writeln(r); Rect(w, h): writeln(w) end end.");

        Run(p, "8g. match несколько паттернов на ветку",
            "program Test; begin match x with 1, 2, 3: writeln('small'); _: writeln('big') end end.");

        Run(p, "9. async function / await expr",
            "async function Foo: integer; begin result := await Bar() end;");

        Run(p, "9a. await в присваивании",
            "program Test; begin x := await Foo() end.");

        Run(p, "24. new class(Field := val)",
            "program Test; begin p := new class(X := 1, Y := 2) end.");

        Run(p, "24a. new class с выражениями",
            "program Test; begin p := new class(Name := 'Alice', Age := n + 1) end.");

        Run(p, "25. auto property X: T",
            "type TFoo = class auto property X: integer; end;");

        Run(p, "25a. auto property с read/write",
            "type TFoo = class auto property Name: string read write; end;");

        Run(p, "10. lock obj do",
            "program Test; begin lock obj do writeln('ok') end.");

        Run(p, "11. for i := 1 to 10 step 2 do",
            "program Test; begin for i := 1 to 10 step 2 do writeln(i) end.");

        Run(p, "11a. for var i := 1 to 10 step 2 do",
            "program Test; begin for var i := 1 to 10 step 2 do writeln(i) end.");

        Run(p, "22. Срез a[1:5]",
            "program Test; begin x := a[1:5] end.");

        Run(p, "22a. Срез a[1:10:2]",
            "program Test; begin x := a[1:10:2] end.");

        Run(p, "22b. Срез a[:5]",
            "program Test; begin x := a[:5] end.");

        Run(p, "22c. Срез a[::2]",
            "program Test; begin x := a[::2] end.");

        Run(p, "22d. Обычный a[i] не сломан",
            "program Test; begin x := a[i] end.");

        Run(p, "23. Кортеж (a, b)",
            "program Test; begin t := (x, y) end.");

        Run(p, "23a. Кортеж (a, b, c)",
            "program Test; begin t := (x, y, z) end.");

        Run(p, "23b. Кортеж с выражениями",
            "program Test; begin t := (x + 1, y * 2) end.");

        Run(p, "12. BigInteger литерал 123bi",
            "program Test; begin x := 123bi end.");

        Run(p, "13. Форматная строка $'...'",
            "program Test; begin writeln($'Hello') end.");

        Run(p, "14. Многострочная строка '''...'''",
            "program Test; begin s := '''\nHello\n''' end.");

        Run(p, "15. Тип sequence of T",
            "program Test; var s: sequence of integer; begin end.");

        Run(p, "15a. sequence of T как тип локальной переменной",
            "procedure Gen; var s: sequence of integer; begin end;");

        Run(p, "16. Nullable тип T?",
            "program Test; var x: integer?; begin end.");

        Run(p, "16a. Nullable параметр",
            "procedure Foo(x: integer?); begin end;");

        Run(p, "26. default(integer)",
            "program Test; begin x := default(integer) end.");

        Run(p, "26a. default(TObject)",
            "program Test; begin obj := default(TObject) end.");

        Run(p, "26b. default(integer?)",
            "program Test; begin x := default(integer?) end.");

        Run(p, "27. sizeof(integer)",
            "program Test; begin n := sizeof(integer) end.");

        Run(p, "27a. sizeof(array of integer)",
            "program Test; begin n := sizeof(array of integer) end.");

        Run(p, "28. typeof(integer)",
            "program Test; begin t := typeof(integer) end.");

        Run(p, "28a. typeof(TObject)",
            "program Test; begin t := typeof(TObject) end.");

        Run(p, "29. where T: class на generic-типе",
            "type TBox<T> = class where T: class end;");

        Run(p, "29a. where T: IComparable",
            "type TBox<T> = class where T: IComparable end;");

        Run(p, "29b. where T: class на функции",
            "function Foo<T>(x: T): T where T: class; begin end;");

        Run(p, "29c. where T: class, IComparable",
            "type TBox<T> = class where T: class, IComparable end;");

        Run(p, "30. if x > 0 then x else -x",
            "program Test; begin y := if x > 0 then x else -x end.");

        Run(p, "30a. вложенный if-then-else",
            "program Test; begin y := if a then if b then 1 else 2 else 3 end.");

        Run(p, "30b. if-then-else как аргумент",
            "program Test; begin writeln(if x > 0 then 'pos' else 'neg') end.");

        Run(p, "31. List& как тип",
            "program Test; var x: List&; begin end.");

        Run(p, "31a. List&(integer) как тип",
            "program Test; var x: List&(integer); begin end.");

        Run(p, "31b. Pair&(integer, string) как тип",
            "program Test; var x: Pair&(integer, string); begin end.");

        Run(p, "31c. Pair&(integer, string) как выражение",
            "program Test; begin x := Pair&(integer, string) end.");

        Run(p, "17. namespace как заголовок юнита",
            "namespace MyLib;\ninterface\nvar x: integer;\nimplementation\nend.");

        Run(p, "17a. var x := 5 как statement",
            "program Test; begin var x := 42; writeln(x) end.");

        Run(p, "17b. var x: integer := 5 с типом",
            "program Test; begin var x: integer := 42; writeln(x) end.");

        Run(p, "17c. for var i := 1 to 10 do",
            "program Test; begin for var i := 1 to 5 do writeln(i) end.");

        Run(p, "17d. for var i := 10 downto 1 do",
            "program Test; begin for var i := 10 downto 1 do writeln(i) end.");

        Run(p, "18. Короткая программа ##",
            "## writeln('hello');");

        Run(p, "19. Массив |1, 2, 3|",
            "program Test; begin a := |1, 2, 3| end.");

        Run(p, "20. Числа с _ (1_000_000)",
            "program Test; begin x := 1_000_000 end.");

        Run(p, "20a. $FF_FF_FF",
            "program Test; begin x := $FF_FF_FF end.");

        // ── PascalABC.NET: лямбды ────────────────────────────────────────────

        // L1. Один параметр без скобок: x -> expr
        // До: identifier "x", -> неизвестный токен — ERROR
        // После: узел lambdaAbc с args=identifier, body=expr
        Run(p, "L1. x -> x * 2",
            "program Test; begin f := x -> x * 2 end.");

        // L2. Несколько параметров без типов: (x, y) -> expr
        // До: exprTuple + ERROR на ->
        // После: узел lambdaAbc, params — список identifier
        Run(p, "L2. (x, y) -> x + y",
            "program Test; begin f := (x, y) -> x + y end.");

        // L4. Лямбда без параметров: () -> expr
        // После: узел lambdaAbc с пустым params
        Run(p, "L4. () -> 42",
            "program Test; begin f := () -> 42 end.");

        // L5. Лямбда передаётся как аргумент функции
        // После: exprCall с аргументом lambdaAbc
        Run(p, "L5. Map(arr, x -> x * x)",
            "program Test; begin r := Map(arr, x -> x * x) end.");

        // L6. Лямбда в правой части присваивания с вызовом цепочки
        // После: exprCall на lambdaAbc
        Run(p, "L6. Where(arr, x -> x > 0)",
            "program Test; begin r := Where(arr, x -> x > 0) end.");

        // L7. Вложенные лямбды: x -> y -> x + y
        // Правоассоциативность: x -> (y -> x + y)
        Run(p, "L7. x -> y -> x + y (currying)",
            "program Test; begin f := x -> y -> x + y end.");

        // L8. Delphi-style: procedure(x: integer) begin writeln(x) end
        // Уже существующий lambda, проверяем что не сломали
        Run(p, "L8. procedure(x) begin ... end",
            "program Test; begin f := procedure(x: integer) begin writeln(x) end end.");

        // L9. Delphi-style со стрелкой: function(x: integer) -> x * 2
        // Короткое тело через -> в обычном lambda
        Run(p, "L9. function(x: integer) -> x * 2",
            "program Test; begin f := function(x: integer): integer -> x * 2 end.");

        // L10. Лямбда с одним параметром в foreach
        Run(p, "L10. foreach с лямбдой",
            "program Test; begin foreach x in Map(arr, i -> i + 1) do writeln(x) end.");



        // ════════════════════════════════════════════════════════════════════
        // УРОВЕНЬ 2: Конструкции подтверждённые официальной документацией
        // ════════════════════════════════════════════════════════════════════

        // ── Inline method bodies (тела методов внутри class/record) ──────────
        // Официальный сайт: "определение тел методов внутри классов"
        // https://pascalabc.net/pascalabc-net-eto

        // M1. Простая функция с begin/end внутри class
        Run(p, "M1. Inline function в class",
            "type TFoo = class\n" +
            "  function GetX: integer; begin result := 42 end;\n" +
            "end;");

        // M2. Inline-метод с локальной var-секцией
        Run(p, "M2. Inline method с var-секцией",
            "type TFoo = class\n" +
            "  function Sum(a, b: integer): integer;\n" +
            "  var r: integer;\n" +
            "  begin r := a + b; result := r end;\n" +
            "end;");

        // M3. Inline constructor в record
        Run(p, "M3. Inline constructor в record",
            "type TPoint = record\n" +
            "  X, Y: real;\n" +
            "  constructor Create(ax, ay: real); begin X := ax; Y := ay end;\n" +
            "end;");

        // M4. Inline procedure в секции public
        Run(p, "M4. Inline procedure в public-секции",
            "type TFoo = class\n" +
            "public\n" +
            "  procedure Reset; begin FValue := 0 end;\n" +
            "end;");

        // M5. Короткая форма := внутри class — НЕ поддерживается в PascalABC.NET.
        // Согласно документации, короткая форма допустима только на уровне unit,
        // внутри class тело обязательно через begin/end.
        Run(p, "M5. Короткая форма := в class (невалидно, ожидаем ошибки)",
            "type TFoo = class\n" +
            "  function Double(x: integer): integer := x * 2;\n" +
            "end;",
            expectErrors: true);

        // ── Атрибуты на параметрах ────────────────────────────────────────────
        // Delphi RTTI-атрибуты на параметрах — поддерживаются в PascalABC.NET
        // через совместимость с Delphi Object Pascal

        // A1. Простой атрибут без аргументов
        Run(p, "A1. [NotNull] x: integer",
            "procedure Foo([NotNull] x: integer); begin end;");

        // A2. Атрибут с аргументами
        Run(p, "A2. [Range(0, 100)] value: integer",
            "procedure SetValue([Range(0, 100)] value: integer); begin end;");

        // A3. Несколько атрибутов подряд на одном параметре
        Run(p, "A3. [NotNull][Validated] x: string",
            "procedure Foo([NotNull][Validated] x: string); begin end;");

        // A4. Атрибут перед модификатором var
        Run(p, "A4. [Out] var x: integer",
            "procedure Foo([Out] var x: integer); begin end;");

        // A5. Атрибуты на нескольких параметрах
        Run(p, "A5. Атрибуты на нескольких параметрах",
            "procedure Foo([NotNull] x: string; [Range(0,10)] n: integer); begin end;");

        // ── Упрощённый синтаксис модулей ──────────────────────────────────────
        // Официальная документация PascalABC.NET:
        // "Упрощенный синтаксис модулей без разделов интерфейса и реализации"
        // unit Foo; <описания> end.
        // unit Foo; <описания> begin <инициализация> end.

        // S1. unit с переменными и процедурой, без interface/implementation
        Run(p, "S1. simpleUnit с var и procedure",
            "unit MyUnit;\n" +
            "var x: integer;\n" +
            "procedure Foo; begin end;\n" +
            "end.");

        // S2. unit с разделом инициализации begin...end
        Run(p, "S2. simpleUnit с инициализацией begin...end",
            "unit MyUnit;\n" +
            "var x: integer;\n" +
            "begin\n" +
            "  x := 42;\n" +
            "end.");

        // S3. unit только с заголовком и end. (минимальный)
        // Пустой unit — только end. без описаний
        Run(p, "S3. simpleUnit минимальный (только end.)",
            "unit Empty;\nend.");

        // S4. unit с uses и type-секцией
        Run(p, "S4. simpleUnit с uses и type",
            "unit MyUnit;\n" +
            "uses SysUtils;\n" +
            "type TFoo = class end;\n" +
            "end.");

        // ── Открытые массивы (open arrays) ────────────────────────────────────
        // Стандарт Pascal/Delphi/PascalABC.NET: array of T без размерности
        // в параметрах. Уже работало через declArray с optional([...]).

        // O1. Простой открытый массив как параметр
        Run(p, "O1. array of integer как параметр",
            "procedure Foo(a: array of integer); begin end;");

        // O2. const array of T
        Run(p, "O2. const array of string",
            "procedure Print(const a: array of string); begin end;");

        // O3. var array of T плюс обычный параметр
        Run(p, "O3. var array of real с доп. параметром",
            "procedure Fill(var a: array of real; v: real); begin end;");

        // O4. Возвращаемый тип array of T — НЕ поддерживается ни в Delphi,
        // ни в PascalABC.NET: тип возврата ограничен typeref, не type.
        Run(p, "O4. function: array of integer (невалидно в Pascal)",
            "function GetArr: array of integer; begin end;",
            expectErrors: true);

        // O5. Вложенный открытый массив — array of array of T
        Run(p, "O5. array of array of integer",
            "procedure Foo(m: array of array of integer); begin end;");


        // ════════════════════════════════════════════════════════════════════
        // УРОВЕНЬ 3: Паттерны в match и частичное применение
        // ════════════════════════════════════════════════════════════════════

        // ── Паттерн диапазона в match — 1..10: stmt ───────────────────────────
        // Официальный синтаксис PascalABC.NET: числовой диапазон как паттерн

        // R1. Простой числовой диапазон
        Run(p, "R1. match: паттерн диапазона 1..10",
            "program Test; begin\n" +
            "  match x with\n" +
            "    1..10: writeln('small');\n" +
            "    _: writeln('big')\n" +
            "  end\n" +
            "end.");

        // R2. Несколько диапазонов в разных ветках
        Run(p, "R2. match: несколько диапазонов",
            "program Test; begin\n" +
            "  match n with\n" +
            "    1..5:   writeln('low');\n" +
            "    6..10:  writeln('mid');\n" +
            "    11..100: writeln('high');\n" +
            "    _: writeln('other')\n" +
            "  end\n" +
            "end.");

        // R3. Диапазон вместе с константой — несколько паттернов на ветку
        Run(p, "R3. match: диапазон и константа в одной ветке",
            "program Test; begin\n" +
            "  match n with\n" +
            "    0, 1..5: writeln('few');\n" +
            "    _: writeln('many')\n" +
            "  end\n" +
            "end.");

        // R4. Диапазон с when-guard
        Run(p, "R4. match: диапазон с when guard",
            "program Test; begin\n" +
            "  match n with\n" +
            "    1..100 when n mod 2 = 0: writeln('even');\n" +
            "    _: writeln('other')\n" +
            "  end\n" +
            "end.");

        // R5. Диапазон символов (char-range)
        Run(p, "R5. match: диапазон символов",
            "program Test; begin\n" +
            "  match c with\n" +
            "    'a'..'z': writeln('lower');\n" +
            "    'A'..'Z': writeln('upper');\n" +
            "    _: writeln('other')\n" +
            "  end\n" +
            "end.");

        // ── Паттерн типа в match — x: TFoo: stmt ─────────────────────────────
        // Синтаксис: переменная ':' тип — захват с проверкой типа (как is+cast)

        // T1. Простой паттерн типа
        Run(p, "T1. match: паттерн типа x: TCircle",
            "program Test; begin\n" +
            "  match shape with\n" +
            "    s: TCircle: writeln(s.Radius);\n" +
            "    _: writeln('unknown')\n" +
            "  end\n" +
            "end.");

        // T2. Несколько паттернов типа
        Run(p, "T2. match: несколько паттернов типа",
            "program Test; begin\n" +
            "  match shape with\n" +
            "    c: TCircle:  writeln(c.Radius);\n" +
            "    r: TRect:    writeln(r.Width);\n" +
            "    _: writeln('unknown')\n" +
            "  end\n" +
            "end.");

        // T3. Паттерн типа с when-guard
        Run(p, "T3. match: паттерн типа с when guard",
            "program Test; begin\n" +
            "  match shape with\n" +
            "    c: TCircle when c.Radius > 10: writeln('big circle');\n" +
            "    _: writeln('other')\n" +
            "  end\n" +
            "end.");

        // T4. Паттерн типа для базового класса
        Run(p, "T4. match: паттерн типа с точечным именем",
            "program Test; begin\n" +
            "  match obj with\n" +
            "    x: System.Exception: writeln(x.Message);\n" +
            "    _: writeln('ok')\n" +
            "  end\n" +
            "end.");

        // ── Частичное применение функций ──────────────────────────────────────
        // f: integer->integer->integer — функциональный тип
        // f(2) возвращает integer->integer — это обычный exprCall в грамматике,
        // семантика частичного применения обеспечивается системой типов

        // P1. Объявление переменной с функциональным типом и частичное применение
        Run(p, "P1. Частичное применение: var f: integer->integer->integer",
            "program Test;\n" +
            "var f: integer->integer->integer;\n" +
            "begin\n" +
            "  f := g;\n" +
            "  writeln(f(1)(2))\n" +
            "end.");

        // P2. Цепочка вызовов с функциональным типом
        Run(p, "P2. Цепочка вызовов f(1)(2)(3)",
            "program Test; begin\n" +
            "  writeln(add(1)(2))\n" +
            "end.");

        // P3. Функциональный тип как параметр процедуры
        Run(p, "P3. Параметр типа integer->integer->integer",
            "procedure Apply(f: integer->integer->integer; x: integer); begin\n" +
            "  writeln(f(x)(0))\n" +
            "end;");

        // P4. Частичное применение в присваивании
        Run(p, "P4. Присваивание результата частичного применения",
            "program Test;\n" +
            "var f: integer->integer;\n" +
            "    g: integer->integer->integer;\n" +
            "begin\n" +
            "  f := g(1)\n" +
            "end.");

        // P5. Трёхаргументный функциональный тип
        Run(p, "P5. integer->integer->integer->boolean",
            "program Test;\n" +
            "var pred: integer->integer->integer->boolean;\n" +
            "begin end.");

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine("  Готово");
        Console.WriteLine("════════════════════════════════════════════════════════");
    }
}