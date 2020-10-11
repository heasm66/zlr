using System;
using System.Linq;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using JetBrains.Annotations;
using ZLR.VM;
using ZLR.VM.Debugging;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    internal static class ZilExpression
    {
        public static Value Evaluate(ZMachine zm, IDebugger dbg, string exprText, bool wantLvalue = false)
        {
            var lexer = new ZilExpressionLexer(new AntlrInputStream(exprText));
            var parser = new ZilExpressionParser(new CommonTokenStream(lexer));

            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();

            var resultContext = parser.expression();

            var visitor = new EvaluatingVisitor(zm, dbg);
            var result = visitor.Visit(resultContext);
            return wantLvalue ? result : visitor.Resolve(result);
        }

        class EvaluatingVisitor : ZilExpressionBaseVisitor<Value>
        {
            private readonly ZMachine zm;
            private readonly IDebugger dbg;

            public EvaluatingVisitor(ZMachine zm, IDebugger dbg)
            {
                this.zm = zm;
                this.dbg = dbg;
            }
            
            private Value Resolve([NotNull] IParseTree tree)
            {
                return Resolve(Visit(tree));
            }

            public Value Resolve(Value v)
            {
                switch (v.Type)
                {
                    case ValueType.Variable:
                        return Value.Number(dbg.ReadVariable((byte)v.Content));

                    case ValueType.ByteAtAddress:
                        return Value.Number(dbg.ReadByte(v.Content));

                    case ValueType.WordAtAddress:
                        return Value.Number(dbg.ReadWord(v.Content));

                    default:
                        return v;
                }
            }

            private Value ParseIdentifier(string name)
            {
                if (zm.DebugInfo == null)
                    return Value.Invalid;

                var curRtn = zm.DebugInfo.FindRoutine(dbg.CurrentPC);
                if (curRtn != null)
                {
                    for (var i = 0; i < curRtn.Locals.Length; i++)
                    {
                        if (curRtn.Locals[i] == name)
                            return Value.VariableNumber(i + 1);
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
                    return Value.VariableNumber(16 + zm.DebugInfo.Globals[name]);

                return Value.Invalid;
            }

            private static readonly Regex BinLiteralPattern = new Regex(@"#2\s+([01]+)");

            public override Value VisitLogicalNot([NotNull] ZilExpressionParser.LogicalNotContext context)
            {
                var inner = Resolve(context.expression());
                return Value.Guard(inner, i => Value.Boolean(i.Content == 0));
            }

            public override Value VisitLogicalOr([NotNull] ZilExpressionParser.LogicalOrContext context)
            {
                foreach (var arg in context.expression().Select(Resolve))
                {
                    if (!arg.IsValid || arg.Content != 0)
                        return arg;
                }

                return Value.Boolean(false);
            }

            public override Value VisitLogicalAnd([NotNull] ZilExpressionParser.LogicalAndContext context)
            {
                foreach (var arg in context.expression().Select(Resolve))
                {
                    if (!arg.IsValid || arg.Content == 0)
                        return arg;
                }

                return Value.Boolean(true);
            }

            public override Value VisitBinLiteral([NotNull] ZilExpressionParser.BinLiteralContext context)
            {
                var m = BinLiteralPattern.Match(context.GetText());
                return Value.Number(Convert.ToInt32(m.Groups[1].Value, 2));
            }

            public override Value VisitDecLiteral([NotNull] ZilExpressionParser.DecLiteralContext context)
            {
                return Value.Number(int.Parse(context.GetText()));
            }

            public override Value VisitFalseLiteral(ZilExpressionParser.FalseLiteralContext context)
            {
                return Value.Boolean(false);
            }

            public override Value VisitCharLiteral([NotNull] ZilExpressionParser.CharLiteralContext context)
            {
                return Value.Number(context.GetText()[2]);
            }

            private static readonly Regex EscapedCharPattern = new Regex(@"\\(.)");

            [NotNull, Pure]
            private static string Unescape([NotNull] string str)
            {
                return EscapedCharPattern.Replace(str, "$1");
            }

            public override Value VisitAtomLiteral([NotNull] ZilExpressionParser.AtomLiteralContext context)
            {
                var atom = Unescape(context.GetText());
                var parsed = ParseIdentifier(atom);

                return parsed.IsValid || atom.ToUpperInvariant() != "T"
                    ? parsed
                    : Value.Boolean(true);
            }

            public override Value VisitOctLiteral([NotNull] ZilExpressionParser.OctLiteralContext context)
            {
                var text = context.GetText();
                return Value.Number(Convert.ToInt32(text.Substring(1, text.Length - 2), 8));
            }

            public override Value VisitLval([NotNull] ZilExpressionParser.LvalContext context)
            {
                var varNum = Resolve(context.expression());

                return varNum.IsValid && varNum.Content >= 1 && varNum.Content <= 15
                    ? Value.Variable(varNum.Content)
                    : Value.Invalid;
            }

            public override Value VisitGval([NotNull] ZilExpressionParser.GvalContext context)
            {
                var varNum = Resolve(context.expression());

                if (!varNum.IsValid)
                    return Value.Invalid;

                if (varNum.Type == ValueType.Attribute || varNum.Type == ValueType.Object ||
                    varNum.Type == ValueType.Property || varNum.Type == ValueType.Routine)
                {
                    // global-like constant
                    return varNum;
                }

                if (varNum.Content >= 16 && varNum.Content <= 255)
                {
                    return Value.Variable(varNum.Content);
                }

                return Value.Invalid;
            }

            public override Value VisitLessEqual([NotNull] ZilExpressionParser.LessEqualContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) => Value.Boolean(a.Content <= b.Content));
            }

            public override Value VisitGreaterEqual([NotNull] ZilExpressionParser.GreaterEqualContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) => Value.Boolean(a.Content >= b.Content));
            }

            public override Value VisitInequality([NotNull] ZilExpressionParser.InequalityContext context)
            {
                var left = Resolve(context.left);

                if (!left.IsValid)
                    return Value.Invalid;

                foreach (var r in context._rights.Select(Resolve))
                {
                    if (!r.IsValid)
                        return Value.Invalid;

                    if (r.Content == left.Content)
                        return Value.Boolean(false);
                }

                return Value.Boolean(true);
            }

            public override Value VisitLess([NotNull] ZilExpressionParser.LessContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) => Value.Boolean(a.Content < b.Content));
            }

            public override Value VisitGreater([NotNull] ZilExpressionParser.GreaterContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) => Value.Boolean(a.Content > b.Content));
            }

            public override Value VisitEquality([NotNull] ZilExpressionParser.EqualityContext context)
            {
                var left = Resolve(context.left);

                if (!left.IsValid)
                    return Value.Invalid;

                foreach (var r in context._rights.Select(Resolve))
                {
                    if (!r.IsValid)
                        return Value.Invalid;

                    if (r.Content == left.Content)
                        return Value.Boolean(true);
                }

                return Value.Boolean(false);
            }

            public override Value VisitIn([NotNull] ZilExpressionParser.InContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Boolean(dbg.GetObjectParent((ushort) a.Content) == b.Content));
            }

            public override Value VisitMove([NotNull] ZilExpressionParser.MoveContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) =>
                {
                    dbg.MoveObject((ushort) a.Content, (ushort) b.Content);
                    return Value.Boolean(true);
                });
            }

            public override Value VisitRemove([NotNull] ZilExpressionParser.RemoveContext context)
            {
                var left = Resolve(context.left);
                return Value.Guard(left, a =>
                {
                    dbg.MoveObject((ushort) a.Content, 0);
                    return Value.Boolean(true);
                });
            }

            public override Value VisitParent([NotNull] ZilExpressionParser.ParentContext context)
            {
                var expr = Resolve(context.expression());
                return Value.Guard(expr, a => Value.Object(dbg.GetObjectParent((ushort) a.Content)));
            }

            public override Value VisitSibling([NotNull] ZilExpressionParser.SiblingContext context)
            {
                var expr = Resolve(context.expression());
                return Value.Guard(expr, a => Value.Object(dbg.GetObjectSibling((ushort) a.Content)));
            }

            public override Value VisitChild([NotNull] ZilExpressionParser.ChildContext context)
            {
                var expr = Resolve(context.expression());
                return Value.Guard(expr, a => Value.Object(dbg.GetObjectChild((ushort) a.Content)));
            }

            public override Value VisitClearFlag([NotNull] ZilExpressionParser.ClearFlagContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) =>
                {
                    dbg.ClearObjectAttribute((ushort) a.Content, b.Content);
                    return Value.Boolean(true);
                });
            }

            public override Value VisitTestFlag([NotNull] ZilExpressionParser.TestFlagContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right,
                    (a, b) => Value.Boolean(dbg.TestObjectAttribute((ushort) a.Content, b.Content)));
            }

            public override Value VisitSetFlag([NotNull] ZilExpressionParser.SetFlagContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return Value.Guard(left, right, (a, b) =>
                {
                    dbg.SetObjectAttribute((ushort) a.Content, b.Content);
                    return Value.Boolean(true);
                });
            }

            public override Value VisitMultiplication([NotNull] ZilExpressionParser.MultiplicationContext context)
            {
                var resolved = context._args.Select(Resolve).ToArray();

                return resolved.Length == 0 ? Value.Number(1) : resolved.Aggregate((a, b) => a * b);
            }

            public override Value VisitSubtraction([NotNull] ZilExpressionParser.SubtractionContext context)
            {
                var resolved = context._args.Select(Resolve).ToArray();

                switch (resolved.Length)
                {
                    case 0:
                        return Value.Number(0);

                    case 1:
                        return -resolved[0];

                    default:
                        return resolved.Aggregate((a, b) => a - b);
                }
            }

            public override Value VisitDivision([NotNull] ZilExpressionParser.DivisionContext context)
            {
                var resolved = context._args.Select(Resolve).ToArray();

                switch (resolved.Length)
                {
                    case 0:
                        return Value.Number(1);

                    case 1:
                        return Value.Number(1) / resolved[0];

                    default:
                        return resolved.Aggregate((a, b) => a / b);
                }
            }

            public override Value VisitAddition([NotNull] ZilExpressionParser.AdditionContext context)
            {
                var resolved = context._args.Select(Resolve).ToArray();

                return resolved.Length == 0 ? Value.Number(0) : resolved.Aggregate((a, b) => a + b);
            }

            public override Value VisitModulus([NotNull] ZilExpressionParser.ModulusContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left % right;
            }

            public override Value VisitBitwiseNot([NotNull] ZilExpressionParser.BitwiseNotContext context)
            {
                var left = Resolve(context.expression());
                return ~left;
            }

            public override Value VisitBitwiseAnd([NotNull] ZilExpressionParser.BitwiseAndContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left & right;
            }

            public override Value VisitBitwiseOr([NotNull] ZilExpressionParser.BitwiseOrContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                return left | right;
            }

            public override Value VisitSetExpr([NotNull] ZilExpressionParser.SetExprContext context)
            {
                // TODO: check SET vs. SETG?
                var dest = Resolve(context.dest);
                var value = Resolve(context.value);
                return Value.Guard(dest, value, (a, b) =>
                {
                    dbg.WriteVariable((byte) a.Content, (short) b.Content);
                    return Value.Variable((byte) a.Content);
                });
            }

            public override Value VisitIncrementExpr([NotNull] ZilExpressionParser.IncrementExprContext context)
            {
                var dest = Resolve(context.expression());
                return Value.Guard(dest, d =>
                {
                    var oldValue = dbg.ReadVariable((byte) d.Content);
                    var newValue = context.op.Type == ZilExpressionLexer.K_INC ? oldValue + 1 : oldValue - 1;
                    dbg.WriteVariable((byte) d.Content, (short) newValue);
                    return Value.Variable((byte) d.Content);
                });
            }

            public override Value VisitPropertyReadExpr([NotNull] ZilExpressionParser.PropertyReadExprContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);

                if (!left.IsValid || !right.IsValid)
                    return Value.Invalid;

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);

                if (context.op.Type == ZilExpressionLexer.K_GETPT)
                    return Value.Pointer(propAddr);

                System.Diagnostics.Debug.Assert(context.op.Type == ZilExpressionLexer.K_GETP);

                if (propAddr == 0)
                {
                    var objTable = dbg.ReadWord(0xa);
                    return Resolve(Value.WordAtAddress(objTable + right.Content - 1));
                }

                var propLen = dbg.GetPropLength(propAddr);

                switch (propLen)
                {
                    case 1:
                        return Value.ByteAtAddress(propAddr);

                    case 2:
                        return Value.WordAtAddress(propAddr);

                    default:
                        throw new DebuggerException("Reading property with length " + propLen);
                }
            }

            public override Value VisitPropertyWriteExpr([NotNull] ZilExpressionParser.PropertyWriteExprContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                var value = Resolve(context.value);

                if (!left.IsValid || !right.IsValid || !value.IsValid)
                    return Value.Invalid;

                var propAddr = dbg.GetPropAddress((ushort)left.Content, (short)right.Content);

                if (propAddr == 0)
                {
                    throw new DebuggerException(
                        $"Object {(ushort) left.Content} does not have property {(short) right.Content}");
                }

                var propLen = dbg.GetPropLength(propAddr);

                switch (propLen)
                {
                    case 1:
                        dbg.WriteByte(propAddr, (byte) value.Content);
                        return Value.ByteAtAddress(propAddr);

                    case 2:
                        dbg.WriteWord(propAddr, (short) value.Content);
                        return Value.WordAtAddress(propAddr);

                    default:
                        throw new DebuggerException($"Writing property with length {propLen}");
                }
            }

            public override Value VisitPropertyLenExpr([NotNull] ZilExpressionParser.PropertyLenExprContext context)
            {
                var ptr = Resolve(context.expression());
                return Value.Guard(ptr, p => Value.Number(dbg.GetPropLength((ushort) p.Content)));
            }

            public override Value VisitTableReadExpr([NotNull] ZilExpressionParser.TableReadExprContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                switch (context.op.Type)
                {
                    case ZilExpressionLexer.K_GETB:
                    case ZilExpressionLexer.K_GETsB when zm.ZVersion <= 3:
                        return Value.Guard(left, right, (a, b) => Value.ByteAtAddress(a.Content + b.Content));

                    case ZilExpressionLexer.K_GET:
                    case ZilExpressionLexer.K_GETsB when zm.ZVersion > 3:
                        return Value.Guard(left, right, (a, b) => Value.WordAtAddress(a.Content + 2 * b.Content));

                    default:
                        throw new ArgumentException($"Unexpected table op {context.op}");
                }
            }

            public override Value VisitTableWriteExpr([NotNull] ZilExpressionParser.TableWriteExprContext context)
            {
                var left = Resolve(context.left);
                var right = Resolve(context.right);
                var value = Resolve(context.value);
                switch (context.op.Type)
                {
                    case ZilExpressionLexer.K_PUTB:
                    case ZilExpressionLexer.K_PUTsB when zm.ZVersion <= 3:
                        return Value.Guard(left, right, value, (a, b, v) =>
                        {
                            dbg.WriteByte(a.Content+b.Content, (byte) v.Content);
                            return Value.Boolean(true);
                        });

                    case ZilExpressionLexer.K_PUT:
                    case ZilExpressionLexer.K_PUTsB when zm.ZVersion > 3:
                        return Value.Guard(left, right, value, (a, b, v) =>
                        {
                            dbg.WriteWord(a.Content + 2 * b.Content, (short) v.Content);
                            return Value.Boolean(true);
                        });

                    default:
                        throw new ArgumentException($"Unexpected table op {context.op}");
                }
            }

            /*
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
            */
        }
    }
}