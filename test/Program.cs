class Program
{
    static void Main()
    {
        Console.WriteLine("Starting parser...");

        using (var parser = new TreeSitterParser())
        {
            Console.WriteLine("Parser created successfully");

            string code = "program Test; begin end.";

            Console.WriteLine($"Parsing code: {code}");
            Console.WriteLine($"Code length: {code.Length}");

            try
            {
                parser.Parse(code);
                Console.WriteLine("Parse completed");

                var rootNode = parser.GetRootNode();
                Console.WriteLine($"Root node type: {parser.GetNodeType(rootNode)}");
                Console.WriteLine($"Child count: {parser.GetChildCount(rootNode)}");

                parser.PrintTree(code, maxDepth: 3);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        Console.WriteLine("Done");
    }
}