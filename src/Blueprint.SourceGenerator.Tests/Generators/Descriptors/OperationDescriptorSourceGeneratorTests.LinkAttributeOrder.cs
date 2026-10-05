using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

/// <summary>
/// Proves that a descriptor registers an operation's URLs, links and root links in the order its link attributes are
/// declared, whichever kind of attribute each is, as legacy's scanner did.
/// </summary>
/// <remarks>
/// Legacy's <c>OperationScanner</c> made one pass over <c>GetCustomAttributes&lt;LinkAttribute&gt;()</c> in declaration
/// order (Signature365 <c>8b40ecc961</c>, <c>Blueprint/Configuration/OperationScanner.cs:294-298</c>), which covers
/// <c>[SelfLink]</c> and <c>[RootLink]</c> as derived attributes. The order of an operation's URLs is what numbers the
/// operation ids of a document rendered as legacy's was (<c>AdminPortalGetAccountInjectionQuery</c> declares a
/// <c>[SelfLink]</c> and then a <c>[RootLink]</c>), so registering by kind numbered them the other way round.
/// </remarks>
public class OperationDescriptorSourceGeneratorTestsLinkAttributeOrderTests
{
    private const string Resource = """

                                    public class OrderedResource : ApiResource
                                    {
                                        public override string Object => "ordered";
                                        public string Id { get; set; } = null!;
                                    }
                                    """;

    [Fact]
    public void When_a_SelfLink_is_declared_before_a_RootLink_then_its_URL_is_registered_first()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(OrderedResource), "/self/{Id}")]
                     [RootLink("/root/{Id}")]
                     public partial class GetOrderedQuery : IOperation
                     {
                         public string Id { get; set; } = null!;
                     }
                     """ + Resource;

        var descriptor = DescriptorSource(source, "GetOrderedQuery");

        descriptor.Should().Contain("ApiOperationUrl(\"/self/{Id}\")").And.Contain("ApiOperationUrl(\"/root/{Id}\")");
        descriptor.IndexOf("ApiOperationUrl(\"/self/{Id}\")", StringComparison.Ordinal)
            .Should().BeLessThan(descriptor.IndexOf("ApiOperationUrl(\"/root/{Id}\")", StringComparison.Ordinal));
    }

    [Fact]
    public void When_a_RootLink_is_declared_before_a_SelfLink_then_its_URL_is_registered_first()
    {
        // language=csharp
        var source = """
                     [RootLink("/root/{Id}")]
                     [SelfLink(typeof(OrderedResource), "/self/{Id}")]
                     public partial class GetOrderedQuery : IOperation
                     {
                         public string Id { get; set; } = null!;
                     }
                     """ + Resource;

        var descriptor = DescriptorSource(source, "GetOrderedQuery");

        descriptor.IndexOf("ApiOperationUrl(\"/root/{Id}\")", StringComparison.Ordinal)
            .Should().BeLessThan(descriptor.IndexOf("ApiOperationUrl(\"/self/{Id}\")", StringComparison.Ordinal));
    }

    [Fact]
    public void When_a_Link_is_declared_between_a_SelfLink_and_a_RootLink_then_the_three_URLs_are_registered_in_that_order()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(OrderedResource), "/self/{Id}")]
                     [Link(typeof(OrderedResource), "/link/{Id}", Rel = "other")]
                     [RootLink("/root/{Id}")]
                     public partial class GetOrderedQuery : IOperation
                     {
                         public string Id { get; set; } = null!;
                     }
                     """ + Resource;

        var descriptor = DescriptorSource(source, "GetOrderedQuery");

        var self = descriptor.IndexOf("ApiOperationUrl(\"/self/{Id}\")", StringComparison.Ordinal);
        var link = descriptor.IndexOf("ApiOperationUrl(\"/link/{Id}\")", StringComparison.Ordinal);
        var root = descriptor.IndexOf("ApiOperationUrl(\"/root/{Id}\")", StringComparison.Ordinal);

        self.Should().BeGreaterThan(-1);
        link.Should().BeGreaterThan(self);
        root.Should().BeGreaterThan(link);
    }

    [Fact]
    public void When_a_Link_is_declared_after_a_SelfLink_and_a_RootLink_then_its_URL_is_registered_last()
    {
        // language=csharp
        var source = """
                     [SelfLink(typeof(OrderedResource), "/self/{Id}")]
                     [RootLink("/root/{Id}")]
                     [Link(typeof(OrderedResource), "/link/{Id}", Rel = "other")]
                     public partial class GetOrderedQuery : IOperation
                     {
                         public string Id { get; set; } = null!;
                     }
                     """ + Resource;

        var descriptor = DescriptorSource(source, "GetOrderedQuery");

        var self = descriptor.IndexOf("ApiOperationUrl(\"/self/{Id}\")", StringComparison.Ordinal);
        var root = descriptor.IndexOf("ApiOperationUrl(\"/root/{Id}\")", StringComparison.Ordinal);
        var link = descriptor.IndexOf("ApiOperationUrl(\"/link/{Id}\")", StringComparison.Ordinal);

        self.Should().BeGreaterThan(-1);
        root.Should().BeGreaterThan(self);
        link.Should().BeGreaterThan(root);
    }

    [Fact]
    public void When_a_Link_is_declared_before_a_SelfLink_then_its_resource_link_is_added_first()
    {
        // language=csharp
        var source = """
                     [Link(typeof(OrderedResource), "/link/{Id}", Rel = "other")]
                     [SelfLink(typeof(OrderedResource), "/self/{Id}")]
                     public partial class GetOrderedQuery : IOperation
                     {
                         public string Id { get; set; } = null!;
                     }
                     """ + Resource;

        var descriptor = DescriptorSource(source, "GetOrderedQuery");

        descriptor.IndexOf("\"other\"", StringComparison.Ordinal).Should().BeGreaterThan(-1)
            .And.BeLessThan(descriptor.IndexOf("\"self\"", StringComparison.Ordinal));
    }

    [Fact]
    public void When_a_null_resource_Link_is_declared_before_a_RootLink_then_its_root_link_is_recorded_first()
    {
        // language=csharp
        var source = """
                     [Link(null, "/first")]
                     [RootLink("/second", Rel = "second")]
                     public partial class ListOrderedQuery : IOperation {}
                     """;

        var descriptor = DescriptorSource(source, "ListOrderedQuery");

        descriptor.Should().Contain(
            "new global::Blueprint.Model.OperationRootLink(\"ListOrdered\", \"/first\"), "
            + "new global::Blueprint.Model.OperationRootLink(\"second\", \"/second\")");
    }

    private static string DescriptorSource(string source, string typeName)
    {
        return OperationDescriptorSourceGeneratorTests.Compile(source).Output
            .Single(tree => tree.FilePath.EndsWith(typeName + "_Descriptor.g.cs", StringComparison.Ordinal))
            .ToString();
    }
}
