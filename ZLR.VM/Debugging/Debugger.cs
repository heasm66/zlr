using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace ZLR.VM.Debugging
{
    public enum DebuggerState
    {
        Stopped,
        Paused,
        Running,
    }

    [PublicAPI]
    public sealed class EnterFunctionEventArgs : EventArgs
    {
        public short PackedAddress { get; }

        [CanBeNull]
        public short[] Args { get; }

        public int ResultStorage { get; }
        public int ReturnPC { get; }
        public int CallDepth { get; }

        public EnterFunctionEventArgs(short packedAddress, [CanBeNull] short[] args, int resultStorage, int returnPC, int callDepth)
        {
            PackedAddress = packedAddress;
            Args = args;
            ResultStorage = resultStorage;
            ReturnPC = returnPC;
            CallDepth = callDepth;
        }
    }

    public interface IDebuggerEvents
    {
        event EventHandler<EnterFunctionEventArgs> EnteringFunction;
    }

    [PublicAPI]
    public interface IDebugger
    {
        DebuggerState State { get; }

        void Restart();

        void StepInto();
        void StepOver();
        void StepUp();

        void Run();
        void SetBreakpoint(int address, bool enabled);
        int[] GetBreakpoints();

        short Call(short packedAddress, [NotNull] short[] args);

        byte ReadByte(int address);
        short ReadWord(int address);
        void WriteByte(int address, byte value);
        void WriteWord(int address, short value);
        short ReadVariable(byte number);
        void WriteVariable(byte number, short value);

        string DecodeString(int address);

        int GetObjectAddress(ushort number);
        string GetObjectName(ushort number);
        void ParseObject(int address, out byte[] attrs, out ushort parent,
            out ushort sibling, out ushort child, out int propertyTable);
        void ParseProperty(int address, out byte number, out byte length);
        int GetPropAddress(ushort obj, short prop);
        int GetPropLength(int address);
        short GetNextProp(ushort obj, short prop);

        int CallDepth { get; }
        ICallFrame[] GetCallFrames();
        int CurrentPC { get; }
        string Disassemble(int address);

        int StackDepth { get; }
        void StackPush(short value);
        short StackPop();

        int UnpackAddress(short packedAddress, bool forString);
        short PackAddress(int address, bool forString);

        IDebuggerEvents Events { get; }
    }

    public interface ICallFrame
    {
        int ReturnPC { get; }
        int PrevStackDepth { get; }
        short[] Locals { get; }
        int ArgCount { get; }
        int ResultStorage { get; }
    }
}

namespace ZLR.VM
{
    using Debugging;

    partial class ZMachine : IDebuggerEvents
    {
        private int stepping = -1;
        private readonly Dictionary<int, bool> breakpoints = new Dictionary<int, bool>();
        private DebuggerState debugState;

        [NotNull]
        public IDebugger Debug()
        {
            debugging = true;
            cache?.Clear();

            return new Debugger(this);
        }

#pragma warning disable 0169
        private bool DebugCheck(int pcToCheck)
        {
            if (stepping >= 0)
            {
                if (--stepping < 0)
                {
                    pc = pcToCheck;
                    return true;
                }
            }
            else if (breakpoints.ContainsKey(pcToCheck))
            {
                pc = pcToCheck;
                debugState = DebuggerState.Paused;
                return true;
            }

            // continue
            return false;
        }
#pragma warning restore 0169

        private class Debugger : IDebugger
        {
            private readonly ZMachine zm;

            public Debugger(ZMachine zm)
            {
                this.zm = zm;
            }

            #region IDebugger Members

            public DebuggerState State => zm.debugState;

            public void Restart()
            {
                zm.Restart();
                if (zm.cache == null)
                    zm.cache = new LruCache<int, CachedCode>(zm.cacheSize);
                zm.debugState = DebuggerState.Paused;
            }

            private void OneStep()
            {
                CachedCode entry;
                int thisPC = zm.pc;
                if (thisPC < zm.romStart || zm.cache.TryGetValue(thisPC, out entry) == false)
                {
                    int count;
                    entry = new CachedCode(zm.pc, zm.CompileZCode(out count));
                    if (thisPC >= zm.romStart)
                        zm.cache.Add(thisPC, entry, count);
                }
                zm.pc = entry.NextPC;
                entry.Code();
            }

            public void StepInto()
            {
                zm.stepping = 1;
                zm.running = true;

                OneStep();

                zm.stepping = -1;
                zm.debugState = zm.running ? DebuggerState.Paused : DebuggerState.Stopped;
            }

            public void StepOver()
            {
                int callDepth = zm.callStack.Count;
                StepInto();

                while (zm.callStack.Count > callDepth)
                    StepInto();
            }

            public void StepUp()
            {
                int callDepth = zm.callStack.Count;
                StepInto();

                while (zm.callStack.Count >= callDepth)
                    StepInto();
            }

            public void Run()
            {
                // step ahead if the current line has a breakpoint on it
                if (zm.breakpoints.ContainsKey(zm.pc))
                    StepInto();

                zm.running = true;
                zm.debugState = DebuggerState.Running;
                while (zm.running && zm.debugState == DebuggerState.Running)
                    OneStep();

                zm.debugState = zm.running ? DebuggerState.Paused : DebuggerState.Stopped;
            }

            public void SetBreakpoint(int address, bool enabled)
            {
                if (enabled)
                    zm.breakpoints[address] = true;
                else
                    zm.breakpoints.Remove(address);
            }

            [NotNull]
            public int[] GetBreakpoints() => zm.breakpoints.Keys.ToArray();

            public short Call(short packedAddress, short[] args)
            {
                zm.running = true;
                zm.EnterFunctionImpl(packedAddress, args, 0, zm.pc);
                zm.JitLoop();
                return zm.stack.Pop();
            }

            public byte ReadByte(int address) => zm.zmem[address];

            public short ReadWord(int address) => (short)((zm.zmem[address] << 8) | zm.zmem[address + 1]);

            public void WriteByte(int address, byte value)
            {
                zm.zmem[address] = value;
            }

            public void WriteWord(int address, short value)
            {
                zm.zmem[address] = (byte)(value >> 8);
                zm.zmem[address + 1] = (byte)value;
            }

            public short ReadVariable(byte number)
            {
                if (number == 0)
                {
                    return zm.stack.Peek();
                }
                else if (number < 16)
                {
                    return zm.topFrame.Locals[number - 1];
                }
                else
                {
                    return zm.GetWord(zm.GlobalsOffset + 2 * (number - 16));
                }
            }

            public void WriteVariable(byte number, short value)
            {
                if (number == 0)
                {
                    zm.stack.Pop();
                    zm.stack.Push(value);
                }
                else if (number < 16)
                {
                    zm.topFrame.Locals[number - 1] = value;
                }
                else
                {
                    zm.SetWord(zm.GlobalsOffset + 2 * (number - 16), value);
                }
            }

            [NotNull]
            public string DecodeString(int address) => zm.DecodeString(address);

            public int GetObjectAddress(ushort number) => zm.GetObjectAddress(number);

            [NotNull]
            public string GetObjectName(ushort number) => zm.GetObjectName(number);

            public void ParseObject(int address, [NotNull] out byte[] attrs,
                out ushort parent, out ushort sibling, out ushort child, out int propertyTable)
            {
                if (zm.zversion <= 3)
                {
                    attrs = new[] {
                        zm.GetByte(address),
                        zm.GetByte(address+1),
                        zm.GetByte(address+2),
                        zm.GetByte(address+3),
                    };
                    parent = zm.GetByte(address + 4);
                    sibling = zm.GetByte(address + 5);
                    child = zm.GetByte(address + 6);
                    propertyTable = zm.GetWord(address + 7);
                }
                else
                {
                    attrs = new[] {
                        zm.GetByte(address),
                        zm.GetByte(address+1),
                        zm.GetByte(address+2),
                        zm.GetByte(address+3),
                        zm.GetByte(address+4),
                        zm.GetByte(address+5),
                    };
                    parent = (ushort)zm.GetWord(address + 6);
                    sibling = (ushort)zm.GetWord(address + 8);
                    child = (ushort)zm.GetWord(address + 10);
                    propertyTable = zm.GetWord(address + 12);
                }
            }

            public void ParseProperty(int address, out byte number, out byte length)
            {
                //XXX
                throw new NotImplementedException();
            }

            public int GetPropAddress(ushort obj, short prop) => zm.GetPropAddr(obj, prop);

            public int GetPropLength(int address) => zm.GetPropLength((ushort)address);

            public short GetNextProp(ushort obj, short prop) => zm.GetNextProp(obj, prop);

            public int CallDepth => zm.callStack.Count;

            [ItemNotNull]
            [NotNull]
            public ICallFrame[] GetCallFrames() => zm.callStack.ToArray<ICallFrame>();

            public int CurrentPC => zm.pc;

            public string Disassemble(int address)
            {
                int opc = zm.pc;
                try
                {
                    zm.pc = address;
                    OperandType[] types = new OperandType[8];
                    short[] argv = new short[8];

                    Opcode opcode = zm.DecodeOneOp(types, argv);

                    RoutineInfo rtn;
                    if (zm.debugFile == null)
                        rtn = null;
                    else
                        rtn = zm.debugFile.FindRoutine(address);

                    return opcode.Disassemble(delegate(byte varnum)
                    {
                        if (rtn != null && varnum - 1 < rtn.Locals.Length)
                            return "local_" + varnum + "(" + rtn.Locals[varnum - 1] + ")";
                        else if (varnum < 16)
                            return "local_" + varnum;
                        else if (zm.debugFile != null && zm.debugFile.Globals.Contains((byte)(varnum - 16)))
                            return "global_" + varnum + "(" + zm.debugFile.Globals[(byte)(varnum - 16)] + ")";
                        else
                            return "global_" + varnum;
                    });

                }
                finally
                {
                    zm.pc = opc;
                }
            }

            public int StackDepth
            {
                get { return zm.stack.Count; }
            }

            public void StackPush(short value)
            {
                zm.stack.Push(value);
            }

            public short StackPop()
            {
                return zm.stack.Pop();
            }

            public int UnpackAddress(short packedAddress, bool forString)
            {
                return zm.UnpackAddress(packedAddress, forString);
            }

            public short PackAddress(int address, bool forString)
            {
                switch (zm.zversion)
                {
                    case 1:
                    case 2:
                    case 3:
                        return (short) (address/2);

                    case 4:
                    case 5:
                        return (short) (address/4);

                    case 6:
                    case 7:
                        const int HDR_CODE_OFFSET = 0x28;
                        const int HDR_STR_OFFSET = 0x2A;
                        var offset = ReadWord(forString ? HDR_STR_OFFSET : HDR_CODE_OFFSET)*8;
                        return (short) ((address - offset)/4);

                    case 8:
                        return (short) (address/8);

                    default:
                        throw new NotImplementedException();
                }
            }

            public IDebuggerEvents Events
            {
                get { return zm; }
            }

            #endregion
        }

        #region IDebuggerEvents Members

        public event EventHandler<EnterFunctionEventArgs> EnteringFunction;

        #endregion

        private void HandleEnterFunction(short packedAddress, short[] args, int resultStorage, int returnPC)
        {
            var handler = EnteringFunction;
            if (handler != null)
                handler(this, new EnterFunctionEventArgs(
                    packedAddress, args, resultStorage, returnPC, callStack.Count));
        }
    }
}
