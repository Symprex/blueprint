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

    [Fact]
    public void When_user_attribute_named_HttpGetAttribute_declares_PUT_then_collides_on_PUT_as_the_descriptor_does()
    {
        // This used to read a user attribute's short name, so a TestNamespace.Custom.HttpGetAttribute declaring PUT
        // was checked as GET while its descriptor published PUT.
        var source = """
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [Custom.HttpGet("PUT")]
                     public partial class ReplaceItemCommand : ICommand
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpPut]
                     public partial class ReplaceItemDuplicateCommand : ICommand
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
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

                     [BlueprintAppScan]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     }

                     namespace TestNamespace.Custom
                     {
                     [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                     public sealed class HttpGetAttribute : Blueprint.Model.Attributes.HttpMethodAttribute
                     {
                         public HttpGetAttribute(string httpMethod) : base(httpMethod) { }
                     }
                     """;

        var result = Compile(source);

        result.Diagnostics
            .Where(static d => d.Id == "BPLG007")
            .Select(static d => d.GetMessage())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("ReplaceItemCommand")
            .And.Contain("ReplaceItemDuplicateCommand")
            .And.EndWith("HTTP method PUT. Both endpoints will be registered, causing an AmbiguousMatchException at runtime when a request matches this route.");
    }

    [Theory]
    [InlineData("\" \"")]
    [InlineData("null")]
    public void When_operations_declare_an_empty_HTTP_method_then_no_route_collision_is_reported(string httpMethodArgument)
    {
        // This used to check an empty method as "" and a null one as the GET fallback, while the descriptor rejects
        // both with BPOA002 and publishes no method at all.
        var source = $$"""
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpMethod({{httpMethodArgument}})]
                     public partial class GetItemQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpMethod({{httpMethodArgument}})]
                     public partial class GetItemDuplicateQuery : IQuery<FooResource>
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpGet]
                     public partial class GetItemOtherQuery : IQuery<FooResource>
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
            .BeEmpty("an operation with an empty HTTP method has no method to collide on");
    }

    [Fact]
    public void When_operations_declare_several_methods_then_collisions_are_reported_in_ordinal_method_order()
    {
        // This used to report in declaration order, while the descriptor orders its methods ordinally.
        var source = """
                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpPost]
                     [HttpGet]
                     public partial class GetOrCreateItemCommand : ICommand
                     {
                         public string Id { get; set; }
                     }

                     [SelfLink(typeof(FooResource), "/items/{Id}")]
                     [HttpPost]
                     [HttpGet]
                     public partial class GetOrCreateItemDuplicateCommand : ICommand
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
            .Select(static d => d.GetMessage())
            .Should()
            .SatisfyRespectively(
                first => first.Should().Contain("HTTP method GET."),
                second => second.Should().Contain("HTTP method POST."));
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
