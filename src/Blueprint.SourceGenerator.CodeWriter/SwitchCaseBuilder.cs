using System;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents switch case builder.
    /// </summary>
    public sealed class SwitchCaseBuilder : IBuilder
    {
        private SwitchBuilder _parent { get; }
        private string _case { get; }
        private Action<ICodeWriter>? _content;

        /// <summary>
        /// Initializes a new instance of the <see cref="SwitchCaseBuilder"/> class.
        /// </summary>
        /// <param name="parent">The parent value.</param>
        /// <param name="case">The case value.</param>
        public SwitchCaseBuilder(SwitchBuilder parent, string @case)
        {
            _parent = parent;
            _case = @case;
        }

        /// <summary>
        /// Configures content.
        /// </summary>
        /// <param name="contentDelegate">The content delegate value.</param>
        /// <returns>The generated result.</returns>
        public SwitchBuilder WithContent(Action<ICodeWriter> contentDelegate)
        {
            _content = contentDelegate ?? EmptyBody;
            return _parent;
        }

        /// <summary>
        /// Configures expression.
        /// </summary>
        /// <param name="returnValue">The return value value.</param>
        /// <returns>The generated result.</returns>
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
