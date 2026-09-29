using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Describes one HTTP response shape projected from descriptor-level semantic result and failure-outcome metadata.
/// </summary>
/// <param name="StatusCode">The HTTP status code emitted for this response family.</param>
/// <param name="HasResponseBody">Whether the current response family writes a response body.</param>
/// <param name="ResultTypeNames">The semantic result type names that collapse into this HTTP response.</param>
/// <param name="BodyTypeNames">The body payload types currently known for this response family.</param>
/// <param name="ProblemExamples">
/// The title and type of each <c>ApiExceptionFactory</c> field on the operation whose status is this response's, read
/// from the fields themselves when the descriptor is built. Empty when the operation declares none at this status.
/// </param>
public sealed record OperationHttpResponseMetadata(
    int StatusCode,
    bool HasResponseBody,
    ImmutableArray<string> ResultTypeNames,
    ImmutableArray<string> BodyTypeNames,
    ImmutableArray<OperationProblemExample> ProblemExamples = default);

/// <summary>
/// The title and type of one <c>ApiExceptionFactory</c> an operation declares, which an OpenAPI document can show as a
/// named example of the problem the operation may answer with.
/// </summary>
/// <param name="Title">The factory's title.</param>
/// <param name="Type">The factory's problem type.</param>
public sealed record OperationProblemExample(string Title, string Type);
