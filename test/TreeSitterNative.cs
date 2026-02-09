using System;
using System.Runtime.InteropServices;
using System.Text;

// Структура TSNode из tree-sitter
[StructLayout(LayoutKind.Sequential)]
public struct TSNode
{
    public uint context0;
    public uint context1;
    public uint context2;
    public uint context3;
    public IntPtr id;
    public IntPtr tree;
}

public class TreeSitterNative
{
    private const string TreeSitterDll = "tree_sitter.dll";
    private const string YourLanguageDll = "tree_sitter_pascal.dll";

    // Основные функции парсера
    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csharp_ts_parser_new();

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void csharp_ts_parser_delete(IntPtr parser);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool csharp_ts_parser_set_language(IntPtr parser, IntPtr ts_language);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csharp_ts_parser_parse_string(IntPtr parser, IntPtr old_tree,
        [MarshalAs(UnmanagedType.LPStr)] string source, uint length);

    // Функции для работы с деревом
    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern TSNode csharp_ts_tree_root_node(IntPtr tree);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void csharp_ts_tree_delete(IntPtr tree);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csharp_ts_node_string(TSNode node);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void csharp_ts_free_string(IntPtr str);

    // Функции для навигации по узлам
    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint csharp_ts_node_child_count(TSNode node);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern TSNode csharp_ts_node_child(TSNode node, uint index);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csharp_ts_node_type(TSNode node);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint csharp_ts_node_start_byte(TSNode node);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint csharp_ts_node_end_byte(TSNode node);

    [DllImport(TreeSitterDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool csharp_ts_node_is_named(TSNode node);

    // Функция для загрузки языка
    [DllImport(YourLanguageDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr tree_sitter_pascal();
}