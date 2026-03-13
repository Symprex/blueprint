#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    internal abstract class DocumentationComment
    {
        /// <summary>
        /// Writes the current documentation model to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the XML documentation output.</param>
        public abstract void Write(in CodeWriter writer);
    }

    internal class InheritDocumentationComment : DocumentationComment
    {
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

    internal class SummaryDocumentationComment : DocumentationComment
    {
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
