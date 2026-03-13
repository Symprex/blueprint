using System;
using System.Collections.Generic;

namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Provides fluent extension helpers for writing common C# control flow and statements.
    /// </summary>
    public static class ICodeWriterExtensions
    {
        /// <summary>
        /// Starts an <c>if</c>/<c>else</c> expression chain.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="condition">The condition expression used in the generated <c>if</c> statement.</param>
        /// <returns>A logical condition builder used to continue the conditional block chain.</returns>
        public static LogicalConditionBuilder If(this ICodeWriter writer, string condition)
        {
            return new LogicalConditionBuilder(writer, condition);
        }

        /// <summary>
        /// Starts a <c>for</c> loop expression block.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="incrementerName">The loop counter variable name.</param>
        /// <param name="counterStart">The initial loop counter value.</param>
        /// <param name="condition">The loop condition expression.</param>
        /// <param name="increment">
        /// <see langword="true"/> to increment the counter; otherwise decrements it.
        /// </param>
        /// <returns>An expression block builder for writing the loop body.</returns>
        public static ExpressionBlockBuilder For(this ICodeWriter writer, string incrementerName = "i", int counterStart = 0, string condition = "i < 10", bool increment = true)
        {
            var incrementerType = increment ? "++" : "--";
            return new ExpressionBlockBuilder(writer, $"for ({incrementerName} = {counterStart}; {condition}; {incrementerName}{incrementerType}");
        }

        /// <summary>
        /// Starts a <c>while</c> loop expression block.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="condition">The loop condition expression.</param>
        /// <returns>An expression block builder for writing the loop body.</returns>
        public static ExpressionBlockBuilder While(this ICodeWriter writer, string condition)
        {
            return new ExpressionBlockBuilder(writer, $"while ({condition})");
        }

        /// <summary>
        /// Starts a <c>foreach</c> loop expression block.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="parameter">The iteration variable declaration (for example, <c>var item</c>).</param>
        /// <param name="collection">The enumerable expression being iterated.</param>
        /// <returns>An expression block builder for writing the loop body.</returns>
        public static ExpressionBlockBuilder ForEach(this ICodeWriter writer, string parameter, string collection)
        {
            return new ExpressionBlockBuilder(writer, $"foreach ({parameter} in {collection})");
        }

        /// <summary>
        /// Starts a <c>switch</c> statement builder for the supplied expression.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="switchOn">The expression used by the <c>switch</c> statement.</param>
        /// <returns>A switch builder used to add cases and bodies.</returns>
        public static SwitchBuilder Switch(this ICodeWriter writer, string switchOn)
        {
            return new SwitchBuilder(writer, switchOn, false);
        }

        //public static SwitchBuilder SwitchExpression(this ICodeWriter writer, string switchOn)
        //{
        //    return new SwitchBuilder(writer, switchOn, true);
        //}

        /// <summary>
        /// Appends one generated line per input item.
        /// </summary>
        /// <typeparam name="T">The source item type.</typeparam>
        /// <param name="writer">The target writer.</param>
        /// <param name="collection">The source collection.</param>
        /// <param name="predicate">Maps each item to the emitted line text.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter AppendLines<T>(this ICodeWriter writer, IEnumerable<T> collection, Func<T, string> predicate)
        {
            foreach (var value in collection)
                writer.AppendLine(predicate(value));

            return writer;
        }

        /// <summary>
        /// Appends one generated line per dictionary entry.
        /// </summary>
        /// <typeparam name="TKey">The dictionary key type.</typeparam>
        /// <typeparam name="TValue">The dictionary value type.</typeparam>
        /// <param name="writer">The target writer.</param>
        /// <param name="collection">The source dictionary.</param>
        /// <param name="predicate">Maps each key/value pair to the emitted line text.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter AppendLines<TKey, TValue>(this ICodeWriter writer, Dictionary<TKey, TValue> collection, Func<TKey, TValue, string> predicate)
        {
            foreach (var pair in collection)
                writer.AppendLine(predicate(pair.Key, pair.Value));

            return writer;
        }

        /// <summary>
        /// Writes a block expression and executes a callback to populate the block body.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="expression">The block header expression (for example, <c>if (x)</c>).</param>
        /// <param name="innerWrite">A callback that writes lines inside the block.</param>
        /// <param name="constraints">Optional trailing constraints appended to the expression.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter Block(this ICodeWriter writer, string expression, Action<ICodeWriter> innerWrite, params string[] constraints)
        {
            using (writer.Block(expression, constraints))
            {
                innerWrite?.Invoke(writer);
            }

            return writer;
        }

        /// <summary>
        /// Writes a <c>try</c> block and emits its body through a callback.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="innerWrite">A callback that writes lines inside the <c>try</c> block.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter Try(this ICodeWriter writer, Action<ICodeWriter> innerWrite)
        {
            return writer.Block("try", innerWrite);
        }

        /// <summary>
        /// Writes a <c>catch</c> block and emits its body through a callback.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="exceptionDeclaration">The catch declaration, such as <c>System.Exception ex</c>.</param>
        /// <param name="innerWrite">A callback that writes lines inside the <c>catch</c> block.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter Catch(this ICodeWriter writer, string exceptionDeclaration, Action<ICodeWriter> innerWrite)
        {
            return writer.Block($"catch ({exceptionDeclaration})", innerWrite);
        }

        /// <summary>
        /// Writes an <c>if</c> block and emits its body through a callback.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="condition">The condition expression used by the <c>if</c> block.</param>
        /// <param name="innerWrite">A callback that writes lines inside the <c>if</c> block.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter If(this ICodeWriter writer, string condition, Action<ICodeWriter> innerWrite)
        {
            return writer.Block($"if ({condition})", innerWrite);
        }

        /// <summary>
        /// Writes an <c>else</c> block and emits its body through a callback.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="innerWrite">A callback that writes lines inside the <c>else</c> block.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter Else(this ICodeWriter writer, Action<ICodeWriter> innerWrite)
        {
            return writer.Block("else", innerWrite);
        }

        /// <summary>
        /// Emits a local variable declaration using <c>var</c>.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="variableName">The local variable name.</param>
        /// <param name="valueExpression">The right-hand expression assigned to the variable.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter DeclareVar(this ICodeWriter writer, string variableName, string valueExpression)
        {
            writer.AppendLine($"var {variableName} = {valueExpression};");
            return writer;
        }

        /// <summary>
        /// Emits an assignment statement.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="leftExpression">The assignment target expression.</param>
        /// <param name="rightExpression">The value expression assigned to the target.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter Assign(this ICodeWriter writer, string leftExpression, string rightExpression)
        {
            writer.AppendLine($"{leftExpression} = {rightExpression};");
            return writer;
        }

        /// <summary>
        /// Emits a <c>return</c> statement.
        /// </summary>
        /// <param name="writer">The target writer.</param>
        /// <param name="expression">The expression returned from the current scope.</param>
        /// <returns>The same writer instance for fluent chaining.</returns>
        public static ICodeWriter Return(this ICodeWriter writer, string expression)
        {
            writer.AppendLine($"return {expression};");
            return writer;
        }
    }
}
