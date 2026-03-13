using System;
using System.Linq;
using System.Collections.Generic;

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
            writer.AppendLine($"/// <inheritdoc {(InheritFrom is null ? string.Empty : $"cref=\"{InheritFrom}\"")}/>");
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
            if (Summary is null || string.IsNullOrEmpty(Summary))
                return;

            writer.AppendLine("/// <summary>");

            string[] lines = Summary.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
                writer.AppendLine($"/// {line}");

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
            parameterDocs[parameterName] = documentationComment;
        }

        internal void RemoveUnusedParameters<T>(List<ParameterBuilder<T>> parameters)
            where T : BuilderBase<T>, IParameterized<T>
        {
            var unusedParameters = parameterDocs.Where(p =>
                !parameters.Any(x => x.Type == p.Key)).ToArray();
            foreach (var parameter in unusedParameters)
                parameterDocs.Remove(parameter.Key);
        }

        /// <summary>
        /// Writes the summary and parameter documentation to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the XML documentation output.</param>
        public override void Write(in CodeWriter writer)
        {
            base.Write(writer);
            foreach (var param in parameterDocs.OrderBy(x => x.Key))
                writer.AppendLine($"/// <param name=\"{param.Key}\">{param.Value}</param>");
        }
    }
}
