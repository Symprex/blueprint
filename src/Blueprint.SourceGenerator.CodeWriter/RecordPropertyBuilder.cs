using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents record property builder.
    /// </summary>
    public class RecordPropertyBuilder
    {
        public string Type { get; }
        public string Name { get; }
        public RecordBuilder Record { get; }
        public bool HasDefaultValue { get; private set; }
        public object? DefaultValue { get; private set; }
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordPropertyBuilder"/> class.
        /// </summary>
        /// <param name="type">The type value.</param>
        /// <param name="name">The name value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <param name="record">The record value.</param>
        public RecordPropertyBuilder(string type, string name, Accessibility accessModifier, RecordBuilder record)
        {
            Type = type;
            Name = name;
            AccessModifier = accessModifier;
            Record = record;
        }

        /// <summary>
        /// Converts string.
        /// </summary>
        /// <returns>The generated result.</returns>
        public override string ToString()
        {
            var output = $"{Type} {Name}";
            if (HasDefaultValue)
                output += $" = {DefaultValue}";

            return output;
        }

        /// <summary>
        /// Converts positional property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string ToPositionalProperty()
        {
            return ToString();
        }

        /// <summary>
        /// Converts init property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string ToInitProperty()
        {
            string defaultValue = HasDefaultValue ? $" = {DefaultValue};" : string.Empty;
            return $"{AccessModifier.Code()} {Type} {Name} {{ get; init; }}{defaultValue}";
        }

        /// <summary>
        /// Configures default value.
        /// </summary>
        /// <param name="defaultValue">The default value value.</param>
        /// <returns>The generated result.</returns>
        public RecordPropertyBuilder WithDefaultValue(string defaultValue)
        {
            DefaultValue = defaultValue;
            HasDefaultValue = true;
            return this;
        }

        /// <summary>
        /// Configures default value.
        /// </summary>
        /// <param name="value">The value value.</param>
        /// <returns>The generated result.</returns>
        public RecordPropertyBuilder WithDefaultValue(object value) =>
            WithDefaultValue($"{value}");
    }
}
