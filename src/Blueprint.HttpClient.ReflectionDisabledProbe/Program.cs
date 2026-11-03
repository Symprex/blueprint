using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.HttpClient;

// Exit code 0 when a transport given source-generated metadata is made, derives from a context and resolves a type with
// reflection turned off; 1 and the failure on stderr when it does not; 2 when reflection was not in fact off.
if (JsonSerializer.IsReflectionEnabledByDefault)
{
    Console.Error.WriteLine("System.Text.Json reflection is enabled, so this proves nothing.");

    return 2;
}

try
{
    var options = new JsonSerializerOptions { TypeInfoResolver = OptionsContext.Default };
    var client = new BlueprintHttpClient(new HttpClient(), options).WithSerializerContext(ProbeContext.Default);

    return client.ResolveTypeInfo<ProbeBody>().Type == typeof(ProbeBody) ? 0 : 1;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);

    return 1;
}

internal sealed class ProbeBody
{
    public string? Name { get; set; }
}

[JsonSerializable(typeof(ProbeBody))]
internal sealed partial class ProbeContext : JsonSerializerContext;

[JsonSerializable(typeof(string))]
internal sealed partial class OptionsContext : JsonSerializerContext;
