namespace Legacy.Blueprint;

/// <summary>Bulk BPMIG200 sites, file 2.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand2A
{
}

/// <summary>Second site in the same file.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand2B
{
}
