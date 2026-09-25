using System.Net;

namespace Blueprint.Http;

/// <summary>
/// Finds the address of the client a request came from, as legacy Blueprint's <c>HttpRequestExtensions.GetClientIpAddress</c>
/// found it (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/HttpRequestExtensions.cs:16-98</c>), for the <c>IpAddress</c>
/// a command's resource event's metadata is given.
/// </summary>
/// <remarks>
/// Legacy's quirks are kept, because Signature365 stores the address with every audited event: the first public address
/// the request was forwarded for is taken, although legacy's comment says the last; an entry that does not parse, which
/// includes any IPv6 one once its "port" is stripped and an empty one, fails the whole lookup as <c>0.0.0.0</c>; and when
/// no forwarded address is public the connection's address is returned without being parsed.
/// </remarks>
internal static class ClientIpAddress
{
    /// <summary>
    /// Finds the client's address from the connection's remote address and the request's <c>X-Forwarded-For</c> header.
    /// </summary>
    /// <param name="userHostAddress">The connection's remote address, or <see langword="null"/> when it has none.</param>
    /// <param name="xForwardedFor">The request's <c>X-Forwarded-For</c> header, or <see langword="null"/> when it has none.</param>
    /// <returns>
    /// The client's address; <c>0.0.0.0</c> when an address it has to parse does not; <paramref name="userHostAddress"/>, as
    /// it is, when no forwarded address is public.
    /// </returns>
    public static string? Get(string? userHostAddress, string? xForwardedFor)
    {
        try
        {
            // IPv6's loopback is reported as IPv4's, as legacy reported it for systems that could not handle it (:20-25).
            if (userHostAddress == "::1")
            {
                return "127.0.0.1";
            }

            if (string.IsNullOrEmpty(xForwardedFor))
            {
                // Parsed only to fail over to 0.0.0.0 when it is not an address (:27-34).
                IPAddress.Parse(userHostAddress!);

                return userHostAddress;
            }

            var publicForwardingIps = xForwardedFor
                .Split(',')
                .Select(StripPort)
                .Where(static ip => !IsPrivateIpAddress(ip))
                .ToList();

            return publicForwardingIps.Count > 0 ? publicForwardingIps[0] : userHostAddress;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentNullException)
        {
            // IPAddress.Parse's failures, the only exceptions legacy's catch-all (:46-49) could see.
            return "0.0.0.0";
        }
    }

    private static string StripPort(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
        {
            return ipAddress;
        }

        ipAddress = ipAddress.Trim();

        var colonIndex = ipAddress.LastIndexOf(':');

        return colonIndex == -1 ? ipAddress : ipAddress[..colonIndex];
    }

    private static bool IsPrivateIpAddress(string ipAddress)
    {
        // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 and the link-local 169.254.0.0/16 (:66-98).
        var octets = IPAddress.Parse(ipAddress).GetAddressBytes();

        return octets[0] == 10
               || (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
               || (octets[0] == 192 && octets[1] == 168)
               || (octets[0] == 169 && octets[1] == 254);
    }
}
