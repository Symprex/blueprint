using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents parameter builder.
    /// </summary>
    public class ParameterBuilder<T>
        where T : BuilderBase<T>
    {
        internal ParameterBuilder(T parent)
        {
            Parent = parent;
        }

        public T Parent { get; }

        public string? Type { get; internal set; }
        public string? Name { get; internal set; }
        public string? DefaultValue { get; internal set; }
        public bool HasDefaultValue { get; internal set; }

        /// <summary>
        /// Configures type.
        /// </summary>
        /// <param name="typeName">The type name value.</param>
        /// <returns>The generated result.</returns>
        public ParameterBuilder<T> WithType(string typeName)
        {
            Type = typeName;
            return this;
        }

        /// <summary>
        /// Configures type.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public ParameterBuilder<T> WithType(INamedTypeSymbol symbol)
        {
            Parent.AddNamespaceImport(symbol);
            return WithType(symbol.Name);
        }

        /// <summary>
        /// Configures default value.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ParameterBuilder<T> WithDefaultValue() =>
            WithDefaultValue("default");

        /// <summary>
        /// Configures default value.
        /// </summary>
        /// <param name="defaultValue">The default value value.</param>
        /// <returns>The generated result.</returns>
        public ParameterBuilder<T> WithDefaultValue(string defaultValue)
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
        public ParameterBuilder<T> WithDefaultValue(object? value) =>
            WithDefaultValue($"{value}");

        /// <summary>
        /// Configures null default.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ParameterBuilder<T> WithNullDefault() =>
            WithDefaultValue("null");

        /// <summary>
        /// Converts string.
        /// </summary>
        /// <returns>The generated result.</returns>
        public override string ToString()
        {
            var output = $"{Type} {Name}";
            if (HasDefaultValue)
                output += $"= {DefaultValue}";

            return output;
        }

        /// <summary>
        /// Performs the equals operation.
        /// </summary>
        /// <param name="obj">The obj value.</param>
        /// <returns>The generated result.</returns>
        public override bool Equals(object obj)
        {
            return obj != null && obj is ParameterBuilder<T> builder &&
                builder.Type == Type && builder.Name == Name;
        }

        /// <summary>
        /// Gets hash code.
        /// </summary>
        /// <returns>The generated result.</returns>
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }
}
