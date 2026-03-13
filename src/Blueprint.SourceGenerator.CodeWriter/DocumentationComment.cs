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
        /// Writes write.
        /// </summary>
        /// <param name="writer">The writer value.</param>
        public abstract void Write(in CodeWriter writer);
    }

    internal class InheritDocumentationComment : DocumentationComment
    {
        internal string? InheritFrom { get; set; }

        /// <summary>
        /// Writes write.
        /// </summary>
        /// <param name="writer">The writer value.</param>
        public override void Write(in CodeWriter writer)
        {
            writer.AppendLine($"/// <inheritdoc {(InheritFrom is null ? string.Empty : $"cref=\"{InheritFrom}\"")}/>");
        }
    }

    internal class SummaryDocumentationComment : DocumentationComment
    {
        internal string? Summary { get; set; }

        /// <summary>
        /// Writes write.
        /// </summary>
        /// <param name="writer">The writer value.</param>
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
        /// Adds parameter.
        /// </summary>
        /// <param name="parameterName">The parameter name value.</param>
        /// <param name="documentationComment">The documentation comment value.</param>
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
        /// Writes write.
        /// </summary>
        /// <param name="writer">The writer value.</param>
        public override void Write(in CodeWriter writer)
        {
            base.Write(writer);
            foreach (var param in parameterDocs.OrderBy(x => x.Key))
                writer.AppendLine($"/// <param name=\"{param.Key}\">{param.Value}</param>");
        }
    }
}
