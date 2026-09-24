using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Recognises the types legacy Blueprint documented as strings because of their type converter, such as Signature365's
/// strongly typed <c>ApiId</c>.
/// </summary>
/// <remarks>
/// Legacy described every type through Newtonsoft.Json's contracts, and Newtonsoft gives a type whose type converter
/// converts to string a string contract (<c>JsonTypeReflector.CanTypeDescriptorConvertString</c>), which NJsonSchema
/// documented as <c>{"type": "string"}</c>. The base <see cref="TypeConverter"/>, which every type has, and the
/// component and reference converters, do not count, as Newtonsoft does not count them.
/// </remarks>
internal static class OpenApiStringConvertibleTypes
{
    /// <summary>
    /// Returns whether <paramref name="type"/> has a type converter of its own that converts it to string.
    /// </summary>
    /// <param name="type">The type, which is not a primitive, an enum or another type the document maps by name.</param>
    /// <returns><see langword="true"/> when legacy would have documented the type as a string.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The OpenAPI document is built from the app's own operation and resource types, whose type converters are declared by attribute on the types themselves.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "The OpenAPI document is built from the app's own operation and resource types, whose type converters are declared by attribute on the types themselves.")]
    public static bool IsConvertibleToString(Type type)
    {
        var converter = TypeDescriptor.GetConverter(type);
        var converterType = converter.GetType();

        return converterType != typeof(TypeConverter)
            && converterType.FullName is not ("System.ComponentModel.ComponentConverter" or "System.ComponentModel.ReferenceConverter" or "System.Windows.Forms.Design.DataSourceConverter")
            && converter.CanConvertTo(typeof(string));
    }
}
