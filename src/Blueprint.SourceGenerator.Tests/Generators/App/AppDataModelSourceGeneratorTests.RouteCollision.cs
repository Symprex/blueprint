using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTests_RouteCollision
{

    [Fact]
    public void When_two_operations_share_same_route_and_method_then_reports_BPLG007()
    {
        var source = """
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpGet]
                     public partial class GetItemByIdQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpGet]
                     public partial class GetItemByIdDuplicateQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     public class FooResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BPLG007")
            .Should()
            .ContainSingle()
            .Which.GetMessage()
            .Should()
            .Contain("GetItemByIdQuery")
            .And.Contain("GetItemByIdDuplicateQuery")
            .And.Contain("/items/{Id}")
            .And.Contain("GET");
    }

    [Fact]
    public void When_same_route_but_different_methods_then_no_warning()
    {
        var source = """
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpGet]
                     public partial class GetItemQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpPost]
                     public partial class UpdateItemCommand : ICommand
                     {
                         public string Id { get; set; }
                     }

                     public class FooResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BPLG007")
            .Should()
            .BeEmpty("different HTTP methods on same route should not collide");
    }

    [Fact]
    public void When_same_operation_has_multiple_links_same_route_then_no_warning()
    {
        var source = """
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [Link(typeof(BarResource), "/items/{Id}", Rel = "related")]
                     [HttpGet]
                     public partial class GetItemQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     public class FooResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     public class BarResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BPLG007")
            .Should()
            .BeEmpty("multiple links on the same operation is valid HATEOAS, not a collision");
    }

    [Fact]
    public void When_two_commands_share_same_route_with_default_POST_then_reports_BPLG007()
    {
        var source = """
                     [SelfLink(typeof(FooResource), "/items")]
                     public partial class CreateItemCommand : ICommand
                     {
                     }

                     [SelfLink(typeof(FooResource), "/items")]
                     public partial class CreateItemDuplicateCommand : ICommand
                     {
                     }

                     public class FooResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BPLG007")
            .Should()
            .ContainSingle("two commands defaulting to POST on the same route should collide");
    }

    [Fact]
    public void When_different_routes_then_no_warning()
    {
        var source = """
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpGet]
                     public partial class GetItemQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(BarResource), "/widgets/{Id}")]
                     [HttpGet]
                     public partial class GetWidgetQuery : IQuery<BarResource>
                     {
                         public string Id { get; set; }
                     }

                     public class FooResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     public class BarResource : ILinkableResource
                     {
                         public IDictionary<string, Link> Links { get; set; }
                         public void AddLink(string rel, Link link) { }
                     }

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BPLG007")
            .Should()
            .BeEmpty("different routes should never collide");
    }

    private static GenerationResult Compile(string source)
    {
        var fullSource = """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Collections.Generic;

            namespace Blueprint.Model.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class SelfLinkAttribute : System.Attribute
                {
                    public SelfLinkAttribute(System.Type resourceType, string url) { ResourceType = resourceType; Url = url; }
                    public System.Type ResourceType { get; }
                    public string Url { get; }
                }

                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class LinkAttribute : System.Attribute
                {
                    public LinkAttribute(System.Type resourceType, string url) { ResourceType = resourceType; Url = url; }
                    public System.Type ResourceType { get; }
                    public string Url { get; }
                    public string Rel { get; set; }
                }
            }

            namespace TestNamespace
            {
            """ + "\n" + source + "\n}\n";

        return TestHelpers.Compile<AppDataModelSourceGenerator>(fullSource);
    }
}
