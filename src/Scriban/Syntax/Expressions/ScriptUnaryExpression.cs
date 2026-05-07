// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.IO;
using System.Numerics;
using Scriban.Helpers;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Scriban.Syntax
{
    [ScriptSyntax("unary expression", "<operator> <expression>")]
#if SCRIBAN_PUBLIC
    public
#else
    internal
#endif
    partial class ScriptUnaryExpression : ScriptExpression
    {
        private ScriptToken? _operatorToken;
        private ScriptExpression? _right;
        public ScriptUnaryOperator Operator { get; set; }

        public ScriptToken? OperatorToken
        {
            get => _operatorToken;
            set => ParentToThisNullable(ref _operatorToken, value);
        }

        public string? OperatorAsText => OperatorToken?.Value ?? Operator.ToText();

        public ScriptExpression? Right
        {
            get => _right;
            set => ParentToThisNullable(ref _right, value);
        }

        public override object? Evaluate(TemplateContext context)
        {
            if (Operator == ScriptUnaryOperator.FunctionAlias)
            {
                return context.Evaluate(Right, true);
            }

            if (Right is null)
            {
                return Evaluate(context, Span, Operator, null);
            }

            var value = context.Evaluate(Right);

            return Evaluate(context, Right.Span, Operator, value);
        }

        public override void PrintTo(ScriptPrinter printer)
        {
            if (OperatorToken is not null)
            {
                printer.Write(OperatorToken);
            }
            else
            {
                printer.Write(Operator.ToText());
            }
            printer.Write(Right);
        }

        public static object? Evaluate(TemplateContext context, SourceSpan span, ScriptUnaryOperator op, object? value)
        {
            if (value is IScriptCustomUnaryOperation customUnary)
            {
                if (customUnary.TryEvaluate(context, span, op, value, out var result))
                {
                    return result;
                }
            }
            else
            {
                switch (op)
                {
                    case ScriptUnaryOperator.Not:
                    {
                        if (context.UseScientific)
                        {
                            if (!(value is bool))
                            {
                                throw new ScriptRuntimeException(span, $"Expecting a boolean instead of {context.GetTypeName(value)} value: {value}");
                            }

                            return !(bool)value;
                        }
                        else
                        {
                            return !context.ToBool(span, value);
                        }
                    }
                    case ScriptUnaryOperator.Negate:
                    case ScriptUnaryOperator.Plus:
                    {
                        bool negate = op == ScriptUnaryOperator.Negate;

                        if (value is not null)
                        {
                            if (value is int)
                            {
                                return negate ? -((int)value) : value;
                            }
                            else if (value is double)
                            {
                                return negate ? -((double)value) : value;
                            }
                            else if (value is float)
                            {
                                return negate ? -((float)value) : value;
                            }
                            else if (value is long)
                            {
                                return negate ? -((long)value) : value;
                            }
                            else if (value is decimal)
                            {
                                return negate ? -((decimal)value) : value;
                            }
                            else if (value is BigInteger)
                            {
                                return negate ? -((BigInteger)value) : value;
                            }
                        }
                    }
                    break;

                    case ScriptUnaryOperator.FunctionParametersExpand:
                        return value;
                }
            }

            if (value != null && TryEvaluateWithCSharpOperator(context, span, value, op, out var csharpResult))
            {
                return csharpResult;
            }
            throw new ScriptRuntimeException(span, $"Operator `{op.ToText()}` is not supported");
        }


        private static readonly ConcurrentDictionary<(Type, ScriptUnaryOperator), MethodInfo?> UnaryOpMethodCache
            = new ConcurrentDictionary<(Type, ScriptUnaryOperator), MethodInfo?>();

        private static string? GetUnaryOperatorMethodName(ScriptUnaryOperator op) => op switch
        {
            ScriptUnaryOperator.Negate => "op_UnaryNegation",
            ScriptUnaryOperator.Plus   => "op_UnaryPlus",
            ScriptUnaryOperator.Not    => "op_LogicalNot",
            _                          => null
        };

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070",
            Justification = "C# operator overload discovery is intentional. In trimmed/AOT builds this fallback simply finds nothing and is skipped.")]
        private static MethodInfo? FindUnaryOperatorMethod(TemplateContext context, Type type, string methodName)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != methodName) continue;
                var parms = m.GetParameters();
                if (parms.Length == 1 && context.CanConvertTo(type, parms[0].ParameterType))
                    return m;
            }
            return null;
        }

        private static bool TryEvaluateWithCSharpOperator(TemplateContext context, SourceSpan span, object value, ScriptUnaryOperator op, out object? result)
        {
            var methodName = GetUnaryOperatorMethodName(op);
            if (methodName == null) { result = null; return false; }

            var type = value.GetType();
            var method = UnaryOpMethodCache.GetOrAdd((type, op), key =>
            {
                var (t, oper) = key;
                var name = GetUnaryOperatorMethodName(oper)!;
                return FindUnaryOperatorMethod(context, t, name);
            });

            if (method == null) { result = null; return false; }
            var parms = method.GetParameters();
            result = method.Invoke(null, new object?[] { context.ToObject(span, value, parms[0].ParameterType) });
            return true;
        }

        public static ScriptUnaryExpression Wrap(ScriptUnaryOperator unaryOperator, ScriptToken unaryToken, ScriptExpression expression, bool transferTrivia)
        {
            if (expression is null) throw new ArgumentNullException(nameof(expression));
            var unary = new ScriptUnaryExpression()
            {
                Span = expression.Span,
                Operator = unaryOperator,
                OperatorToken = unaryToken,
                Right = expression,
            };

            if (!transferTrivia) return unary;

            var firstTerminal = expression.FindFirstTerminal();
            firstTerminal?.MoveLeadingTriviasTo(unary.OperatorToken);

            return unary;
        }
    }
}
