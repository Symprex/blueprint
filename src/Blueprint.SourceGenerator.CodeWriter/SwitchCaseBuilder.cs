using System;

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
            _parent = parent;
            _case = @case;
        }

        /// <summary>
        /// Supplies the callback that writes the statements for this switch case.
        /// </summary>
        /// <param name="contentDelegate">A callback that writes the statements or expression for this case.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public SwitchBuilder WithContent(Action<ICodeWriter> contentDelegate)
        {
            _content = contentDelegate ?? EmptyBody;
            return _parent;
        }

        /// <summary>
        /// Configures the expression emitted for this switch-expression case.
        /// </summary>
        /// <param name="returnValue">The expression to emit for this switch-expression arm.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public SwitchBuilder WithExpression(string returnValue = "null")
        {
            _content = w => w.Append($"{_case} => {returnValue}");
            return _parent;
        }

        void IBuilder.Write(in CodeWriter writer)
        {
            if (_parent.Expression)
            {
                _content?.Invoke(writer);
            }
            else
            {
                writer.AppendLine($"case {_case}:");
                writer.IncreaseIndent();
                _content?.Invoke(writer);
                writer.AppendLine("break;");
                writer.DecreaseIndent();
            }
        }

        private static void EmptyBody(ICodeWriter writer) { }
    }
}
