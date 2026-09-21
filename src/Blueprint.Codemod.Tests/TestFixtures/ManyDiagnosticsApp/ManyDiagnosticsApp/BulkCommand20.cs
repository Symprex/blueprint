namespace Legacy.Blueprint;

/// <summary>Bulk BPMIG200 sites, file 20.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand20A
{
}

/// <summary>Second site in the same file.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand20B
{
}
