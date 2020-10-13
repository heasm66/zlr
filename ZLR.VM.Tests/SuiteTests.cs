using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZLR.VM.Tests
{
    [TestClass]
    public class SuiteTests
    {
        const string TestCasesDirName = "Test Cases";
        const int PerTestTimeoutMilliseconds = 60000;

        static string testCasesDir = default!;

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            testCasesDir = null!;

            // find the directory containing TestCasesDirName
            var dir = Directory.GetCurrentDirectory();

            do
            {
                if (Directory.Exists(Path.Combine(dir, TestCasesDirName)))
                {
                    testCasesDir = Path.Combine(dir, TestCasesDirName);
                    break;
                }

                dir = Directory.GetParent(dir)?.FullName;
            } while (dir != null && dir != Path.GetPathRoot(dir));

            if (testCasesDir == null)
                Assert.Fail("Can't locate test case directory");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "CodeQuality",
            "IDE0051:Remove unused private members",
            Justification = "Used as a data source for " + nameof(RunTestCaseAndCompareOutput))]
        static IEnumerable<object[]> GetTestCases()
        {
            return from v in TestCase.LoadAll(testCasesDir).Values
                   select new object[] { v };
        }

        public static string GetTestCaseDisplayName(MethodInfo _, object[] data)
        {
            var testCase = (TestCase)data[0];
            return Path.GetFileName(testCase.TestFile);
        }

        [DataTestMethod]
        [DynamicData("GetTestCases", DynamicDataSourceType.Method, DynamicDataDisplayName = "GetTestCaseDisplayName")]
        //[Timeout(PerTestTimeoutMilliseconds)]
        public async Task RunTestCaseAndCompareOutput(object testCaseAsObject)
        {
            var test = (TestCase)testCaseAsObject;

            if (!File.Exists(test.InputFile))
            {
                Assert.Inconclusive(string.Format(
                    "The test was not run, because no test input has been recorded for it. If no input is needed, " +
                    "create \"{0}\" as an empty file.",
                    test.InputFile));
            }

            try
            {
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

                    if (!File.Exists(test.OutputFile))
                    {
                        Console.WriteLine("Test output (no expected output to compare against):");
                        Console.Write(output);
                        File.WriteAllText(test.FailureFile, output);

                        Assert.Inconclusive(
                            string.Format(
                                "The test ran to completion, but no expected output has been recorded for it. " +
                                "The actual output was written to the console. If it's correct, rename \"{0}\" to \"{1}\".",
                                test.FailureFile,
                                test.OutputFile));
                    }

                    var expectedOutput = File.ReadAllText(test.OutputFile);

                    if (OutputDiffers(expectedOutput, output))
                    {
                        Console.WriteLine("Test output (differs from expected):");
                        Console.Write(output);
                        File.WriteAllText(test.FailureFile, output);

                        Assert.Fail("Actual output differs from expected.");
                    }
                    else
                    {
                        // passed
                    }
                }
                catch (FailedToCompileTestCase ex)
                {
                    if (!string.IsNullOrEmpty(ex.CompilerErrorOutput))
                    {
                        Console.WriteLine("Console output (stderr):");
                        Console.Write(ex.CompilerErrorOutput);
                    }

                    if (!string.IsNullOrEmpty(ex.CompilerStandardOutput))
                    {
                        Console.WriteLine("Compiler output (stdout):");
                        Console.Write(ex.CompilerStandardOutput);
                    }

                    Assert.Fail("Failed to compile test case.");
                }
            }
            finally
            {
                test.CleanUp();
            }
        }

        private static async Task<string> RunAndCollectOutputAsync(ZMachine zm, TestCaseIO io)
        {
            string? output = null;

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
    }
}
