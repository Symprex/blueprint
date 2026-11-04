using System.ComponentModel.DataAnnotations;

namespace Blueprint.Validation;

/// <summary>
/// A failure <see cref="RecursiveDataAnnotationValidator"/> found on a nested or indexed member: its member name is the
/// camelCased path it has always been, and <see cref="MemberPath"/> is the same path of the members' own names.
/// </summary>
/// <remarks>
/// The HTTP host names a 422's key with the app's naming policy from <see cref="MemberPath"/>, as the body's properties are
/// named from the members' own names; with no policy it writes the member name, unchanged.
/// </remarks>
internal sealed class MemberPathValidationResult : ValidationResult
{
    /// <summary>
    /// Initialises a new instance of the <see cref="MemberPathValidationResult"/> class.
    /// </summary>
    /// <param name="errorMessage">The failure's message.</param>
    /// <param name="memberName">The camelCased member path, the failure's only member name.</param>
    /// <param name="memberPath">The member path of the members' own names.</param>
    public MemberPathValidationResult(string? errorMessage, string memberName, string memberPath)
        : base(errorMessage, [memberName])
    {
        this.MemberPath = memberPath;
    }

    /// <summary>
    /// Gets the member path of the members' own names, such as <c>Items[0].UnitPrice</c>.
    /// </summary>
    public string MemberPath { get; }
}
