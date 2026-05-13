using System;
using System.IO;
using System.Text;

static class Program
{
    static readonly string SamplesFolder =
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "samples");

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
                new GrammarAnalyzer(parser).Run(SamplesFolder);
                return;
            }

            switch (args[0])
            {
                case "--lsp":
                case "--stdio":
                    new LspServer(parser).Run();
                    break;

                case "--test":
                    GrammarTests.RunAll(parser);
                    break;

                case "--inspect":
                    string filePath = args.Length > 1 ? args[1] : args[^1];
                    new GrammarAnalyzer(parser).InspectFile(filePath);
                    break;

                default:
                    new GrammarAnalyzer(parser).InspectFile(args[^1]);
                    break;
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