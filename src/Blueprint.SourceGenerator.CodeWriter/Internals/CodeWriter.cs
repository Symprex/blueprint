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
        /// <param name="indentStyle">The indentation style to use when rendering generated lines.</param>
        /// <param name="startingLevel">The initial indentation depth for the writer.</param>
        public CodeWriter(IndentStyle indentStyle, int startingLevel = 0)
        {
            this._indentStyle = indentStyle;
            this.Indent = indentStyle switch
            {
                IndentStyle.Tabs => "\t",
                _ => "    "
            };
        }

        /// <summary>
        /// Increases indentation for subsequently emitted lines.
        /// </summary>
        public void IncreaseIndent()
        {
            this._extraIndent++;
        }

        /// <summary>
        /// Decreases indentation for subsequently emitted lines.
        /// </summary>
        public void DecreaseIndent()
        {
            this._extraIndent--;
        }

        /// <summary>
        /// Writes a block header, emits an opening brace, and returns the writer so disposal can close the block.
        /// </summary>
        /// <param name="value">The block header to emit before the opening brace.</param>
        /// <param name="constraints">Optional trailing lines to emit between the header and the opening brace.</param>
        /// <returns>An <see cref="IDisposable"/> that closes the block when disposed.</returns>
        public IDisposable Block(string value, params string[] constraints)
        {
            this.AppendLine(value.TrimEnd());
            this._indentLevel++;
            foreach (var constraint in constraints)
            {
                if (string.IsNullOrEmpty(constraint))
                {
                    continue;
                }

                this.AppendLine(constraint);
            }

            this._indentLevel--;
            this.AppendLine("{");
            this._indentLevel++;
            return this;
        }

        /// <summary>
        /// Creates a child writer for a nested block so its contents can be composed separately and rendered back into this writer.
        /// </summary>
        /// <param name="originalLine">The block header to emit before the nested content, or <see langword="null"/> to omit one.</param>
        /// <returns>A writer that emits inside the requested nested block.</returns>
        public ICodeWriter BlockWriter(string? originalLine)
        {
            var writer = new CodeWriter(this._indentStyle, this._indentLevel);
            if (originalLine is { })
            {
                writer.AppendLine(originalLine);
            }
            writer.AppendLine("{");
            writer._indentLevel++;
            this._blocks.Add((null, writer));
            return writer;
        }

        private StringBuilder EnsureStringBuilder()
        {
            if (this._blocks.LastOrDefault().StringBuilder is not { } sb)
            {
                sb = new StringBuilder();
                this._blocks.Add((sb, null));
            }

            return sb;
        }

        /// <summary>
        /// Appends text using the current indentation level.
        /// </summary>
        /// <param name="value">The text fragment to append.</param>
        public void Append(string value)
        {
            this.EnsureStringBuilder().Append(this.GetIndentedValue(value.TrimEnd()));
        }

        /// <summary>
        /// Appends raw text without applying indentation.
        /// </summary>
        /// <param name="value">The text fragment to append exactly as provided.</param>
        public void AppendUnindented(string value)
        {
            this.EnsureStringBuilder().Append(value.TrimEnd());
        }

        /// <summary>
        /// Appends a newline to the current output buffer.
        /// </summary>
        public void NewLine()
        {
            this.EnsureStringBuilder().AppendLine();
        }

        /// <summary>
        /// Appends a complete indented line and a newline.
        /// </summary>
        /// <param name="value">The line content to emit.</param>
        public void AppendLine(string value)
        {
            this.EnsureStringBuilder().AppendLine(this.GetIndentedValue(value.TrimEnd()));
        }

        /// <summary>
        /// Appends a raw line and a newline without applying indentation.
        /// </summary>
        /// <param name="value">The line content to emit exactly as provided.</param>
        public void AppendUnindentedLine(string value)
        {
            this.EnsureStringBuilder().AppendLine(value.TrimEnd());
        }

        private string GetIndentedValue(string value)
        {
            var indent = string.Empty;
            for (var i = 0; i < this._indentLevel + this._extraIndent; i++)
            {
                indent += this.Indent;
            }

            return indent + value;
        }

        /// <summary>
        /// Closes the current block, if one is open, by emitting a closing brace at the current indentation level.
        /// </summary>
        public void Dispose()
        {
            if (this._indentLevel > 0)
            {
                this._indentLevel--;
                this.EnsureStringBuilder().AppendLine(this.GetIndentedValue("}"));
            }
        }

        /// <summary>
        /// Renders all accumulated output, including nested block writers, into a single source string.
        /// </summary>
        /// <returns>The rendered source accumulated by the writer.</returns>
        public string Render()
        {
            var result = new StringBuilder();

            foreach (var block in this._blocks)
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

            this.Dispose();

            return result.ToString();
        }
    }
}
