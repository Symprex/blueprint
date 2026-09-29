using System.Collections.Immutable;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries descriptor-planning metadata for one distinct HTTP response so later render steps can emit runtime feature
/// data without taking a compile-time dependency on Blueprint.Runtime.Model.
/// </summary>
/// <param name="StatusCode">The HTTP status code emitted by this response family.</param>
/// <param name="HasResponseBody">Whether the response family writes a body.</param>
/// <param name="ResultTypeNames">The semantic result type names contributing to this response.</param>
/// <param name="BodyTypeNames">The compile-time body payload types currently known for this response.</param>
/// <param name="ProblemFactoryReferences">
/// The fully qualified <c>ApiExceptionFactory</c> fields contributing to this response
/// (<see cref="ApiExceptionFactoryRegistration.FieldReference"/>). Default or empty when there are none.
/// </param>
internal sealed record HttpResponseMetadataRegistration(
    int StatusCode,
    bool HasResponseBody,
    ImmutableArray<string> ResultTypeNames,
    ImmutableArray<string> BodyTypeNames,
    ImmutableArray<string> ProblemFactoryReferences = default);
