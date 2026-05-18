namespace Blueprint.Model;

/// <summary>
/// A <see cref="ScopePair"/> that has had its <see cref="ScopePair.ResourceKeyTemplate"/> tokens
/// expanded into a concrete resource key. The generated pipeline produces these by substituting
/// operation property values and claim values at compile-time–known property access sites,
/// eliminating the need for regex parsing and reflection at runtime.
/// </summary>
/// <param name="Noun">The resource noun (e.g. "account", "tenant").</param>
/// <param name="Verb">The action verb (e.g. "read", "write").</param>
/// <param name="ResourceKeyTemplate">
/// The original template string, or <see langword="null"/> if the scope pair has no template.
/// Retained so that authorization providers can log or inspect the template pattern.
/// </param>
/// <param name="ExpandedResourceKey">
/// The fully-expanded resource key with all <c>{PropertyName}</c> and <c>{Auth.ClaimType}</c>
/// tokens substituted, or <see langword="null"/> when no template was declared.
/// </param>
public readonly record struct ResolvedScopePair(
    string Noun,
    string Verb,
    string? ResourceKeyTemplate,
    string? ExpandedResourceKey);
