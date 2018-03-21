using System;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    internal class ValueFormatter
    {
        private readonly ZMachine zm;
        private readonly IDebugger dbg;

        public ValueFormatter(ZMachine zm, IDebugger dbg)
        {
            this.zm = zm;
            this.dbg = dbg;
        }

        [JetBrains.Annotations.NotNull]
        public string Format(Value value)
        {
            try
            {
                switch (value.Type)
                {
                    case ValueType.Invalid:
                        return "<invalid>";

                    case ValueType.Number:
                        return value.Content.ToString();

                    case ValueType.Object:
                        var objInfo = zm.DebugInfo?.FindObject(value.Content);
                        var objName = objInfo != null ? objInfo.Name + " " : "";
                        return $"{objName}#{value.Content} (\"{dbg.GetObjectName((ushort) value.Content)}\")";

                    case ValueType.Routine:
                        var rtnInfo = zm.DebugInfo?.FindRoutine(dbg.UnpackAddress((short) value.Content, false));
                        return rtnInfo != null
                            ? $"routine {rtnInfo.Name} ${value.Content:x5}"
                            : $"routine ${value.Content:x5}";

                    case ValueType.Attribute:
                        if (zm.DebugInfo?.Attributes.Contains((ushort) value.Content) == true)
                        {
                            var attrName = zm.DebugInfo.Attributes[(ushort) value.Content];
                            return $"attribute {attrName} #{value.Content}";
                        }
                        else
                        {
                            return $"attribute #{value.Content}";
                        }

                    case ValueType.Property:
                        if (zm.DebugInfo?.Properties.Contains((ushort) value.Content) == true)
                        {
                            var propName = zm.DebugInfo.Properties[(ushort) value.Content];
                            return $"property {propName} #{value.Content}";
                        }
                        else
                        {
                            return $"property #{value.Content}";
                        }

                    case ValueType.Variable:
                    case ValueType.VariableNumber:
                        string name;
                        if (value.Content == 0)
                        {
                            name = "sp";
                        }
                        else if (value.Content < 16)
                        {
                            var curRtn = zm.DebugInfo?.FindRoutine(dbg.CurrentPC);
                            name = curRtn?.Locals[value.Content - 1] ?? "local_" + value.Content;
                        }
                        else
                        {
                            name = zm.DebugInfo?.Globals[(byte) (value.Content - 16)] ?? "global_" + value.Content;
                        }

                        if (value.Type == ValueType.Variable)
                            return $"{name} = {dbg.ReadVariable((byte) value.Content)}";

                        return $"variable '{name} #{value.Content}";

                    case ValueType.ByteAtAddress:
                        return $"byte at ${value.Content:x5} = {dbg.ReadByte(value.Content)}";

                    case ValueType.WordAtAddress:
                        return $"word at ${value.Content:x5} = {dbg.ReadWord(value.Content)}";

                    default:
                        return $"${value.Content:x5}";
                }
            }
            catch (Exception ex)
            {
                return $"<{value.Type} {value.Content}: {ex.GetType().Name}>";
            }
        }
    }
}