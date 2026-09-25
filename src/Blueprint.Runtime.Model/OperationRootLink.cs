namespace Blueprint.Model;

/// <summary>
/// One root link of an operation, as <see cref="OperationRootLinksFeatureData"/> records it: a link that belongs to no
/// resource, which the app's root-metadata document lists.
/// </summary>
/// <param name="Rel">
/// The key the root-metadata document lists the link under: the attribute's <c>Rel</c> when it has one, otherwise the
/// operation's own descriptor name, before any app renamed it, as legacy Blueprint's default was.
/// </param>
/// <param name="UrlFormat">The link's route as declared, relative to the app's base path, placeholders included.</param>
public sealed record OperationRootLink(string Rel, string UrlFormat);
