using System;
using System.Reflection;
using System.Reflection.Emit;
using JetBrains.Annotations;

namespace ZLR.VM
{
    partial class Opcode
    {
#pragma warning disable 0169
        [Opcode(OpCount.Var, 224, true, Terminates = true, MaxVersion = 3, Alias = "call")]
        [Opcode(OpCount.Var, 224, true, Terminates = true, MinVersion = 4)]
        private void op_call_vs([NotNull] ILGenerator il)
        {
            EnterFunction(il, true);
        }

        [Opcode(OpCount.Var, 225)]
        private void op_storew([NotNull] ILGenerator il)
        {
            MethodInfo setWordCheckedMI = ZMachine.GetMethodInfo(nameof(ZMachine.SetWordChecked));
            MethodInfo setWordMI = ZMachine.GetMethodInfo(nameof(ZMachine.SetWord));
            MethodInfo trapMemoryMI = ZMachine.GetMethodInfo(nameof(ZMachine.TrapMemory));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, zm.TempWordLocal);
            il.Emit(OpCodes.Ldloc, zm.TempWordLocal);
            il.Emit(OpCodes.Conv_U2);
            LoadOperand(il, 2);

            MethodInfo impl = setWordCheckedMI;
            if (operandTypes[0] != OperandType.Variable && operandTypes[1] != OperandType.Variable)
            {
                int address = (ushort) operandValues[0] + 2 * operandValues[1];
                if (address > 64 && address + 1 < zm.RomStart)
                    impl = setWordMI;
            }
            il.Emit(OpCodes.Call, impl);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, zm.TempWordLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Call, trapMemoryMI);
        }

        [Opcode(OpCount.Var, 226)]
        private void op_storeb([NotNull] ILGenerator il)
        {
            MethodInfo setByteCheckedMI = ZMachine.GetMethodInfo(nameof(ZMachine.SetByteChecked));
            MethodInfo setByteMI = ZMachine.GetMethodInfo(nameof(ZMachine.SetByte));
            MethodInfo trapMemoryMI = ZMachine.GetMethodInfo(nameof(ZMachine.TrapMemory));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, zm.TempWordLocal);
            il.Emit(OpCodes.Ldloc, zm.TempWordLocal);
            il.Emit(OpCodes.Conv_U2);
            LoadOperand(il, 2);

            MethodInfo impl = setByteCheckedMI;
            if (operandTypes[0] != OperandType.Variable && operandTypes[1] != OperandType.Variable)
            {
                int address = (ushort) operandValues[0] + operandValues[1];
                if (address > 64 && address < zm.RomStart)
                    impl = setByteMI;
            }
            il.Emit(OpCodes.Call, impl);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, zm.TempWordLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, trapMemoryMI);
        }

        [Opcode(OpCount.Var, 227)]
        private void op_put_prop([NotNull] ILGenerator il)
        {
            MethodInfo setPropMI = ZMachine.GetMethodInfo(nameof(ZMachine.SetPropValue));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            il.Emit(OpCodes.Call, setPropMI);
        }

        [Opcode(OpCount.Var, 228, MaxVersion = 4)]
        private void op_sread([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.ReadImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
            il.Emit(OpCodes.Pop);
        }

        [Opcode(OpCount.Var, 228, true, MinVersion = 5)]
        private void op_aread([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.ReadImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
            StoreResult(il);
        }

        [Opcode(OpCount.Var, 229)]
        private void op_print_char([NotNull] ILGenerator il)
        {
            MethodInfo printZsciiMI = ZMachine.GetMethodInfo(nameof(ZMachine.PrintZSCII));
            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, printZsciiMI);
        }

        [Opcode(OpCount.Var, 230)]
        private void op_print_num([NotNull] ILGenerator il)
        {
            MethodInfo toStringMI = typeof(Convert).GetMethod(nameof(Convert.ToString), new[] {typeof(short)});
            MethodInfo printStringMI = ZMachine.GetMethodInfo(nameof(ZMachine.PrintString));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, toStringMI);
            il.Emit(OpCodes.Call, printStringMI);
        }

        [Opcode(OpCount.Var, 231, true)]
        private void op_random([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.RandomImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, impl);
            StoreResult(il);
        }

        [Opcode(OpCount.Var, 232)]
        private void op_push([NotNull] ILGenerator il)
        {
            LoadOperand(il, 0);
            PushOntoStack(il);
        }

        [Opcode(OpCount.Var, 233, IndirectVar = true, MaxVersion = 5)]
        [Opcode(OpCount.Var, 233, true, MinVersion = 6, MaxVersion = 6)]
        private void op_pull([NotNull] ILGenerator il)
        {
            if (zm.ZVersion == 6)
            {
                if (argc == 0)
                {
                    PopFromStack(il);
                }
                else
                {
                    MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.PullFromUserStack));

                    System.Diagnostics.Debug.Assert(argc == 1);

                    il.Emit(OpCodes.Ldarg_0);
                    LoadOperand(il, 0);
                    il.Emit(OpCodes.Call, impl);
                }

                StoreResult(il);
            }
            else
            {
                MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.StoreVariableImpl));

                System.Diagnostics.Debug.Assert(argc == 1);

                il.Emit(OpCodes.Ldarg_0);
                LoadOperand(il, 0);
                PopFromStack(il);
                il.Emit(OpCodes.Call, impl);
            }
        }

        [Opcode(OpCount.Var, 234, MinVersion = 3)]
        private void op_split_window([NotNull] ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo impl = typeof(IZMachineIO).GetMethod(nameof(IZMachineIO.SplitWindow));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 235, MinVersion = 3)]
        private void op_set_window([NotNull] ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo impl = typeof(IZMachineIO).GetMethod(nameof(IZMachineIO.SelectWindow));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 236, true, Terminates = true, MinVersion = 4)]
        private void op_call_vs2([NotNull] ILGenerator il)
        {
            EnterFunction(il, true);
        }

        [Opcode(OpCount.Var, 237, MinVersion = 4)]
        private void op_erase_window([NotNull] ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo eraseWindowMI = typeof(IZMachineIO).GetMethod(nameof(IZMachineIO.EraseWindow));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, eraseWindowMI);
        }

        [Opcode(OpCount.Var, 238, MinVersion = 4)]
        private void op_erase_line(ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo eraseLineMI = typeof(IZMachineIO).GetMethod(nameof(IZMachineIO.EraseLine));

            Label? skip = null;
            if (argc >= 1)
            {
                if (operandTypes[0] == OperandType.Variable)
                {
                    skip = il.DefineLabel();
                    LoadOperand(il, 0);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Bne_Un, skip.Value);
                }
                else if (operandValues[0] != 1)
                    return;
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            il.Emit(OpCodes.Call, eraseLineMI);

            if (skip != null)
                il.MarkLabel(skip.Value);
        }

        [Opcode(OpCount.Var, 239, MinVersion = 4)]
        private void op_set_cursor([NotNull] ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo moveCursorMI = typeof(IZMachineIO).GetMethod(nameof(IZMachineIO.MoveCursor));

            LoadOperand(il, 0);
            il.Emit(OpCodes.Stloc, zm.TempWordLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            LoadOperand(il, 1); // x
            il.Emit(OpCodes.Ldloc, zm.TempWordLocal); // y
            il.Emit(OpCodes.Call, moveCursorMI);
        }

        [Opcode(OpCount.Var, 240, MinVersion = 4)]
        private void op_get_cursor([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.GetCursorPos));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 241, MinVersion = 4)]
        private void op_set_text_style([NotNull] ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo impl = typeof(IZMachineIO).GetMethod(nameof(IZMachineIO.SetTextStyle));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 242, MinVersion = 4)]
        private void op_buffer_mode([NotNull] ILGenerator il)
        {
            var ioFI = ZMachine.GetFieldInfo(nameof(ZMachine.io));
            MethodInfo impl = typeof(IZMachineIO).GetMethod("set_" + nameof(IZMachineIO.Buffering));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ioFI);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 243, MinVersion = 3)]
        private void op_output_stream([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.SetOutputStream));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 244, MinVersion = 3)]
        private void op_input_stream([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.SetInputStream));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 245, MinVersion = 3)]
        private void op_sound_effect([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.SoundEffectImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 246, true, MinVersion = 4)]
        private void op_read_char([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.ReadCharImpl));

            if (operandTypes.Length > 0)
            {
                // the operand value is ignored (standard says it must be 1)
                if (operandTypes[0] == OperandType.Variable && operandValues[0] == 0)
                {
                    PopFromStack(il);
                    il.Emit(OpCodes.Pop);
                }
            }

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            il.Emit(OpCodes.Call, impl);
            StoreResult(il);
        }

        [Opcode(OpCount.Var, 247, true, true, MinVersion = 4)]
        private void op_scan_table([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.ScanTableImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Conv_U2);
            StoreResult(il);
            Branch(il, OpCodes.Brtrue, OpCodes.Brfalse);
        }

        [Opcode(OpCount.One, 143, true, MaxVersion = 4)]
        [Opcode(OpCount.Var, 248, true, MinVersion = 5)]
        private void op_not([NotNull] ILGenerator il)
        {
            LoadOperand(il, 0);
            il.Emit(OpCodes.Not);
            StoreResult(il);
        }

        [Opcode(OpCount.Var, 249, Terminates = true, MinVersion = 5)]
        private void op_call_vn([NotNull] ILGenerator il)
        {
            EnterFunction(il, false);
        }

        [Opcode(OpCount.Var, 250, Terminates = true, MinVersion = 5)]
        private void op_call_vn2([NotNull] ILGenerator il)
        {
            EnterFunction(il, false);
        }

        [Opcode(OpCount.Var, 251, MinVersion = 5)]
        private void op_tokenise([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.Tokenize));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 252, MinVersion = 5)]
        private void op_encode_text([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.EncodeTextImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 253, MinVersion = 5)]
        private void op_copy_table([NotNull] ILGenerator il)
        {
            if (operandTypes[1] != OperandType.Variable && operandValues[1] == 0)
            {
                MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.ZeroMemory));

                il.Emit(OpCodes.Ldarg_0);
                LoadOperand(il, 0);
                LoadOperand(il, 2);
                il.Emit(OpCodes.Call, impl);
            }
            else
            {
                MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.CopyTableImpl));

                il.Emit(OpCodes.Ldarg_0);
                LoadOperand(il, 0);
                LoadOperand(il, 1);
                LoadOperand(il, 2);
                il.Emit(OpCodes.Call, impl);
            }
        }

        [Opcode(OpCount.Var, 254, MinVersion = 5)]
        private void op_print_table([NotNull] ILGenerator il)
        {
            MethodInfo impl = ZMachine.GetMethodInfo(nameof(ZMachine.PrintTableImpl));

            il.Emit(OpCodes.Ldarg_0);
            LoadOperand(il, 0);
            LoadOperand(il, 1);
            LoadOperand(il, 2);
            LoadOperand(il, 3);
            il.Emit(OpCodes.Call, impl);
        }

        [Opcode(OpCount.Var, 255, false, true, MinVersion = 5)]
        private void op_check_arg_count([NotNull] ILGenerator il)
        {
            MethodInfo getTopFrameMI = ZMachine.GetMethodInfo("get_" + nameof(ZMachine.TopFrame));
            FieldInfo argCountMI = typeof(ZMachine.CallFrame).GetField(nameof(ZMachine.CallFrame.ArgCount));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getTopFrameMI);
            il.Emit(OpCodes.Ldfld, argCountMI);

            LoadOperand(il, 0);
            Branch(il, OpCodes.Bge, OpCodes.Blt);
        }
#pragma warning restore 0169
    }
}
