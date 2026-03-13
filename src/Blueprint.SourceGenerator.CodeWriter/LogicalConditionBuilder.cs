using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents logical condition builder.
    /// </summary>
    public sealed class LogicalConditionBuilder
    {
        private ICodeWriter _writer { get; }
        private string? _condition { get; }
        private string _operator { get; }
        private Queue<LogicalConditionBuilder> _builders { get; }
        private Action<ICodeWriter>? _innerWrite { get; set; }
        private LogicalConditionBuilder? _parent { get; }

        internal LogicalConditionBuilder(ICodeWriter writer, string? condition, string @operator = "if", LogicalConditionBuilder? parent = null)
        {
            _writer = writer;
            _condition = condition;
            _operator = @operator;
            _parent = parent;
            _builders = new Queue<LogicalConditionBuilder>();
        }

        /// <summary>
        /// Configures body.
        /// </summary>
        /// <param name="innerWrite">The inner write value.</param>
        /// <returns>The generated result.</returns>
        public LogicalConditionBuilder WithBody(Action<ICodeWriter> innerWrite)
        {
            _innerWrite = innerWrite;
            return this;
        }

        /// <summary>
        /// Performs the else if operation.
        /// </summary>
        /// <param name="condition">The condition value.</param>
        /// <returns>The generated result.</returns>
        public LogicalConditionBuilder ElseIf(string condition)
        {
            var builder = new LogicalConditionBuilder(_writer, condition, "else if", this);
            _builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Performs the else if operation.
        /// </summary>
        /// <param name="condition">The condition value.</param>
        /// <param name="innerWrite">The inner write value.</param>
        /// <returns>The generated result.</returns>
        public LogicalConditionBuilder ElseIf(string condition, Action<ICodeWriter> innerWrite)
        {
            var builder = new LogicalConditionBuilder(_writer, condition, "else if", this)
                .WithBody(innerWrite);
            _builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Performs the else operation.
        /// </summary>
        /// <returns>The generated result.</returns>
        public LogicalConditionBuilder Else()
        {
            var builder = new LogicalConditionBuilder(_writer, null, "else", this);
            _builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Performs the else operation.
        /// </summary>
        /// <param name="innerWrite">The inner write value.</param>
        /// <returns>The generated result.</returns>
        public LogicalConditionBuilder Else(Action<ICodeWriter> innerWrite)
        {
            var builder = new LogicalConditionBuilder(_writer, null, "else", this)
                .WithBody(innerWrite);
            _builders.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Ends if.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ICodeWriter EndIf()
        {
            if (_parent != null)
                _parent.EndIf();
            else
                WriteInternal();

            return _writer;
        }

        private void WriteInternal()
        {
            var expression = string.IsNullOrEmpty(_condition) ? _operator : $"{_operator} ({_condition})";
            using (_writer.Block(expression))
            {
                _innerWrite?.Invoke(_writer);
            }


            while (_builders.Any())
            {
                var builder = _builders.Dequeue();
                builder.WriteInternal();
            }
        }
    }
}
