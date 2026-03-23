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

        /// <summary>Gets a value indicating whether this switch should be emitted as a switch expression rather than a switch statement.</summary>
        internal bool Expression { get; }

        private readonly List<SwitchCaseBuilder> _switchCases = new List<SwitchCaseBuilder>();

        /// <summary>
        /// Initializes a new instance attached to the supplied writer.
        /// </summary>
        /// <param name="writer">The writer that the generated switch will be emitted to.</param>
        /// <param name="switchOn">The expression to evaluate for each generated switch case.</param>
        /// <param name="expression"><see langword="true"/> to emit a switch expression; <see langword="false"/> for a switch statement.</param>
        internal SwitchBuilder(ICodeWriter writer, string switchOn, bool expression)
        {
            this._writer = writer;
            this._switchOn = switchOn;
            this.Expression = expression;
        }

        /// <summary>
        /// Adds a case to the switch builder and returns a builder for its body.
        /// </summary>
        /// <param name="case">The case label or pattern to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public SwitchCaseBuilder AddCase(string @case)
        {
            var builder = new SwitchCaseBuilder(this, @case);
            this._switchCases.Add(builder);
            return builder;
        }

        /// <summary>
        /// Writes the collected switch cases to the underlying writer and returns it.
        /// </summary>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public ICodeWriter Close()
        {
            if (this.Expression)
            {
                this.WriterExpressionSwitchCase();
            }
            else if (this._writer is CodeWriter codeWriter)
            {
                this.WriteClassicSwitchCase(codeWriter);
            }

            return this._writer;
        }

        private void WriteClassicSwitchCase(CodeWriter writer)
        {
            using (this._writer.Block($"switch({this._switchOn})"))
            {
                foreach (IBuilder @case in this._switchCases)
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
