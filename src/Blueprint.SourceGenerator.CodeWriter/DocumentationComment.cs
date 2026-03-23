#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Base for the internal documentation model written into generated source.
    /// Subclasses decide the exact XML form (summary, inheritdoc, or parameter docs).
    /// </summary>
    internal abstract class DocumentationComment
    {
        /// <summary>
        /// Writes the current documentation model to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the XML documentation output.</param>
        public abstract void Write(in CodeWriter writer);
    }

    /// <summary>
    /// Emits an <c>&lt;inheritdoc/&gt;</c> element, optionally targeting a specific cref.
    /// </summary>
    internal class InheritDocumentationComment : DocumentationComment
    {
        /// <summary>
        /// Gets or sets the optional cref value placed on the emitted inheritdoc element.
        /// When <see langword="null"/> a bare <c>&lt;inheritdoc/&gt;</c> is emitted.
        /// </summary>
        internal string? InheritFrom { get; set; }

        /// <summary>
        /// Writes an <c>&lt;inheritdoc/&gt;</c> element to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the XML documentation output.</param>
        public override void Write(in CodeWriter writer)
        {
            writer.AppendLine($"/// <inheritdoc {(this.InheritFrom is null ? string.Empty : $"cref=\"{this.InheritFrom}\"")}/>");
        }
    }

    /// <summary>
    /// Emits a <c>&lt;summary&gt;</c> element from a caller-supplied text string.
    /// </summary>
    internal class SummaryDocumentationComment : DocumentationComment
    {
        /// <summary>
        /// Gets or sets the text to emit inside the generated summary element.
        /// </summary>
        internal string? Summary { get; set; }

        /// <summary>
        /// Writes a <c>&lt;summary&gt;</c> element to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the XML documentation output.</param>
        public override void Write(in CodeWriter writer)
        {
            if (this.Summary is null || string.IsNullOrEmpty(this.Summary))
            {
                return;
            }

            writer.AppendLine("/// <summary>");

            var lines = this.Summary.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                writer.AppendLine($"/// {line}");
            }

            writer.AppendLine("/// </summary>");
        }
    }

    /// <summary>
    /// Emits a <c>&lt;summary&gt;</c> element followed by <c>&lt;param&gt;</c> elements for each documented parameter.
    /// </summary>
    internal class ParameterDocumentationComment : SummaryDocumentationComment
    {
        private readonly Dictionary<string, string> parameterDocs = new Dictionary<string, string>();

        /// <summary>
        /// Adds or replaces XML documentation for a named parameter.
        /// </summary>
        /// <param name="parameterName">The name of the parameter whose documentation should be emitted.</param>
        /// <param name="documentationComment">The text to emit inside the generated <c>&lt;param&gt;</c> element.</param>
        public void AddParameter(string parameterName, string documentationComment)
        {
            this.parameterDocs[parameterName] = documentationComment;
        }

        /// <summary>
        /// Removes any recorded parameter docs whose names do not match a parameter on the supplied list,
        /// preventing orphaned <c>&lt;param&gt;</c> elements in the generated output.
        /// </summary>
        /// <typeparam name="T">The concrete builder type that owns the parameter list.</typeparam>
        /// <param name="parameters">The active parameter builders whose names define the valid set.</param>
        internal void RemoveUnusedParameters<T>(List<ParameterBuilder<T>> parameters)
            where T : BuilderBase<T>, IParameterized<T>
        {
            var unusedParameters = this.parameterDocs.Where(p =>
                !parameters.Any(x => x.Type == p.Key)).ToArray();
            foreach (var parameter in unusedParameters)
            {
                this.parameterDocs.Remove(parameter.Key);
            }
        }

        /// <summary>
        /// Writes the summary and parameter documentation to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the XML documentation output.</param>
        public override void Write(in CodeWriter writer)
        {
            base.Write(writer);
            foreach (var param in this.parameterDocs.OrderBy(x => x.Key))
            {
                writer.AppendLine($"/// <param name=\"{param.Key}\">{param.Value}</param>");
            }
        }
    }
}
