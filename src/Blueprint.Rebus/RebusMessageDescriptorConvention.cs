using Blueprint.Model;

namespace Blueprint.Rebus;

/// <summary>
/// Marks the descriptor of every message a consumer subscribes to as a Rebus message — one that allows anonymous access
/// and is not exposed — as the app is made, as legacy Signature365's <c>RebusScannerConvention</c> marked every subscribed
/// message at start-up (Signature365 <c>8b40ecc961</c>,
/// <c>Symprex.Signature365.BlueprintRebus/RebusScannerConvention.cs:30-44</c>).
/// </summary>
/// <remarks>
/// A message is executed with no user, so were it not marked before it first executes it would be refused as an anonymous
/// caller; and executed in process with a user, as a test fixture may, it would be asked of authorisers that legacy never
/// asked. <see cref="BlueprintApiActivator"/> still marks a message it delivers, for an app made without
/// <c>AddBlueprintApi</c>.
/// </remarks>
internal sealed class RebusMessageDescriptorConvention : IOperationDescriptorConvention
{
    private readonly BlueprintRebusConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="RebusMessageDescriptorConvention"/> class.
    /// </summary>
    /// <param name="configuration">The consumer's configuration, whose subscribed messages are marked.</param>
    public RebusMessageDescriptorConvention(BlueprintRebusConfiguration configuration)
    {
        this._configuration = configuration;
    }

    /// <inheritdoc />
    public void Apply(ApiOperationDescriptor descriptor)
    {
        if (this._configuration.SubscribedMessages.Contains(descriptor.OperationType))
        {
            MarkAsMessage(descriptor);
        }
    }

    /// <summary>
    /// Marks <paramref name="descriptor"/> as a Rebus message, once: tags it with <see cref="RebusMessageFeature"/>, hides it
    /// from HTTP exposure and allows it anonymous access. Shared with <see cref="BlueprintApiActivator"/> so that a message
    /// is marked the same whichever marks it first.
    /// </summary>
    /// <param name="descriptor">The message's descriptor.</param>
    internal static void MarkAsMessage(ApiOperationDescriptor descriptor)
    {
        if (descriptor.TryGetFeatureData<RebusMessageFeature>(out _))
        {
            return;
        }

        descriptor.SetFeatureData(new RebusMessageFeature());
        descriptor.IsExposed = false;
        descriptor.AnonymousAccessAllowed = true;
    }
}
