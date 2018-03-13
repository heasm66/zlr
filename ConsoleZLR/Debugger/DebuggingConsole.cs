using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    public sealed class DebuggingConsole
    {
        [NotNull]
        private readonly ZMachine zm;

        [NotNull]
        private readonly TextReader reader;

        [NotNull]
        private readonly TextWriter writer;

        [NotNull, ItemNotNull]
        private readonly string[] sourcePath;

        private readonly bool sharingIO;

        private enum ActiveState
        {
            NotStarted,
            Active,
            Finished,
        }

        private ActiveState active;

        private IDebugger dbg;
        private SourceCache src;
        private bool tracingCalls;
        private string lastCmd;
        private ValueFormatter valueFormatter;

        private static readonly char[] COMMAND_DELIM = { ' ' };

        public DebuggingConsole([NotNull] ZMachine zm, [NotNull] IAsyncZMachineIO io,
            [ItemNotNull, NotNull] IEnumerable<string> sourcePath)
            : this(zm, new ZIOReader(io), new ZIOWriter(io), sourcePath)
        {
            sharingIO = true;
        }

        public DebuggingConsole([NotNull] ZMachine zm, [NotNull] TextReader reader, [NotNull] TextWriter writer,
            [NotNull, ItemNotNull] IEnumerable<string> sourcePath)
        {
            this.zm = zm;
            this.reader = reader;
            this.writer = writer;
            this.sourcePath = sourcePath.ToArray();
        }

        [System.Diagnostics.Conditional("DEBUG_DEBUGGER")]
        private static void AttachDebugger()
        {
            System.Diagnostics.Debugger.Launch();
        }

        [System.Diagnostics.Conditional("DEBUG_DEBUGGER"), StringFormatMethod("format")]
        private static void DebugWriteLine([NotNull] string format, [NotNull] params object[] args)
        {
            System.Diagnostics.Debug.WriteLine(format, args);
        }

        public async Task RunDebuggerAsync()
        {
            Activate();
            await writer.FlushAsync();

            var pendingTasks = new Queue<Task>();

            AttachDebugger();

            async Task WaitForCommand(Task prevTask)
            {
                try
                {
                    // wait for the command to finish
                    DebugWriteLine("[{0}] Awaiting prev task {1}", Task.CurrentId, prevTask.Id);
                    await prevTask;
                    DebugWriteLine("[{0}] Done awaiting task {1}", Task.CurrentId, prevTask.Id);
                }
                catch (Exception ex)
                {
                    DebugWriteLine("[{0}] Caught exception while awaiting task {1}: {2}: {3}",
                        Task.CurrentId, prevTask.Id, ex.GetType().Name, ex.Message);
                    writer.WriteLine(ex);
                }

                if (Active)
                    ShowStatus();

                DebugWriteLine("[{0}] Flushing", Task.CurrentId);
                await writer.FlushAsync();
                DebugWriteLine("[{0}] Flushed", Task.CurrentId);
            }

            while (Active)
            {
                DebugWriteLine("[{0}] Reading line", Task.CurrentId);
                var result =
                    await reader.ReadLineAsync(); //XXX need to abort the read if a pending task sets Active = false
                if (result == null)
                {
                    DebugWriteLine("[{0}] Read null, exiting loop", Task.CurrentId);
                    break;
                }

                DebugWriteLine("[{0}] Handling command: {1}", Task.CurrentId, result);
                var task = HandleCommandAsync(result);

                if (sharingIO)
                {
                    DebugWriteLine("[{0}] Blocking on task {1}", Task.CurrentId, task.Id);
                    await WaitForCommand(task);
                    DebugWriteLine("[{0}] Done blocking on task {1}", Task.CurrentId, task.Id);
                }
                else
                {
                    // let the user enter another command while this one is running
                    //XXX if there's a previous pending task, this one should wait for it to finish before printing any output, otherwise the writer may throw
                    DebugWriteLine("[{0}] Queueing pending task", Task.CurrentId);
                    pendingTasks.Enqueue(WaitForCommand(task));

                    // ...but only one more
                    DebugWriteLine("[{0}] {1} task(s) in queue", Task.CurrentId, pendingTasks.Count);
                    while (pendingTasks.Count >= 2)
                    {
                        var pend = pendingTasks.Dequeue();
                        DebugWriteLine("[{0}] Waiting for pending task {1}", Task.CurrentId, pend.Id);
                        try
                        {
                            await pend;
                            DebugWriteLine("[{0}] Done waiting for pending task {1}", Task.CurrentId, pend.Id);
                        }
                        catch (TaskCanceledException)
                        {
                            // nada
                            DebugWriteLine("[{0}] Pending task {1} was canceled", Task.CurrentId, pend.Id);
                        }
                    }
                }
            }

            DebugWriteLine("[{0}] Exiting debugger loop, {1} pending task(s) remaining",
                Task.CurrentId, pendingTasks.Count);

            if (pendingTasks.Count > 0)
            {
                await Task.WhenAll(pendingTasks);
            }

            DebugWriteLine("[{0}] No more pending tasks, exiting for real", Task.CurrentId);

            //XXX find out why we're hanging after the program ends in listen mode
        }

        public bool Active => active == ActiveState.Active;

        private void Activate()
        {
            if (active != ActiveState.NotStarted)
                throw new InvalidOperationException("Wrong state");

            active = ActiveState.Active;

            dbg = zm.Debug();
            src = new SourceCache(sourcePath);
            valueFormatter = new ValueFormatter(zm, dbg);

            writer.WriteLine("ZLR Debugger {0}", typeof(DebuggingConsole).Assembly.GetName().Version);
            dbg.Restart();
            ShowStatus();
        }

        private void TraceCallsEventHandler(object sender, [NotNull] EnterFunctionEventArgs e)
        {
            writer.Write("[ ");

            for (var i = 0; i < e.CallDepth; i++)
                writer.Write(". ");

            RoutineInfo rtn;
            if (zm.DebugInfo != null &&
                (rtn = zm.DebugInfo.FindRoutine(dbg.UnpackAddress(e.PackedAddress, false))) != null)
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

        private void ShowStatus()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                ShowCurrentPC();
            }
            else if (dbg.State == DebuggerState.Stopped)
            {
                writer.WriteLine("Debugger is stopped.");
            }

            // prompt
            writer.Write("D> ");
        }

        private void ShowCurrentPC()
        {
            RoutineInfo rtn;
            if (zm.DebugInfo != null &&
                (rtn = zm.DebugInfo.FindRoutine(dbg.CurrentPC)) != null)
            {
                writer.WriteLine(
                    $"${dbg.CurrentPC:x5} ({rtn.Name}+{dbg.CurrentPC - rtn.CodeStart})   {dbg.Disassemble(dbg.CurrentPC)}");

                var li = zm.DebugInfo.FindLine(dbg.CurrentPC);
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

        private async Task HandleCommandAsync([NotNull] string cmd)
        {
            if (cmd.Trim() == "")
            {
                if (lastCmd == null)
                {
                    writer.WriteLine("No last command.");
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
                        if (dbg.State == DebuggerState.Paused)
                            await dbg.StepIntoAsync();
                        break;

                    case "o":
                    case "over":
                        if (dbg.State == DebuggerState.Paused)
                            await dbg.StepOverAsync();
                        break;

                    case "up":
                        if (dbg.State == DebuggerState.Paused)
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
                        if (dbg.State == DebuggerState.Stopped)
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
                        writer.WriteLine("Goodbye.");
                        active = ActiveState.Finished;
                        return;

                    case "h":
                    case "help":
                    case "?":
                        writer.WriteLine("Commands:");
                        writer.WriteLine("reset, (s)tep, (o)ver, stepline (sl), overline (ol), up, (r)un,");
                        writer.WriteLine("(b)reak, (c)lear, breakpoints (bps), tracecalls (tc)");
                        writer.WriteLine("backtrace (bt), (l)ocals, (g)lobals");
                        writer.WriteLine("(p)rint, showobj (so), tree");
                        writer.WriteLine("(q)uit");
                        break;

                    default:
                        writer.WriteLine("Unrecognized debugger command.");
                        writer.WriteLine();
                        goto case "help";

                }
            }
            catch (DebuggerException ex)
            {
                writer.WriteLine(ex.ToString());
            }
        }

        private Value Evaluate([NotNull] string exprText, bool wantLvalue = false)
        {
            //return InformExpression.Evaluate(zm, dbg, exprText, wantLvalue);
            return ZilExpression.Evaluate(zm, dbg, exprText, wantLvalue);
        }

        private void DoPrint([ItemNotNull, NotNull] string[] parts)
        {
            if (parts.Length < 2)
            {
                writer.WriteLine("Usage: print <expr>");
                return;
            }

            var value = Evaluate(parts[1], true);
            writer.WriteLine(valueFormatter.Format(value));
        }

        private void DoShowObject([ItemNotNull, NotNull] string[] parts)
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

        private void DoShowTree([ItemNotNull, NotNull] string[] parts)
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
            if (zm.DebugInfo != null)
                return (ushort) zm.DebugInfo.Objects.Max(o => o.Number);

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

                    var rtn = zm.DebugInfo?.FindRoutine(dbg.CurrentPC);
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

            var globals = zm.DebugInfo != null
                ? (from p in zm.DebugInfo.Globals
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
            writer.WriteLine($"PC = {DumpCodeAddress(zm, dbg.CurrentPC)}");

            for (var i = 0; i < frames.Length; i++)
            {
                var cf = frames[i];
                writer.WriteLine("==========");
                writer.WriteLine($"[{i + 1}] return PC = {DumpCodeAddress(zm, cf.ReturnPC)}");
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
                        var rtn = zm.DebugInfo?.FindRoutine(cf.ReturnPC);
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
                else if (zm.DebugInfo != null && zm.DebugInfo.Globals.Contains((byte) cf.ResultStorage))
                {
                    writer.WriteLine(
                        $"storing result to global {cf.ResultStorage} " +
                        $"({zm.DebugInfo.Globals[(byte) (cf.ResultStorage - 16)]})");
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
                    writer.WriteLine($"    {DumpCodeAddress(zm, bp)}");
            }
        }

        private void DoClearBreakpoint([ItemNotNull, NotNull] string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(zm, parts[1])) < 0)
            {
                writer.WriteLine("Usage: clear <addrspec>");
            }
            else
            {
                dbg.SetBreakpoint(address, false);
                writer.WriteLine($"Cleared breakpoint at {DumpCodeAddress(zm, address)}.");
            }
        }

        private void DoSetBreakpoint([ItemNotNull, NotNull] string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(zm, parts[1])) < 0)
            {
                writer.WriteLine("Usage: break <addrspec>");
            }
            else
            {
                dbg.SetBreakpoint(address, true);
                writer.WriteLine($"Set breakpoint at {DumpCodeAddress(zm, address)}.");
            }
        }

        private async Task DoOverLineAsync()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                if (zm.DebugInfo == null)
                {
                    writer.WriteLine("No line information.");
                }
                else
                {
                    var oldLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    LineInfo? newLI;
                    do
                    {
                        await dbg.StepOverAsync();
                        if (dbg.State != DebuggerState.Paused)
                            break;

                        newLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    } while (newLI != null && newLI == oldLI);
                }
            }
        }

        private async Task DoStepLineAsync()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                if (zm.DebugInfo == null)
                {
                    writer.WriteLine("No line information.");
                }
                else
                {
                    var oldLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    LineInfo? newLI;
                    do
                    {
                        await dbg.StepIntoAsync();
                        if (dbg.State != DebuggerState.Paused)
                            break;

                        newLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    } while (newLI != null && newLI == oldLI);
                }
            }
        }

        [NotNull]
        private static string DumpCodeAddress([NotNull] ZMachine zm, int address)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("${0:x5}", address);

            var rtn = zm.DebugInfo?.FindRoutine(address);
            if (rtn != null)
            {
                sb.AppendFormat(" ({0}+{1}", rtn.Name, address - rtn.CodeStart);

                var li = zm.DebugInfo.FindLine(address);
                if (li != null)
                    sb.AppendFormat(", {0}:{1}", li.Value.File, li.Value.Line);

                sb.Append(')');
            }

            return sb.ToString();
        }

        private static int ParseAddress(ZMachine zm, [NotNull] string spec)
        {
            if (string.IsNullOrEmpty(spec)) return -1;

            if (spec[0] == '$')
                return Convert.ToInt32(spec.Substring(1), 16);

            if (char.IsDigit(spec[0]))
                return Convert.ToInt32(spec);

            if (zm.DebugInfo == null) return -1;

            var idx = spec.LastIndexOf(':');
            if (idx >= 0)
            {
                try
                {
                    var result = zm.DebugInfo.FindCodeAddress(
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
                    rtn = zm.DebugInfo.FindRoutine(spec.Substring(0, idx));
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

            rtn = zm.DebugInfo.FindRoutine(spec);
            if (rtn != null && rtn.LineOffsets.Length > 0)
                return rtn.CodeStart + rtn.LineOffsets[0];

            return -1;
        }

        class SourceCache
        {
            private const int MAX_SRC_LINE_LEN = 50;

            private readonly string[] searchPath;
            private readonly Dictionary<string, string[]> cache = new Dictionary<string, string[]>();

            public SourceCache(string[] searchPath)
            {
                this.searchPath = searchPath;
            }

            [CanBeNull]
            private string FindFile(string filename)
            {
                foreach (var p in searchPath)
                {
                    var combined = Path.Combine(p, filename);
                    if (File.Exists(combined))
                        return combined;
                }

                return File.Exists(filename) ? Path.GetFullPath(filename) : null;
            }

            [CanBeNull]
            public string Load(LineInfo li)
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

            public override string ReadLine()
            {
                return ReadLineAsync().GetAwaiter().GetResult();
            }

            [ItemNotNull]
            public override async Task<string> ReadLineAsync()
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

            public override void Write([NotNull] string value)
            {
                io.PutString(value);
            }

            // TODO: async methods, if io.PutStringAsync() etc is implemented

            public override void WriteLine([NotNull] string value)
            {
                io.PutString(value);
                io.PutChar('\n');
            }
        }

        #endregion
    }
}
