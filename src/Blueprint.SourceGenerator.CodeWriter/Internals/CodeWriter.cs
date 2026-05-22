using System.Text;

#pragma warning disable IDE0008
#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Low-level source writer that tracks indentation, manages nested block writers, and accumulates lines into a renderable string.
    /// Builders obtain an instance via <see cref="CodeBuilder"/> and should not construct one directly.
    /// </summary>
    internal sealed class CodeWriter : IDisposable, ICodeWriter
    {
        // Pre-built indent strings up to depth 16 (covers virtually all generated code).
        // Deeper nesting falls back to on-demand construction.
        private static readonly string[] SpaceIndents = BuildIndentCache("    ", 16);
        private static readonly string[] TabIndents = BuildIndentCache("\t", 16);

        private readonly string[] _indentCache;
        private readonly string _indentUnit;
        private readonly IndentStyle _indentStyle;

        private int _indentLevel = 0;
        private int _extraIndent = 0;
        private readonly List<(StringBuilder? StringBuilder, CodeWriter? Writer)> _blocks = new();

        private static string[] BuildIndentCache(string unit, int maxDepth)
        {
            var cache = new string[maxDepth + 1];
            cache[0] = string.Empty;
            for (var i = 1; i <= maxDepth; i++)
            {
                cache[i] = cache[i - 1] + unit;
            }
            return cache;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeWriter"/> class.
        /// </summary>
        /// <param name="indentStyle">The indentation style to use when rendering generated lines.</param>
        /// <param name="startingLevel">The initial indentation depth for the writer.</param>
        public CodeWriter(IndentStyle indentStyle, int startingLevel = 0)
        {
            this._indentStyle = indentStyle;
            this._indentUnit = indentStyle switch
            {
                IndentStyle.Tabs => "\t",
                _ => "    "
            };
            this._indentCache = indentStyle switch
            {
                IndentStyle.Tabs => TabIndents,
                _ => SpaceIndents
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
                // Typical generated code block is 512-2048 chars; start at 256 to avoid
                // early resize cycles while keeping allocation small for trivial blocks.
                sb = new StringBuilder(256);
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
            var sb = this.EnsureStringBuilder();
            var trimmed = value.TrimEnd();
            this.AppendIndentTo(sb);
            sb.Append(trimmed);
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
            var sb = this.EnsureStringBuilder();
            var trimmed = value.TrimEnd();
            this.AppendIndentTo(sb);
            sb.Append(trimmed);
            sb.AppendLine();
        }

        /// <summary>
        /// Appends a raw line and a newline without applying indentation.
        /// </summary>
        /// <param name="value">The line content to emit exactly as provided.</param>
        public void AppendUnindentedLine(string value)
        {
            var sb = this.EnsureStringBuilder();
            sb.Append(value.TrimEnd());
            sb.AppendLine();
        }

        private void AppendIndentTo(StringBuilder sb)
        {
            var depth = this._indentLevel + this._extraIndent;
            if (depth == 0)
            {
                return;
            }

            if (depth < this._indentCache.Length)
            {
                sb.Append(this._indentCache[depth]);
            }
            else
            {
                // Beyond cache depth — build on demand (extremely rare).
                for (var i = 0; i < depth; i++)
                {
                    sb.Append(this._indentUnit);
                }
            }
        }

        /// <summary>
        /// Closes the current block, if one is open, by emitting a closing brace at the current indentation level.
        /// </summary>
        public void Dispose()
        {
            if (this._indentLevel > 0)
            {
                this._indentLevel--;
                var sb = this.EnsureStringBuilder();
                this.AppendIndentTo(sb);
                sb.Append('}');
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Renders all accumulated output, including nested block writers, into a single source string.
        /// </summary>
        /// <returns>The rendered source accumulated by the writer.</returns>
        public string Render()
        {
            // Pre-compute total length to avoid StringBuilder resize+copy cycles.
            var totalLength = this.ComputeTotalLength();
            var result = new StringBuilder(totalLength);
            this.RenderTo(result);
            return result.ToString();
        }

        /// <summary>
        /// Renders all accumulated output directly into the target <see cref="StringBuilder"/>,
        /// avoiding intermediate string allocations for nested writers.
        /// </summary>
        /// <param name="target">The StringBuilder to append all content into.</param>
        private void RenderTo(StringBuilder target)
        {
            foreach (var block in this._blocks)
            {
                if (block.StringBuilder is { })
                {
                    target.Append(block.StringBuilder);
                }

                if (block.Writer is { })
                {
                    block.Writer.Dispose();
                    block.Writer.RenderTo(target);
                }
            }

            this.Dispose();
        }

        private int ComputeTotalLength()
        {
            var totalLength = 0;
            foreach (var block in this._blocks)
            {
                if (block.StringBuilder is { })
                {
                    totalLength += block.StringBuilder.Length;
                }

                if (block.Writer is { })
                {
                    totalLength += block.Writer.ComputeTotalLength();
                }
            }

            return totalLength;
        }
    }
}
