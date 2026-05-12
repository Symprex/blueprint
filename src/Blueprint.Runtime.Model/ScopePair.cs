namespace Blueprint.Model;

/// <summary>
/// A noun+verb pair that represents a resource permission scope requirement.
/// For example, <c>new ScopePair("account", "read")</c> means the caller must hold
/// permission to read accounts.
/// </summary>
/// <param name="Noun">The resource noun (e.g. "account", "tenant", "user").</param>
/// <param name="Verb">The action verb (e.g. "read", "write", "manage", "delete").</param>
/// <param name="ResourceKeyTemplate">
/// An optional hierarchical resource key template (e.g. <c>"/Tenants/{Auth:AccountId}/Campaigns/{Id}"</c>)
/// for instance-level permission checks. Tokens of the form <c>{PropertyName}</c> are bound from the
/// operation instance, and <c>{Auth:ClaimType}</c> tokens from the authenticated principal's claims.
/// When <see langword="null"/>, only the noun+verb scope pair is checked.
/// </param>
public readonly record struct ScopePair(string Noun, string Verb, string? ResourceKeyTemplate = null);
