class Program
{
    static void Main()
    {
        using (var parser = new TreeSitterParser())
        {
            string code = @"
                1+2-3
            ";

            parser.Parse(code);

            parser.PrintTree(code);

        }
    }
}