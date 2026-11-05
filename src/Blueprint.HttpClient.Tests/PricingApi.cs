namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A typed client that reads and writes with a serializer context.
/// </summary>
/// <remarks>
/// The context is hand-written, so that it has no <c>[JsonSerializable]</c> attributes for the client to read its coverage from.
/// </remarks>
#pragma warning disable BPCL007
[BlueprintClient(SerializerContext = typeof(PricingSerializerContext))]
[BlueprintClientInclude(typeof(QuotePriceCommand))]
internal partial class PricingApi;
#pragma warning restore BPCL007
