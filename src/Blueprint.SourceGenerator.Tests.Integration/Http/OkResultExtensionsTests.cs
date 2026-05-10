using System.Net.Http.Headers;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Tests for <see cref="OkResultExtensions"/> extension methods that fluently
/// add response headers to <see cref="OkResult"/> instances.
/// </summary>
public class OkResultExtensionsTests
{
    [Fact]
    public void WithCacheControl_sets_cache_control_header()
    {
        // Arrange
        var result = new OkResult<TestDto>(new TestDto("hello"));
        var cacheControl = new CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.FromHours(1) };

        // Act
        var returned = result.WithCacheControl(cacheControl);

        // Assert
        Assert.Same(result, returned);
        var header = result.Headers["Cache-Control"].ToString();
        Assert.Contains("max-age=3600", header);
        Assert.Contains("private", header);
    }

    [Fact]
    public void WithETag_sets_etag_header()
    {
        // Arrange
        var result = new OkResult<TestDto>(new TestDto("world"));

        // Act
        var returned = result.WithETag("\"abc123\"");

        // Assert
        Assert.Same(result, returned);
        Assert.Equal("\"abc123\"", result.Headers["ETag"].ToString());
    }

    [Fact]
    public void WithHeader_sets_custom_header()
    {
        // Arrange
        var result = new OkResult<TestDto>(new TestDto("custom"));

        // Act
        var returned = result.WithHeader("X-Custom", "value");

        // Assert
        Assert.Same(result, returned);
        Assert.Equal("value", result.Headers["X-Custom"].ToString());
    }

    [Fact]
    public void Fluent_chaining_sets_multiple_headers()
    {
        // Arrange & Act
        var result = new OkResult<TestDto>(new TestDto("chained"))
            .WithCacheControl(new CacheControlHeaderValue { NoCache = true })
            .WithETag("\"v2\"")
            .WithHeader("X-Request-Id", "abc");

        // Assert
        Assert.Equal("no-cache", result.Headers["Cache-Control"].ToString());
        Assert.Equal("\"v2\"", result.Headers["ETag"].ToString());
        Assert.Equal("abc", result.Headers["X-Request-Id"].ToString());
    }

    [Fact]
    public void WithCacheControl_throws_on_null()
    {
        var result = new OkResult<TestDto>(new TestDto("x"));

        Assert.Throws<ArgumentNullException>(() => result.WithCacheControl(null!));
    }

    [Fact]
    public void WithETag_throws_on_null_or_empty()
    {
        var result = new OkResult<TestDto>(new TestDto("x"));

        Assert.ThrowsAny<ArgumentException>(() => result.WithETag(null!));
        Assert.ThrowsAny<ArgumentException>(() => result.WithETag(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => result.WithETag("   "));
    }

    [Fact]
    public void WithHeader_throws_on_null_or_empty_name()
    {
        var result = new OkResult<TestDto>(new TestDto("x"));

        Assert.ThrowsAny<ArgumentException>(() => result.WithHeader(null!, "value"));
        Assert.ThrowsAny<ArgumentException>(() => result.WithHeader(string.Empty, "value"));
    }

    [Fact]
    public void WithMaxAge_sets_max_age_cache_control()
    {
        var result = new OkResult<TestDto>(new TestDto("cached"));

        var returned = result.WithMaxAge(600);

        Assert.Same(result, returned);
        Assert.Equal("max-age=600", result.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void WithMaxAge_zero_sets_max_age_zero()
    {
        var result = new OkResult<TestDto>(new TestDto("x"));

        result.WithMaxAge(0);

        Assert.Equal("max-age=0", result.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public void WithMaxAge_throws_on_negative()
    {
        var result = new OkResult<TestDto>(new TestDto("x"));

        Assert.Throws<ArgumentOutOfRangeException>(() => result.WithMaxAge(-1));
    }

    [Fact]
    public void WithNoStore_sets_no_store_cache_control()
    {
        var result = new OkResult<TestDto>(new TestDto("sensitive"));

        var returned = result.WithNoStore();

        Assert.Same(result, returned);
        Assert.Equal("no-store", result.Headers["Cache-Control"].ToString());
    }

    private sealed record TestDto(string Value);
}
