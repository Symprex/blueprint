using Blueprint.Model;

namespace Blueprint;

/// <summary>
/// A convention applied to every operation descriptor of a Blueprint app as the app is made, before any of its operations
/// can execute, for an integration that decides something about an operation from its own configuration rather than from
/// the operation's attributes, as <c>Blueprint.Rebus</c> marks a subscribed message as allowing anonymous access.
/// </summary>
/// <remarks>
/// <para>
/// It is legacy Blueprint's <c>IOperationScannerConvention</c> (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint/Configuration/IOperationScannerConvention.cs</c>), which every descriptor was given as the API's operations
/// were scanned at start-up, at the one point the source-generated app has left: its creation.
/// </para>
/// <para>
/// Register one as a singleton <see cref="IOperationDescriptorConvention"/>. <c>AddBlueprintApi</c> applies every one
/// registered, in registration order, the first time the app is resolved, whether it was registered before or after the
/// app. It must not depend on the app, which is still being made.
/// </para>
/// </remarks>
public interface IOperationDescriptorConvention
{
    /// <summary>
    /// Applies the convention to an operation's descriptor, which it may change.
    /// </summary>
    /// <param name="descriptor">The descriptor of one of the app's operations.</param>
    void Apply(ApiOperationDescriptor descriptor);
}
