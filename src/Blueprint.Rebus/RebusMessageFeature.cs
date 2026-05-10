using Blueprint.Model;

namespace Blueprint.Rebus;

/// <summary>
/// Feature data attached to <see cref="ApiOperationDescriptor"/> instances that represent
/// Rebus messages rather than HTTP-routed operations. Presence of this feature indicates
/// the operation was registered via <see cref="BlueprintRebusConfiguration.Subscribe{T}"/>.
/// </summary>
public sealed class RebusMessageFeature;
