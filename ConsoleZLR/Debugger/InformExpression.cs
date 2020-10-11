using System;
using System.Linq;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    internal static class InformExpression
    {
        public static Value Evaluate(ZMachine zm, IDebugger dbg, string exprText, bool wantLvalue = false)
        {
            var lexer = new InformExpressionLexer(new AntlrInputStream(exprText));
            var parser = new InformExpressionParser(new CommonTokenStream(lexer));

            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();

            var resultContext = parser.expression();

            var visitor = new EvaluatingVisitor(zm, dbg);
            var result = visitor.Visit(resultContext);
            if (!wantLvalue)
                result = visitor.Resolve(result);
            return result;
        }

        class EvaluatingVisitor : InformExpressionBaseVisitor<Value>
        {
            private readonly ZMachine zm;
            private readonly IDebugger dbg;

            public EvaluatingVisitor(ZMachine zm, IDebugger dbg)
            {
                this.zm = zm;
                this.dbg = dbg;
            }

            public override Value VisitDecLiteral([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.DecLiteralContext context)
            {
                return Value.Number(int.Parse(context.Decimal_literal().GetText()));
            }

            public override Value VisitBinLiteral([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.BinLiteralContext context)
            {
                return Value.Number(Convert.ToInt32(context.Binary_literal().GetText().Substring(2), 2));
            }

            public override Value VisitHexLiteral([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.HexLiteralContext context)
            {
                return Value.Number(Convert.ToInt32(context.Hex_literal().GetText().Substring(1), 16));
            }

            public override Value VisitCharLiteral([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.CharLiteralContext context)
            {
                var text = context.Char_literal().GetText();
                return Value.Number(text[^2]);
            }

            public override Value VisitIdentifier([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.IdentifierContext context)
            {
                var text = context.Identifier().GetText();

                return ParseIdentifier(text);
            }

            public override Value VisitQuotedIdentifier([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.QuotedIdentifierContext context)
            {
                var sb = new StringBuilder(context.Quoted_identifier().GetText());

                // remove brackets
                sb.Remove(0, 1);
                sb.Remove(sb.Length - 1, 1);

                // remove backslashes
                for (var i = 0; i < sb.Length; i++)
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
                        for (var i = 0; i < curRtn.Locals.Length; i++)
                        {
                            if (curRtn.Locals[i] == name)
                            {
                                return Value.Variable(i + 1);
                            }
                        }
                    }

                    var rtn = zm.DebugInfo.FindRoutine(name);
                    if (rtn != null)
                        return Value.Routine(dbg.PackAddress(rtn.CodeStart, false));

                    var obj = zm.DebugInfo.FindObject(name);
                    if (obj != null)
                        return Value.Object(obj.Number);

                    if (zm.DebugInfo.Attributes.Contains(name))
                        return Value.Attribute(zm.DebugInfo.Attributes[name]);

                    if (zm.DebugInfo.Properties.Contains(name))
                        return Value.Property(zm.DebugInfo.Properties[name]);

                    if (zm.DebugInfo.Globals.Contains(name))
                        return Value.Variable(16 + zm.DebugInfo.Globals[name]);
                }

                return Value.Invalid;
            }

            public override Value VisitAddition([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.AdditionContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left + right;
            }

            public override Value VisitSubtraction([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.SubtractionContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left - right;
            }

            public override Value VisitMultiplication([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.MultiplicationContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left * right;
            }

            public override Value VisitDivision([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.DivisionContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left / right;
            }

            public override Value VisitModulus([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.ModulusContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left % right;
            }

            public override Value VisitBitwiseAnd([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.BitwiseAndContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left & right;
            }

            public override Value VisitBitwiseOr([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.BitwiseOrContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left | right;
            }

            public override Value VisitBitwiseNot([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.BitwiseNotContext context)
            {
                var right = Resolve(context.right);
                return ~right;
            }

            public override Value VisitLogicalAnd([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.LogicalAndContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left.LogicalAnd(right);
            }

            public override Value VisitLogicalOr([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.LogicalOrContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left.LogicalOr(right);
            }

            public override Value VisitLogicalNot([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.LogicalNotContext context)
            {
                var right = Resolve(context.right);
                return !right;
            }

            public override Value VisitDereferenceByte([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.DereferenceByteContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) => Value.ByteAtAddress(a.Content + b.Content));
            }

            public override Value VisitDereferenceWord([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.DereferenceWordContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) => Value.WordAtAddress(a.Content + 2 * b.Content));
            }

            public override Value VisitUnaryMinus([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.UnaryMinusContext context)
            {
                var right = Resolve(context.right);
                return -right;
            }

            public override Value VisitParens([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.ParensContext context)
            {
                return Visit(context.expression());
            }

            public override Value VisitEquality([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.EqualityContext context)
            {
                var left = Resolve(context.left);

                if (!left.IsValid)
                    return Value.Invalid;

                foreach (var r in context.orSequence()._alts.Select(Resolve))
                {
                    if (!r.IsValid)
                        return Value.Invalid;

                    if (r.Content == left.Content)
                        return Value.Boolean(true);
                }

                return Value.Boolean(false);
            }

            public override Value VisitInequality([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.InequalityContext context)
            {
                var left = Resolve(context.left);

                if (!left.IsValid)
                    return Value.Invalid;

                foreach (var r in context.orSequence()._alts.Select(Resolve))
                {
                    if (!r.IsValid)
                        return Value.Invalid;

                    if (r.Content == left.Content)
                        return Value.Boolean(false);
                }

                return Value.Boolean(true);
            }

            public override Value VisitGreater([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.GreaterContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left > right;
            }

            public override Value VisitGreaterEqual([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.GreaterEqualContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left >= right;
            }

            public override Value VisitLess([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.LessContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left < right;
            }

            public override Value VisitLessEqual([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.LessEqualContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left <= right;
            }

            public override Value VisitHas([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.HasContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Boolean(TestAttribute((ushort) a.Content, b.Content)));
            }

            public override Value VisitHasnt([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.HasntContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Boolean(!TestAttribute((ushort) a.Content, b.Content)));
            }

            private bool TestAttribute(ushort obj, int attr)
            {
                var attrs = dbg.GetObjectAttrs(obj);
                var bit = 128 >> (attr & 7);
                var offset = attr >> 3;
                return (attrs[offset] & bit) != 0;
            }

            public override Value VisitIn([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.InContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Boolean(TestParent((ushort) a.Content, (ushort) b.Content)));
            }

            public override Value VisitNotin([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.NotinContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Boolean(!TestParent((ushort) a.Content, (ushort) b.Content)));
            }

            private bool TestParent(ushort obj, ushort possibleParent)
            {
                return dbg.GetObjectParent(obj) == possibleParent;
            }

            public override Value VisitProvides([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.ProvidesContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);

                return Value.Guard(left, right, (a, b) =>
                    Value.Boolean(dbg.GetPropAddress((ushort) a.Content, (short) b.Content) != 0));
            }

            public override Value VisitAssignment([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.AssignmentContext context)
            {
                var left = Visit(context.left);
                var right = Resolve(context.right);

                if (!left.IsValid || !right.IsValid)
                    return Value.Invalid;

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

            public override Value VisitMember([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.MemberContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);

                if (!left.IsValid || !right.IsValid)
                    return Value.Invalid;

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);

                if (propAddr == 0)
                {
                    var objTable = dbg.ReadWord(0xa);
                    return Resolve(Value.WordAtAddress(objTable + right.Content - 1));
                }

                var propLen = dbg.GetPropLength(propAddr);

                return propLen switch
                {
                    1 => Value.ByteAtAddress(propAddr),
                    2 => Value.WordAtAddress(propAddr),
                    _ => throw new DebuggerException("Reading property with length " + propLen),
                };
            }

            public override Value VisitMemberAddress([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.MemberAddressContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Pointer(dbg.GetPropAddress((ushort) a.Content, (short) b.Content)));
            }

            public override Value VisitMemberLength([JetBrains.Annotations.NotNull] [NotNull] InformExpressionParser.MemberLengthContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);

                return Value.Guard(left, right,
                    (a, b) => Value.Number(
                        dbg.GetPropLength(dbg.GetPropAddress((ushort) a.Content, (short) b.Content))));
            }

            public override Value VisitCall([JetBrains.Annotations.NotNull] [NotNull]
                InformExpressionParser.CallContext context)
            {
                var func = Resolve(context.left);
                var resolvedArgs = context.arguments()._values.Select(Resolve).ToArray();
                if (!func.IsValid || !resolvedArgs.All(r => r.IsValid))
                    return Value.Invalid;

                var args = resolvedArgs.Select(r => (short) r.Content).ToArray();
                var result = dbg.CallAsync((short) func.Content, args).Result; // TODO: asyncify?
                return result != null ? Value.Number((int) result) : Value.Invalid;
            }

            private Value Resolve([JetBrains.Annotations.NotNull] IParseTree tree)
            {
                return Resolve(Visit(tree));
            }

            public Value Resolve(Value v)
            {
                return v.Type switch
                {
                    ValueType.Variable => Value.Number(dbg.ReadVariable((byte)v.Content)),
                    ValueType.ByteAtAddress => Value.Number(dbg.ReadByte(v.Content)),
                    ValueType.WordAtAddress => Value.Number(dbg.ReadWord(v.Content)),
                    _ => v,
                };
            }
        }
    }
}