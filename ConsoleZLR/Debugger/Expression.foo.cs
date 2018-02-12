using System;
using System.Linq;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    internal enum ValueType
    {
        Invalid,

        // rvalues
        Number,
        Object,
        Attribute,
        Property,
        Pointer,
        PackedString,
        UnpackedString,
        Routine,
        ReadBuf,
        LexBuf,

        // lvalues
        Variable,
        ByteAtAddress,
        WordAtAddress,
    }

    internal struct Value
    {
        public readonly ValueType Type;
        public readonly int Content;

        public static readonly Value Invalid = new Value(ValueType.Invalid, 0);

        public Value(ValueType type, int content)
        {
            Type = type;
            Content = content;
        }
    }

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
            switch (value.Type)
            {
                case ValueType.Invalid:
                    return "<invalid>";

                case ValueType.Number:
                    return value.Content.ToString();

                case ValueType.Object:
                    var objInfo = zm.DebugInfo.FindObject(value.Content);
                    var objName = objInfo != null ? objInfo.Name + " " : "";
                    return $"{objName}#{value.Content} (\"{dbg.GetObjectName((ushort) value.Content)}\")";

                case ValueType.Routine:
                    var rtnInfo = zm.DebugInfo.FindRoutine(dbg.UnpackAddress((short)value.Content, false));
                    return rtnInfo != null
                        ? $"routine {rtnInfo.Name} ${value.Content:x5}"
                        : $"routine ${value.Content:x5}";

                case ValueType.Attribute:
                    if (zm.DebugInfo.Attributes.Contains((ushort)value.Content))
                    {
                        var attrName = zm.DebugInfo.Attributes[(ushort)value.Content];
                        return $"attribute {attrName} #{value.Content}";
                    }
                    else
                    {
                        goto default;
                    }

                case ValueType.Property:
                    if (zm.DebugInfo.Properties.Contains((ushort)value.Content))
                    {
                        var propName = zm.DebugInfo.Properties[(ushort)value.Content];
                        return $"property {propName} #{value.Content}";
                    }
                    else
                    {
                        goto default;
                    }

                case ValueType.Variable:
                    string name;
                    if (value.Content == 0)
                    {
                        name = "sp";
                    }
                    else if (value.Content < 16)
                    {
                        var curRtn = zm.DebugInfo.FindRoutine(dbg.CurrentPC);
                        if (curRtn != null)
                        {
                            name = curRtn.Locals[value.Content - 1];
                        }
                        else
                        {
                            name = "local_" + value.Content;
                        }
                    }
                    else
                    {
                        name = zm.DebugInfo.Globals[(byte) (value.Content - 16)] ?? "global_" + value.Content;
                    }
                    return $"{name} = {dbg.ReadVariable((byte) value.Content)}";

                case ValueType.ByteAtAddress:
                    return $"byte at ${value.Content:x5} = {dbg.ReadByte(value.Content)}";

                case ValueType.WordAtAddress:
                    return $"word at ${value.Content:x5} = {dbg.ReadWord(value.Content)}";

                default:
                    return $"${value.Content:x5}";
            }
        }
    }

    internal static class Expression
    {
        public static Value Evaluate(ZMachine zm, IDebugger dbg, string exprText, bool wantLvalue = false)
        {
            var lexer = new ExpressionLexer(new AntlrInputStream(exprText));
            var parser = new ExpressionParser(new CommonTokenStream(lexer));

            var resultContext = parser.expression();

            var visitor = new EvaluatingVisitor(zm, dbg);
            var result = visitor.Visit(resultContext);
            if (!wantLvalue)
                result = visitor.Resolve(result);
            return result;
        }

        class EvaluatingVisitor : ExpressionBaseVisitor<Value>
        {
            private readonly ZMachine zm;
            private readonly IDebugger dbg;

            public EvaluatingVisitor(ZMachine zm, IDebugger dbg)
            {
                this.zm = zm;
                this.dbg = dbg;
            }

            public override Value VisitDecLiteral([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.DecLiteralContext context)
            {
                return new Value(ValueType.Number, int.Parse(context.Decimal_literal().GetText()));
            }

            public override Value VisitBinLiteral([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.BinLiteralContext context)
            {
                return new Value(ValueType.Number, Convert.ToInt32(context.Binary_literal().GetText().Substring(2), 2));
            }

            public override Value VisitHexLiteral([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.HexLiteralContext context)
            {
                return new Value(ValueType.Number, Convert.ToInt32(context.Hex_literal().GetText().Substring(1), 16));
            }

            public override Value VisitCharLiteral([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.CharLiteralContext context)
            {
                var text = context.Char_literal().GetText();
                return new Value(ValueType.Number, text[text.Length - 2]);
            }

            public override Value VisitIdentifier([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.IdentifierContext context)
            {
                var text = context.Identifier().GetText();

                return ParseIdentifier(text);
            }

            public override Value VisitQuotedIdentifier([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.QuotedIdentifierContext context)
            {
                var sb = new StringBuilder(context.Quoted_identifier().GetText());

                // remove brackets
                sb.Remove(0, 1);
                sb.Remove(sb.Length - 1, 1);

                // remove backslashes
                for (int i = 0; i < sb.Length; i++)
                {
                    if (sb[i] == '\\')
                    {
                        sb.Remove(i, 1);
                        // this will skip the next character
                    }
                }

                return ParseIdentifier(sb.ToString());
            }

            private Value ParseIdentifier(string name)
            {
                if (zm.DebugInfo != null)
                {
                    var curRtn = zm.DebugInfo.FindRoutine(dbg.CurrentPC);
                    if (curRtn != null)
                    {
                        for (int i = 0; i < curRtn.Locals.Length; i++)
                        {
                            if (curRtn.Locals[i] == name)
                            {
                                return new Value(ValueType.Variable, i + 1);
                            }
                        }
                    }

                    var rtn = zm.DebugInfo.FindRoutine(name);
                    if (rtn != null)
                        return new Value(ValueType.Routine, dbg.PackAddress(rtn.CodeStart, false));

                    var obj = zm.DebugInfo.FindObject(name);
                    if (obj != null)
                        return new Value(ValueType.Object, obj.Number);

                    if (zm.DebugInfo.Attributes.Contains(name))
                        return new Value(ValueType.Attribute, zm.DebugInfo.Attributes[name]);

                    if (zm.DebugInfo.Properties.Contains(name))
                        return new Value(ValueType.Property, zm.DebugInfo.Properties[name]);

                    if (zm.DebugInfo.Globals.Contains(name))
                        return new Value(ValueType.Variable, 16 + zm.DebugInfo.Globals[name]);
                }

                return Value.Invalid;
            }

            public override Value VisitAddition([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.AdditionContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content + right.Content);
            }

            public override Value VisitSubtraction([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.SubtractionContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content - right.Content);
            }

            public override Value VisitMultiplication([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.MultiplicationContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content * right.Content);
            }

            public override Value VisitDivision([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.DivisionContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                if (right.Content == 0)
                    return Value.Invalid;

                return new Value(left.Type, left.Content / right.Content);
            }

            public override Value VisitModulus([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.ModulusContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                if (right.Content == 0)
                    return Value.Invalid;

                return new Value(left.Type, left.Content % right.Content);
            }

            public override Value VisitBitwiseAnd([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.BitwiseAndContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content & right.Content);
            }

            public override Value VisitBitwiseOr([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.BitwiseOrContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content | right.Content);
            }

            public override Value VisitBitwiseNot([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.BitwiseNotContext context)
            {
                var right = Resolve(Visit(context.right));
                return new Value(right.Type, ~right.Content);
            }

            public override Value VisitLogicalAnd([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.LogicalAndContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, (left.Content != 0) & (right.Content != 0) ? 1 : 0);
            }

            public override Value VisitLogicalOr([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.LogicalOrContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, (left.Content != 0) & (right.Content != 0) ? 1 : 0);
            }

            public override Value VisitLogicalNot([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.LogicalNotContext context)
            {
                var right = Resolve(Visit(context.right));
                return new Value(right.Type, right.Content == 0 ? 1 : 0);
            }

            public override Value VisitDereferenceByte([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.DereferenceByteContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.ByteAtAddress, left.Content + right.Content);
            }

            public override Value VisitDereferenceWord([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.DereferenceWordContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.WordAtAddress, left.Content + 2 * right.Content);
            }

            public override Value VisitUnaryMinus([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.UnaryMinusContext context)
            {
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, -right.Content);
            }

            public override Value VisitParens([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.ParensContext context)
            {
                return Visit(context.expression());
            }

            public override Value VisitEquality([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.EqualityContext context)
            {
                var left = Resolve(Visit(context.left));

                foreach (var r in context.orSequence()._alts)
                {
                    if (Resolve(Visit(r)).Content == left.Content)
                        return new Value(ValueType.Number, 1);
                }

                return new Value(ValueType.Number, 0);
            }

            public override Value VisitInequality([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.InequalityContext context)
            {
                var left = Resolve(Visit(context.left));

                foreach (var r in context.orSequence()._alts)
                {
                    if (Resolve(Visit(r)).Content == left.Content)
                        return new Value(ValueType.Number, 0);
                }

                return new Value(ValueType.Number, 1);
            }

            public override Value VisitGreater([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.GreaterContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content > right.Content ? 1 : 0);
            }

            public override Value VisitGreaterEqual([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.GreaterEqualContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content >= right.Content ? 1 : 0);
            }

            public override Value VisitLess([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.LessContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content < right.Content ? 1 : 0);
            }

            public override Value VisitLessEqual([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.LessEqualContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content <= right.Content ? 1 : 0);
            }

            public override Value VisitHas([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.HasContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestAttribute((ushort)left.Content, right.Content) ? 1 : 0);
            }

            public override Value VisitHasnt([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.HasntContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestAttribute((ushort)left.Content, right.Content) ? 0 : 1);
            }

            private bool TestAttribute(ushort obj, int attr)
            {
                var objAddr = dbg.GetObjectAddress(obj);

                dbg.ParseObject(objAddr, out var attrs, out _, out _, out _, out _);

                int bit = 128 >> (attr & 7);
                int offset = attr >> 3;
                return (attrs[offset] & bit) != 0;
            }

            public override Value VisitIn([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.InContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestParent((ushort)left.Content, (ushort)right.Content) ? 0 : 1);
            }

            public override Value VisitNotin([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.NotinContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestParent((ushort)left.Content, (ushort)right.Content) ? 1 : 0);
            }

            private bool TestParent(ushort obj, ushort possibleParent)
            {
                var objAddr = dbg.GetObjectAddress(obj);

                dbg.ParseObject(objAddr, out _, out var parent, out _, out _, out _);

                return parent == possibleParent;
            }

            public override Value VisitProvides([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.ProvidesContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);
                return new Value(ValueType.Number, propAddr != 0 ? 1 : 0);
            }

            public override Value VisitAssignment([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.AssignmentContext context)
            {
                var left = Visit(context.left);
                var right = Resolve(Visit(context.right));

                switch (left.Type)
                {
                    case ValueType.Variable:
                        dbg.WriteVariable((byte)left.Content, (short)right.Content);
                        break;

                    case ValueType.ByteAtAddress:
                        dbg.WriteByte(left.Content, (byte)right.Content);
                        break;

                    case ValueType.WordAtAddress:
                        dbg.WriteWord(left.Content, (short)right.Content);
                        break;

                    default:
                        throw new DebuggerException("Assignment to non-lvalue");
                }

                return right;
            }

            public override Value VisitMember([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.MemberContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);
                var propLen = dbg.GetPropLength(propAddr);

                switch (propLen)
                {
                    case 1:
                        return new Value(ValueType.Number, dbg.ReadByte(propAddr));

                    case 2:
                        return new Value(ValueType.Number, dbg.ReadWord(propAddr));

                    default:
                        throw new DebuggerException("Reading property with length " + propLen);
                }
            }

            public override Value VisitMemberAddress([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.MemberAddressContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Pointer, dbg.GetPropAddress((ushort)left.Content, (short)right.Content));
            }

            public override Value VisitMemberLength([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.MemberLengthContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);
                return new Value(ValueType.Number, dbg.GetPropLength(propAddr));
            }

            public override Value VisitCall([JetBrains.Annotations.NotNull] [NotNull] ExpressionParser.CallContext context)
            {
                var func = Resolve(Visit(context.left));
                var args = context.arguments()._values.Select(v => (short)Resolve(Visit(v)).Content).ToArray();
                var result = dbg.CallAsync((short) func.Content, args).Result;  // TODO: asyncify?
                return result != null ? new Value(ValueType.Number, (int)result) : new Value(ValueType.Invalid, 0);
            }

            public Value Resolve(Value v)
            {
                switch (v.Type)
                {
                    case ValueType.Variable:
                        return new Value(ValueType.Number, dbg.ReadVariable((byte)v.Content));

                    case ValueType.ByteAtAddress:
                        return new Value(ValueType.Number, dbg.ReadByte(v.Content));

                    case ValueType.WordAtAddress:
                        return new Value(ValueType.Number, dbg.ReadWord(v.Content));

                    default:
                        return v;
                }
            }
        }
    }
}
