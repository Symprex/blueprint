namespace Blueprint.Runtime.Exec;

/// <summary>
/// What <see cref="PipelineHelpers.ReadFormAsync"/> read from a request: its form, or why it could not, or neither when the
/// request had no body to read.
/// </summary>
/// <param name="Form">The request's form, or <see langword="null"/> when it had no body or could not be read.</param>
/// <param name="Failure">
/// The result the generated pipeline returns at once, a 415 or a 400, or <see langword="null"/> when the form was read or
/// there was none.
/// </param>
public readonly record struct FormReadResult(IFormCollection? Form, OperationResult? Failure);
