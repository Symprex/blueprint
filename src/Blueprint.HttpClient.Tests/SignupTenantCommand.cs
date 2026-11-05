using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Signs a tenant up, carrying an idempotency key that <see cref="RegionalOperationsApi"/> sends as a header. Declares a
/// <see langword="string"/> result so that it needs nothing of the sample contract.
/// </summary>
[RootLink("/tenants")]
[HttpPost]
[AllowAnonymous]
public partial class SignupTenantCommand : ICommand<string>
{
    /// <summary>
    /// Gets or sets the key that makes a repeated sign-up the same sign-up.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
