namespace Blueprint.SourceGenerator.Modules.LinkGeneration;

/// <summary>
/// Records a root link discovered from a <c>[RootLink]</c> or a resource-less <c>[Link(null, ...)]</c> attribute, with the
/// rel the app's root-metadata document keys it by. Rendered into the descriptor as
/// <c>OperationRootLinksFeatureData</c>.
/// </summary>
/// <param name="Rel">
/// The attribute's <c>Rel</c> when it has one, otherwise the operation's descriptor name: legacy Blueprint built each link
/// with <c>linkAttribute.Rel ?? descriptor.Name</c> before an app's conventions renamed the descriptor (Signature365
/// <c>8b40ecc961</c>, <c>Blueprint/Configuration/OperationScanner.cs:294-303</c>).
/// </param>
/// <param name="Url">The route as declared.</param>
public sealed record RootLinkRegistration(string Rel, string Url);
