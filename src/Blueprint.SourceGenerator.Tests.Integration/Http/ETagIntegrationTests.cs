using Blueprint.Http;
using Blueprint.Runtime.Exec;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Integration tests for <see cref="ETagger"/> conditional request handling
/// and <see cref="NotModifiedResult{T}"/> response semantics.
/// </summary>
public class ETagIntegrationTests
{
    [Fact]
    public void When_no_if_none_match_header_then_returns_ok_result_with_etag()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var accessor = new TestHttpContextAccessor(httpContext);
        var etagger = new ETagger(accessor);

        // Act
        var result = etagger.WithETag("\"abc123\"", () => new TestResource { Id = 1, Name = "Test" });

        // Assert
        Assert.IsType<OkResult<TestResource>>(result);
        Assert.False(result is NotModifiedResult<TestResource>);
        Assert.Equal(1, result.Content.Id);
        Assert.Equal("Test", result.Content.Name);
        Assert.Equal("\"abc123\"", result.Headers["ETag"].ToString());
        Assert.Equal("private, max-age=3600", result.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void When_if_none_match_matches_then_returns_not_modified()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["If-None-Match"] = "\"abc123\"";
        var accessor = new TestHttpContextAccessor(httpContext);
        var etagger = new ETagger(accessor);

        // Act
        var result = etagger.WithETag<TestResource>("\"abc123\"", () => throw new InvalidOperationException("Should not be called"));

        // Assert
        Assert.IsType<NotModifiedResult<TestResource>>(result);
        var notModified = (NotModifiedResult<TestResource>)result;
        Assert.True(notModified.IsNotModified);
        Assert.Equal("\"abc123\"", notModified.ETag);
        Assert.Equal("\"abc123\"", notModified.Headers["ETag"].ToString());
        Assert.Equal("private, max-age=3600", notModified.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void When_if_none_match_does_not_match_then_returns_ok_result()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["If-None-Match"] = "\"old-etag\"";
        var accessor = new TestHttpContextAccessor(httpContext);
        var etagger = new ETagger(accessor);

        // Act
        var result = etagger.WithETag("\"new-etag\"", () => new TestResource { Id = 2, Name = "Fresh" });

        // Assert
        Assert.IsType<OkResult<TestResource>>(result);
        Assert.False(result is NotModifiedResult<TestResource>);
        Assert.Equal(2, result.Content.Id);
    }

    [Fact]
    public void When_if_none_match_has_multiple_values_and_one_matches_then_returns_not_modified()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["If-None-Match"] = "\"old\", \"abc123\", \"other\"";
        var accessor = new TestHttpContextAccessor(httpContext);
        var etagger = new ETagger(accessor);

        // Act
        var result = etagger.WithETag<TestResource>("\"abc123\"", () => throw new InvalidOperationException("Should not be called"));

        // Assert
        Assert.IsType<NotModifiedResult<TestResource>>(result);
    }

    [Fact]
    public void OkResult_For_factory_creates_result_with_headers()
    {
        // Arrange & Act
        var result = OkResult.For(
            new TestResource { Id = 5, Name = "Factory" },
            new Dictionary<string, StringValues>
            {
                ["X-Custom"] = "value",
                ["ETag"] = "\"xyz\"",
            });

        // Assert
        Assert.Equal(5, result.Content.Id);
        Assert.Equal("value", result.Headers["X-Custom"].ToString());
        Assert.Equal("\"xyz\"", result.Headers["ETag"].ToString());
    }

    [Fact]
    public void OkResult_For_factory_without_headers_creates_plain_result()
    {
        // Arrange & Act
        var result = OkResult.For(new TestResource { Id = 3, Name = "NoHeaders" });

        // Assert
        Assert.Equal(3, result.Content.Id);
        Assert.False(result.HasHeaders);
    }

    private sealed class TestHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private sealed class TestResource
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
