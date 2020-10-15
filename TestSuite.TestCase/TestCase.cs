using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ZLR.VM.Tests
{
    abstract class TestCase
    {
        private const string INPUT_SUFFIX = ".input.txt";
        private const string OUTPUT_SUFFIX = ".output.txt";
        private const string FAILURE_SUFFIX = ".failed-output.txt";

        protected readonly string testFile;

        protected TestCase(string file)
        {
            testFile = file;
        }

        public abstract Stream GetZCode();

        public string TestFile => testFile;

        public string InputFile => testFile + INPUT_SUFFIX;

        public string OutputFile => testFile + OUTPUT_SUFFIX;

        public string FailureFile => testFile + FAILURE_SUFFIX;

        public virtual void CleanUp()
        {
            // nada
        }

        public static Dictionary<string, TestCase> LoadAll(string path)
        {
            var result = new Dictionary<string, TestCase>();

            foreach (var file in Directory.GetFiles(path))
            {
                Debug.Assert(file != null);
                var shortname = Path.GetFileNameWithoutExtension(file);

                if (result.ContainsKey(shortname))
                {
                    var num = 2;
                    var shortbase = shortname;
                    do
                    {
                        shortname = shortbase + num;
                        num++;
                    } while (result.ContainsKey(shortname));
                }

                var ext = Path.GetExtension(file).ToLower();
                switch (ext)
                {
                    case ".z1":
                    case ".z2":
                    case ".z3":
                    case ".z4":
                    case ".z5":
                    case ".z6":
                    case ".z7":
                    case ".z8":
                    case ".zcode":
                    case ".zlb":
                    case ".zblorb":
                        result.Add(shortname, new CompiledTestCase(file));
                        break;

                    case ".inf":
                        result.Add(shortname, new InformTestCase(file));
                        break;

                    case ".zil":
                        result.Add(shortname, new ZilTestCase(file));
                        break;
                }
            }

            return result;
        }
    }

    class CompiledTestCase : TestCase
    {
        public CompiledTestCase(string file) : base(file) { }

        public override Stream GetZCode()
        {
            return new FileStream(testFile, FileMode.Open, FileAccess.Read);
        }
    }

    sealed class FailedToCompileTestCase : Exception
    {
        public string CompilerStandardOutput { get; }
        public string CompilerErrorOutput { get; }

        public FailedToCompileTestCase(TestCase testCase, string compilerStdOut, string compilerStdErr)
            : base("Failed to compile test case " + testCase.TestFile)
        {
            CompilerStandardOutput = compilerStdOut;
            CompilerErrorOutput = compilerStdErr;
        }
    }

    class SourceCodeTestCase : TestCase, IDisposable
    {
        protected const int COMPILER_TIMEOUT_MS = 10000;

        private readonly string compiler;
        private readonly int timeout;

        private string? zfile;

        protected SourceCodeTestCase(string compiler, string file)
            : this(compiler, file, COMPILER_TIMEOUT_MS) { }

        protected SourceCodeTestCase(string compiler, string file, int timeout)
            : base(file)
        {
            this.compiler = compiler;
            this.timeout = timeout;

            // finalizer only needs to be called once we've compiled the test
            GC.SuppressFinalize(this);
        }

        public override Stream GetZCode()
        {
            var path = Path.GetDirectoryName(testFile);
            Debug.Assert(path != null, "path != null");
            var compilerPath = Path.Combine(path, compiler);
            var infbase = Path.GetFileNameWithoutExtension(testFile);

            var info = new ProcessStartInfo
            {
                WorkingDirectory = path,
                FileName = compilerPath,
                Arguments = infbase,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // TODO: check for compiler errors

            string stdout, stderr;

            using (var compilerProcess = Process.Start(info))
            {
                stdout = compilerProcess.StandardOutput.ReadToEnd();
                stderr = compilerProcess.StandardError.ReadToEnd();

                compilerProcess.WaitForExit(timeout);
            }

            var outpath = Path.Combine(path, "Compiled");
            var outfile = Path.Combine(outpath, infbase + ".zcode");
            if (!File.Exists(outfile))
            {
                throw new FailedToCompileTestCase(this, stdout, stderr);
            }

            zfile = outfile;
            GC.ReRegisterForFinalize(this);
            return new FileStream(outfile, FileMode.Open, FileAccess.Read);
        }

        public override void CleanUp()
        {
            if (zfile != null)
            {
                try
                {
                    File.Delete(zfile);
                    var dbgFile = Path.ChangeExtension(zfile, ".dbg");
                    if (dbgFile != null) File.Delete(dbgFile);
                }
                catch
                {
                    return;
                }

                zfile = null;
                GC.SuppressFinalize(this);
            }
        }

        void IDisposable.Dispose()
        {
            CleanUp();
        }

        ~SourceCodeTestCase()
        {
            CleanUp();
        }
    }

    class InformTestCase : SourceCodeTestCase
    {
        public InformTestCase(string file) :
            base("compile-inform-case.bat", file)
        {
        }
    }

    class ZilTestCase : SourceCodeTestCase
    {
        public ZilTestCase(string file) :
            base("compile-zil-case.bat", file)
        {
        }
    }
}
