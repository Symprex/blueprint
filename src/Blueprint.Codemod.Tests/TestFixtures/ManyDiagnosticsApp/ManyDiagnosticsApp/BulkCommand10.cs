namespace Legacy.Blueprint;

/// <summary>Bulk BPMIG200 sites, file 10.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand10A
{
}

/// <summary>Second site in the same file.</summary>
[FromResource(typeof(AccountLookupQuery), "{Auth:AccountId}")]
public sealed class BulkCommand10B
{
}
