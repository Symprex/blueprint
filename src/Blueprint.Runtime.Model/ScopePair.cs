namespace Blueprint.Model;

/// <summary>
/// A noun+verb pair that represents a resource permission scope requirement.
/// For example, <c>new ScopePair("account", "read")</c> means the caller must hold
/// permission to read accounts.
/// </summary>
/// <param name="Noun">The resource noun (e.g. "account", "tenant", "user").</param>
/// <param name="Verb">The action verb (e.g. "read", "write", "manage", "delete").</param>
public sealed record ScopePair(string Noun, string Verb);
