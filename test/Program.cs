using System;
using System.IO;
using System.Text;

static class Program
{
    const string SamplesFolder =
        @"..\..\..\samples";

    static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            bool lspMode = args.Length > 0 && (args[0] == "--lsp" || args[0] == "--stdio");
            if (lspMode)
                RedirectConsoleToStderr();

            using var parser = new TreeSitterParser();

            if (args.Length == 0)
            {
                var grammarAnalyzer = new GrammarAnalyzer(parser);
                grammarAnalyzer.Run(SamplesFolder);
                return;
            }

            switch (args[0])
            {
                case "--lsp":
                case "--stdio":
                    var lsp = new LspServer(parser);
                    lsp.Run();
                    break;

                case "--test":
                    GrammarTests.RunAll(parser);
                    break;

                case "--inspect":
                    {
                        string filePath = args.Length > 1 ? args[1] : args[^1];
                        var grammarAnalyzer = new GrammarAnalyzer(parser);
                        grammarAnalyzer.InspectFile(filePath);
                        break;
                    }

                case "--tree":
                    {
                        string filePath = args.Length > 1 ? args[1] : args[^1];
                        var grammarAnalyzer = new GrammarAnalyzer(parser);
                        grammarAnalyzer.DumpTree(filePath);
                        break;
                    }
                default:
                    {
                        var grammarAnalyzer = new GrammarAnalyzer(parser);
                        grammarAnalyzer.InspectFile(args[^1]);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static void RedirectConsoleToStderr()
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8)
        {
            AutoFlush = true,
        });
    }
}