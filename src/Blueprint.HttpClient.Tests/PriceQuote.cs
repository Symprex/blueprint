namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The result of <see cref="QuotePriceCommand"/>.
/// </summary>
public sealed class PriceQuote
{
    /// <summary>
    /// Gets or sets the quoted total, which a serializer context names differently to the transport's options.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Gets or sets the name of the customer the price was quoted for.
    /// </summary>
    public string? QuotedFor { get; set; }
}
