using System.Net;
using FourC.Cmms.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FourC.Cmms.Tests;

/// <summary>Which address a visitor is judged by (0.29.0): the proxy's header when the operator names one, else the connection.</summary>
public class VisitorAddressTests
{
    private static string Of(string? configuredHeader, string? headerValue, string connection = "10.0.0.5")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Abuse:ViewerAddressHeader"] = configuredHeader }).Build();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(connection);
        if (headerValue is not null) http.Request.Headers["CloudFront-Viewer-Address"] = headerValue;
        return VisitorAddress.Of(http, config);
    }

    [Fact]
    public void Header_wins_when_configured() => Assert.Equal("198.51.100.10", Of("CloudFront-Viewer-Address", "198.51.100.10:46532"));

    [Fact]
    public void Ipv6_header_drops_port() => Assert.Equal("2001:db8::7", Of("CloudFront-Viewer-Address", "2001:db8::7:46532"));

    [Fact]
    public void Ipv6_in_brackets_drops_port() => Assert.Equal("2001:db8::7", Of("CloudFront-Viewer-Address", "[2001:db8::7]:46532"));

    [Fact]
    public void Malformed_header_falls_back_to_connection() => Assert.Equal("10.0.0.5", Of("CloudFront-Viewer-Address", "not an address"));

    [Fact]
    public void Header_ignored_when_not_configured() => Assert.Equal("10.0.0.5", Of("", "198.51.100.10:46532"));
}
