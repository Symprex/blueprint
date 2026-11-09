using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// An operation that gets the nested <c>Http</c> builder class also implements <c>IHttpRequestBuilder&lt;TSelf&gt;</c>, so
/// <c>BlueprintHttpClient.ForwardAsync</c> can reach its HTTP method and URL through a generic constraint, with no app or
/// descriptor. Compiled as a contract assembly that references <c>Blueprint.Runtime.Model</c> only, as inner services'
/// contracts do.
/// </summary>
public class HttpRequestBuilderInterfaceTests
{
    [Fact]
    public void When_an_operation_gets_the_Http_class_then_it_implements_the_request_builder_interface_of_itself()
    {
        var output = CompileContract(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Contract;

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }

                public int Page { get; set; }
            }

            [HttpDelete]
            [RootLink("/widgets/{Id}")]
            public partial class DeleteWidgetCommand : ICommand
            {
                public string Id { get; set; }
            }
            """);

        ImplementsBuilderOfItself(output, "Contract.GetWidgetQuery").Should().BeTrue();
        ImplementsBuilderOfItself(output, "Contract.DeleteWidgetCommand").Should().BeTrue();
    }

    [Fact]
    public void When_a_derived_operation_gets_its_own_Http_class_then_it_implements_the_interface_of_itself_as_well_as_its_bases()
    {
        var output = CompileContract(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Contract;

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }
            }

            [RootLink("/special-widgets/{Id}")]
            public partial class GetSpecialWidgetQuery : GetWidgetQuery;
            """);

        ImplementsBuilderOfItself(output, "Contract.GetSpecialWidgetQuery").Should().BeTrue();
    }

    [Fact]
    public void When_an_operation_cannot_take_the_Http_class_then_it_does_not_implement_the_interface()
    {
        // The interface's members are the Http class's builders, so an operation that keeps the inline delegates only has
        // nothing to implement it with, and a forward to it does not compile.
        var output = CompileContract(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Contract;

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }

                public string Http { get; set; }
            }
            """);

        ImplementsBuilderOfItself(output, "Contract.GetWidgetQuery").Should().BeFalse();
    }

    [Fact]
    public void When_an_operation_has_no_route_then_it_does_not_implement_the_interface()
    {
        var output = CompileContract(
            """
            using Blueprint.Model;

            namespace Contract;

            public partial class UnroutedQuery : IQuery<string>
            {
            }
            """);

        ImplementsBuilderOfItself(output, "Contract.UnroutedQuery").Should().BeFalse();
    }

    [Fact]
    public void When_an_operation_uses_a_standard_method_then_it_returns_the_shared_static_instance_without_parsing()
    {
        var output = CompileContract(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Contract;

            [HttpDelete]
            [RootLink("/widgets/{Id}")]
            public partial class DeleteWidgetCommand : ICommand
            {
                public string Id { get; set; }
            }
            """);

        // The method once came from HttpMethod.Parse on every call, which looks the name up each time.
        GeneratedSources(output).Should().NotContain(static source => source.Contains("HttpMethod.Parse"));
        GetHttpMethod(output, "Contract.DeleteWidgetCommand").Should().BeSameAs(HttpMethod.Delete);
    }

    [Fact]
    public void When_an_operation_uses_a_custom_method_then_every_call_returns_one_cached_instance()
    {
        var output = CompileContract(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace Contract;

            [HttpMethod("PURGE")]
            [RootLink("/widgets/{Id}")]
            public partial class PurgeWidgetCommand : ICommand
            {
                public string Id { get; set; }
            }
            """);

        var first = GetHttpMethod(output, "Contract.PurgeWidgetCommand");

        first.Method.Should().Be("PURGE");
        GetHttpMethod(output, "Contract.PurgeWidgetCommand").Should().BeSameAs(first, "a custom method is made once, not on every forward");
    }

    private static IEnumerable<string> GeneratedSources(Compilation output)
    {
        return output.SyntaxTrees.Skip(1).Select(static tree => tree.ToString());
    }

    /// <summary>
    /// Loads the compiled contract and calls the operation's <c>IHttpRequestBuilder&lt;TSelf&gt;.GetHttpMethod</c>, as
    /// <c>ForwardAsync</c> does through its constraint.
    /// </summary>
    private static HttpMethod GetHttpMethod(Compilation output, string operationMetadataName)
    {
        var assembly = Load(output);
        var operation = assembly.GetType(operationMetadataName, throwOnError: true)!;
        var builder = typeof(Blueprint.Model.IHttpRequestBuilder<>).MakeGenericType(operation);
        var map = operation.GetInterfaceMap(builder);
        var index = Array.FindIndex(map.InterfaceMethods, static method => method.Name == "GetHttpMethod");

        return (HttpMethod)map.TargetMethods[index].Invoke(null, null)!;
    }

    private static readonly Dictionary<Compilation, System.Reflection.Assembly> _loaded = new();

    private static System.Reflection.Assembly Load(Compilation output)
    {
        if (!_loaded.TryGetValue(output, out var assembly))
        {
            using var stream = new MemoryStream();
            var emitted = output.Emit(stream);
            emitted.Success.Should().BeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));

            assembly = new System.Runtime.Loader.AssemblyLoadContext(name: null, isCollectible: true).LoadFromStream(new MemoryStream(stream.ToArray()));
            _loaded[output] = assembly;
        }

        return assembly;
    }

    private static bool ImplementsBuilderOfItself(Compilation output, string operationMetadataName)
    {
        var operation = output.GetTypeByMetadataName(operationMetadataName);
        operation.Should().NotBeNull();

        var builder = output.GetTypeByMetadataName("Blueprint.Model.IHttpRequestBuilder`1");
        builder.Should().NotBeNull("the interface lives in Blueprint.Runtime.Model, which a contract references");

        var ofItself = builder!.Construct(operation!);

        return operation!.AllInterfaces.Contains(ofItself, SymbolEqualityComparer.Default);
    }

    private static Compilation CompileContract(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var runtimeAssemblyPath = typeof(Blueprint.OperationResult).Assembly.Location;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(assembly => !string.Equals(assembly.Location, runtimeAssemblyPath, StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "HttpRequestBuilderInterfaceContract",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new OperationDescriptorSourceGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        diagnostics.Should().BeEmpty();
        output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        return output;
    }
}
