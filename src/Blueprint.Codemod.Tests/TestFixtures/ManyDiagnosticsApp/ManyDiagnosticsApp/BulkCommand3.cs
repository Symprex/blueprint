namespace Legacy.Blueprint;

/// <summary>Bulk BPMIG200 sites, file 3.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand3A
{
}

/// <summary>Second site in the same file.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand3B
{
}
