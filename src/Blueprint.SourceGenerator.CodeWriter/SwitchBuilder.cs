using System;
using System.Collections.Generic;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Collects switch cases and writes a switch statement back to an ICodeWriter when the builder is closed.
    /// </summary>
    public sealed class SwitchBuilder
    {
        private ICodeWriter _writer { get; }
        private string _switchOn { get; }
        internal bool Expression { get; }
        private readonly List<SwitchCaseBuilder> _switchCases = new List<SwitchCaseBuilder>();

        internal SwitchBuilder(ICodeWriter writer, string switchOn, bool expression)
        {
            _writer = writer;
            _switchOn = switchOn;
            Expression = expression;
        }

        /// <summary>
        /// Adds a case to the switch builder and returns a builder for its body.
        /// </summary>
        /// <param name="case">The case label or pattern to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public SwitchCaseBuilder AddCase(string @case)
        {
            var builder = new SwitchCaseBuilder(this, @case);
            _switchCases.Add(builder);
            return builder;
        }

        /// <summary>
        /// Writes the collected switch cases to the underlying writer and returns it.
        /// </summary>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public ICodeWriter Close()
        {
            if (Expression)
                WriterExpressionSwitchCase();
            else if(_writer is CodeWriter codeWriter)
                WriteClassicSwitchCase(codeWriter);
            return _writer;
        }

        private void WriteClassicSwitchCase(CodeWriter writer)
        {
            using (_writer.Block($"switch({_switchOn})"))
            {
                foreach (IBuilder @case in _switchCases)
                {
                    @case.Write(writer);
                }
            }
        }

        private void WriterExpressionSwitchCase()
        {
            throw new NotImplementedException();
        }
    }
}
