using Microsoft.CodeAnalysis;

#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter.Internals
{
    internal static class CodeBuilderExtensions
    {
        private static readonly Dictionary<string, string> _mappings = new Dictionary<string, string>
        {
            { "Boolean", "bool" },
            { "Byte", "byte" },
            { "SByte", "sbyte" },
            { "Char", "char" },
            { "Decimal", "decimal" },
            { "Double", "double" },
            { "Single", "float" },
            { "Int32", "int" },
            { "UInt32", "uint" },
            { "Int64", "long" },
            { "UInt64", "ulong" },
            { "Int16", "short" },
            { "UInt16", "ushort" },
            { "Object", "object" },
            { "String", "string" }
        };

        /// <summary>
        /// Formats a Roslyn type symbol as the C# type name that should be emitted by CodeWriter.
        /// </summary>
        /// <param name="symbol">The Roslyn type symbol to format.</param>
        /// <returns>The C# type name that should be emitted for the supplied symbol.</returns>
        public static string GetTypeName(this ITypeSymbol symbol)
        {
            return symbol.ContainingNamespace.Name == "System" && _mappings.ContainsKey(symbol.Name)
                ? _mappings[symbol.Name]
                : symbol.GetFullMetadataName();
        }

        /// <summary>
        /// Formats a CLR type as the C# type name that should be emitted by CodeWriter.
        /// </summary>
        /// <param name="type">The CLR type to format.</param>
        /// <returns>The C# type name that should be emitted for the supplied CLR type.</returns>
        public static string GetTypeName(this Type type)
        {
            return type.Namespace == "System" && _mappings.ContainsKey(type.Name) ? _mappings[type.Name] : type.FullName;
        }
    }
}
