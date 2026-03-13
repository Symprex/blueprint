using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a single parameter within a generated signature, including its type, name, and optional default value.
    /// </summary>
    public class ParameterBuilder<T>
        where T : BuilderBase<T>
    {
        internal ParameterBuilder(T parent)
        {
            this.Parent = parent;
        }

        /// <summary>
        /// Gets the builder that owns this generated parameter.
        /// </summary>
        public T Parent { get; }

        /// <summary>
        /// Gets the type text that will be emitted for the generated parameter.
        /// </summary>
        public string? Type { get; internal set; }

        /// <summary>
        /// Gets the parameter name that will be emitted.
        /// </summary>
        public string? Name { get; internal set; }

        /// <summary>
        /// Gets the default value that will be emitted for the generated parameter, when configured.
        /// </summary>
        public string? DefaultValue { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the generated parameter should emit a default value.
        /// </summary>
        public bool HasDefaultValue { get; internal set; }

        /// <summary>
        /// Sets the type emitted for the generated parameter.
        /// </summary>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ParameterBuilder<T> WithType(string typeName)
        {
            this.Type = typeName;
            return this;
        }

        /// <summary>
        /// Sets the type emitted for the generated parameter.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ParameterBuilder<T> WithType(INamedTypeSymbol symbol)
        {
            this.Parent.AddNamespaceImport(symbol);
            return this.WithType(symbol.Name);
        }

        /// <summary>
        /// Sets the default value emitted for the generated parameter.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ParameterBuilder<T> WithDefaultValue()
        {
            return this.WithDefaultValue("default");
        }

        /// <summary>
        /// Sets the default value emitted for the generated parameter.
        /// </summary>
        /// <param name="defaultValue">The default value to emit in generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ParameterBuilder<T> WithDefaultValue(string defaultValue)
        {
            this.DefaultValue = defaultValue;
            this.HasDefaultValue = true;
            return this;
        }

        /// <summary>
        /// Sets the default value emitted for the generated parameter.
        /// </summary>
        /// <param name="value">The value to emit or configure.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ParameterBuilder<T> WithDefaultValue(object? value)
        {
            return this.WithDefaultValue($"{value}");
        }

        /// <summary>
        /// Configures the generated parameter to use null as its default value.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ParameterBuilder<T> WithNullDefault()
        {
            return this.WithDefaultValue("null");
        }

        /// <summary>
        /// Formats the parameter as C# signature text.
        /// </summary>
        /// <returns>The generated source fragment represented by the current builder.</returns>
        public override string ToString()
        {
            var output = $"{this.Type} {this.Name}";
            if (this.HasDefaultValue)
            {
                output += $"= {this.DefaultValue}";
            }

            return output;
        }

        /// <summary>
        /// Determines whether another value represents the same generated parameter shape.
        /// </summary>
        /// <param name="obj">The value to compare with this parameter builder.</param>
        /// <returns>True when the supplied value represents the same generated parameter shape; otherwise false.</returns>
        public override bool Equals(object obj)
        {
            return obj != null && obj is ParameterBuilder<T> builder &&
                builder.Type == this.Type && builder.Name == this.Name;
        }

        /// <summary>
        /// Computes a hash code based on the emitted parameter text.
        /// </summary>
        /// <returns>A hash code based on the emitted parameter text.</returns>
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }
    }
}
