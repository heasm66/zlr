#if !DEBUG
#define CATCH_EXCEPTIONS
#endif

using System;
using System.Collections.Generic;
using System.IO;
using ZLR.VM;
using System.Reflection;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ZLR.Interfaces.SystemConsole.Debugger;
using System.Net;
using System.Net.Sockets;

namespace ZLR.Interfaces.SystemConsole
{
    static class Program
    {
        enum DisplayType { FullScreen, Dumb, DumbBottomWinOnly }

        // ReSharper disable once InconsistentNaming
        static async Task<int> Main([ItemNotNull] [NotNull] string[] args)
        {
#if CATCH_EXCEPTIONS
            try
#endif
            {
                var redirected = Console.IsOutputRedirected;

                if (!redirected)
                {
                    Console.Title = "ConsoleZLR";
                }

                Stream? gameStream, debugStream = null;
                string? gameDir, debugDir = null;
                string? fileName, commandFile = null;
                var displayType = redirected ? DisplayType.DumbBottomWinOnly : DisplayType.FullScreen;
                bool debugger = false, predictable = false;
                var wait = true;
                int? listen = null;

                // TODO: use a command-line parsing library
                if (args.Length >= 1 && args[0].Length > 0)
                {
                    var n = 0;

                    var parsing = true;
                    do
                    {
                        switch (args[n].ToLower())
                        {
                            case "-commands":
                                if (args.Length > n + 1)
                                {
                                    commandFile = args[n + 1];
                                    n += 2;
                                    if (args.Length <= n)
                                        return Usage();
                                }
                                else
                                {
                                    return Usage();
                                }

                                break;
                            case "-dumb":
                                n++;
                                displayType = DisplayType.Dumb;
                                break;
                            case "-dumb2":
                                n++;
                                displayType = DisplayType.DumbBottomWinOnly;
                                break;
                            case "-debug":
                                n++;
                                debugger = true;
                                break;
                            case "-listen":
                                if (args.Length > n + 1 && int.TryParse(args[n + 1], out var num))
                                {
                                    listen = num;
                                    n += 2;
                                    if (args.Length <= n)
                                        return Usage();
                                }
                                else
                                {
                                    return Usage();
                                }

                                break;
                            case "-predictable":
                                n++;
                                predictable = true;
                                break;
                            case "-nowait":
                                n++;
                                wait = false;
                                break;
                            default:
                                parsing = false;
                                break;
                        }
                    } while (parsing);

                    gameStream = new FileStream(args[n], FileMode.Open, FileAccess.Read);
                    gameDir = Path.GetDirectoryName(Path.GetFullPath(args[n]));
                    fileName = Path.GetFileName(args[n]);

                    if (args.Length > n + 1)
                    {
                        debugStream = new FileStream(args[n + 1], FileMode.Open, FileAccess.Read);
                        debugDir = Path.GetDirectoryName(Path.GetFullPath(args[n + 1]));
                    }
                }
                else
                {
                    return Usage();
                }

                IZMachineIO io;

                switch (displayType)
                {
                    case DisplayType.Dumb:
                        io = new DumbIO(false, commandFile);
                        break;

                    case DisplayType.DumbBottomWinOnly:
                        io = new DumbIO(true, commandFile);
                        break;

                    case DisplayType.FullScreen:
                        var cio = new ConsoleIO(fileName);
                        if (commandFile != null)
                        {
                            cio.SuppliedCommandFile = commandFile;
                            cio.HideMorePrompts = true;
                        }

                        io = cio;
                        break;

                    default:
                        throw new NotImplementedException();
                }

                var zm = new ZMachine(gameStream, io) { PredictableRandom = predictable };
                if (commandFile != null)
                    await zm.SetReadingCommandsFromFileAsync(true);
                if (debugStream != null)
                    zm.LoadDebugInfo(debugStream);

                if (debugger)
                {
                    var sourcePath = new List<string>(3);
                    if (debugDir != null)
                        sourcePath.Add(debugDir);
                    if (gameDir != null)
                        sourcePath.Add(gameDir);
                    sourcePath.Add(Directory.GetCurrentDirectory());

                    using var console = await CreateDebuggingConsole(zm, listen, sourcePath);
                    await console.RunDebuggerAsync();
                }
                else
                {
#if DEBUG
                    await zm.RunAsync();
#else
                    try
                    {
                        zm.Run();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
#endif
                    if (wait)
                    {
                        Console.WriteLine("Press any key to exit...");
                        Console.ReadKey(true);
                    }
                }

                return 0;
            }
#if CATCH_EXCEPTIONS
            catch (Exception ex)
            {
                return Error(ex.Message + " (" + ex.GetType().Name + ")");
            }
#endif
        }

        [ItemNotNull]
        private static async Task<DebuggingConsole> CreateDebuggingConsole(
            [NotNull] ZMachine zm, [CanBeNull] int? listen, [NotNull] IEnumerable<string> sourcePath)
        {
            if (listen == null)
            {
                return new DebuggingConsole(zm, zm.IO, sourcePath);
            }

            var listener = new TcpListener(IPAddress.Loopback, (int) listen);
            listener.Start(1);

            Console.Error.WriteLine("Debugger listening on {0}.", listener.LocalEndpoint);

            var client = await listener.AcceptTcpClientAsync();

            Console.Error.WriteLine("Accepted connection from {0}.", client.Client.RemoteEndPoint);
            listener.Stop();

            return new DebuggingConsole(zm, client.GetStream(), sourcePath);
        }

        private static int Usage()
        {
            var exe = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            Console.WriteLine("Usage: {0} [-commands <commandfile.txt>] [-dumb | -dumb2] [-debug [-listen <port>]] [-predictable] [-nowait] <game_file.z5/z8> [<debug_file.dbg>]", exe);
            return 1;
        }

        private static int Error(string msg)
        {
            Console.Error.Write("Error: ");
            Console.Error.WriteLine(msg);
            return 2;
        }
    }
}
