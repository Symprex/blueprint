using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Benchmarks.Serialization;
using Microsoft.AspNetCore.Http;

namespace Blueprint.SourceGenerator.Benchmarks.Http;

/// <summary>
/// Measures the bytes allocated writing one JSON response body through the host's result executor, for a ~1 KB and a
/// ~250 KB body, to a body stream that finishes its write at once and to one that finishes it after an await (as
/// Kestrel does when its output is backed up, and as a compressing wrapper always does).
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
public class ResponseBodyWriteBenchmarks
{
    private ResponseBenchmarkApp _app = null!;
    private SerBenchmarkPagedResource _content = null!;
    private DefaultHttpContext _http = null!;
    private Stream _body = null!;

    /// <summary>Gets or sets the number of items in the body: 6 is about 1 KB, 1700 is about 250 KB.</summary>
    [Params(6, 1700)]
    public int Items { get; set; }

    /// <summary>Gets or sets whether the body stream completes its write after an await.</summary>
    [Params(false, true)]
    public bool PendingWrite { get; set; }

    /// <summary>Builds the body and the context it is written to.</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        this._app = new ResponseBenchmarkApp();

        var items = new SerBenchmarkResource[this.Items];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new SerBenchmarkResource
            {
                Id = $"user-{i:D5}",
                Name = $"Person number {i}",
                Description = $"Description for person {i}",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                IsActive = i % 3 != 0,
            };
        }

        this._content = new SerBenchmarkPagedResource { Items = items, TotalCount = items.Length };

        this._body = this.PendingWrite ? new YieldingSinkStream() : new DiscardingSinkStream();
        this._http = new DefaultHttpContext();
        this._http.Response.Body = this._body;

        // A first write sizes the thread's buffer, as a warm server's is.
        this.WriteAsync().GetAwaiter().GetResult();
        Console.WriteLine($"Response body is {this._http.Response.ContentLength} bytes");
    }

    /// <summary>Writes the body as a 200 JSON response.</summary>
    /// <returns>A task that completes when the body has been written.</returns>
    [Benchmark]
    public Task WriteAsync()
    {
        return HttpOperationResultExecutor.WriteOkJsonAsync(this._http, this._app, this._content);
    }

    private sealed class ResponseBenchmarkApp : Blueprint.Model.BlueprintApp
    {
        public ResponseBenchmarkApp()
        {
            this.AppOwnedSerializerContext = SerBenchmarkJsonContext.Default;
        }
    }

    private sealed class DiscardingSinkStream : SinkStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class YieldingSinkStream : SinkStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
        }
    }

    private abstract class SinkStream : Stream
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
