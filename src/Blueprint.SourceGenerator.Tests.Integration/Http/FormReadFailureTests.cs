using Blueprint.Runtime.Exec;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// How <see cref="PipelineHelpers.ReadFormAsync"/> (#71) answers a form its reader fails on: a body that is not a well-formed
/// form is the caller's fault, a 400; the server failing to spool a file to disk is its own, and escapes as a 500.
/// </summary>
public sealed class FormReadFailureTests
{
    [Fact]
    public async Task A_body_that_ends_before_its_closing_boundary_is_a_400()
    {
        // The multipart reader raises a plain IOException of its own for a truncated body.
        var result = await ReadFormAsync(new IOException("Unexpected end of Stream, the content may have already been read by another component."));

        result.Failure.Should().BeOfType<BindingFailureResult>();
    }

    [Fact]
    public async Task A_missing_temporary_directory_is_the_servers_failure()
    {
        // Every IOException but a full disk's used to be answered as a malformed body.
        var read = () => ReadFormAsync(new DirectoryNotFoundException("Could not find a part of the path."));

        await read.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task A_file_system_error_spooling_a_file_is_the_servers_failure()
    {
        // ERROR_SHARING_VIOLATION, as the operating system reports a temporary file it cannot write.
        var read = () => ReadFormAsync(new IOException("The process cannot access the file.", unchecked((int)0x80070020)));

        await read.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task A_full_disk_is_the_servers_failure()
    {
        var read = () => ReadFormAsync(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)));

        await read.Should().ThrowAsync<IOException>();
    }

    private static Task<FormReadResult> ReadFormAsync(Exception failure)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IFormFeature>(new FailingFormFeature(failure));

        return PipelineHelpers.ReadFormAsync(context, CancellationToken.None);
    }

    /// <summary>A form reader that fails as ASP.NET Core's does, for a request whose content type is a form's.</summary>
    private sealed class FailingFormFeature(Exception failure) : IFormFeature
    {
        public bool HasFormContentType => true;

        public IFormCollection? Form { get; set; }

        public IFormCollection ReadForm()
        {
            throw failure;
        }

        public Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken)
        {
            return Task.FromException<IFormCollection>(failure);
        }
    }
}
