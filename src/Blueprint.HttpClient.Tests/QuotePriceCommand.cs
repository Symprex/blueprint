using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Asks for a price, declaring a body whose <see cref="Amount"/> a serializer context names differently to the transport's
/// options, so that a request shows which of the two it was written with. Called through <see cref="PricingApi"/>.
/// </summary>
[RootLink("/pricing/quotes")]
[HttpPost]
[AllowAnonymous]
public partial class QuotePriceCommand : ICommand<PriceQuote>
{
    /// <summary>
    /// Gets or sets the amount to price, a <see cref="decimal"/> that the transport's options write with a converter.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Gets or sets the name of the customer the price is quoted for.
    /// </summary>
    public string? CustomerName { get; set; }
}
