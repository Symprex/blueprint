using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the operation's root links: its <c>[RootLink]</c> and its
/// resource-less <c>[Link(null, ...)]</c> declarations, each with the rel the app's root-metadata document keys it by.
/// </summary>
/// <remarks>
/// Root links are kept out of <see cref="ApiOperationDescriptor.Links"/>, which holds resource links only and is indexed by
/// resource type. The rel is resolved when the descriptor is generated, because legacy Blueprint defaulted it to the
/// descriptor's own name before an app's naming convention renamed the descriptor, and that name is gone once an app's
/// <c>[BlueprintAppOperationNames]</c> removal has run.
/// </remarks>
/// <param name="Links">The operation's root links, in declaration order.</param>
public sealed record OperationRootLinksFeatureData(ImmutableArray<OperationRootLink> Links);
