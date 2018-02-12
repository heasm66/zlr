using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    public sealed class DebuggingConsole
    {
        private readonly ZMachine zm;
        private readonly IZMachineIO io;
        private readonly string[] sourcePath;

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

        public DebuggingConsole(ZMachine zm, IZMachineIO io, string[] sourcePath)
        {
            this.zm = zm;
            this.io = io;
            this.sourcePath = sourcePath;
        }

        public bool Active => active == ActiveState.Active;

        public void Activate()
        {
            if (active != ActiveState.NotStarted)
                throw new InvalidOperationException("Wrong state");

            active = ActiveState.Active;

            dbg = zm.Debug();
            src = new SourceCache(sourcePath);
            valueFormatter = new ValueFormatter(zm, dbg);

            io.PutString("ZLR Debugger\n");
            dbg.Restart();
            ShowStatus();
        }

        private void TraceCallsEventHandler(object sender, [NotNull] EnterFunctionEventArgs e)
        {
            io.PutString("[ ");

            for (var i = 0; i < e.CallDepth; i++)
                io.PutString(". ");

            RoutineInfo rtn;
            if (zm.DebugInfo != null &&
                (rtn = zm.DebugInfo.FindRoutine(dbg.UnpackAddress(e.PackedAddress, false))) != null)
            {
                io.PutString(rtn.Name);
            }
            else
            {
                io.PutString($"${e.PackedAddress:x4}");
            }

            io.PutChar('(');
            if (e.Args != null)
            {
                for (var i = 0; i < e.Args.Length; i++)
                {
                    if (i > 0)
                        io.PutString(", ");

                    io.PutString(e.Args[i].ToString());
                }
            }
            io.PutString(") ]\n");
        }

        private void ShowStatus()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                ShowCurrentPC();
            }
            else if (dbg.State == DebuggerState.Stopped)
            {
                io.PutString("Debugger is stopped.\n");
            }

            // prompt
            io.PutString("D> ");
        }

        private void ShowCurrentPC()
        {
            RoutineInfo rtn;
            if (zm.DebugInfo != null &&
                (rtn = zm.DebugInfo.FindRoutine(dbg.CurrentPC)) != null)
            {
                io.PutString(
                    $"${dbg.CurrentPC:x5} ({rtn.Name}+{dbg.CurrentPC - rtn.CodeStart})   {dbg.Disassemble(dbg.CurrentPC)}\n");

                var li = zm.DebugInfo.FindLine(dbg.CurrentPC);
                if (li != null)
                {
                    io.PutString($"{li.Value.File}:{li.Value.Line}: {src.Load(li.Value)}\n");
                }
            }
            else
            {
                io.PutString($"${dbg.CurrentPC:x5}   {dbg.Disassemble(dbg.CurrentPC)}\n");
            }
        }

        public async Task HandleCommandAsync([NotNull] string cmd)
        {
            if (cmd.Trim() == "")
            {
                if (lastCmd == null)
                {
                    io.PutString("No last command.\n");
                    ShowStatus();
                    return;
                }
                cmd = lastCmd;
            }
            else
            {
                lastCmd = cmd;
            }

            try {
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
                        io.PutString("Not implemented.\n");
                        break;

                    case "p":
                    case "print":
                        DoPrint(parts);
                        break;

                    case "so":
                    case "showobj":
                        DoShowObject(parts);
                        break;

                    case "q":
                    case "quit":
                        io.PutString("Goodbye.\n");
                        active = ActiveState.Finished;
                        return;

                    default:
                        io.PutString("Unrecognized debugger command.\n");

                        io.PutString("Commands:\n");
                        io.PutString("reset, (s)tep, (o)ver, stepline (sl), overline (ol), up, (r)un,\n");
                        io.PutString("(b)reak, (c)lear, breakpoints (bps), tracecalls (tc)\n");
                        io.PutString("backtrace (bt), (l)ocals, (g)lobals\n");
                        io.PutString("(p)rint, showobj (so)\n");
                        io.PutString("(q)uit\n");
                        break;
                }
            }
            catch (DebuggerException ex)
            {
                io.PutString(ex.ToString());
            }

            ShowStatus();
        }

        private void DoPrint([ItemNotNull] [NotNull] string[] parts)
        {
            if (parts.Length < 2)
            {
                io.PutString("Usage: print <expr>\n");
                return;
            }

            var value = Expression.Evaluate(zm, dbg, parts[1], true);
            io.PutString(valueFormatter.Format(value));
            io.PutChar('\n');
        }

        private void DoShowObject([ItemNotNull] [NotNull] string[] parts)
        {
            if (parts.Length < 2)
            {
                io.PutString("Usage: showobj <expr>\n");
                return;
            }

            var value = Expression.Evaluate(zm, dbg, parts[1]);

            var address = dbg.GetObjectAddress((ushort)value.Content);

            dbg.ParseObject(address, out var attrs, out var parent, out var sibling, out var child, out var propertyTable);

            io.PutString(
                $"=== {valueFormatter.Format(new Value(ValueType.Object, value.Content))} ===\n" +
                $"Parent: {valueFormatter.Format(new Value(ValueType.Object, parent))}\n" +
                $"Sibling: {valueFormatter.Format(new Value(ValueType.Object, sibling))}\n" +
                $"Child: {valueFormatter.Format(new Value(ValueType.Object, child))}\n");

            io.PutString("Attributes:\n");
            for (var i = 0; i < attrs.Length; i++)
            {
                byte bit = 0x80;

                for (var j = 0; j < 8; j++)
                {
                    if ((attrs[i] & bit) != 0)
                    {
                        io.PutString("  ");
                        io.PutString(valueFormatter.Format(new Value(ValueType.Attribute, i * 8 + j)));
                        io.PutChar('\n');
                    }

                    bit >>= 1;
                }
            }

            io.PutString($"Properties (table at ${propertyTable:x4}):\n");
            for (var prop = dbg.GetNextProp((ushort)value.Content, 0); prop != 0; prop = dbg.GetNextProp((ushort)value.Content, prop))
            {
                var addr = dbg.GetPropAddress((ushort)value.Content, prop);
                var length = dbg.GetPropLength(addr);

                io.PutString($"  {valueFormatter.Format(new Value(ValueType.Property, prop))} (length {length}):\n");

                io.PutString("   ");
                for (var i = 0; i < length; i++)
                {
                    var b = dbg.ReadByte(addr + i);
                    io.PutString($" {b:x2}");
                }
                io.PutChar('\n');
            }

            io.PutString("==========\n");
        }

        private void DoShowLocals()
        {
            var frames = dbg.GetCallFrames();
            int stackItems;
            if (frames.Length == 0)
            {
                io.PutString("No call frame.\n");
                stackItems = dbg.StackDepth;
            }
            else
            {
                var cf = frames[0];
                if (cf.Locals.Length == 0)
                {
                    io.PutString("No local variables.\n");
                }
                else
                {
                    io.PutString($"{cf.Locals.Length} local variable{(cf.Locals.Length == 1 ? "" : "s")}:\n");

                    var rtn = zm.DebugInfo?.FindRoutine(dbg.CurrentPC);
                    for (var i = 0; i < cf.Locals.Length; i++)
                    {
                        io.PutString("    ");
                        if (rtn != null && i < rtn.Locals.Length)
                            io.PutString(rtn.Locals[i]);
                        else
                            io.PutString($"local_{i + 1}");
                        io.PutString(string.Format(" = {0} (${0:x4})\n", cf.Locals[i]));
                    }
                }
                stackItems = dbg.StackDepth - cf.PrevStackDepth;
            }
            if (stackItems == 0)
            {
                io.PutString("No data on stack.\n");
            }
            else
            {
                io.PutString($"{stackItems} word{(stackItems == 1 ? "" : "s")} on stack:\n");
                var temp = new Stack<short>();
                for (var i = 0; i < stackItems; i++)
                {
                    var value = dbg.StackPop();
                    temp.Push(value);
                    io.PutString(string.Format("    ${0:x4} (${0})\n", value));
                }
                while (temp.Count > 0)
                    dbg.StackPush(temp.Pop());
            }
        }

        private void DoShowBacktrace()
        {
            var frames = dbg.GetCallFrames();
            io.PutString($"Call depth: {frames.Length}\n");
            io.PutString($"PC = {DumpCodeAddress(zm, dbg.CurrentPC)}\n");

            for (var i = 0; i < frames.Length; i++)
            {
                var cf = frames[i];
                io.PutString("==========\n");
                io.PutString($"[{i + 1}] return PC = {DumpCodeAddress(zm, cf.ReturnPC)}\n");
                io.PutString(
                    $"called with {cf.ArgCount} arg{(cf.ArgCount == 1 ? "" : "s")}, " +
                    $"stack depth {cf.PrevStackDepth}\n");

                if (cf.ResultStorage < 16)
                {
                    if (cf.ResultStorage == -1)
                    {
                        io.PutString("discarding result\n");
                    }
                    else if (cf.ResultStorage == 0)
                    {
                        io.PutString("storing result to stack\n");
                    }
                    else
                    {
                        var rtn = zm.DebugInfo?.FindRoutine(cf.ReturnPC);
                        if (rtn != null && cf.ResultStorage - 1 < rtn.Locals.Length)
                        {
                            io.PutString(
                                $"storing result to local {cf.ResultStorage} " +
                                $"({rtn.Locals[cf.ResultStorage - 1]})\n");
                        }
                        else
                        {
                            io.PutString($"storing result to local {cf.ResultStorage}\n");
                        }
                    }
                }
                else if (zm.DebugInfo != null && zm.DebugInfo.Globals.Contains((byte)cf.ResultStorage))
                {
                    io.PutString(
                        $"storing result to global {cf.ResultStorage} " +
                        $"({zm.DebugInfo.Globals[(byte) (cf.ResultStorage - 16)]})\n");
                }
                else
                {
                    io.PutString($"storing result to global {cf.ResultStorage}\n");
                }
            }
            io.PutString("==========\n");
        }

        private void DoToggleTraceCalls()
        {
            if (tracingCalls)
            {
                tracingCalls = false;
                dbg.Events.EnteringFunction -= TraceCallsEventHandler;
                io.PutString("Tracing calls disabled.\n");
            }
            else
            {
                tracingCalls = true;
                dbg.Events.EnteringFunction += TraceCallsEventHandler;
                io.PutString("Tracing calls enabled.\n");
            }
        }

        private void DoShowBreakpoints()
        {
            var breakpoints = dbg.GetBreakpoints();
            if (breakpoints.Length == 0)
            {
                io.PutString("No breakpoints.\n");
            }
            else
            {
                io.PutString($"{breakpoints.Length} breakpoint{(breakpoints.Length == 1 ? "" : "s")}:\n");

                Array.Sort(breakpoints);
                foreach (var bp in breakpoints)
                    io.PutString($"    {DumpCodeAddress(zm, bp)}\n");
            }
        }

        private void DoClearBreakpoint([ItemNotNull] [NotNull] string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(zm, parts[1])) < 0)
            {
                io.PutString("Usage: clear <addrspec>\n");
            }
            else
            {
                dbg.SetBreakpoint(address, false);
                io.PutString($"Cleared breakpoint at {DumpCodeAddress(zm, address)}.\n");
            }
        }

        private void DoSetBreakpoint([ItemNotNull] [NotNull] string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(zm, parts[1])) < 0)
            {
                io.PutString("Usage: break <addrspec>\n");
            }
            else
            {
                dbg.SetBreakpoint(address, true);
                io.PutString($"Set breakpoint at {DumpCodeAddress(zm, address)}.\n");
            }
        }

        private async Task DoOverLineAsync()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                if (zm.DebugInfo == null)
                {
                    io.PutString("No line information.\n");
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
                    io.PutString("No line information.\n");
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
                catch (FormatException) { }
                catch (OverflowException) { }
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
                catch (FormatException) { }
                catch (OverflowException) { }
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
    }
}
