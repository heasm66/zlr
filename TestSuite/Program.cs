using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ZLR.VM;
using ZLR.VM.Tests;

namespace TestSuite
{
    static class Program
    {
        private static Dictionary<string, TestCase> testCases;
        private static string testPath;

        private const string TESTCASES_DIR_NAME = "Test Cases";

        // ReSharper disable once InconsistentNaming
        static async Task Main()
        {
            Console.WriteLine("ZLR Test Suite {0}", ZMachine.ZLR_VERSION);
            Console.WriteLine();

            testPath = FindTestCases();
            if (testPath == null)
            {
                Console.WriteLine("Test cases not found.");
                return;
            }

            Console.WriteLine("Using test cases in {0}.", testPath);
            testCases = TestCase.LoadAll(testPath);

            do
            {
                Console.WriteLine();
                Console.WriteLine("=== Menu ===");
                Console.WriteLine();
                Console.WriteLine("1. List all tests");
                Console.WriteLine("2. Run all tests");
                Console.WriteLine("3. Run one test");
                Console.WriteLine("4. Record expected outcome");
                Console.WriteLine();
                Console.WriteLine("0. Quit");
                Console.WriteLine();
                Console.Write("Choice: ");

                var info = Console.ReadKey();
                Console.WriteLine();
                Console.WriteLine();

                switch (info.KeyChar)
                {
                    case '1':
                        ListAllTests();
                        break;

                    case '2':
                        await RunAllTestsAsync();
                        break;

                    case '3':
                        await RunOneTestAsync();
                        break;

                    case '4':
                        await RecordExpectedOutcomeAsync();
                        break;

                    case '0':
                        Console.WriteLine("Goodbye.");
                        return;

                    default:
                        Console.WriteLine("Invalid selection.");
                        break;
                }
            }
            while (true);
        }

        [CanBeNull]
        static string FindTestCases()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            while (!string.IsNullOrEmpty(path))
            {
                if (Directory.Exists(Path.Combine(path, TESTCASES_DIR_NAME)))
                    return Path.Combine(path, TESTCASES_DIR_NAME);

                path = Path.GetDirectoryName(path);
            }

            return null;
        }

        private static async Task RecordExpectedOutcomeAsync()
        {
            var selected = PromptForTestCase();

            if (selected != null)
            {
                try
                {
                    using var zcode = selected.GetZCode();
                    var io = new RecordingIO(selected.InputFile);
                    var zm = new ZMachine(zcode, io)
                    {
                        PredictableRandom = true,
                    };
                    await zm.SetWritingCommandsToFileAsync(true);

                    var output = await RunAndCollectOutputAsync(zm, io);
                    File.WriteAllText(selected.OutputFile, output);
                }
                finally
                {
                    selected.CleanUp();
                }
            }
        }

        [NotNull]
        [ItemNotNull]
        private static async Task<string> RunAndCollectOutputAsync([NotNull] ZMachine zm, [NotNull] TestCaseIO io)
        {
            string output = null;

            try
            {
                zm.PredictableRandom = true;
                await zm.RunAsync();
                output = io.CollectOutput();
            }
            // ReSharper disable once CatchAllClause
            catch (Exception ex)
            {
                if (output == null)
                    output = io.CollectOutput();

                output += "\n\n*** Exception ***\n" + ex;
            }

            return output;
        }

        private static async Task RunOneTestAsync()
        {
            var selected = PromptForTestCase();

            if (selected != null)
            {
                try
                {
                    await RunOneTestAsync(selected);
                }
                finally
                {
                    selected.CleanUp();
                }
            }
        }

        private static async Task RunAllTestsAsync()
        {
            var names = new List<string>(testCases.Keys);
            names.Sort();

            if (names.Count == 0)
            {
                Console.WriteLine("No tests to run.");
                return;
            }

            var failures = 0;

            // TODO: run test cases in parallel
            foreach (var name in names)
            {
                var test = testCases[name];

                Console.Write("{0} - ", name);

                if (await RunOneTestAsync(test) == false)
                    failures++;
            }

            if (failures > 0)
            {
                Console.WriteLine();
                Console.WriteLine("{0} test{1} failed. The actual output is saved with the suffix \".failed-output.txt\".",
                    failures,
                    failures == 1 ? "" : "s");
            }
        }

        private static async Task<bool?> RunOneTestAsync([NotNull] TestCase test)
        {
            if (!File.Exists(test.InputFile) || !File.Exists(test.OutputFile))
            {
                Console.WriteLine("skipping (expected outcome not recorded).");
                return null;
            }
            try
            {
                using var zcode = test.GetZCode();
                var io = new ReplayIO(test.InputFile);
                var zm = new ZMachine(zcode, io)
                {
                    PredictableRandom = true,
                };
                await zm.SetReadingCommandsFromFileAsync(true);

                var output = await RunAndCollectOutputAsync(zm, io);
                var expectedOutput = File.ReadAllText(test.OutputFile);

                if (OutputDiffers(expectedOutput, output))
                {
                    Console.WriteLine("failed!");
                    File.WriteAllText(test.FailureFile, output);
                    return false;
                }
                else
                {
                    Console.WriteLine("passed.");
                    return true;
                }
            }
            finally
            {
                test.CleanUp();
            }
        }

        private static bool OutputDiffers(string expected, string actual)
        {
            // ignore compilation dates and tool versions in the output
            var rex = new Regex(
                @"serial number \d{6}|sn \d{6}|" +                              // serial number
                @"inform \d+ build .{4}|i\d/v\d\.\d+|lib \d+/\d+n?( [sd]+)?|" +  // I7 versions
                @"inform v\d\.\d+|library \d+/\d+n?( [sd]+)?",                  // I6 versions
                RegexOptions.IgnoreCase);
            expected = rex.Replace(expected, "");
            actual = rex.Replace(actual, "");

            return expected != actual;
        }

        private static void ListAllTests()
        {
            var names = new List<string>(testCases.Keys);
            names.Sort();

            if (names.Count == 0)
            {
                Console.WriteLine("No tests.");
            }
            else
            {
                foreach (var name in names)
                    Console.WriteLine("{0} - {1}", name, Path.GetFileName(testCases[name].TestFile));
            }
        }

        private static TestCase PromptForTestCase()
        {
            const string PROMPT = "Select a test case (blank to cancel, \"?\" for list): ";

            while (true)
            {
                Console.Write(PROMPT);
                var line = Console.ReadLine()?.Trim();

                Console.WriteLine();

                if (string.IsNullOrEmpty(line))
                    return null;

                if (line == "?")
                    ListAllTests();
                else if (testCases.ContainsKey(line))
                    return testCases[line];
                else
                    Console.WriteLine("Invalid selection.");

                Console.WriteLine();
            }
        }
    }
}
