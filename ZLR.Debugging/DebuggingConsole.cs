using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Debugging
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

        private static readonly char[] COMMAND_DELIM = new char[] { ' ' };

        public DebuggingConsole(ZMachine zm, IZMachineIO io, string[] sourcePath)
        {
            this.zm = zm;
            this.io = io;
            this.sourcePath = sourcePath;
        }

        public bool Active
        {
            get { return active == ActiveState.Active; }
        }

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

        private void TraceCallsEventHandler(object sender, EnterFunctionEventArgs e)
        {
            io.PutString("[ ");

            for (int i = 0; i < e.CallDepth; i++)
                io.PutString(". ");

            RoutineInfo rtn;
            if (zm.DebugInfo != null &&
                (rtn = zm.DebugInfo.FindRoutine(dbg.UnpackAddress(e.PackedAddress, false))) != null)
            {
                io.PutString(rtn.Name);
            }
            else
            {
                io.PutString(string.Format("${0:x4}", e.PackedAddress));
            }

            io.PutChar('(');
            if (e.Args != null)
            {
                for (int i = 0; i < e.Args.Length; i++)
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
                io.PutString(string.Format("${0:x5} ({1}+{2})   {3}\n",
                    dbg.CurrentPC,
                    rtn.Name,
                    dbg.CurrentPC - rtn.CodeStart,
                    dbg.Disassemble(dbg.CurrentPC)));

                LineInfo? li = zm.DebugInfo.FindLine(dbg.CurrentPC);
                if (li != null)
                {
                    io.PutString(string.Format("{0}:{1}: {2}\n",
                        li.Value.File,
                        li.Value.Line,
                        src.Load(li.Value)));
                }
            }
            else
            {
                io.PutString(string.Format("${0:x5}   {1}\n",
                    dbg.CurrentPC,
                    dbg.Disassemble(dbg.CurrentPC)));
            }
        }

        public void HandleCommand(string cmd)
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
                string[] parts = cmd.Split(COMMAND_DELIM, 2, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0].ToLower())
                {
                    case "reset":
                        dbg.Restart();
                        break;

                    case "s":
                    case "step":
                        if (dbg.State == DebuggerState.Paused)
                            dbg.StepInto();
                        break;

                    case "o":
                    case "over":
                        if (dbg.State == DebuggerState.Paused)
                            dbg.StepOver();
                        break;

                    case "up":
                        if (dbg.State == DebuggerState.Paused)
                            dbg.StepUp();
                        break;

                    case "sl":
                    case "stepline":
                        DoStepLine();
                        break;

                    case "ol":
                    case "overline":
                        DoOverLine();
                        break;

                    case "r":
                    case "run":
                        if (dbg.State == DebuggerState.Stopped)
                            dbg.Restart();
                        dbg.Run();
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

        private void DoPrint(string[] parts)
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

        private void DoShowObject(string[] parts)
        {
            if (parts.Length < 2)
            {
                io.PutString("Usage: showobj <expr>\n");
                return;
            }

            var value = Expression.Evaluate(zm, dbg, parts[1]);

            int address = dbg.GetObjectAddress((ushort)value.Content);

            byte[] attrs;
            ushort parent, sibling, child;
            int propertyTable;
            dbg.ParseObject(address, out attrs, out parent, out sibling, out child, out propertyTable);

            io.PutString(string.Format("=== {0} ===\nParent: {1}\nSibling: {2}\nChild: {3}\n",
                valueFormatter.Format(new Value(ValueType.Object, value.Content)),
                valueFormatter.Format(new Value(ValueType.Object, parent)),
                valueFormatter.Format(new Value(ValueType.Object, sibling)),
                valueFormatter.Format(new Value(ValueType.Object, child))));

            io.PutString("Attributes:\n");
            for (int i = 0; i < attrs.Length; i++)
            {
                byte bit = 0x80;

                for (int j = 0; j < 8; j++)
                {
                    if ((attrs[i] & bit) != 0)
                    {
                        io.PutString("  ");
                        io.PutString(valueFormatter.Format(new Value(ValueType.Attribute, i * 8 + j)));
                        io.PutChar('\n');
                    }
                }
            }

            io.PutString(string.Format("Properties (table at ${0:x4}):\n", propertyTable));
            for (short prop = dbg.GetNextProp((ushort)value.Content, 0); prop != 0; prop = dbg.GetNextProp((ushort)value.Content, prop))
            {
                var addr = dbg.GetPropAddress((ushort)value.Content, prop);
                var length = dbg.GetPropLength(addr);

                io.PutString(string.Format("  {0} (length {1}):\n",
                    valueFormatter.Format(new Value(ValueType.Property, prop)),
                    length));

                io.PutString("   ");
                for (int i = 0; i < length; i++)
                {
                    byte b = dbg.ReadByte(addr + i);
                    io.PutString(string.Format(" {0:x2}", b));
                }
                io.PutChar('\n');
            }

            io.PutString("==========\n");
        }

        private void DoShowLocals()
        {
            ICallFrame[] frames;
            frames = dbg.GetCallFrames();
            int stackItems;
            if (frames.Length == 0)
            {
                io.PutString("No call frame.\n");
                stackItems = dbg.StackDepth;
            }
            else
            {
                ICallFrame cf = frames[0];
                if (cf.Locals.Length == 0)
                {
                    io.PutString("No local variables.\n");
                }
                else
                {
                    io.PutString(string.Format("{0} local variable{1}:\n",
                        cf.Locals.Length,
                        cf.Locals.Length == 1 ? "" : "s"));

                    var rtn = zm.DebugInfo != null ? zm.DebugInfo.FindRoutine(dbg.CurrentPC) : null;
                    for (int i = 0; i < cf.Locals.Length; i++)
                    {
                        io.PutString("    ");
                        if (rtn != null && i < rtn.Locals.Length)
                            io.PutString(rtn.Locals[i]);
                        else
                            io.PutString(string.Format("local_{0}", i + 1));
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
                io.PutString(string.Format("{0} word{1} on stack:\n",
                    stackItems,
                    stackItems == 1 ? "" : "s"));
                Stack<short> temp = new Stack<short>();
                for (int i = 0; i < stackItems; i++)
                {
                    short value = dbg.StackPop();
                    temp.Push(value);
                    io.PutString(string.Format("    ${0:x4} (${0})\n", value));
                }
                while (temp.Count > 0)
                    dbg.StackPush(temp.Pop());
            }
        }

        private void DoShowBacktrace()
        {
            ICallFrame[] frames;
            frames = dbg.GetCallFrames();
            io.PutString(string.Format("Call depth: {0}\n", frames.Length));
            io.PutString(string.Format("PC = {0}\n", DumpCodeAddress(zm, dbg, dbg.CurrentPC)));

            for (int i = 0; i < frames.Length; i++)
            {
                ICallFrame cf = frames[i];
                io.PutString("==========\n");
                io.PutString(string.Format("[{0}] return PC = {1}\n", i + 1, DumpCodeAddress(zm, dbg, cf.ReturnPC)));
                io.PutString(string.Format("called with {0} arg{1}, stack depth {2}\n",
                    cf.ArgCount,
                    cf.ArgCount == 1 ? "" : "s",
                    cf.PrevStackDepth));

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
                        RoutineInfo rtn = null;
                        if (zm.DebugInfo != null)
                            rtn = zm.DebugInfo.FindRoutine(cf.ReturnPC);
                        if (rtn != null && cf.ResultStorage - 1 < rtn.Locals.Length)
                            io.PutString(string.Format("storing result to local {0} ({1})\n",
                                cf.ResultStorage,
                                rtn.Locals[cf.ResultStorage - 1]));
                        else
                            io.PutString(string.Format("storing result to local {0}\n", cf.ResultStorage));
                    }
                }
                else if (zm.DebugInfo.Globals.Contains((byte)cf.ResultStorage))
                {
                    io.PutString(string.Format("storing result to global {0} ({1})\n", cf.ResultStorage,
                        zm.DebugInfo.Globals[(byte)(cf.ResultStorage - 16)]));
                }
                else
                {
                    io.PutString(string.Format("storing result to global {0}\n", cf.ResultStorage));
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
            int[] breakpoints = dbg.GetBreakpoints();
            if (breakpoints.Length == 0)
            {
                io.PutString("No breakpoints.\n");
            }
            else
            {
                io.PutString(string.Format("{0} breakpoint{1}:\n",
                    breakpoints.Length,
                    breakpoints.Length == 1 ? "" : "s"));

                Array.Sort(breakpoints);
                foreach (int bp in breakpoints)
                    io.PutString(string.Format("    {0}\n", DumpCodeAddress(zm, dbg, bp)));
            }
        }

        private void DoClearBreakpoint(string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(zm, dbg, parts[1])) < 0)
            {
                io.PutString("Usage: clear <addrspec>\n");
            }
            else
            {
                dbg.SetBreakpoint(address, false);
                io.PutString(string.Format("Cleared breakpoint at {0}.\n", DumpCodeAddress(zm, dbg, address)));
            }
        }

        private void DoSetBreakpoint(string[] parts)
        {
            int address;
            if (parts.Length < 2 || (address = ParseAddress(zm, dbg, parts[1])) < 0)
            {
                io.PutString("Usage: break <addrspec>\n");
            }
            else
            {
                dbg.SetBreakpoint(address, true);
                io.PutString(string.Format("Set breakpoint at {0}.\n", DumpCodeAddress(zm, dbg, address)));
            }
        }

        private void DoOverLine()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                if (zm.DebugInfo == null)
                {
                    io.PutString("No line information.\n");
                }
                else
                {
                    LineInfo? oldLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    LineInfo? newLI;
                    do
                    {
                        dbg.StepOver();
                        if (dbg.State != DebuggerState.Paused)
                            break;

                        newLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    } while (newLI != null && newLI == oldLI);
                }
            }
        }

        private void DoStepLine()
        {
            if (dbg.State == DebuggerState.Paused)
            {
                if (zm.DebugInfo == null)
                {
                    io.PutString("No line information.\n");
                }
                else
                {
                    LineInfo? oldLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    LineInfo? newLI;
                    do
                    {
                        dbg.StepInto();
                        if (dbg.State != DebuggerState.Paused)
                            break;

                        newLI = zm.DebugInfo.FindLine(dbg.CurrentPC);
                    } while (newLI != null && newLI == oldLI);
                }
            }
        }

        private static string DumpCodeAddress(ZMachine zm, IDebugger dbg, int address)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("${0:x5}", address);

            if (zm.DebugInfo != null)
            {
                RoutineInfo rtn = zm.DebugInfo.FindRoutine(address);
                if (rtn != null)
                {
                    sb.AppendFormat(" ({0}+{1}", rtn.Name, address - rtn.CodeStart);

                    LineInfo? li = zm.DebugInfo.FindLine(address);
                    if (li != null)
                        sb.AppendFormat(", {0}:{1}", li.Value.File, li.Value.Line);

                    sb.Append(')');
                }
            }

            return sb.ToString();
        }

        private static int ParseAddress(ZMachine zm, IDebugger dbg, string spec)
        {
            if (!string.IsNullOrEmpty(spec))
            {
                if (spec[0] == '$')
                    return Convert.ToInt32(spec.Substring(1), 16);

                if (char.IsDigit(spec[0]))
                    return Convert.ToInt32(spec);

                if (zm.DebugInfo != null)
                {
                    int idx = spec.LastIndexOf(':');
                    if (idx >= 0)
                    {
                        try
                        {
                            int result = zm.DebugInfo.FindCodeAddress(
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
                }
            }

            return -1;
        }



        class SourceCache
        {
            private const int MAX_SRC_LINE_LEN = 50;

            private readonly string[] searchPath;
            private Dictionary<string, string[]> cache = new Dictionary<string, string[]>();

            public SourceCache(string[] searchPath)
            {
                this.searchPath = searchPath;
            }

            private string FindFile(string filename)
            {
                foreach (string p in searchPath)
                {
                    string combined = Path.Combine(p, filename);
                    if (File.Exists(combined))
                        return combined;
                }

                if (File.Exists(filename))
                    return Path.GetFullPath(filename);

                return null;
            }

            public string Load(LineInfo li)
            {
                string[] lines;

                if (cache.TryGetValue(li.File, out lines) == false)
                {
                    string file = FindFile(li.File);
                    if (file == null)
                    {
                        cache.Add(li.File, null);
                    }
                    else if (cache.TryGetValue(file, out lines) == true)
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
                    int line = li.Line - 1;
                    if (line < lines.Length)
                    {
                        string result = lines[line];
                        if (result.Length > MAX_SRC_LINE_LEN)
                            return result.Substring(0, MAX_SRC_LINE_LEN - 3) + "...";
                        else
                            return result;
                    }
                }

                return null;
            }
        }
    }
}
