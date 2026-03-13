using System.Collections.Generic;
using System.Linq;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a single enum value within an EnumBuilder.
    /// </summary>
    public class EnumValueBuilder : IBuilder
    {
        private readonly List<string> _attributes = new List<string>();
        private DocumentationComment? _xmlDoc;

        internal EnumValueBuilder(string name, EnumBuilder builder, int? value)
        {
            Name = name;
            Value = value;
            Enum = builder;
        }

        /// <summary>
        /// Gets the enum member name that will be emitted.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the numeric value that will be emitted for the enum member, when one has been assigned.
        /// </summary>
        public int? Value { get; private set; }

        /// <summary>
        /// Gets the EnumBuilder that owns this value.
        /// </summary>
        public EnumBuilder Enum { get; }

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated enum member.
        /// </summary>
        /// <param name="summary">The text to place inside the emitted summary documentation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumValueBuilder WithSummary(string summary)
        {
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures the generated enum member to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated enum member. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumValueBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumValueBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Adds another enum value to the owning enum and returns its builder.
        /// </summary>
        /// <param name="value">The enum member name to emit.</param>
        /// <param name="numericValue">The numeric value to assign to the generated enum member.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumValueBuilder AddValue(string value, int? numericValue = null)
        {
            return Enum.AddValue(value, numericValue);
        }

        /// <summary>
        /// Adds an attribute to the generated enum member if it is not already present.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EnumValueBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        void IBuilder.Write(in CodeWriter writer)
        {
            _xmlDoc?.Write(writer);

            foreach (var attr in _attributes.OrderBy(x => x))
            {
                writer.AppendLine($"[{attr}]");
            }

            writer.Append(Name);
            if (Value.HasValue)
            {
                writer.AppendUnindented($" = {Value}");
            }
        }
    }
}
