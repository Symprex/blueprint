using System.Diagnostics;

namespace Blueprint.HttpClient;

/// <summary>
/// Opts a <c>[BlueprintAppScan]</c> app into the generated per-operation extension methods on
/// <see cref="BlueprintHttpClient{TApp}"/> (for example <c>client.GetTemplatesAsync(...)</c>). An app without this
/// attribute gets no such extensions; the typed service clients declared with <see cref="BlueprintClientAttribute"/>
/// are the supported way to call it.
/// </summary>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BlueprintHttpClientExtensionsAttribute : Attribute;
