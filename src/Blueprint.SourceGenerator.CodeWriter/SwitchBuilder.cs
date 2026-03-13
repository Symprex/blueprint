using System;
using System.Collections.Generic;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents switch builder.
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
        /// Adds case.
        /// </summary>
        /// <param name="case">The case value.</param>
        /// <returns>The generated result.</returns>
        public SwitchCaseBuilder AddCase(string @case)
        {
            var builder = new SwitchCaseBuilder(this, @case);
            _switchCases.Add(builder);
            return builder;
        }

        /// <summary>
        /// Performs the close operation.
        /// </summary>
        /// <returns>The generated result.</returns>
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
