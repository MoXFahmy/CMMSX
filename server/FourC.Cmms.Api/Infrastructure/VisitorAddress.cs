using System.Net;
using System.Net.Sockets;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// The address a visitor is judged by for per-address limits (0.29.0). Behind CloudFront the connection is the proxy's, so the
/// operator names the header that carries the visitor's own address (CloudFront-Viewer-Address, "198.51.100.10:46532");
/// with no header named, or one that doesn't parse, it is the connection's address. An IPv4 address counts as itself. An IPv6
/// address counts by its /64 network ("2001:db8:1:2::/64"): a home or a server is given a whole /64 and can pick any address in
/// it, so counting single addresses would let one visitor try without limit. An IPv4 address written as IPv6 (::ffff:a.b.c.d,
/// as a dual-stack server reports IPv4 visitors) counts as the IPv4 one. Addresses live in memory only.
/// </summary>
public static class VisitorAddress
{
    public static string Of(HttpContext http, IConfiguration config)
    {
        var name = config["Abuse:ViewerAddressHeader"];
        if (!string.IsNullOrWhiteSpace(name) && http.Request.Headers.TryGetValue(name, out var values)
            && Parse(values.ToString()) is { } fromHeader)
            return Key(fromHeader);
        return http.Connection.RemoteIpAddress is { } connection ? Key(connection) : "unknown";
    }

    /// <summary>What an address is counted under: IPv4 as itself, IPv6 as its /64 network.</summary>
    private static string Key(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes(); // 16 bytes, without any scope id
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>"1.2.3.4:5", "[::1]:5", "2001:db8::7:5" (port after the last colon) or a bare address; null when it isn't one.</summary>
    private static IPAddress? Parse(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        if (value.StartsWith('[')) // [ipv6]:port
        {
            var close = value.IndexOf(']');
            return close > 1 && IPAddress.TryParse(value[1..close], out var v6) ? v6 : null;
        }
        var colons = value.Count(c => c == ':');
        if (colons == 0) return IPAddress.TryParse(value, out var bare) ? bare : null;
        var last = value.LastIndexOf(':');
        var host = value[..last];
        if (!ushort.TryParse(value[(last + 1)..], out _)) return null;
        return IPAddress.TryParse(host, out var ip) ? ip : null;
    }
}
