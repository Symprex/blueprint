using System.Collections.Generic;
using System.Linq;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0008
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Adds the common parameter-construction helpers used by fluent builders such as methods, constructors, and delegates.
    /// Prefer these overloads when you want to append parameters quickly without constructing <see cref="ParameterBuilder{T}"/> instances manually.
    /// </summary>
    public static class ParameterizedExtensions
    {
        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, string typeName)
            where T : BuilderBase<T>, IParameterized<T>
        {
            return parameterized.AddParameterInternal(GetParameter(parameterized.Parent, typeName, null), -1);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, ITypeSymbol typeSymbol)
            where T : BuilderBase<T>, IParameterized<T>
        {
            parameterized.Parent.AddNamespaceImport(typeSymbol);
            return parameterized.AddParameter(typeSymbol.Name);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, string typeName, string parameterName)
            where T : BuilderBase<T>, IParameterized<T>
        {
            return parameterized.AddParameterInternal(GetParameter(parameterized.Parent, typeName, parameterName), -1);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, ITypeSymbol typeSymbol, string parameterName)
            where T : BuilderBase<T>, IParameterized<T>
        {
            parameterized.Parent.AddNamespaceImport(typeSymbol);
            return parameterized.AddParameter(typeSymbol.GetGloballyQualifiedTypeName(), parameterName);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, string typeName, string parameterName, int index)
            where T : BuilderBase<T>, IParameterized<T>
        {
            return parameterized.AddParameterInternal(GetParameter(parameterized.Parent, typeName, parameterName), index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, ITypeSymbol typeSymbol, string parameterName, int index)
            where T : BuilderBase<T>, IParameterized<T>
        {
            parameterized.Parent.AddNamespaceImport(typeSymbol);
            return parameterized.AddParameter(typeSymbol.Name, parameterName, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature and emits a default value.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="defaultValue">The default value to emit in generated source.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameterWithDefaultValue<T>(this IParameterized<T> parameterized, string typeName, string? parameterName = null, object? defaultValue = null, int index = -1)
            where T : BuilderBase<T>, IParameterized<T>
        {
            var parameter = GetParameter(parameterized.Parent, typeName, parameterName)
                .WithDefaultValue(defaultValue);
            return parameterized.AddParameterInternal(parameter, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature and emits a default value.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="defaultValue">The default value to emit in generated source.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameterWithDefaultValue<T>(this IParameterized<T> parameterized, ITypeSymbol typeSymbol, string? parameterName = null, object? defaultValue = null, int index = -1)
            where T : BuilderBase<T>, IParameterized<T>
        {
            parameterized.Parent.AddNamespaceImport(typeSymbol);
            return parameterized.AddParameterWithDefaultValue(typeSymbol.Name, parameterName, defaultValue, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature and emits a default value.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameterWithDefaultValue<T>(this IParameterized<T> parameterized, string typeName, string? parameterName = null, int index = -1)
            where T : BuilderBase<T>, IParameterized<T>
        {
            var parameter = GetParameter(parameterized.Parent, typeName, parameterName)
                .WithDefaultValue();
            return parameterized.AddParameterInternal(parameter, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature and emits a default value.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameterWithDefaultValue<T>(this IParameterized<T> parameterized, ITypeSymbol typeSymbol, string? parameterName = null, int index = -1)
            where T : BuilderBase<T>, IParameterized<T>
        {
            parameterized.Parent.AddNamespaceImport(typeSymbol);
            return parameterized.AddParameterWithDefaultValue(typeSymbol, parameterName, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature and emits a null default value.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeName">The C# type text to emit.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameterWithNullValue<T>(this IParameterized<T> parameterized, string typeName, string? parameterName = null, int index = -1)
            where T : BuilderBase<T>, IParameterized<T>
        {
            var parameter = GetParameter(parameterized.Parent, typeName, parameterName)
                .WithNullDefault();
            return parameterized.AddParameterInternal(parameter, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature and emits a null default value.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
        /// <param name="parameterName">The parameter name to emit. When omitted, a name is inferred from the type.</param>
        /// <param name="index">The zero-based insertion index. Use -1 to append the parameter.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameterWithNullValue<T>(this IParameterized<T> parameterized, ITypeSymbol typeSymbol, string? parameterName = null, int index = -1)
            where T : BuilderBase<T>, IParameterized<T>
        {
            parameterized.Parent.AddNamespaceImport(typeSymbol);
            return parameterized.AddParameterWithNullValue(typeSymbol.Name, parameterName, index);
        }

        /// <summary>
        /// Adds a parameter to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="parameter">The Roslyn parameter symbol to mirror into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameter<T>(this IParameterized<T> parameterized, IParameterSymbol parameter)
            where T : BuilderBase<T>, IParameterized<T>
        {
            if (parameter.HasExplicitDefaultValue)
                return parameterized.AddParameterWithDefaultValue(parameter.Type, parameter.Name, parameter.ExplicitDefaultValue);

            return parameterized.AddParameter(parameter.Type, parameter.Name);
        }

        /// <summary>
        /// Adds multiple Roslyn parameters to the current generated signature.
        /// </summary>
        /// <param name="parameterized">The builder whose parameter list should be updated.</param>
        /// <param name="parameters">The parameters or argument expressions to mirror into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public static T AddParameters<T>(this IParameterized<T> parameterized, IEnumerable<IParameterSymbol> parameters)
            where T : BuilderBase<T>, IParameterized<T>
        {
            if (parameters is null || !parameters.Any())
                return parameterized.Parent;

            foreach (var parameter in parameters)
                parameterized.AddParameter(parameter);

            return parameterized.Parent;
        }

        private static T AddParameterInternal<T>(this IParameterized<T> parameterized, ParameterBuilder<T> parameter, int index)
            where T : BuilderBase<T>, IParameterized<T>
        {
            if (parameterized.Parameters.Any(p => p.Type == parameter.Type && p.Name == parameter.Name))
                return parameterized.Parent;

            if (index > -1)
            {
                parameterized.Parameters.Insert(index, parameter);
            }
            else
            {
                parameterized.Parameters.Add(parameter);
            }

            return parameterized.Parent;
        }

        private static ParameterBuilder<T> GetParameter<T>(T parent, string typeName, string? parameterName)
            where T : BuilderBase<T>
        {
            if (parameterName is null || string.IsNullOrEmpty(parameterName))
            {
                parameterName = char.ToLower(typeName[0]) + typeName.Substring(1);
            }

            return new ParameterBuilder<T>(parent)
            {
                Type = typeName,
                Name = parameterName
            };
        }
    }
}
