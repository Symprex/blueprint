using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a response body the host serialises into its per-thread buffer is still the one it wrote when the body
/// stream reads it after an await, however many responses the same thread writes meanwhile.
/// </summary>
/// <remarks>
/// <para>
/// A body stream may keep reading the memory it was given until the task its <c>WriteAsync</c> returned completes (the
/// <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/> contract). A compressing wrapper does exactly
/// that: <c>BrotliStream</c> encodes its input a chunk at a time and awaits its inner stream between chunks. While it waits
/// the thread is free, and it can serialise another request's body into the same <c>[ThreadStatic]</c> buffer. If the
/// pending write still reads that buffer, it sends another request's bytes: cross-request data mixing.
/// </para>
/// <para>
/// Each test runs on one thread with no await between the two requests, so both reach the same thread-static buffer, as
/// two Kestrel requests on one pool thread would. The stream holds on to the memory it was given, not a copy, and reads
/// it only once released.
/// </para>
/// </remarks>
public sealed class ResponseBufferOwnershipTests
{
    private const string FirstId = "widget-aaaa";
    private const string SecondId = "widget-bbbb";

    [Fact]
    public async Task When_an_ok_body_write_is_still_pending_then_the_next_response_on_the_thread_does_not_change_it()
    {
        var (services, context) = CreateOperationContext();

        await AssertFirstBodyKeptAsync(
            services,
            http => new HttpOperationResultExecutor().TryExecuteAsync(http, context, new OkResult<LinkWireWidgetApiResource>(new LinkWireWidgetApiResource { Id = FirstId })),
            http => new HttpOperationResultExecutor().TryExecuteAsync(http, context, new OkResult<LinkWireWidgetApiResource>(new LinkWireWidgetApiResource { Id = SecondId })),
            static body => body.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task When_a_resource_event_body_write_is_still_pending_then_the_next_response_on_the_thread_does_not_change_it()
    {
        var (services, context) = CreateOperationContext();

        await AssertFirstBodyKeptAsync(
            services,
            http => new HttpOperationResultExecutor().TryExecuteAsync(http, context, CreateEvent(FirstId)),
            http => new HttpOperationResultExecutor().TryExecuteAsync(http, context, CreateEvent(SecondId)),
            static body => body.RootElement.GetProperty("data").GetProperty("id").GetString());
    }

    [Fact]
    public async Task When_a_deferred_resource_event_body_write_is_still_pending_then_the_next_response_on_the_thread_does_not_change_it()
    {
        var (services, context) = CreateOperationContext();

        await AssertFirstBodyKeptAsync(
            services,
            http => new HttpOperationResultExecutor().TryExecuteAsync(http, context, CreateDeferredEvent(FirstId)),
            http => new HttpOperationResultExecutor().TryExecuteAsync(http, context, CreateDeferredEvent(SecondId)),
            static body => body.RootElement.GetProperty("data").GetProperty("id").GetString());
    }

    [Fact]
    public async Task When_a_pipeline_written_ok_body_write_is_still_pending_then_the_next_response_on_the_thread_does_not_change_it()
    {
        var (services, context) = CreateOperationContext();

        await AssertFirstBodyKeptAsync(
            services,
            http => PipelineHelpers.WriteOkJsonResponseAsync(http, context, new LinkWireWidgetApiResource { Id = FirstId }),
            http => PipelineHelpers.WriteOkJsonResponseAsync(http, context, new LinkWireWidgetApiResource { Id = SecondId }),
            static body => body.RootElement.GetProperty("id").GetString());
    }

    private static async Task AssertFirstBodyKeptAsync(
        IServiceProvider services,
        Func<HttpContext, Task> writeFirst,
        Func<HttpContext, Task> writeSecond,
        Func<JsonDocument, string?> readId)
    {
        var firstBody = new ReadsAfterAnAwaitStream();
        var first = writeFirst(new DefaultHttpContext { RequestServices = services, Response = { Body = firstBody } });

        first.IsCompleted.Should().BeFalse("the first body's stream has not read its input yet");

        // The same thread, with no await since the first write started: this serialises into the thread's buffer.
        using var secondBody = new MemoryStream();
        var second = writeSecond(new DefaultHttpContext { RequestServices = services, Response = { Body = secondBody } });
        second.IsCompletedSuccessfully.Should().BeTrue("a memory stream finishes its write at once");

        firstBody.Release();
        await first;

        var written = Encoding.UTF8.GetString(firstBody.Written!);
        written.Should().NotContain(SecondId, "the first response must carry only its own bytes; it was {0}", written);
        using var parsed = JsonDocument.Parse(written);
        readId(parsed).Should().Be(FirstId);

        using var secondParsed = JsonDocument.Parse(secondBody.ToArray());
        readId(secondParsed).Should().Be(SecondId);
    }

    private static (IServiceProvider Services, ApiOperationContext Context) CreateOperationContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>()
            .BuildServiceProvider();
        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();

        // LinkWireWidgetApiResource is covered by the app's serializer context, so the body goes through the buffer.
        return (services, new ApiOperationContext(services, app, app.FindOperation(typeof(GetLinkWireWidgetQuery)), CancellationToken.None));
    }

    private static ResourceEventResult<LinkWireWidgetApiResource> CreateEvent(string id)
    {
        return new ResourceEventResult<LinkWireWidgetApiResource>(ChangeType.Updated, "linkWireWidget.updated", id, new LinkWireWidgetApiResource { Id = id });
    }

    private static DeferredResourceEventResult<LinkWireWidgetApiResource> CreateDeferredEvent(string id)
    {
        var result = new DeferredResourceEventResult<LinkWireWidgetApiResource>(ChangeType.Updated, "linkWireWidget.updated", id, new GetLinkWireWidgetQuery { Id = id });
        result.SetResolvedContent(new LinkWireWidgetApiResource { Id = id });

        return result;
    }

    /// <summary>
    /// A write-only body stream that keeps the memory it is given and reads it only after an await, as a compressing
    /// wrapper does between chunks.
    /// </summary>
    private sealed class ReadsAfterAnAwaitStream : Stream
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[]? Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Release()
        {
            this._released.SetResult();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await this._released.Task.ConfigureAwait(false);

            this.Written = buffer.ToArray();
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
