using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.HttpClient;
using Blueprint.HttpClient.Readers;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Validation;
using Microsoft.Extensions.DependencyInjection;
using Probe;

// Exit code 0 when a transport given source-generated metadata is made, derives from a context and resolves a type with
// reflection turned off; 1 and the failure on stderr when it does not; 2 when reflection was not in fact off. Given the
// argument "registration", it is instead a generated client declaring a context, registered with AddBlueprintClient over
// default DI options, which must resolve twice and share one set of derived options. Given "validation", it is instead a
// transport whose options and context cover none of the transport's own types, sent a 422 with errors, which must fail as a
// ValidationException carrying them, keys as sent.
if (JsonSerializer.IsReflectionEnabledByDefault)
{
    Console.Error.WriteLine("System.Text.Json reflection is enabled, so this proves nothing.");

    return 2;
}

try
{
    if (args is ["registration"])
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IProbeApi, ProbeApi>(static (_, client) => client.BaseAddress = new Uri("https://localhost/"));

        using var provider = services.BuildServiceProvider();

        // The first resolves, together, are what freeze the shared options; none may fail for another's having done so.
        using var gate = new Barrier(16);
        var resolves = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            gate.SignalAndWait();

            return provider.GetRequiredService<IProbeApi>();
        })).ToArray();
        Task.WaitAll(resolves);

        var first = TransportOf(provider.GetRequiredService<IProbeApi>());
        var second = TransportOf(provider.GetRequiredService<IProbeApi>());

        return ReferenceEquals(first.JsonOptions, second.JsonOptions) && first.ResolveTypeInfo<ProbeBody>().Type == typeof(ProbeBody) ? 0 : 1;
    }

    if (args is ["validation"])
    {
        var validationClient = new BlueprintHttpClient(
            new HttpClient(new UnprocessableHandler()) { BaseAddress = new Uri("https://localhost/") },
            new JsonSerializerOptions { TypeInfoResolver = OptionsContext.Default }).WithSerializerContext(ProbeContext.Default);

        try
        {
            await validationClient.SendAsync(
                HttpMethod.Post,
                "/probe",
                new ProbeCommand(),
                ProbeContext.Default.ProbeCommand,
                new JsonResponseReader<ProbeBody>(),
                nameof(ProbeCommand),
                options: null);
        }
        catch (ValidationException e)
        {
            return e.Errors.Count == 2
                   && e.Errors["Name"].SequenceEqual(["Name is required.", "Name is too short."])
                   && e.Errors["lower.Field"].SequenceEqual(["Bad."])
                ? 0
                : 1;
        }

        return 1;
    }

    var options = new JsonSerializerOptions { TypeInfoResolver = OptionsContext.Default };
    var client = new BlueprintHttpClient(new HttpClient(), options).WithSerializerContext(ProbeContext.Default);

    return client.ResolveTypeInfo<ProbeBody>().Type == typeof(ProbeBody) ? 0 : 1;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);

    return 1;
}

static BlueprintHttpClient TransportOf(object client)
{
    return (BlueprintHttpClient)client.GetType().GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
}

internal sealed class UnprocessableHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                """{"title":"Validation Failed","status":422,"errors":{"Name":["Name is required.","Name is too short."],"lower.Field":["Bad."]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        });
    }
}

public sealed class ProbeBody
{
    public string? Name { get; set; }
}

[JsonSerializable(typeof(ProbeBody))]
[JsonSerializable(typeof(ProbeCommand))]
internal sealed partial class ProbeContext : JsonSerializerContext;

[JsonSerializable(typeof(string))]
internal sealed partial class OptionsContext : JsonSerializerContext;

namespace Probe
{
    [RootLink("/probe")]
    [HttpPost]
    [AllowAnonymous]
    public partial class ProbeCommand : ICommand<ProbeBody>
    {
        public string? Name { get; set; }
    }

    [BlueprintClient(SerializerContext = typeof(ProbeContext))]
    [BlueprintClientInclude(typeof(ProbeCommand))]
    internal partial class ProbeApi;
}
