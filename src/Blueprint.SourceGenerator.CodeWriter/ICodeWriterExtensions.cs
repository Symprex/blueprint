namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Adds fluent statement and control-flow helpers for use inside <c>WithBody</c> callbacks.
    /// These helpers let generators describe emitted statements in a structured way instead of concatenating raw source fragments by hand.
    /// </summary>
    public static class ICodeWriterExtensions
    {
        /// <summary>
        /// Starts an if/else chain against the writer so the block can be composed fluently.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="condition">The condition expression to place inside the emitted <c>if (...)</c> header.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static LogicalConditionBuilder If(this ICodeWriter writer, string condition)
        {
            return new LogicalConditionBuilder(writer, condition);
        }

        /// <summary>
        /// Starts a for-loop block against the writer and returns a block builder for its body.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="incrementerName">The loop variable name to emit for the generated <c>for</c> statement.</param>
        /// <param name="counterStart">The initial value assigned to the generated loop variable.</param>
        /// <param name="condition">The condition expression that controls when this block is emitted.</param>
        /// <param name="increment"><see langword="true"/> to increment the loop variable; <see langword="false"/> to decrement it.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static ExpressionBlockBuilder For(this ICodeWriter writer, string incrementerName = "i", int counterStart = 0, string condition = "i < 10", bool increment = true)
        {
            var incrementerType = increment ? "++" : "--";
            return new ExpressionBlockBuilder(writer, $"for ({incrementerName} = {counterStart}; {condition}; {incrementerName}{incrementerType}");
        }

        /// <summary>
        /// Starts a while-loop block against the writer and returns a block builder for its body.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="condition">The condition expression to place inside the emitted <c>while (...)</c> header.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static ExpressionBlockBuilder While(this ICodeWriter writer, string condition)
        {
            return new ExpressionBlockBuilder(writer, $"while ({condition})");
        }

        /// <summary>
        /// Starts a foreach block against the writer and returns a block builder for its body.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="parameter">The Roslyn parameter symbol to mirror into generated source.</param>
        /// <param name="collection">The enumerable expression to iterate over in generated code.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static ExpressionBlockBuilder ForEach(this ICodeWriter writer, string parameter, string collection)
        {
            return new ExpressionBlockBuilder(writer, $"foreach ({parameter} in {collection})");
        }

        /// <summary>
        /// Starts a switch builder against the writer for the supplied expression.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="switchOn">The expression to evaluate for each generated switch case.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static SwitchBuilder Switch(this ICodeWriter writer, string switchOn)
        {
            return new SwitchBuilder(writer, switchOn, false);
        }

        //public static SwitchBuilder SwitchExpression(this ICodeWriter writer, string switchOn)
        //{
        //    return new SwitchBuilder(writer, switchOn, true);
        //}

        /// <summary>
        /// Emits one generated line per item in the supplied collection.
        /// </summary>
        /// <typeparam name="T">The element type of the source collection.</typeparam>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="collection">The enumerable expression to iterate over in generated code.</param>
        /// <param name="predicate">A callback that converts each source item into the line text that should be emitted.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter AppendLines<T>(this ICodeWriter writer, IEnumerable<T> collection, Func<T, string> predicate)
        {
            foreach (var value in collection)
            {
                writer.AppendLine(predicate(value));
            }

            return writer;
        }

        /// <summary>
        /// Emits one generated line per key/value pair in the supplied dictionary.
        /// </summary>
        /// <typeparam name="TKey">The dictionary key type.</typeparam>
        /// <typeparam name="TValue">The dictionary value type.</typeparam>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="collection">The source dictionary whose entries should be emitted.</param>
        /// <param name="predicate">A callback that converts each key/value pair into the line text that should be emitted.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter AppendLines<TKey, TValue>(this ICodeWriter writer, Dictionary<TKey, TValue> collection, Func<TKey, TValue, string> predicate)
        {
            foreach (var pair in collection)
            {
                writer.AppendLine(predicate(pair.Key, pair.Value));
            }

            return writer;
        }

        /// <summary>
        /// Writes a block header and uses a callback to populate the block body immediately.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="expression">The header expression to emit before the generated block body.</param>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <param name="constraints">Optional trailing clauses to append after the block header.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter Block(this ICodeWriter writer, string expression, Action<ICodeWriter> innerWrite, params string[] constraints)
        {
            using (writer.Block(expression, constraints))
            {
                innerWrite?.Invoke(writer);
            }

            return writer;
        }

        /// <summary>
        /// Writes a try block and uses a callback to populate its body.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter Try(this ICodeWriter writer, Action<ICodeWriter> innerWrite)
        {
            return writer.Block("try", innerWrite);
        }

        /// <summary>
        /// Writes a catch block and uses a callback to populate its body.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="exceptionDeclaration">The catch declaration to emit, such as System.Exception ex.</param>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter Catch(this ICodeWriter writer, string exceptionDeclaration, Action<ICodeWriter> innerWrite)
        {
            return writer.Block($"catch ({exceptionDeclaration})", innerWrite);
        }

        /// <summary>
        /// Starts an if/else chain against the writer so the block can be composed fluently.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="condition">The condition expression that controls when this block is emitted.</param>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter If(this ICodeWriter writer, string condition, Action<ICodeWriter> innerWrite)
        {
            return writer.Block($"if ({condition})", innerWrite);
        }

        /// <summary>
        /// Writes an else block and uses a callback to populate its body.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter Else(this ICodeWriter writer, Action<ICodeWriter> innerWrite)
        {
            return writer.Block("else", innerWrite);
        }

        /// <summary>
        /// Emits a local variable declaration using var.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="variableName">The local variable name to emit.</param>
        /// <param name="valueExpression">The expression to emit on the right side of the declaration.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter DeclareVar(this ICodeWriter writer, string variableName, string valueExpression)
        {
            writer.AppendLine($"var {variableName} = {valueExpression};");
            return writer;
        }

        /// <summary>
        /// Emits an assignment statement.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="leftExpression">The assignment target expression to emit.</param>
        /// <param name="rightExpression">The assignment value expression to emit.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter Assign(this ICodeWriter writer, string leftExpression, string rightExpression)
        {
            writer.AppendLine($"{leftExpression} = {rightExpression};");
            return writer;
        }

        /// <summary>
        /// Emits a return statement.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        /// <param name="expression">The header expression to emit before the generated block body.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public static ICodeWriter Return(this ICodeWriter writer, string expression)
        {
            writer.AppendLine($"return {expression};");
            return writer;
        }
    }
}
