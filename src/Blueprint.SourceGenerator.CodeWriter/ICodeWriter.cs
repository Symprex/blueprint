namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Defines the low-level text-writing operations that the higher-level builders use to emit formatted C# source.
    /// Most callers encounter this interface inside <c>WithBody</c> callbacks, where it acts as the target for structured body helpers rather than ad-hoc string concatenation.
    /// </summary>
    public interface ICodeWriter
    {
        /// <summary>
        /// Writes text at the current indentation level without finishing the line.
        /// </summary>
        /// <param name="value">The text fragment to append to the current line.</param>
        void Append(string value);

        /// <summary>
        /// Writes raw text without applying indentation logic.
        /// </summary>
        /// <param name="value">The raw text fragment to append exactly as provided.</param>
        void AppendUnindented(string value);

        /// <summary>
        /// Ends the current line and advances the writer to the next line.
        /// </summary>
        void NewLine();

        /// <summary>
        /// Writes a complete indented line and then appends a newline.
        /// </summary>
        /// <param name="value">The line content to emit.</param>
        void AppendLine(string value);

        /// <summary>
        /// Writes a raw line without indentation handling and then appends a newline.
        /// </summary>
        /// <param name="value">The line content to emit exactly as provided.</param>
        void AppendUnindentedLine(string value);

        /// <summary>
        /// Starts a scoped block and returns an IDisposable that closes the block when disposed.
        /// </summary>
        /// <param name="value">The block header to emit before the opening brace.</param>
        /// <param name="constraints">Optional trailing clauses to append after the block header.</param>
        /// <returns>An IDisposable that should be disposed when the generated block is complete.</returns>
        IDisposable Block(string value, params string[] constraints);

        /// <summary>
        /// Creates a child writer for a nested block so its contents can be composed separately.
        /// </summary>
        /// <param name="originalLine">The original block header to emit before the nested content.</param>
        /// <returns>A writer that emits inside the requested nested block.</returns>
        ICodeWriter BlockWriter(string originalLine);
    }
}
