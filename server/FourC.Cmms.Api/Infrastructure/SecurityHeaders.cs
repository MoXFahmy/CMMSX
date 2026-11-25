namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// Browser security headers on every response (0.29.0), on every host. A header an endpoint already set stays: file downloads
/// keep their own stricter policy (no script, sandboxed) so an upload can never run as a page. Strict-Transport-Security is
/// added only when the request arrived over HTTPS (behind CloudFront, after the forwarded-protocol header is read).
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; media-src 'self' blob:; worker-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var h = http.Response.Headers;
                Set(h, "Content-Security-Policy", ContentSecurityPolicy);
                Set(h, "X-Content-Type-Options", "nosniff");
                Set(h, "X-Frame-Options", "DENY");
                Set(h, "Referrer-Policy", "strict-origin-when-cross-origin");
                Set(h, "Cross-Origin-Opener-Policy", "same-origin");
                Set(h, "Permissions-Policy", "camera=(self), microphone=(self), geolocation=(self)");
                if (http.Request.IsHttps) Set(h, "Strict-Transport-Security", "max-age=31536000");
                return Task.CompletedTask;
            });
            return next();
        });
        return app;
    }

    private static void Set(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name)) headers[name] = value;
    }
}
