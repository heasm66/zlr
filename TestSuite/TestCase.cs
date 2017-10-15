using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;

namespace TestSuite
{
    abstract class TestCase
    {
        private const string INPUT_SUFFIX = ".input.txt";
        private const string OUTPUT_SUFFIX = ".output.txt";
        private const string FAILURE_SUFFIX = ".failed-output.txt";

        [NotNull]
        protected readonly string testFile;

        protected TestCase([NotNull] string file)
        {
            testFile = file;
        }

        [NotNull]
        public abstract Stream GetZCode();

        [NotNull]
        public string TestFile => testFile;

        [NotNull]
        public string InputFile => testFile + INPUT_SUFFIX;

        [NotNull]
        public string OutputFile => testFile + OUTPUT_SUFFIX;

        [NotNull]
        public string FailureFile => testFile + FAILURE_SUFFIX;

        public virtual void CleanUp()
        {
            // nada
        }

        [NotNull]
        public static Dictionary<string, TestCase> LoadAll([NotNull] string path)
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
                }
            }

            return result;
        }
    }

    class CompiledTestCase : TestCase
    {
        public CompiledTestCase([NotNull] string file) : base(file) { }

        public override Stream GetZCode()
        {
            return new FileStream(testFile, FileMode.Open, FileAccess.Read);
        }
    }

    class SourceCodeTestCase : TestCase, IDisposable
    {
        [NotNull]
        private readonly string compiler;

        private string zfile;

        protected SourceCodeTestCase([NotNull] string compiler, [NotNull] string file)
            : base(file)
        {
            this.compiler = compiler;

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
                Arguments = infbase
            };

            // TODO: check for compiler errors

            using (var compilerProcess = Process.Start(info))
            {
                compilerProcess?.WaitForExit();
            }

            var outpath = Path.Combine(path, "Compiled");
            var outfile = Path.Combine(outpath, infbase + ".zcode");
            if (!File.Exists(outfile))
                throw new Exception("Failed to compile test case");

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
        public InformTestCase([NotNull] string file) :
            base("compile-inform-case.bat", file)
        {
        }
    }
}
