using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Describes the authorization scope requirements for an operation, determined at compile time
/// from <c>[Resource("noun", "verb")]</c> attributes. Multiple scope pairs on a single operation
/// use any-of semantics: the caller must satisfy at least one declared pair.
/// </summary>
/// <param name="ScopePairs">
/// The set of noun+verb scope pairs declared on the operation. Multiple entries
/// indicate any-of semantics: satisfying any one pair grants access.
/// </param>
public sealed record OperationAuthorizationFeatureData(ImmutableArray<ScopePair> ScopePairs);
