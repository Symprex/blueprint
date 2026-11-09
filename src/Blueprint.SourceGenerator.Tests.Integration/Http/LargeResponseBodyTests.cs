using System.Text;
using System.Text.Json;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a response body far larger than the per-thread buffer's starting size is written exactly as it serialises,
/// with its own <c>Content-Length</c>, and that writing it does not allocate an array of its own, however its body stream
/// completes the write.
/// </summary>
/// <remarks>
/// The allocation tests compare the bytes the thread allocated across one write with the body's size, so they fail for a
/// writer that grows a fresh array chain for each response (the sum of its arrays is about twice the body) and pass for one
/// that serialises into pooled memory. Each warms the thread first, as a running server's is.
/// </remarks>
public sealed class LargeResponseBodyTests
{
    private const int LargeIdLength = 250_000;

    [Fact]
    public async Task When_a_large_ok_body_is_written_then_it_is_the_serialised_content_with_its_content_length()
    {
        var (services, context) = CreateOperationContext();
        var content = new LinkWireWidgetApiResource { Id = new string('a', LargeIdLength) + "é\"\\" };
        var expected = JsonSerializer.SerializeToUtf8Bytes(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var body = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = services, Response = { Body = body } };

        await new HttpOperationResultExecutor().TryExecuteAsync(http, context, new OkResult<LinkWireWidgetApiResource>(content));

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        http.Response.ContentLength.Should().Be(body.Length);
        body.Length.Should().BeGreaterThan(LargeIdLength);
        Encoding.UTF8.GetString(body.ToArray()).Should().Be(Encoding.UTF8.GetString(expected));
    }

    [Fact]
    public async Task When_a_large_ok_body_write_finishes_after_an_await_then_it_is_the_serialised_content()
    {
        var (services, context) = CreateOperationContext();
        var content = new LinkWireWidgetApiResource { Id = new string('b', LargeIdLength) };
        var expected = JsonSerializer.SerializeToUtf8Bytes(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var inner = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = services, Response = { Body = new YieldingStream(inner) } };

        await new HttpOperationResultExecutor().TryExecuteAsync(http, context, new OkResult<LinkWireWidgetApiResource>(content));

        http.Response.ContentLength.Should().Be(inner.Length);
        Encoding.UTF8.GetString(inner.ToArray()).Should().Be(Encoding.UTF8.GetString(expected));
    }

    [Fact]
    public async Task When_a_large_ok_body_write_finishes_at_once_then_the_write_allocates_less_than_the_body()
    {
        await AssertAllocatesLessThanBodyAsync(inner => inner);
    }

    [Fact]
    public async Task When_a_large_ok_body_write_finishes_after_an_await_then_the_write_allocates_less_than_the_body()
    {
        await AssertAllocatesLessThanBodyAsync(inner => new YieldingStream(inner));
    }

    private static async Task AssertAllocatesLessThanBodyAsync(Func<Stream, Stream> wrap)
    {
        var (services, context) = CreateOperationContext();
        var content = new LinkWireWidgetApiResource { Id = new string('c', LargeIdLength) };
        var result = new OkResult<LinkWireWidgetApiResource>(content);
        var executor = new HttpOperationResultExecutor();

        using var inner = new DiscardStream();
        var http = new DefaultHttpContext { RequestServices = services, Response = { Body = wrap(inner) } };

        // Warm up: the first writes size the pool's arrays, the serializer's metadata and the thread's buffer.
        for (var i = 0; i < 3; i++)
        {
            await executor.TryExecuteAsync(http, context, result);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var write = executor.TryExecuteAsync(http, context, result);
        var allocatedStarting = GC.GetAllocatedBytesForCurrentThread() - before;
        await write;

        // The continuation after an await may run on another thread, so only what the writing thread allocated up to the
        // await is counted: that is where the buffer is built, and it is the whole of the write when it finishes at once.
        http.Response.ContentLength.Should().BeGreaterThan(LargeIdLength);
        allocatedStarting.Should().BeLessThan(LargeIdLength / 2, "a ~250 KB body must be serialised into pooled memory, not into arrays of its own");
    }

    private static (IServiceProvider Services, ApiOperationContext Context) CreateOperationContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>()
            .BuildServiceProvider();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();

        return (services, new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None));
    }

    /// <summary>
    /// Forwards a write after an await, so the write does not finish at once, as Kestrel's does when its output is backed up.
    /// </summary>
    private sealed class YieldingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// A body stream that finishes every write at once and keeps nothing.
    /// </summary>
    private sealed class DiscardStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
