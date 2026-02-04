using System;
using System.Runtime.InteropServices;
using System.Text;

public class TreeSitterParser : IDisposable
{
    private IntPtr _parser;
    private IntPtr _tree;
    private string _lastSourceCode = "";
    private bool _disposed = false;

    public TreeSitterParser()
    {
        _parser = TreeSitterNative.csharp_ts_parser_new();
        if (_parser == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create parser");
        }

        IntPtr ts_language = TreeSitterNative.tree_sitter_calc();
        if (ts_language == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to load language");
        }

        bool result = TreeSitterNative.csharp_ts_parser_set_language(_parser, ts_language);
        if (!result)
        {
            throw new InvalidOperationException("Failed to set language for parser");
        }
    }

    public void Parse(string sourceCode)
    {
        if (_tree != IntPtr.Zero)
        {
            TreeSitterNative.csharp_ts_tree_delete(_tree);
            _tree = IntPtr.Zero;
        }

        _tree = TreeSitterNative.csharp_ts_parser_parse_string(_parser, IntPtr.Zero, sourceCode, (uint)sourceCode.Length);

        if (_tree == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to parse source code");
        }
    }

    public string GetRootNodeString()
    {
        if (_tree == IntPtr.Zero)
            return "No tree";

        TSNode rootNode = TreeSitterNative.csharp_ts_tree_root_node(_tree);

        var nodeStringPtr = TreeSitterNative.csharp_ts_node_string(rootNode);
        if (nodeStringPtr == IntPtr.Zero)
            return "Empty node";

        string result = Marshal.PtrToStringAnsi(nodeStringPtr);
        TreeSitterNative.csharp_ts_free_string(nodeStringPtr);

        return result ?? "null";
    }

    public TSNode GetRootNode()
    {
        if (_tree == IntPtr.Zero)
            throw new InvalidOperationException("No tree parsed");

        return TreeSitterNative.csharp_ts_tree_root_node(_tree);
    }

    public string GetNodeType(TSNode node)
    {
        IntPtr typePtr = TreeSitterNative.csharp_ts_node_type(node);
        return typePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(typePtr) ?? "" : "";
    }

    public uint GetChildCount(TSNode node)
    {
        return TreeSitterNative.csharp_ts_node_child_count(node);
    }

    public TSNode GetChild(TSNode node, uint index)
    {
        return TreeSitterNative.csharp_ts_node_child(node, index);
    }

    public string GetNodeText(TSNode node, string sourceCode)
    {
        uint start = TreeSitterNative.csharp_ts_node_start_byte(node);
        uint end = TreeSitterNative.csharp_ts_node_end_byte(node);

        if (start >= sourceCode.Length || end > sourceCode.Length)
            return "";

        return sourceCode.Substring((int)start, (int)(end - start));
    }

    public bool IsNamedNode(TSNode node)
    {
        return TreeSitterNative.csharp_ts_node_is_named(node);
    }

    public void PrintTree(string sourceCode, int maxDepth = 10)
    {
        if (_tree == IntPtr.Zero)
        {
            Console.WriteLine("No tree");
            return;
        }

        TSNode root = GetRootNode();
        PrintNode(root, sourceCode, "", true, 0, maxDepth);
    }

    private void PrintNode(TSNode node, string sourceCode, string prefix, bool isLast, int depth, int maxDepth)
    {
        if (depth > maxDepth)
            return;

        string nodeType = GetNodeType(node);
        string nodeText = GetNodeText(node, sourceCode);
        bool isNamed = IsNamedNode(node);

        // Форматируем вывод
        string connector = isLast ? "└── " : "├── ";
        string nodeInfo = isNamed ? $"{nodeType}" : $"'{nodeType}'";

        // Показываем текст только для листовых узлов или коротких текстов
        if (!string.IsNullOrWhiteSpace(nodeText) && nodeText.Length < 50)
        {
            nodeInfo += $": \"{nodeText.Replace("\n", "\\n").Replace("\r", "")}\"";
        }

        Console.WriteLine($"{prefix}{connector}{nodeInfo}");

        // Обходим дочерние узлы
        uint childCount = GetChildCount(node);
        string newPrefix = prefix + (isLast ? "    " : "│   ");

        for (uint i = 0; i < childCount; i++)
        {
            TSNode child = GetChild(node, i);
            PrintNode(child, sourceCode, newPrefix, i == childCount - 1, depth + 1, maxDepth);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_tree != IntPtr.Zero)
            {
                TreeSitterNative.csharp_ts_tree_delete(_tree);
                _tree = IntPtr.Zero;
            }

            if (_parser != IntPtr.Zero)
            {
                TreeSitterNative.csharp_ts_parser_delete(_parser);
                _parser = IntPtr.Zero;
            }

            _disposed = true;
        }
    }
}