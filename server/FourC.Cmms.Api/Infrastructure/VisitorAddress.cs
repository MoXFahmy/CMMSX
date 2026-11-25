using System.Net;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// The address a visitor is judged by for per-address limits (0.29.0). Behind CloudFront the connection is the proxy's, so the
/// operator names the header that carries the visitor's own address (CloudFront-Viewer-Address, "198.51.100.10:46532");
/// with no header named, or one that doesn't parse, it is the connection's address. Addresses live in memory only.
/// </summary>
public static class VisitorAddress
{
    public static string Of(HttpContext http, IConfiguration config)
    {
        var name = config["Abuse:ViewerAddressHeader"];
        if (!string.IsNullOrWhiteSpace(name) && http.Request.Headers.TryGetValue(name, out var values)
            && Parse(values.ToString()) is { } fromHeader)
            return fromHeader;
        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>"1.2.3.4:5", "[::1]:5", "2001:db8::7:5" (port after the last colon) or a bare address; null when it isn't one.</summary>
    private static string? Parse(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        if (value.StartsWith('[')) // [ipv6]:port
        {
            var close = value.IndexOf(']');
            return close > 1 && IPAddress.TryParse(value[1..close], out var v6) ? v6.ToString() : null;
        }
        var colons = value.Count(c => c == ':');
        if (colons == 0) return IPAddress.TryParse(value, out var bare) ? bare.ToString() : null;
        var last = value.LastIndexOf(':');
        var host = value[..last];
        if (!ushort.TryParse(value[(last + 1)..], out _)) return null;
        return IPAddress.TryParse(host, out var ip) ? ip.ToString() : null;
    }
}
