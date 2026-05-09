using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class StreamExtensionsTests
{
    [Fact]
    public void ReadFully_Returns_All_Remaining_Bytes()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);

        stream.ReadFully().Should().Equal(data);
    }

    [Fact]
    public async Task ReadFullyAsStringAsync_Returns_String_Content()
    {
        using var stream = "hello world".AsUtf8Stream();

        var result = await stream.ReadFullyAsStringAsync();

        result.Should().Be("hello world");
    }

    [Fact]
    public void AsUtf8Stream_Creates_Readable_Stream()
    {
        using var stream = "test".AsUtf8Stream();

        stream.Should().NotBeNull();
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AsStream_Creates_Stream_At_Position_Zero()
    {
        var bytes = new byte[] { 10, 20, 30 };
        using var stream = bytes.AsStream();

        stream.Position.Should().Be(0);
        stream.ReadFully().Should().Equal(bytes);
    }
}
