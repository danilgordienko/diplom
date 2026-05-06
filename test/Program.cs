using System;
using System.IO;
using System.Text;

static class Program
{
    const string SamplesFolder =
        @"C:\Users\danil\OneDrive\Рабочий стол\projects\димплом\test\test\samples";

    static void Main(string[] args)
    {
        try
        {
            using var parser = new TreeSitterParser();

            if (args.Length == 0)
            {
                var grammarAnalyzer = new GrammarAnalyzer(parser);
                grammarAnalyzer.Run(SamplesFolder);
                return;
            }

            switch (args[0])
            {
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

                case "--find":
                    {
                        // program.exe --find COUNT C:\MyProject
                        // program.exe --find COUNT main.pas
                        if (args.Length < 3)
                        {
                            Console.Error.WriteLine("Использование: program.exe --find ИМЯ ПУТЬ");
                            Console.Error.WriteLine("  ПУТЬ — папка с проектом или один .pas файл");
                            break;
                        }
                        string symbolName = args[1];
                        string projectPath = args[2];

                        var projectAnalyzer = new ProjectAnalyzer(parser);
                        projectAnalyzer.FindSymbol(symbolName, projectPath);
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
}