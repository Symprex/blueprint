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
    ImmutableArray<OperationProblemExample> ProblemExamples = default)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationHttpResponseMetadata"/> class with no problem examples.
    /// </summary>
    /// <remarks>
    /// The 1.0.2 signature, kept so that a descriptor its generator emitted (<c>new OperationHttpResponseMetadata(status,
    /// hasBody, results, bodies)</c>) still binds: <see cref="ProblemExamples"/> was added in 1.0.3 as an optional parameter,
    /// which a recompile fills in but an existing binary cannot.
    /// </remarks>
    /// <param name="statusCode">The HTTP status code emitted for this response family.</param>
    /// <param name="hasResponseBody">Whether the current response family writes a response body.</param>
    /// <param name="resultTypeNames">The semantic result type names that collapse into this HTTP response.</param>
    /// <param name="bodyTypeNames">The body payload types currently known for this response family.</param>
    public OperationHttpResponseMetadata(int statusCode, bool hasResponseBody, ImmutableArray<string> resultTypeNames, ImmutableArray<string> bodyTypeNames)
        : this(statusCode, hasResponseBody, resultTypeNames, bodyTypeNames, default)
    {
    }

    /// <summary>Deconstructs the response into the four members it had in 1.0.2, kept for binaries built against that signature.</summary>
    /// <param name="statusCode">The HTTP status code emitted for this response family.</param>
    /// <param name="hasResponseBody">Whether the current response family writes a response body.</param>
    /// <param name="resultTypeNames">The semantic result type names that collapse into this HTTP response.</param>
    /// <param name="bodyTypeNames">The body payload types currently known for this response family.</param>
    public void Deconstruct(out int statusCode, out bool hasResponseBody, out ImmutableArray<string> resultTypeNames, out ImmutableArray<string> bodyTypeNames)
    {
        statusCode = this.StatusCode;
        hasResponseBody = this.HasResponseBody;
        resultTypeNames = this.ResultTypeNames;
        bodyTypeNames = this.BodyTypeNames;
    }
}

/// <summary>
/// The title and type of one <c>ApiExceptionFactory</c> an operation declares, which an OpenAPI document can show as a
/// named example of the problem the operation may answer with.
/// </summary>
/// <param name="Title">The factory's title.</param>
/// <param name="Type">The factory's problem type.</param>
public sealed record OperationProblemExample(string Title, string Type);
