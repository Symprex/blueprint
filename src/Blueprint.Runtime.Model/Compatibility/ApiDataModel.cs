using System.ComponentModel;

namespace Blueprint.Model;

/// <summary>
/// Legacy compatibility alias for <see cref="BlueprintApp"/>. Consumer code that references the old
/// <c>ApiDataModel</c> type from legacy Blueprint can use this alias during migration to the
/// source-generated framework without immediate namespace or type-name changes.
/// </summary>
/// <remarks>
/// New code should use <see cref="BlueprintApp"/> directly. This alias exists solely to reduce
/// migration friction for Signature365 and other legacy Blueprint consumers.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use BlueprintApp instead. ApiDataModel is a legacy compatibility alias.", error: false)]
public class ApiDataModel : BlueprintApp;
