using System.ComponentModel;
using System.Globalization;
using System.Text;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The pieces a generated <see cref="IHttpRequestBodyBuilder"/> builds a stream or multipart body from (#71): a stream body
/// that can be sent again, as a redirect or a retry sends it, and form fields the host parses back to the value sent.
/// </summary>
public class HttpRequestBodiesTests
{
    [Fact]
    public async Task A_seekable_stream_body_sends_the_whole_stream_each_time_it_is_sent()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("xxhello")) { Position = 2 };
        using var content = HttpRequestBodies.CreateStreamContent(stream);

        var first = await SendAsync(content);
        var second = await SendAsync(content);

        first.Should().Be("hello");
        second.Should().Be("hello", "a 307/308 redirect or a retry sends the body again, from where it started");
        content.Headers.ContentLength.Should().Be(5);
    }

    [Fact]
    public async Task A_stream_body_that_cannot_seek_refuses_to_be_sent_a_second_time()
    {
        using var content = HttpRequestBodies.CreateStreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("once")));

        (await SendAsync(content)).Should().Be("once");

        var again = () => SendAsync(content);
        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been sent*");
    }

    [Fact]
    public async Task A_TimeOnly_field_keeps_its_seconds_and_fractions()
    {
        var value = new TimeOnly(12, 34, 56, 789);
        using var form = new MultipartFormDataContent();

        HttpRequestBodies.AddField(form, "at", value);

        var text = await form.Single().ReadAsStringAsync();
        TimeOnly.Parse(text, CultureInfo.InvariantCulture).Should().Be(value);
    }

    [Fact]
    public async Task A_field_with_a_TypeConverter_is_written_as_its_converter_writes_it()
    {
        // The host reads such a field with ConvertFromInvariantString; ToString() used to send the CLR type name instead.
        using var form = new MultipartFormDataContent();

        HttpRequestBodies.AddField(form, "id", new ConvertedId("abc-123"));

        var text = await form.Single().ReadAsStringAsync();
        text.Should().Be("abc-123");
        TypeDescriptor.GetConverter(typeof(ConvertedId)).ConvertFromInvariantString(text).Should().Be(new ConvertedId("abc-123"));
    }

    private static async Task<string> SendAsync(HttpContent content)
    {
        using var destination = new MemoryStream();
        await content.CopyToAsync(destination);

        return Encoding.UTF8.GetString(destination.ToArray());
    }

    /// <summary>An identifier bound through a <see cref="TypeConverter"/>, with no <see cref="object.ToString"/> of its own.</summary>
    [TypeConverter(typeof(ConvertedIdConverter))]
    private sealed class ConvertedId(string value) : IEquatable<ConvertedId>
    {
        public string Value { get; } = value;

        public bool Equals(ConvertedId? other)
        {
            return other is not null && other.Value == this.Value;
        }

        public override bool Equals(object? obj)
        {
            return this.Equals(obj as ConvertedId);
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode(StringComparison.Ordinal);
        }
    }

    private sealed class ConvertedIdConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(string);
        }

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        {
            return destinationType == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            return new ConvertedId((string)value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            return destinationType == typeof(string) && value is ConvertedId id ? id.Value : base.ConvertTo(context, culture, value, destinationType);
        }
    }

    /// <summary>A stream that cannot seek, as a request body.</summary>
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
