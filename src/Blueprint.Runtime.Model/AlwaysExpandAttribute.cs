using JetBrains.Annotations;

namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks an <see cref="Expandable{T}"/> property as unconditionally expanded — the expansion
/// runs after every successful operation regardless of whether the client includes an
/// <c>?expand=</c> query parameter. Use this for properties whose expanded value is always
/// required by consumers.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class AlwaysExpandAttribute : Attribute
{
}
