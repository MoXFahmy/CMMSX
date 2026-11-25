using System.Net;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FourC.Cmms.Tests;

/// <summary>
/// Which address a visitor is judged by (0.29.0): the proxy's header when the operator names one, else the connection. An IPv4
/// address counts as itself; an IPv6 address by its /64 network, since one home or server picks freely among the addresses of
/// its block (an IPv4 address written as IPv6 counts as the IPv4 one).
/// </summary>
public class VisitorAddressTests
{
    private const string Header = "CloudFront-Viewer-Address";

    private static string Of(string? configuredHeader, string? headerValue, string connection = "10.0.0.5")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Abuse:ViewerAddressHeader"] = configuredHeader }).Build();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(connection);
        if (headerValue is not null) http.Request.Headers[Header] = headerValue;
        return VisitorAddress.Of(http, config);
    }

    [Fact]
    public void Header_wins_when_configured() => Assert.Equal("198.51.100.10", Of(Header, "198.51.100.10:46532"));

    [Fact]
    public void Ipv6_header_drops_port_and_counts_by_64() => Assert.Equal("2001:db8::/64", Of(Header, "2001:db8::7:46532"));

    [Fact]
    public void Ipv6_in_brackets_drops_port_and_counts_by_64() => Assert.Equal("2001:db8::/64", Of(Header, "[2001:db8::7]:46532"));

    [Fact]
    public void Ipv6_addresses_of_one_64_share_a_key_and_other_networks_do_not()
    {
        var a = Of(Header, "[2001:db8:1:2:aaaa:bbbb:cccc:dddd]:443");
        Assert.Equal("2001:db8:1:2::/64", a);
        Assert.Equal(a, Of(Header, "[2001:db8:1:2::1]:443"));
        Assert.Equal(a, Of(Header, "2001:db8:1:2:ffff:ffff:ffff:ffff:443"));
        Assert.NotEqual(a, Of(Header, "[2001:db8:1:3::1]:443"));
    }

    [Fact]
    public void Ipv4_addresses_still_count_one_by_one()
    {
        Assert.Equal("198.51.100.10", Of(Header, "198.51.100.10"));
        Assert.NotEqual(Of(Header, "198.51.100.10:1"), Of(Header, "198.51.100.11:1"));
    }

    [Fact]
    public void Ipv4_written_as_ipv6_counts_as_ipv4()
    {
        // Kestrel on a dual-stack socket reports IPv4 visitors this way; keying those by /64 would put all of them in "::/64".
        Assert.Equal("10.0.0.5", Of(null, null, connection: "::ffff:10.0.0.5"));
        Assert.Equal("198.51.100.10", Of(Header, "[::ffff:198.51.100.10]:443"));
    }

    [Fact]
    public void Ipv6_connection_counts_by_64() => Assert.Equal("2001:db8:5:6::/64", Of(null, null, connection: "2001:db8:5:6::9"));

    [Fact]
    public void Malformed_header_falls_back_to_connection() => Assert.Equal("10.0.0.5", Of(Header, "not an address"));

    [Fact]
    public void Header_ignored_when_not_configured() => Assert.Equal("10.0.0.5", Of("", "198.51.100.10:46532"));
}
