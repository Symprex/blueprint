using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Captures the distinct body payload types that later serializer-context validation and OpenAPI schema slices must
/// account for. Keeping this as additive descriptor feature data lets later work inspect serializer requirements without
/// rediscovering body-bearing HTTP responses from runtime executors.
/// </summary>
/// <param name="BodyTypeNames">The distinct body payload CLR type names currently known for this operation.</param>
public sealed record OperationSerializerContextCoverageFeatureData(ImmutableArray<string> BodyTypeNames);
