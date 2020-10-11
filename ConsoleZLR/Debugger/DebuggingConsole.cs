//#define DEBUG_DEBUGGER
//#define TRACE_DEBUGGER

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using JetBrains.Annotations;
using Nito.AsyncEx;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    public sealed class DebuggingConsole : IDisposable
    {
        private readonly ZMachine zm;

        private readonly DebugInfo? debugInfo;

        private readonly IDisposable?[]? disposables;

        private readonly TextReader reader;

        private readonly TextWriter writer;

        private readonly string[] sourcePath;

        private readonly bool sharingIO;

        private enum ActiveState
        {
            NotStarted,
            Active,
            Finished,
        }

        private ActiveState active;

        private IDebugger dbg = null!;
        private SourceCache src = null!;
        private ValueFormatter valueFormatter = null!;

        private bool tracingCalls;
        private string? lastCmd;

        private static readonly char[] COMMAND_DELIM = { ' ' };

        public DebuggingConsole(
            ZMachine zm,
            IAsyncZMachineIO io,
            IEnumerable<string> sourcePath)
            : this(zm, new ZIOReader(io), new ZIOWriter(io), sourcePath)
        {
            sharingIO = true;

            // ReSharper disable once SuspiciousTypeConversion.Global
            if (io is IDisposable dio)
                disposables = new[] { dio };
        }

        public DebuggingConsole(
            ZMachine zm,
            Stream stream,
            IEnumerable<string> sourcePath)
            : this(zm, stream, Encoding.UTF8, sourcePath)
        {
        }

        public DebuggingConsole(
            ZMachine zm,
            Stream stream,
            Encoding encoding,
            IEnumerable<string> sourcePath)
            : this(zm, new StreamReader(stream, encoding), new StreamWriter(stream, encoding), sourcePath)
        {
            disposables = new IDisposable[] { stream };
        }

        private DebuggingConsole(ZMachine zm, TextReader reader, TextWriter writer,
            IEnumerable<string> sourcePath)
        {
            this.zm = zm;
            this.debugInfo = zm.DebugInfo;
            this.reader = reader;
            this.writer = writer;
            this.sourcePath = sourcePath.ToArray();
        }

        [System.Diagnostics.Conditional("DEBUG_DEBUGGER")]
        private static void AttachDebugger()
        {
            System.Diagnostics.Debugger.Launch();
        }

        [System.Diagnostics.Conditional("TRACE_DEBUGGER"), JetBrains.Annotations.StringFormatMethod("format")]
        private static void DebugWriteLine([NotNull] string format, [NotNull] params object[] args)
        {
            System.Diagnostics.Debug.Write(
                $"[{TaskScheduler.Current.Id} @ {System.Threading.Thread.CurrentThread.ManagedThreadId}] ");
            System.Diagnostics.Debug.WriteLine(format, args);
        }

        public async Task RunDebuggerAsync()
        {
            AttachDebugger();

            Activate();
            try
            {
                if (sharingIO)
                {
                    // simple case, no interrupts
                    await SimpleDebuggerLoopAsync();
                }
                else
                {
                    // complex case, allow interrupts
                    await InterruptibleDebuggerLoopAsync();
                }
            }
            finally
            {
                Deactivate();
            }

            DebugWriteLine("Goodbye.");
        }

        private async Task SimpleDebuggerLoopAsync()
        {
            DebugWriteLine("Hi, I'm the mayor of Simpleton");

            while (this.Active)
            {
                ShowStatus();
                await writer.FlushAsync().ConfigureAwait(false);

                var command = await reader.ReadLineAsync().ConfigureAwait(false);

                if (command is null)
                    break;

                DebugWriteLine("Read command: {0}", command);

                await HandleCommandAsync(command).ConfigureAwait(false);
            }
        }

        private async Task InterruptibleDebuggerLoopAsync(CancellationToken loopCancellationToken = default)
        {
            DebugWriteLine("Help, I'm steppin' into the twilight zone");

            ShowStatus();
            await writer.FlushAsync().ConfigureAwait(false);

            // interrupts are handled by the producer when read
            // non-interrupts are queued for the consumer to handle in order
            var queue = new AsyncProducerConsumerQueue<string>();

            var producerCanceller = new CancellationTokenSource();
            var producerTask = Task.Run(ProduceAsync, producerCanceller.Token);
            var consumerTask = Task.Run(ConsumeAsync, loopCancellationToken);

            async Task ProduceAsync()
            {
                var ct = producerCanceller.Token;

                try
                {
                    while (this.Active)
                    {
                        var command = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);

                        if (command is null)
                            break;

                        DebugWriteLine("Read command: {0}", command);

                        try
                        {
                            if (await TryHandleInterruptAsync(command).ConfigureAwait(false))
                            {
                                // don't do any I/O here, since the stream may be locked by the consumer
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugWriteLine("Producer exception while handling interrupt \"{0}\": {1}", command, ex);
                            writer.WriteLine("*** ERROR ({0}): {1}", ex.GetType().Name, ex.Message);
                        }

                        DebugWriteLine("Queuing: {0}", command);
                        await queue.EnqueueAsync(command, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    DebugWriteLine("Producer finished");
                    queue.CompleteAdding();
                }
            }

            async Task ConsumeAsync()
            {
                var ct = loopCancellationToken;

                try
                {
                    while (Active && await queue.OutputAvailableAsync(ct).ConfigureAwait(false))
                    {
                        var command = await queue.DequeueAsync(ct).ConfigureAwait(false);
                        DebugWriteLine("Dequeued: {0}", command);

                        try
                        {
                            await HandleCommandAsync(command).ConfigureAwait(false);
                            await writer.FlushAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            DebugWriteLine("Consumer exception while handling non-interrupt \"{0}\": {1}", command, ex);
                            writer.WriteLine("*** ERROR ({0}): {1}", ex.GetType().Name, ex.Message);
                        }

                        if (Active)
                            ShowStatus();

                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    DebugWriteLine("Consumer finished");
                    producerCanceller.Cancel();
                }
            }

            try
            {
                await Task.WhenAll(producerTask, consumerTask).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == producerCanceller.Token && !loopCancellationToken.IsCancellationRequested)
            {
                // loop terminated normally
            }
        }

        public bool Active => active == ActiveState.Active;

        private void Activate()
        {
            if (active != ActiveState.NotStarted)
                throw new InvalidOperationException("Wrong state");

            active = ActiveState.Active;

            dbg = zm.Debug();
            dbg.Events.DebuggerStateChanged += DebuggerStateChangedEventHandler;

            src = new SourceCache(sourcePath);
            valueFormatter = new ValueFormatter(zm, dbg);

            writer.WriteLine("ZLR Debugger {0}", typeof(DebuggingConsole).Assembly.GetName().Version);
            dbg.Restart();
        }

        private void Deactivate()
        {
            dbg.Events.DebuggerStateChanged -= DebuggerStateChangedEventHandler;

            dbg = null!;
            src = null!;
            valueFormatter = null!;
        }

        private void TraceCallsEventHandler(object? sender, [NotNull] EnterFunctionEventArgs e)
        {
            writer.Write("[ ");

            for (var i = 0; i < e.CallDepth; i++)
                writer.Write(". ");

            RoutineInfo? rtn;
            if (debugInfo != null &&
                (rtn = debugInfo.FindRoutine(dbg.UnpackAddress(e.PackedAddress, false))) != null)
            {
                writer.Write(rtn.Name);
            }
            else
            {
                writer.Write($"${e.PackedAddress:x4}");
            }

            writer.Write('(');
            if (e.Args != null)
            {
                for (var i = 0; i < e.Args.Count; i++)
                {
                    if (i > 0)
                        writer.Write(", ");

                    writer.Write(e.Args[i].ToString());
                }
            }

            writer.WriteLine(") ]");
        }

        private DebuggerState? lastState = DebuggerState.PausedOnEntry;

        private void DebuggerStateChangedEventHandler(object? sender, DebuggerStateEventArgs e)
        {
            // re-report the pause reason the next time we pause after running or stepping
            if (e.State == DebuggerState.Running)
                lastState = null;
        }

        private static readonly ImmutableDictionary<DebuggerState, string> DebuggerStateExplanations =
            ImmutableDictionary<DebuggerState, string>.Empty
                .Add(DebuggerState.PausedByBreakpoint, "Game is paused (breakpoint).")
                .Add(DebuggerState.PausedByError, "Game is paused (error).")
                .Add(DebuggerState.PausedByUser, "Game is paused (user request).")
                .Add(DebuggerState.PausedOnEntry, "Game is paused (entry point).")
                .Add(DebuggerState.Terminated, "Game has ended.");

        private void ShowStatus()
        {
            bool pcShown;

            if (dbg.State.IsPaused())
            {
                ShowCurrentPC();
                pcShown = true;
            }
            else
            {
                pcShown = false;
            }

            if ((!pcShown || dbg.State != lastState) &&
                DebuggerStateExplanations.TryGetValue(dbg.State, out var explanation))
            {
                writer.WriteLine(explanation);
            }

            lastState = dbg.State;

            // prompt
            writer.Write("D> ");
        }

        private void ShowCurrentPC()
        {
            RoutineInfo? rtn;
            if (debugInfo != null &&
                (rtn = debugInfo.FindRoutine(dbg.CurrentPC)) != null)
            {
                writer.WriteLine(
                    $"${dbg.CurrentPC:x5} ({rtn.Name}+{dbg.CurrentPC - rtn.CodeStart})   {dbg.Disassemble(dbg.CurrentPC)}");

                var li = debugInfo.FindLine(dbg.CurrentPC);
                if (li != null)
                {
                    writer.WriteLine($"{li.Value.File}:{li.Value.Line}: {src.Load(li.Value)}");
                }
            }
            else
            {
                writer.WriteLine($"${dbg.CurrentPC:x5}   {dbg.Disassemble(dbg.CurrentPC)}");
            }
        }

        private async Task<bool> TryHandleInterruptAsync([NotNull] string cmd)
        {
            switch (cmd.ToLower())
            {
                case "!pause":
                    await dbg.PauseAsync();
                    return true;
            }

            return false;
        }

        private async Task HandleCommandAsync([NotNull] string cmd)
        {
            if (cmd.Trim() == "")
            {
                if (lastCmd == null)
                {
                    await writer.WriteLineAsync("No last command.");
                    return;
                }

                cmd = lastCmd;
            }
            else
            {
                lastCmd = cmd;
            }

            try
            {
                var parts = cmd.Split(COMMAND_DELIM, 2, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0].ToLower())
                {
                    case "reset":
                        dbg.Restart();
                        break;

                    case "s":
                    case "step":
                        if (dbg.State.IsPaused())
                            await dbg.StepIntoAsync();
                        break;

                    case "o":
                    case "over":
                        if (dbg.State.IsPaused())
                            await dbg.StepOverAsync();
                        break;

                    case "up":
                        if (dbg.State.IsPaused())
                            await dbg.StepUpAsync();
                        break;

                    case "sl":
                    case "stepline":
                        await DoStepLineAsync();
                        break;

                    case "ol":
                    case "overline":
                        await DoOverLineAsync();
                        break;

                    case "r":
                    case "run":
                        if (dbg.State.IsTerminated())
                            dbg.Restart();
                        await dbg.RunAsync();
                        break;

                    case "b":
                    case "break":
                        DoSetBreakpoint(parts);
                        break;

                    case "c":
                    case "clear":
                        DoClearBreakpoint(parts);
                        break;

                    case "bps":
                    case "breakpoints":
                        DoShowBreakpoints();
                        break;

                    case "tc":
                    case "tracecalls":
                        DoToggleTraceCalls();
                        break;

                    case "bt":
                    case "backtrace":
                        DoShowBacktrace();
                        break;

                    case "l":
                    case "locals":
                        DoShowLocals();
                        break;

                    case "g":
                    case "globals":
                        DoShowGlobals();
                        break;

                    case "p":
                    case "print":
                        DoPrint(parts);
                        break;

                    case "so":
                    case "showobj":
                        DoShowObject(parts);
                        break;

                    case "tree":
                        DoShowTree(parts);
                        break;

                    case "q":
                    case "quit":
                        await writer.WriteLineAsync("Goodbye.");
                        active = ActiveState.Finished;
                        return;

                    case "h":
                    case "help":
                    case "?":
                        await writer.WriteLineAsync("Commands:");
                        await writer.WriteLineAsync("reset, (s)tep, (o)ver, stepline (sl), overline (ol), up, (r)un,");
                        await writer.WriteLineAsync("(b)reak, (c)lear, breakpoints (bps), tracecalls (tc)");
                        await writer.WriteLineAsync("backtrace (bt), (l)ocals, (g)lobals");
                        await writer.WriteLineAsync("(p)rint, showobj (so), tree");
                        await writer.WriteLineAsync("(q)uit");

                        // TODO: mention interrupts? or ask IO to explain debugger break key?
                        break;

                    default:
                        await writer.WriteLineAsync("Unrecognized debugger command.");
                        await writer.WriteLineAsync();
                        goto case "help";

                }
            }
            catch (DebuggerException ex)
            {
                await writer.WriteLineAsync(ex.ToString());
            }
        }

        private Value Evaluate([NotNull] string exprText, bool wantLvalue = false)
        {
            // TODO: option to switch between Inform and ZIL expression syntax
            //return InformExpression.Evaluate(zm, dbg, exprText, wantLvalue);
            return ZilExpression.Evaluate(zm, dbg, exprText, wantLvalue);
        }

        private void DoPrint(string[] parts)
        {
            if (parts.Length < 2)
            {
                writer.WriteLine("Usage: print <expr>");
                return;
            }

            var value = Evaluate(parts[1], true);
            writer.WriteLine(valueFormatter.Format(value));
        }

        private void DoShowObject(string[] parts)
        {
            if (parts.Length < 2)
            {
                writer.WriteLine("Usage: showobj <expr>");
                return;
            }

            var value = Evaluate(parts[1]);

            var address = dbg.GetObjectAddress((ushort) value.Content);

            dbg.ParseObject(address, out var attrs, out var parent, out var sibling, out var child,
                out var propertyTable);

            writer.WriteLine($"=== {valueFormatter.Format(Value.Object(value.Content))} ===");
            writer.WriteLine($"Parent: {valueFormatter.Format(Value.Object(parent))}");
            writer.WriteLine($"Sibling: {valueFormatter.Format(Value.Object(sibling))}");
            writer.WriteLine($"Child: {valueFormatter.Format(Value.Object(child))}");

            writer.WriteLine("Attributes:");
            for (var i = 0; i < attrs.Length; i++)
            {
                byte bit = 0x80;

                for (var j = 0; j < 8; j++)
                {
                    if ((attrs[i] & bit) != 0)
                        writer.WriteLine("  {0}", valueFormatter.Format(Value.Attribute(i * 8 + j)));

                    bit >>= 1;
                }
            }

            writer.WriteLine($"Properties (table at ${propertyTable:x4}):");
            for (var prop = dbg.GetNextProp((ushort) value.Content, 0);
                prop != 0;
                prop = dbg.GetNextProp((ushort) value.Content, prop))
            {
                var addr = dbg.GetPropAddress((ushort) value.Content, prop);
                var length = dbg.GetPropLength(addr);

                writer.WriteLine($"  {valueFormatter.Format(Value.Property(prop))} (length {length}):");

                writer.Write("   ");
                for (var i = 0; i < length; i++)
                {
                    var b = dbg.ReadByte(addr + i);
                    writer.Write($" {b:x2}");
                }

                writer.WriteLine();
            }

            writer.WriteLine("==========");
        }

        private void DoShowTree(string[] parts)
        {
            var seen = new HashSet<ushort>();

            if (parts.Length >= 2)
            {
                var obj = Evaluate(parts[1]);
                if (!obj.IsValid)
                {
                    writer.WriteLine("Usage: tree [<expr>]");
                    return;
                }

                WriteTreeFrom((ushort) obj.Content, "- ", "  ");
                return;
            }

            var lastObj = GuessLastObject();

            for (ushort i = 1; i <= lastObj; i++)
            {
                if (seen.Contains(i))
                    continue;

                if (dbg.GetObjectParent(i) == 0)
                    WriteTreeFrom(i, "- ", "  ");
            }

            // TODO: stacks of prefix chunks instead of string concatenation?
            void WriteTreeFrom(ushort obj, string firstPrefix, string innerPrefix)
            {
                seen.Add(obj);

                writer.Write(firstPrefix);
                writer.WriteLine(valueFormatter.Format(Value.Object(obj)));

                var child = GetUnseenChild(obj);

                while (child != 0 && !seen.Contains(child) /* avoid cycles */)
                {
                    var next = GetUnseenSibling(child);

                    if (next != 0)
                        WriteTreeFrom(child, innerPrefix + "|- ", innerPrefix + "|  ");
                    else
                        WriteTreeFrom(child, innerPrefix + "`- ", innerPrefix + "   ");

                    child = next;
                }
            }

            ushort GetUnseenChild(ushort obj)
            {
                var child = dbg.GetObjectChild(obj);
                return seen.Contains(child) ? GetUnseenSibling(child) : child;
            }

            ushort GetUnseenSibling(ushort obj)
            {
                do
                {
                    obj = dbg.GetObjectSibling(obj);
                } while (obj != 0 && seen.Contains(obj));

                return obj;
            }
        }

        private ushort GuessLastObject()
        {
            if (debugInfo != null)
                return (ushort) debugInfo.Objects.Max(o => o.Number);

            // Inform and ZILF both put the property tables immediately after the object table
            var firstPropsTable = dbg.GetObjectPropertyTable(1);
            ushort i;
            for (i = 2; dbg.GetObjectAddress(i) < firstPropsTable; i++)
            {
                var nextPropsTable = dbg.GetObjectPropertyTable(i);
                firstPropsTable = Math.Min(firstPropsTable, nextPropsTable);
            }

            return (ushort) (i - 1);
        }

        private void DoShowLocals()
        {
            var frames = dbg.GetCallFrames();
            int stackItems;
            if (frames.Length == 0)
            {
                writer.WriteLine("No call frame.");
                stackItems = dbg.StackDepth;
            }
            else
            {
                var cf = frames[0];
                if (cf.Locals.Length == 0)
                {
                    writer.WriteLine("No local variables.");
                }
                else
                {
                    writer.WriteLine($"{cf.Locals.Length} local variable{(cf.Locals.Length == 1 ? "" : "s")}:");

                    var rtn = debugInfo?.FindRoutine(dbg.CurrentPC);
                    for (var i = 0; i < cf.Locals.Length; i++)
                    {
                        writer.Write("    ");
                        if (rtn != null && i < rtn.Locals.Length)
                            writer.Write(rtn.Locals[i]);
                        else
                            writer.Write($"local_{i + 1}");
                        writer.WriteLine(" = ${0:x4} ({0})", cf.Locals[i]);
                    }
                }

                stackItems = dbg.StackDepth - cf.PrevStackDepth;
            }

            if (stackItems == 0)
            {
                writer.WriteLine("No data on stack.");
            }
            else
            {
                writer.WriteLine($"{stackItems} word{(stackItems == 1 ? "" : "s")} on stack:");
                var temp = new Stack<short>();
                for (var i = 0; i < stackItems; i++)
                {
                    var value = dbg.StackPop();
                    temp.Push(value);
                    writer.WriteLine("    ${0:x4} ({0})", value);
                }

                while (temp.Count > 0)
                    dbg.StackPush(temp.Pop());
            }
        }

        private void DoShowGlobals()
        {
            int GuessNumberOfGlobals()
            {
                /* In games compiled by ZILF, the globals table is followed by the property defaults.
                 * Inform's globals are followed by the dictionary (V1-4) or terminating characters (V5+).
                 */
                var zversion = dbg.ReadByte(0);
                var globalsStart = (ushort) dbg.ReadWord(0xc);
                var propdefStart = (ushort) dbg.ReadWord(0xa);
                var dictStart = (ushort) dbg.ReadWord(0x8);
                var tcharsStart = zversion < 5 ? (ushort) 0 : (ushort) dbg.ReadWord(0x2e);

                var globalsEnd = (ushort) 0xffff;
                if (propdefStart >= globalsStart && propdefStart < globalsEnd)
                    globalsEnd = propdefStart;
                if (dictStart >= globalsStart && dictStart < globalsEnd)
                    globalsEnd = dictStart;
                if (tcharsStart >= globalsStart && tcharsStart < globalsEnd)
                    globalsEnd = tcharsStart;

                return (globalsEnd - globalsStart) / 2;
            }

            var globals = debugInfo != null
                ? (from p in debugInfo.Globals
                   orderby p.Value
                   select new { num = (byte) (p.Value + 16), name = p.Key })
                : (from byte i in Enumerable.Range(16, GuessNumberOfGlobals())
                   select new { num = i, name = $"global_{i}" });

            foreach (var g in globals)
            {
                var value = dbg.ReadVariable(g.num);
                writer.WriteLine($"    {g.name} = ${value:x4} ({value})");
            }
        }

        private void DoShowBacktrace()
        {
            var frames = dbg.GetCallFrames();
            writer.WriteLine($"Call depth: {frames.Length}");
            writer.WriteLine($"PC = {DumpCodeAddress(dbg.CurrentPC)}");

            for (var i = 0; i < frames.Length; i++)
            {
                var cf = frames[i];
                writer.WriteLine("==========");
                writer.WriteLine($"[{i + 1}] return PC = {DumpCodeAddress(cf.ReturnPC)}");
                writer.WriteLine(
                    $"called with {cf.ArgCount} arg{(cf.ArgCount == 1 ? "" : "s")}, " +
                    $"stack depth {cf.PrevStackDepth}");

                if (cf.ResultStorage < 16)
                {
                    if (cf.ResultStorage == -1)
                    {
                        writer.WriteLine("discarding result");
                    }
                    else if (cf.ResultStorage == 0)
                    {
                        writer.WriteLine("storing result to stack");
                    }
                    else
                    {
                        var rtn = debugInfo?.FindRoutine(cf.ReturnPC);
                        if (rtn != null && cf.ResultStorage - 1 < rtn.Locals.Length)
                        {
                            writer.WriteLine(
                                $"storing result to local {cf.ResultStorage} " +
                                $"({rtn.Locals[cf.ResultStorage - 1]})");
                        }
                        else
                        {
                            writer.WriteLine($"storing result to local {cf.ResultStorage}");
                        }
                    }
                }
                else if (debugInfo != null && debugInfo.Globals.Contains((byte) cf.ResultStorage))
                {
                    writer.WriteLine(
                        $"storing result to global {cf.ResultStorage} " +
                        $"({debugInfo.Globals[(byte) (cf.ResultStorage - 16)]})");
                }
                else
                {
                    writer.WriteLine($"storing result to global {cf.ResultStorage}");
                }
            }

            writer.WriteLine("==========");
        }

        private void DoToggleTraceCalls()
        {
            if (tracingCalls)
            {
                tracingCalls = false;
                dbg.Events.EnteringFunction -= TraceCallsEventHandler;
                writer.WriteLine("Tracing calls disabled.");
            }
            else
            {
                tracingCalls = true;
                dbg.Events.EnteringFunction += TraceCallsEventHandler;
                writer.WriteLine("Tracing calls enabled.");
            }
        }

        private void DoShowBreakpoints()
        {
            var breakpoints = dbg.GetBreakpoints();
            if (breakpoints.Length == 0)
            {
                writer.WriteLine("No breakpoints.");
            }
            else
            {
                writer.WriteLine($"{breakpoints.Length} breakpoint{(breakpoints.Length == 1 ? "" : "s")}:");

                Array.Sort(breakpoints);
                foreach (var bp in breakpoints)
                    writer.WriteLine($"    {DumpCodeAddress(bp)}");
            }
        }

        private void DoClearBreakpoint(string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(parts[1])) < 0)
            {
                writer.WriteLine("Usage: clear <addrspec>");
            }
            else
            {
                dbg.SetBreakpoint(address, false);
                writer.WriteLine($"Cleared breakpoint at {DumpCodeAddress(address)}.");
            }
        }

        private void DoSetBreakpoint(string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(parts[1])) < 0)
            {
                writer.WriteLine("Usage: break <addrspec>");
            }
            else
            {
                dbg.SetBreakpoint(address, true);
                writer.WriteLine($"Set breakpoint at {DumpCodeAddress(address)}.");
            }
        }

        private async Task DoOverLineAsync()
        {
            if (dbg.State.IsPaused())
            {
                if (debugInfo == null)
                {
                    await writer.WriteLineAsync("No line information.");
                }
                else
                {
                    var oldLI = debugInfo.FindLine(dbg.CurrentPC);
                    LineInfo? newLI;
                    do
                    {
                        await dbg.StepOverAsync();
                        if (!dbg.State.IsPaused())
                            break;

                        newLI = debugInfo.FindLine(dbg.CurrentPC);
                    } while (newLI != null && newLI == oldLI);
                }
            }
        }

        private async Task DoStepLineAsync()
        {
            if (dbg.State.IsPaused())
            {
                if (debugInfo == null)
                {
                    await writer.WriteLineAsync("No line information.");
                }
                else
                {
                    var oldLI = debugInfo.FindLine(dbg.CurrentPC);
                    LineInfo? newLI;
                    do
                    {
                        await dbg.StepIntoAsync();
                        if (!dbg.State.IsPaused())
                            break;

                        newLI = debugInfo.FindLine(dbg.CurrentPC);
                    } while (newLI != null && newLI == oldLI);
                }
            }
        }

        private string DumpCodeAddress(int address)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("${0:x5}", address);

            if (debugInfo != null)
            {
                var rtn = debugInfo.FindRoutine(address);
                if (rtn != null)
                {
                    sb.AppendFormat(" ({0}+{1}", rtn.Name, address - rtn.CodeStart);

                    var li = debugInfo.FindLine(address);
                    if (li != null)
                        sb.AppendFormat(", {0}:{1}", li.Value.File, li.Value.Line);

                    sb.Append(')');
                }
            }

            return sb.ToString();
        }

        private int ParseAddress([NotNull] string spec)
        {
            if (string.IsNullOrEmpty(spec)) return -1;

            if (spec[0] == '$')
                return Convert.ToInt32(spec.Substring(1), 16);

            if (char.IsDigit(spec[0]))
                return Convert.ToInt32(spec);

            if (debugInfo == null) return -1;

            var idx = spec.LastIndexOf(':');
            if (idx >= 0)
            {
                try
                {
                    var result = debugInfo.FindCodeAddress(
                        spec.Substring(0, idx),
                        Convert.ToInt32(spec.Substring(idx + 1)));
                    if (result >= 0)
                        return result;
                }
                catch (FormatException)
                {
                }
                catch (OverflowException)
                {
                }
            }

            RoutineInfo rtn;

            idx = spec.LastIndexOf('+');
            if (idx >= 0)
            {
                try
                {
                    rtn = debugInfo.FindRoutine(spec.Substring(0, idx));
                    if (rtn != null)
                        return rtn.CodeStart + Convert.ToInt32(spec.Substring(idx + 1));
                }
                catch (FormatException)
                {
                }
                catch (OverflowException)
                {
                }
            }

            rtn = debugInfo.FindRoutine(spec);
            if (rtn != null && rtn.LineOffsets.Length > 0)
                return rtn.CodeStart + rtn.LineOffsets[0];

            return -1;
        }

        class SourceCache
        {
            private const int MAX_SRC_LINE_LEN = 50;

            private readonly string[] searchPath;
            private readonly Dictionary<string, string[]?> cache = new Dictionary<string, string[]?>();

            public SourceCache(string[] searchPath)
            {
                this.searchPath = searchPath;
            }

            private string? FindFile(string filename)
            {
                foreach (var p in searchPath)
                {
                    var combined = Path.Combine(p, filename);
                    if (File.Exists(combined))
                        return combined;
                }

                return File.Exists(filename) ? Path.GetFullPath(filename) : null;
            }

            public string? Load(LineInfo li)
            {
                if (!cache.TryGetValue(li.File, out var lines))
                {
                    var file = FindFile(li.File);
                    if (file == null)
                    {
                        cache.Add(li.File, null);
                    }
                    else if (cache.TryGetValue(file, out lines))
                    {
                        cache.Add(li.File, lines);
                    }
                    else
                    {
                        lines = File.ReadAllLines(file);
                        cache.Add(li.File, lines);
                        if (file != li.File)
                            cache.Add(file, lines);
                    }
                }

                if (lines != null)
                {
                    var line = li.Line - 1;
                    if (line < lines.Length)
                    {
                        var result = lines[line];
                        return result.Length > MAX_SRC_LINE_LEN
                            ? result.Substring(0, MAX_SRC_LINE_LEN - 3) + "..."
                            : result;
                    }
                }

                return null;
            }
        }

        #region IO adapters

        private sealed class ZIOReader : TextReader
        {
            private static readonly byte[] DummyTerminatingKeys = { };

            private readonly IAsyncZMachineIO io;

            public ZIOReader(IAsyncZMachineIO io)
            {
                this.io = io;
            }

            public override string? ReadLine()
            {
                return ReadLineAsync().GetAwaiter().GetResult();
            }

            public override async Task<string?> ReadLineAsync()
            {
                var result = await io.ReadLineAsync(string.Empty, DummyTerminatingKeys, allowDebuggerBreak: false);
                if (result.Outcome != ReadOutcome.KeyPressed)
                    throw new InvalidOperationException(
                        $"{nameof(io.ReadLineAsync)} had unexpected outcome ${result.Outcome}");

                return result.Text;
            }

            public override int Peek()
            {
                throw new NotSupportedException();
            }

            public override int Read()
            {
                throw new NotSupportedException();
            }

            public override int Read(char[] buffer, int index, int count)
            {
                throw new NotSupportedException();
            }

            public override Task<int> ReadAsync(char[] buffer, int index, int count)
            {
                throw new NotSupportedException();
            }

            public override int ReadBlock(char[] buffer, int index, int count)
            {
                throw new NotSupportedException();
            }

            public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
            {
                throw new NotSupportedException();
            }

            public override string ReadToEnd()
            {
                throw new NotSupportedException();
            }

            public override Task<string> ReadToEndAsync()
            {
                throw new NotSupportedException();
            }
        }

        internal sealed class ZIOWriter : TextWriter
        {
            private readonly IAsyncZMachineIO io;

            public ZIOWriter(IAsyncZMachineIO io)
            {
                this.io = io;

                base.NewLine = "\n";
            }

            public override Encoding Encoding => Encoding.Default;

            [AllowNull]
            public override string NewLine
            {
                get => "\n";
                set
                {
                    if (value != "\n")
                        throw new ArgumentException("Cannot change line ending", nameof(value));
                }
            }

            public override void Write(char value)
            {
                io.PutChar(value);
            }

            public override void Write(char[] buffer, int index, int count)
            {
                io.PutString(new string(buffer, index, count));
            }

            public override void Write(string? value)
            {
                if (value != null)
                    io.PutString(value);
            }

            // TODO: async methods, if io.PutStringAsync() etc is implemented

            public override void WriteLine(string? value)
            {
                if (value != null)
                    io.PutString(value);

                io.PutChar('\n');
            }
        }

        #endregion

        public void Dispose()
        {
            if (disposables == null)
                return;

            for (var i = 0; i < disposables.Length; i++)
            {
                disposables[i]?.Dispose();
                disposables[i] = null;
            }
        }
    }
}
