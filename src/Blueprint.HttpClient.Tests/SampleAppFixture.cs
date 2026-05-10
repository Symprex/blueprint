using Blueprint.SourceGenerator.Sample;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Shared test fixture that creates a <see cref="SampleBlueprintApp"/> once so all HTTP client
/// tests can reuse the operation descriptor registry without re-instantiation per test.
/// The generated partial constructor populates all descriptors at <c>new()</c> time.
/// </summary>
public sealed class SampleAppFixture
{
    /// <summary>
    /// The resolved <see cref="SampleBlueprintApp"/> with fully registered descriptors.
    /// </summary>
    public SampleBlueprintApp App { get; } = new();

    /// <summary>
    /// Creates a <see cref="BlueprintHttpClient{TApp}"/> backed by the given <see cref="FakeHttpHandler"/>
    /// so that tests can intercept and verify the outgoing HTTP request shape.
    /// </summary>
    /// <param name="handler">The fake handler that intercepts requests.</param>
    /// <returns>A configured client using the shared app descriptor registry.</returns>
    public BlueprintHttpClient<SampleBlueprintApp> CreateClient(FakeHttpHandler handler)
    {
        var httpClient = new System.Net.Http.HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost"),
        };

        return new BlueprintHttpClient<SampleBlueprintApp>(httpClient, this.App);
    }
}
