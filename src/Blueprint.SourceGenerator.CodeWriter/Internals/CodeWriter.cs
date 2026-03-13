using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#pragma warning disable IDE0008
#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    internal sealed class CodeWriter : IDisposable, ICodeWriter
    {
        private readonly IndentStyle _indentStyle;
        private readonly string Indent;

        private int _indentLevel = 0;
        private int _extraIndent = 0;
        private readonly List<(StringBuilder? StringBuilder, CodeWriter? Writer)> _blocks = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeWriter"/> class.
        /// </summary>
        /// <param name="indentStyle">The indent style value.</param>
        /// <param name="startingLevel">The starting level value.</param>
        public CodeWriter(IndentStyle indentStyle, int startingLevel = 0)
        {
            _indentStyle = indentStyle;
            Indent = indentStyle switch
            {
                IndentStyle.Tabs => "\t",
                _ => "    "
            };
        }

        /// <summary>
        /// Performs the increase indent operation.
        /// </summary>
        public void IncreaseIndent() => _extraIndent++;

        /// <summary>
        /// Performs the decrease indent operation.
        /// </summary>
        public void DecreaseIndent() => _extraIndent--;

        /// <summary>
        /// Performs the block operation.
        /// </summary>
        /// <param name="value">The value value.</param>
        /// <param name="constraints">The constraints value.</param>
        /// <returns>The generated result.</returns>
        public IDisposable Block(string value, params string[] constraints)
        {
            AppendLine(value.TrimEnd());
            _indentLevel++;
            foreach (var constraint in constraints)
            {
                if (string.IsNullOrEmpty(constraint))
                    continue;

                AppendLine(constraint);
            }

            _indentLevel--;
            AppendLine("{");
            _indentLevel++;
            return this;
        }

        /// <summary>
        /// Performs the block writer operation.
        /// </summary>
        /// <param name="originalLine">The original line value.</param>
        /// <returns>The generated result.</returns>
        public ICodeWriter BlockWriter(string? originalLine)
        {
            var writer = new CodeWriter(_indentStyle, _indentLevel);
            if (originalLine is { })
            {
                writer.AppendLine(originalLine);
            }
            writer.AppendLine("{");
            writer._indentLevel++;
            _blocks.Add((null, writer));
            return writer;
        }

        private StringBuilder EnsureStringBuilder()
        {
            if (_blocks.LastOrDefault().StringBuilder is not { } sb)
            {
                sb = new StringBuilder();
                _blocks.Add((sb, null));
            }

            return sb;
        }

        /// <summary>
        /// Appends append.
        /// </summary>
        /// <param name="value">The value value.</param>
        public void Append(string value)
        {
            EnsureStringBuilder().Append(GetIndentedValue(value.TrimEnd()));
        }

        /// <summary>
        /// Appends unindented.
        /// </summary>
        /// <param name="value">The value value.</param>
        public void AppendUnindented(string value)
        {
            EnsureStringBuilder().Append(value.TrimEnd());
        }

        /// <summary>
        /// Performs the new line operation.
        /// </summary>
        public void NewLine()
        {
            EnsureStringBuilder().AppendLine();
        }

        /// <summary>
        /// Appends line.
        /// </summary>
        /// <param name="value">The value value.</param>
        public void AppendLine(string value)
        {
            EnsureStringBuilder().AppendLine(GetIndentedValue(value.TrimEnd()));
        }

        /// <summary>
        /// Appends unindented line.
        /// </summary>
        /// <param name="value">The value value.</param>
        public void AppendUnindentedLine(string value)
        {
            EnsureStringBuilder().AppendLine(value.TrimEnd());
        }

        private string GetIndentedValue(string value)
        {
            var indent = string.Empty;
            for (var i = 0; i < _indentLevel + _extraIndent; i++)
                indent += Indent;

            return indent + value;
        }

        /// <summary>
        /// Performs the dispose operation.
        /// </summary>
        public void Dispose()
        {
            if (_indentLevel > 0)
            {
                _indentLevel--;
                EnsureStringBuilder().AppendLine(GetIndentedValue("}"));
            }
        }

        /// <summary>
        /// Renders render.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string Render()
        {
            var result = new StringBuilder();

            foreach (var block in _blocks)
            {
                if (block.StringBuilder is { })
                {
                    result.Append(block.StringBuilder);
                }

                if (block.Writer is { })
                {
                    block.Writer.Dispose();
                    result.Append(block.Writer.Render());
                }
            }

            Dispose();

            return result.ToString();
        }
    }
}
