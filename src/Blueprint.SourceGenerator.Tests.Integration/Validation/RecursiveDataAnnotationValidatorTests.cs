using System.ComponentModel.DataAnnotations;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Tests.Integration.Validation;

/// <summary>
/// Tests for <see cref="RecursiveDataAnnotationValidator"/> ensuring nested objects and
/// lists have their validation attributes checked with correct path prefixes.
/// </summary>
public class RecursiveDataAnnotationValidatorTests
{
    [Fact]
    public void ValidateChildObject_null_produces_no_errors()
    {
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject<Address>("Address", null, context)
            .ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void ValidateChildObject_valid_object_produces_no_errors()
    {
        var address = new Address { Street = "123 Main St", City = "Springfield" };
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject("Address", address, context)
            .ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void ValidateChildObject_invalid_object_reports_errors_with_path()
    {
        var address = new Address { Street = null!, City = null! };
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject("Address", address, context)
            .ToList();

        Assert.NotEmpty(results);

        var memberNames = results.SelectMany(r => r.MemberNames).ToList();
        Assert.Contains("address.street", memberNames);
        Assert.Contains("address.city", memberNames);
    }

    [Fact]
    public void ValidateChildList_null_produces_no_errors()
    {
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildList<Address>("Items", null, context)
            .ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void ValidateChildList_valid_items_produce_no_errors()
    {
        var items = new List<Address>
        {
            new() { Street = "1 A St", City = "Alpha" },
            new() { Street = "2 B St", City = "Beta" },
        };
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildList("Items", items, context)
            .ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void ValidateChildList_invalid_items_report_indexed_paths()
    {
        var items = new List<Address>
        {
            new() { Street = "Valid", City = "Valid" },
            new() { Street = null!, City = null! },
        };
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildList("Items", items, context)
            .ToList();

        Assert.NotEmpty(results);

        var memberNames = results.SelectMany(r => r.MemberNames).ToList();
        Assert.Contains("items[1].street", memberNames);
        Assert.Contains("items[1].city", memberNames);
    }

    [Fact]
    public void ValidateChildObject_nested_complex_type_recurses()
    {
        var order = new Order { Customer = new Customer { Name = null! } };
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject("Order", order, context)
            .ToList();

        var memberNames = results.SelectMany(r => r.MemberNames).ToList();
        Assert.Contains("order.customer.name", memberNames);
    }

    [Fact]
    public void ValidateChildObject_respects_max_depth()
    {
        var order = new Order { Customer = new Customer { Name = null! } };
        var context = new ValidationContext(new object());

        // depth=1 means only validate Order's direct attributes, don't recurse into Customer
        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject("Order", order, context, maxDepth: 1)
            .ToList();

        var memberNames = results.SelectMany(r => r.MemberNames).ToList();

        // Should NOT contain the nested customer validation error
        Assert.DoesNotContain("order.customer.name", memberNames);
    }

    [Fact]
    public void ValidateChildObject_handles_circular_references()
    {
        var node = new TreeNode { Name = "root" };
        node.Parent = node; // circular reference
        var context = new ValidationContext(new object());

        // Should not infinite loop
        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject("Node", node, context)
            .ToList();

        // No exception thrown = pass
        Assert.NotNull(results);
    }

    [Fact]
    public void ValidateChildList_with_nested_list_property()
    {
        var container = new Container
        {
            Items =
            [
                new() { Street = null!, City = "Valid" },
            ],
        };
        var context = new ValidationContext(new object());

        var results = RecursiveDataAnnotationValidator
            .ValidateChildObject("Container", container, context)
            .ToList();

        var memberNames = results.SelectMany(r => r.MemberNames).ToList();
        Assert.Contains("container.items[0].street", memberNames);
    }

    private sealed class Address
    {
        [Required]
        public string Street { get; set; } = string.Empty;

        [Required]
        public string City { get; set; } = string.Empty;
    }

    private sealed class Customer
    {
        [Required]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Order
    {
        public Customer Customer { get; set; } = new();
    }

    private sealed class TreeNode
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public TreeNode? Parent { get; set; }
    }

    private sealed class Container
    {
        public List<Address> Items { get; set; } = [];
    }
}
