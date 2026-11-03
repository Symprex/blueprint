using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Probe;

// Exit code 0 when a transport given source-generated metadata is made, derives from a context and resolves a type with
// reflection turned off; 1 and the failure on stderr when it does not; 2 when reflection was not in fact off. Given the
// argument "registration", it is instead a generated client declaring a context, registered with AddBlueprintClient over
// default DI options, which must resolve twice and share one set of derived options.
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
