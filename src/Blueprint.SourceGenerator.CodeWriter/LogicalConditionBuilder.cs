#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds chained if/else if/else blocks before writing them back to an ICodeWriter.
    /// </summary>
    public sealed class LogicalConditionBuilder
    {
        private ICodeWriter _writer { get; }
        private string? _condition { get; }
        private string _operator { get; }
        private Queue<LogicalConditionBuilder> _builders { get; }
        private Action<ICodeWriter>? _innerWrite { get; set; }
        private LogicalConditionBuilder? _parent { get; }

        /// <summary>
        /// Initializes a new branch node in a conditional chain.
        /// </summary>
        /// <param name="writer">The writer that the generated conditional block will be emitted to.</param>
        /// <param name="condition">The condition expression to emit inside the branch header, or <see langword="null"/> for an unconditional else.</param>
        /// <param name="operator">The C# keyword to use for this branch, such as <c>if</c>, <c>else if</c>, or <c>else</c>.</param>
        /// <param name="parent">The parent branch node, or <see langword="null"/> when this is the root of the chain.</param>
        internal LogicalConditionBuilder(ICodeWriter writer, string? condition, string @operator = "if", LogicalConditionBuilder? parent = null)
        {
            this._writer = writer;
            this._condition = condition;
            this._operator = @operator;
            this._parent = parent;
            this._builders = new Queue<LogicalConditionBuilder>();
        }

        /// <summary>
        /// Supplies the callback that writes the body for this conditional branch.
        /// </summary>
        /// <param name="innerWrite">A callback that writes the statements for this conditional branch.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public LogicalConditionBuilder WithBody(Action<ICodeWriter> innerWrite)
        {
            this._innerWrite = innerWrite;
            return this;
        }

        /// <summary>
        /// Adds an else-if branch to the current conditional chain.
        /// </summary>
        /// <param name="condition">The condition expression that controls when this block is emitted.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public LogicalConditionBuilder ElseIf(string condition)
        {
            var builder = new LogicalConditionBuilder(this._writer, condition, "else if", this);
            this._builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Adds an else-if branch to the current conditional chain.
        /// </summary>
        /// <param name="condition">The condition expression that controls when this block is emitted.</param>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public LogicalConditionBuilder ElseIf(string condition, Action<ICodeWriter> innerWrite)
        {
            var builder = new LogicalConditionBuilder(this._writer, condition, "else if", this)
                .WithBody(innerWrite);
            this._builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Adds an else branch to the current conditional chain.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public LogicalConditionBuilder Else()
        {
            var builder = new LogicalConditionBuilder(this._writer, null, "else", this);
            this._builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Adds an else branch to the current conditional chain.
        /// </summary>
        /// <param name="innerWrite">A callback that writes the nested statements for this generated block.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public LogicalConditionBuilder Else(Action<ICodeWriter> innerWrite)
        {
            var builder = new LogicalConditionBuilder(this._writer, null, "else", this)
                .WithBody(innerWrite);
            this._builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Writes the accumulated conditional chain and returns the underlying writer.
        /// </summary>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public ICodeWriter EndIf()
        {
            if (this._parent != null)
            {
                this._parent.EndIf();
            }
            else
            {
                this.WriteInternal();
            }

            return this._writer;
        }

        private void WriteInternal()
        {
            var expression = string.IsNullOrEmpty(this._condition) ? this._operator : $"{this._operator} ({this._condition})";
            using (this._writer.Block(expression))
            {
                this._innerWrite?.Invoke(this._writer);
            }


            while (this._builders.Any())
            {
                var builder = this._builders.Dequeue();
                builder.WriteInternal();
            }
        }
    }
}
