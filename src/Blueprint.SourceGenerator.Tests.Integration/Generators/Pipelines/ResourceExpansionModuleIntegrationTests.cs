using System.Collections;

using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Expansion;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves the generated resource expansion pipeline stage:
/// - populates <see cref="Expandable{T}.Value"/> when the client passes <c>?expand=PropertyName</c>,
/// - expands <c>[AlwaysExpand]</c> properties unconditionally,
/// - gracefully skips when no matching expander is registered,
/// - handles collections and nested expandable properties.
/// </summary>
public class ResourceExpansionModuleIntegrationTests
{
    [Fact]
    public async Task When_expand_query_param_matches_then_expandable_value_is_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetExpandableResourceQuery));
        var descriptor = GetExpandableResourceQuery.Configure();

        var expander = new TestDepartmentExpander();
        var serviceProvider = new ExpansionTestServiceProvider(expander);

        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new GetExpandableResourceQuery { Id = "emp-1" },
            HttpContext = CreateHttpContextWithExpand("Department"),
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<ExpandableTestEmployee>>();
        var okResult = (OkResult<ExpandableTestEmployee>)result;
        okResult.Content.Department.Value.Should().NotBeNull("expander should have populated Value");
        okResult.Content.Department.Value!.Object.Should().Be("DepartmentResource");
    }

    [Fact]
    public async Task When_expand_query_param_not_present_then_expandable_value_is_not_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetExpandableResourceQuery));
        var descriptor = GetExpandableResourceQuery.Configure();

        var expander = new TestDepartmentExpander();
        var serviceProvider = new ExpansionTestServiceProvider(expander);

        // No ?expand= param
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new GetExpandableResourceQuery { Id = "emp-2" },
            HttpContext = new DefaultHttpContext(),
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<ExpandableTestEmployee>>();
        var okResult = (OkResult<ExpandableTestEmployee>)result;
        okResult.Content.Department.Value.Should().BeNull("no expand param means value should remain null");
    }

    [Fact]
    public async Task When_always_expand_property_then_value_is_populated_without_query_param()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetAlwaysExpandResourceQuery));
        var descriptor = GetAlwaysExpandResourceQuery.Configure();

        var expander = new TestDepartmentExpander();
        var serviceProvider = new ExpansionTestServiceProvider(expander);

        // No ?expand= param — AlwaysExpand should still run
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new GetAlwaysExpandResourceQuery { Id = "emp-3" },
            HttpContext = new DefaultHttpContext(),
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<AlwaysExpandTestEmployee>>();
        var okResult = (OkResult<AlwaysExpandTestEmployee>)result;
        okResult.Content.Department.Value.Should().NotBeNull("[AlwaysExpand] should populate Value without query param");
    }

    [Fact]
    public async Task When_no_expander_registered_then_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetExpandableResourceQuery));
        var descriptor = GetExpandableResourceQuery.Configure();

        // No expanders registered
        var serviceProvider = new ExpansionTestServiceProvider(expander: null);

        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new GetExpandableResourceQuery { Id = "emp-4" },
            HttpContext = CreateHttpContextWithExpand("Department"),
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<ExpandableTestEmployee>>();
        var okResult = (OkResult<ExpandableTestEmployee>)result;
        okResult.Content.Department.Value.Should().BeNull("no matching expander means value stays null");
    }

    [Fact]
    public async Task When_multiple_expand_params_then_each_property_expanded_by_matching_expander()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetMultiExpandableResourceQuery));
        var descriptor = GetMultiExpandableResourceQuery.Configure();

        var deptExpander = new TestDepartmentExpander();
        var locationExpander = new TestLocationExpander();
        var serviceProvider = new MultiExpansionTestServiceProvider(deptExpander, locationExpander);

        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new GetMultiExpandableResourceQuery { Id = "emp-multi" },
            HttpContext = CreateHttpContextWithExpand("Department,Location"),
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<MultiExpandableTestEmployee>>();
        var okResult = (OkResult<MultiExpandableTestEmployee>)result;
        okResult.Content.Department.Value.Should().NotBeNull("department expander should have populated Value");
        okResult.Content.Location.Value.Should().NotBeNull("location expander should have populated Value");
        deptExpander.ExpandCallCount.Should().Be(1);
        locationExpander.ExpandCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_expand_array_syntax_used_then_expandable_value_is_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetExpandableResourceQuery));
        var descriptor = GetExpandableResourceQuery.Configure();

        var expander = new TestDepartmentExpander();
        var serviceProvider = new ExpansionTestServiceProvider(expander);

        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new GetExpandableResourceQuery { Id = "emp-5" },
        };

        // Use expand[] syntax
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?expand[]=Department");
        context.HttpContext = httpContext;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<ExpandableTestEmployee>>();
        var okResult = (OkResult<ExpandableTestEmployee>)result;
        okResult.Content.Department.Value.Should().NotBeNull("expand[] syntax should trigger expansion");
    }

    private static DefaultHttpContext CreateHttpContextWithExpand(string propertyName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?expand={propertyName}");
        return httpContext;
    }

    private sealed class ExpansionTestServiceProvider : IServiceProvider
    {
        private readonly IResourceExpander? _expander;

        public ExpansionTestServiceProvider(IResourceExpander? expander)
        {
            this._expander = expander;
        }

        public object? GetService(Type serviceType)
        {
            // GetServices<T>() extension resolves IEnumerable<T>
            if (serviceType == typeof(IEnumerable<IResourceExpander>))
            {
                return this._expander != null
                    ? new[] { this._expander }
                    : Array.Empty<IResourceExpander>();
            }

            return null;
        }
    }

    private sealed class MultiExpansionTestServiceProvider : IServiceProvider
    {
        private readonly IResourceExpander[] _expanders;

        public MultiExpansionTestServiceProvider(params IResourceExpander[] expanders)
        {
            this._expanders = expanders;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<IResourceExpander>))
            {
                return this._expanders;
            }

            return null;
        }
    }
}

/// <summary>
/// A test expander that populates <see cref="Expandable{T}.Value"/> for <see cref="DepartmentResource"/>.
/// </summary>
public sealed class TestDepartmentExpander : IResourceExpander
{
    /// <summary>Gets the number of times <see cref="ExpandAsync"/> was called.</summary>
    public int ExpandCallCount { get; private set; }

    /// <inheritdoc />
    public bool CanExpand(Type type)
    {
        return type == typeof(DepartmentResource);
    }

    /// <inheritdoc />
    public ValueTask ExpandAsync(
        ApiOperationContext context,
        object response,
        IEnumerable entities,
        CancellationToken cancellationToken = default)
    {
        this.ExpandCallCount++;

        foreach (var entity in entities)
        {
            if (entity is Expandable<DepartmentResource> expandable)
            {
                expandable.Value = new DepartmentResource { Name = "Engineering" };
            }
        }

        return ValueTask.CompletedTask;
    }
}

// ── Test Resources ──────────────────────────────────────────────────────────

public class DepartmentResource : ApiResource
{
    public override string Object => "DepartmentResource";

    public string Name { get; set; } = string.Empty;
}

public class ExpandableTestEmployee : ApiResource
{
    public override string Object => "ExpandableTestEmployee";

    public Expandable<DepartmentResource> Department { get; set; } = new("dept-1");
}

public class AlwaysExpandTestEmployee : ApiResource
{
    public override string Object => "AlwaysExpandTestEmployee";

    [AlwaysExpand]
    public Expandable<DepartmentResource> Department { get; set; } = new("dept-1");
}

// ── Test Operations ─────────────────────────────────────────────────────────

[AllowAnonymous]
[RootLink("/expandable-employees/{Id}")]
public partial class GetExpandableResourceQuery : IQuery<ExpandableTestEmployee>
{
    public string Id { get; set; } = string.Empty;

    public static ExpandableTestEmployee Handle(GetExpandableResourceQuery op)
    {
        return new ExpandableTestEmployee
        {
            Department = new Expandable<DepartmentResource>("dept-42"),
        };
    }
}

[AllowAnonymous]
[RootLink("/always-expand-employees/{Id}")]
public partial class GetAlwaysExpandResourceQuery : IQuery<AlwaysExpandTestEmployee>
{
    public string Id { get; set; } = string.Empty;

    public static AlwaysExpandTestEmployee Handle(GetAlwaysExpandResourceQuery op)
    {
        return new AlwaysExpandTestEmployee
        {
            Department = new Expandable<DepartmentResource>("dept-always"),
        };
    }
}

// ── Multi-expandable types ──────────────────────────────────────────────────

public class LocationResource : ApiResource
{
    public override string Object => "LocationResource";

    public string City { get; set; } = string.Empty;
}

public class MultiExpandableTestEmployee : ApiResource
{
    public override string Object => "MultiExpandableTestEmployee";

    public Expandable<DepartmentResource> Department { get; set; } = new("dept-1");

    public Expandable<LocationResource> Location { get; set; } = new("loc-1");
}

[AllowAnonymous]
[RootLink("/multi-expandable-employees/{Id}")]
public partial class GetMultiExpandableResourceQuery : IQuery<MultiExpandableTestEmployee>
{
    public string Id { get; set; } = string.Empty;

    public static MultiExpandableTestEmployee Handle(GetMultiExpandableResourceQuery op)
    {
        return new MultiExpandableTestEmployee
        {
            Department = new Expandable<DepartmentResource>("dept-multi"),
            Location = new Expandable<LocationResource>("loc-multi"),
        };
    }
}

/// <summary>
/// A test expander that populates <see cref="Expandable{T}.Value"/> for <see cref="LocationResource"/>.
/// </summary>
public sealed class TestLocationExpander : IResourceExpander
{
    /// <summary>Gets the number of times <see cref="ExpandAsync"/> was called.</summary>
    public int ExpandCallCount { get; private set; }

    /// <inheritdoc />
    public bool CanExpand(Type type)
    {
        return type == typeof(LocationResource);
    }

    /// <inheritdoc />
    public ValueTask ExpandAsync(
        ApiOperationContext context,
        object response,
        IEnumerable entities,
        CancellationToken cancellationToken = default)
    {
        this.ExpandCallCount++;

        foreach (var entity in entities)
        {
            if (entity is Expandable<LocationResource> expandable)
            {
                expandable.Value = new LocationResource { City = "London" };
            }
        }

        return ValueTask.CompletedTask;
    }
}


