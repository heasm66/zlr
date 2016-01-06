using Antlr4.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZLR.VM;
using ZLR.VM.Debugging;
using Antlr4.Runtime.Tree;
using Antlr4.Runtime.Misc;

namespace ZLR.Debugging
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
            this.Type = type;
            this.Content = content;
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
                    return string.Format("{0}{1}#{2} (\"{3}\")",
                        objInfo != null ? objInfo.Name : "",
                        objInfo != null ? " " : "",
                        value.Content,
                        dbg.GetObjectName((ushort)value.Content));

                case ValueType.Routine:
                    var rtnInfo = zm.DebugInfo.FindRoutine(dbg.UnpackAddress((short)value.Content, false));
                    if (rtnInfo != null)
                    {
                        return string.Format("routine {0} ${1:x5}", rtnInfo.Name, value.Content);
                    }
                    else
                    {
                        return string.Format("routine ${0:x5}", value.Content);
                    }

                case ValueType.Attribute:
                    if (zm.DebugInfo.Attributes.Contains((ushort)value.Content))
                    {
                        var attrName = zm.DebugInfo.Attributes[(ushort)value.Content];
                        return string.Format("attribute {0} #{1}", attrName, value.Content);
                    }
                    else
                    {
                        goto default;
                    }

                case ValueType.Property:
                    if (zm.DebugInfo.Properties.Contains((ushort)value.Content))
                    {
                        var propName = zm.DebugInfo.Properties[(ushort)value.Content];
                        return string.Format("property {0} #{1}", propName, value.Content);
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
                        name = zm.DebugInfo.Globals[(byte)(value.Content - 16)];
                        if (name == null)
                        {
                            name = "global_" + value.Content;
                        }
                    }
                    return string.Format("{0} = {1}", name, dbg.ReadVariable((byte)value.Content));

                case ValueType.ByteAtAddress:
                    return string.Format("byte at ${0:x5} = {1}", value.Content, dbg.ReadByte(value.Content));

                case ValueType.WordAtAddress:
                    return string.Format("word at ${0:x5} = {1}", value.Content, dbg.ReadWord(value.Content));

                default:
                    return string.Format("${0:x5}", value.Content);
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

            public override Value VisitDecLiteral([NotNull] ExpressionParser.DecLiteralContext context)
            {
                return new Value(ValueType.Number, int.Parse(context.Decimal_literal().GetText()));
            }

            public override Value VisitBinLiteral([NotNull] ExpressionParser.BinLiteralContext context)
            {
                return new Value(ValueType.Number, Convert.ToInt32(context.Binary_literal().GetText().Substring(2), 2));
            }

            public override Value VisitHexLiteral([NotNull] ExpressionParser.HexLiteralContext context)
            {
                return new Value(ValueType.Number, Convert.ToInt32(context.Hex_literal().GetText().Substring(1), 16));
            }

            public override Value VisitCharLiteral([NotNull] ExpressionParser.CharLiteralContext context)
            {
                var text = context.Char_literal().GetText();
                return new Value(ValueType.Number, text[text.Length - 2]);
            }

            public override Value VisitIdentifier([NotNull] ExpressionParser.IdentifierContext context)
            {
                var text = context.Identifier().GetText();

                return ParseIdentifier(text);
            }

            public override Value VisitQuotedIdentifier([NotNull] ExpressionParser.QuotedIdentifierContext context)
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

            public override Value VisitAddition([NotNull] ExpressionParser.AdditionContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content + right.Content);
            }

            public override Value VisitSubtraction([NotNull] ExpressionParser.SubtractionContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content - right.Content);
            }

            public override Value VisitMultiplication([NotNull] ExpressionParser.MultiplicationContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content * right.Content);
            }

            public override Value VisitDivision([NotNull] ExpressionParser.DivisionContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                if (right.Content == 0)
                    return Value.Invalid;

                return new Value(left.Type, left.Content / right.Content);
            }

            public override Value VisitModulus([NotNull] ExpressionParser.ModulusContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                if (right.Content == 0)
                    return Value.Invalid;

                return new Value(left.Type, left.Content % right.Content);
            }

            public override Value VisitBitwiseAnd([NotNull] ExpressionParser.BitwiseAndContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content & right.Content);
            }

            public override Value VisitBitwiseOr([NotNull] ExpressionParser.BitwiseOrContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content | right.Content);
            }

            public override Value VisitBitwiseNot([NotNull] ExpressionParser.BitwiseNotContext context)
            {
                var right = Resolve(Visit(context.right));
                return new Value(right.Type, ~right.Content);
            }

            public override Value VisitLogicalAnd([NotNull] ExpressionParser.LogicalAndContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, (left.Content != 0) & (right.Content != 0) ? 1 : 0);
            }

            public override Value VisitLogicalOr([NotNull] ExpressionParser.LogicalOrContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, (left.Content != 0) & (right.Content != 0) ? 1 : 0);
            }

            public override Value VisitLogicalNot([NotNull] ExpressionParser.LogicalNotContext context)
            {
                var right = Resolve(Visit(context.right));
                return new Value(right.Type, right.Content == 0 ? 1 : 0);
            }

            public override Value VisitDereferenceByte([NotNull] ExpressionParser.DereferenceByteContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.ByteAtAddress, left.Content + right.Content);
            }

            public override Value VisitDereferenceWord([NotNull] ExpressionParser.DereferenceWordContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.WordAtAddress, left.Content + 2 * right.Content);
            }

            public override Value VisitUnaryMinus([NotNull] ExpressionParser.UnaryMinusContext context)
            {
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, -right.Content);
            }

            public override Value VisitParens([NotNull] ExpressionParser.ParensContext context)
            {
                return Visit(context.expression());
            }

            public override Value VisitEquality([NotNull] ExpressionParser.EqualityContext context)
            {
                var left = Resolve(Visit(context.left));

                foreach (var r in context.orSequence()._alts)
                {
                    if (Resolve(Visit(r)).Content == left.Content)
                        return new Value(ValueType.Number, 1);
                }

                return new Value(ValueType.Number, 0);
            }

            public override Value VisitInequality([NotNull] ExpressionParser.InequalityContext context)
            {
                var left = Resolve(Visit(context.left));

                foreach (var r in context.orSequence()._alts)
                {
                    if (Resolve(Visit(r)).Content == left.Content)
                        return new Value(ValueType.Number, 0);
                }

                return new Value(ValueType.Number, 1);
            }

            public override Value VisitGreater([NotNull] ExpressionParser.GreaterContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content > right.Content ? 1 : 0);
            }

            public override Value VisitGreaterEqual([NotNull] ExpressionParser.GreaterEqualContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content >= right.Content ? 1 : 0);
            }

            public override Value VisitLess([NotNull] ExpressionParser.LessContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content < right.Content ? 1 : 0);
            }

            public override Value VisitLessEqual([NotNull] ExpressionParser.LessEqualContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(left.Type, left.Content <= right.Content ? 1 : 0);
            }

            public override Value VisitHas([NotNull] ExpressionParser.HasContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestAttribute((ushort)left.Content, right.Content) ? 1 : 0);
            }

            public override Value VisitHasnt([NotNull] ExpressionParser.HasntContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestAttribute((ushort)left.Content, right.Content) ? 0 : 1);
            }

            private bool TestAttribute(ushort obj, int attr)
            {
                var objAddr = dbg.GetObjectAddress(obj);

                byte[] attrs;
                ushort parent, sibling, child;
                int propertyTable;

                dbg.ParseObject(objAddr, out attrs, out parent, out sibling, out child, out propertyTable);

                int bit = 128 >> (attr & 7);
                int offset = attr >> 3;
                return (attrs[offset] & bit) != 0;
            }

            public override Value VisitIn([NotNull] ExpressionParser.InContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestParent((ushort)left.Content, (ushort)right.Content) ? 0 : 1);
            }

            public override Value VisitNotin([NotNull] ExpressionParser.NotinContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Number, TestParent((ushort)left.Content, (ushort)right.Content) ? 1 : 0);
            }

            private bool TestParent(ushort obj, ushort possibleParent)
            {
                var objAddr = dbg.GetObjectAddress(obj);

                byte[] attrs;
                ushort parent, sibling, child;
                int propertyTable;

                dbg.ParseObject(objAddr, out attrs, out parent, out sibling, out child, out propertyTable);

                return parent == possibleParent;
            }

            public override Value VisitProvides([NotNull] ExpressionParser.ProvidesContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);
                return new Value(ValueType.Number, propAddr != 0 ? 1 : 0);
            }

            public override Value VisitAssignment([NotNull] ExpressionParser.AssignmentContext context)
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

            public override Value VisitMember([NotNull] ExpressionParser.MemberContext context)
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

            public override Value VisitMemberAddress([NotNull] ExpressionParser.MemberAddressContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));
                return new Value(ValueType.Pointer, dbg.GetPropAddress((ushort)left.Content, (short)right.Content));
            }

            public override Value VisitMemberLength([NotNull] ExpressionParser.MemberLengthContext context)
            {
                var left = Resolve(Visit(context.left));
                var right = Resolve(Visit(context.right));

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);
                return new Value(ValueType.Number, dbg.GetPropLength(propAddr));
            }

            public override Value VisitCall([NotNull] ExpressionParser.CallContext context)
            {
                var func = Resolve(Visit(context.left));
                var args = context.arguments()._values.Select(v => (short)Resolve(Visit(v)).Content).ToArray();
                return new Value(ValueType.Number, dbg.Call((short)func.Content, args));
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
