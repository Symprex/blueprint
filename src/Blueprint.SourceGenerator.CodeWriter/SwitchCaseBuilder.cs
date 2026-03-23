#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds the body for a single case within a SwitchBuilder.
    /// </summary>
    public sealed class SwitchCaseBuilder : IBuilder
    {
        private SwitchBuilder _parent { get; }
        private string _case { get; }
        private Action<ICodeWriter>? _content;

        /// <summary>
        /// Initializes a new instance of the <see cref="SwitchCaseBuilder"/> class.
        /// </summary>
        /// <param name="parent">The parent used by this operation.</param>
        /// <param name="case">The case used by this operation.</param>
        public SwitchCaseBuilder(SwitchBuilder parent, string @case)
        {
            this._parent = parent;
            this._case = @case;
        }

        /// <summary>
        /// Supplies the callback that writes the statements for this switch case.
        /// </summary>
        /// <param name="contentDelegate">A callback that writes the statements or expression for this case.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public SwitchBuilder WithContent(Action<ICodeWriter> contentDelegate)
        {
            this._content = contentDelegate ?? EmptyBody;
            return this._parent;
        }

        /// <summary>
        /// Configures the expression emitted for this switch-expression case.
        /// </summary>
        /// <param name="returnValue">The expression to emit for this switch-expression arm.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public SwitchBuilder WithExpression(string returnValue = "null")
        {
            this._content = w => w.Append($"{this._case} => {returnValue}");
            return this._parent;
        }

        /// <inheritdoc/>
        void IBuilder.Write(in CodeWriter writer)
        {
            if (this._parent.Expression)
            {
                this._content?.Invoke(writer);
            }
            else
            {
                writer.AppendLine($"case {this._case}:");
                writer.IncreaseIndent();
                this._content?.Invoke(writer);
                writer.AppendLine("break;");
                writer.DecreaseIndent();
            }
        }

        private static void EmptyBody(ICodeWriter writer) { }
    }
}
