using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a single property emitted by a generated record.
    /// </summary>
    public class RecordPropertyBuilder
    {
        /// <summary>
        /// Gets the property type that will be emitted for the generated record property.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the property name that will be emitted for the generated record property.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the RecordBuilder that owns this property.
        /// </summary>
        public RecordBuilder Record { get; }

        /// <summary>
        /// Gets a value indicating whether the generated record property should emit a default value.
        /// </summary>
        public bool HasDefaultValue { get; private set; }

        /// <summary>
        /// Gets the default value that will be emitted for the generated record property, when configured.
        /// </summary>
        public object? DefaultValue { get; private set; }

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated record property.
        /// </summary>
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordPropertyBuilder"/> class.
        /// </summary>
        /// <param name="type">The type to emit in generated source.</param>
        /// <param name="name">The generated name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <param name="record">The record builder that owns this property.</param>
        public RecordPropertyBuilder(string type, string name, Accessibility accessModifier, RecordBuilder record)
        {
            Type = type;
            Name = name;
            AccessModifier = accessModifier;
            Record = record;
        }

        /// <summary>
        /// Formats the record property as the shared source fragment used by record emitters.
        /// </summary>
        /// <returns>The generated source fragment represented by the current builder.</returns>
        public override string ToString()
        {
            var output = $"{Type} {Name}";
            if (HasDefaultValue)
                output += $" = {DefaultValue}";

            return output;
        }

        /// <summary>
        /// Formats the property for positional-record emission.
        /// </summary>
        /// <returns>The positional property declaration text.</returns>
        public string ToPositionalProperty()
        {
            return ToString();
        }

        /// <summary>
        /// Formats the property for init-only record emission.
        /// </summary>
        /// <returns>The init-only property declaration text.</returns>
        public string ToInitProperty()
        {
            string defaultValue = HasDefaultValue ? $" = {DefaultValue};" : string.Empty;
            return $"{AccessModifier.Code()} {Type} {Name} {{ get; init; }}{defaultValue}";
        }

        /// <summary>
        /// Sets the default value emitted for the generated record property.
        /// </summary>
        /// <param name="defaultValue">The default value to emit in generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordPropertyBuilder WithDefaultValue(string defaultValue)
        {
            DefaultValue = defaultValue;
            HasDefaultValue = true;
            return this;
        }

        /// <summary>
        /// Sets the default value emitted for the generated record property.
        /// </summary>
        /// <param name="value">The value to emit or configure.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public RecordPropertyBuilder WithDefaultValue(object value) =>
            WithDefaultValue($"{value}");
    }
}
