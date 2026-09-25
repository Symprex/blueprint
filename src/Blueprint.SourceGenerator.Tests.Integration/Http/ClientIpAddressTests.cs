using Blueprint.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the <c>IpAddress</c> a resource event's metadata is given is the one legacy's
/// <c>HttpContextMetadataProvider</c> gave it (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/HttpContextMetadataProvider.cs:18</c>),
/// through legacy's <c>HttpRequestExtensions.GetClientIpAddress</c> (<c>Blueprint.Http/HttpRequestExtensions.cs:16-98</c>).
/// Each expected value is worked from that code by hand.
/// </summary>
public sealed class ClientIpAddressTests
{
    [Theory]

    // :22-25 — the IPv6 loopback is reported as IPv4's, and before the header is looked at.
    [InlineData("::1", null, "127.0.0.1")]
    [InlineData("::1", "203.0.113.9", "127.0.0.1")]

    // :27-34 — no header: the connection's address, once it parses.
    [InlineData("127.0.0.1", null, "127.0.0.1")]
    [InlineData("127.0.0.1", "", "127.0.0.1")]

    // :31, :46-49 — an address that does not parse, or none, is 0.0.0.0.
    [InlineData("not-an-ip", null, "0.0.0.0")]
    [InlineData(null, null, "0.0.0.0")]

    // :37-44 — the header's entries, trimmed and without a port (:52-64), private ones dropped (:66-98); the FIRST public
    // one, whatever the comment at :43 says.
    [InlineData("127.0.0.1", "10.0.0.5, 203.0.113.9:4711, 198.51.100.2", "203.0.113.9")]
    [InlineData(null, "203.0.113.9", "203.0.113.9")]

    // :78-97 — 10/8, 172.16/12, 192.168/16 and 169.254/16 are private, so the connection's address is kept.
    [InlineData("127.0.0.1", "10.0.0.5, 172.16.4.4, 172.31.0.1, 192.168.1.1, 169.254.0.1", "127.0.0.1")]

    // :44 — that address is returned as it is, never parsed, so with none there is none.
    [InlineData(null, "10.0.0.5", null)]

    // :84 — 172.15 and 172.32 fall outside the 20-bit block.
    [InlineData("127.0.0.1", "172.15.0.1", "172.15.0.1")]
    [InlineData("127.0.0.1", "172.32.0.1", "172.32.0.1")]

    // :75, :46-49 — an entry that does not parse fails the whole lookup. An IPv6 entry is one: stripping its "port" (:61-63)
    // leaves "2001:db8:".
    [InlineData("127.0.0.1", "not-an-ip", "0.0.0.0")]
    [InlineData("127.0.0.1", "2001:db8::1", "0.0.0.0")]

    // :38-40, :54-57, :75 — so does an empty entry, which a trailing comma makes.
    [InlineData("127.0.0.1", "203.0.113.9,", "0.0.0.0")]
    public void The_client_ip_address_is_legacys(string? userHostAddress, string? xForwardedFor, string? expected)
    {
        ClientIpAddress.Get(userHostAddress, xForwardedFor).Should().Be(expected);
    }
}
