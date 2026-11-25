using System.Security.Cryptography;
using System.Text;
using FourC.Cmms.Api.Features;

namespace FourC.Cmms.Api.Features;

/// <summary>New organizations confirm their e-mail before use (0.29.0): the link's token, its life and whether the server asks for it.</summary>
public static class SignUpConfirmation
{
    /// <summary>How long a confirmation link works.</summary>
    public static readonly TimeSpan LinkLife = TimeSpan.FromHours(48);

    /// <summary>32 random bytes, URL-safe base64 (no padding).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>What is stored: the token itself is only in the e-mail.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    /// <summary>
    /// <c>Auth:RequireEmailConfirmation</c>: unset or true = on when this server can send e-mail; false = off. Never on without a
    /// way to send the link: a new owner would wait for an e-mail that never comes and lose the organization after 7 days
    /// (<see cref="StartupWarning"/> says so in the log when the setting asks for it anyway).
    /// </summary>
    public static bool Required(IConfiguration config, EmailOutbox mail) =>
        (config.GetValue<bool?>("Auth:RequireEmailConfirmation") ?? true) && mail.Available;

    /// <summary>The line logged at start-up when the setting is explicitly true but this server can't send e-mail; null otherwise.</summary>
    public static string? StartupWarning(IConfiguration config, bool mailAvailable) =>
        config.GetValue<bool?>("Auth:RequireEmailConfirmation") == true && !mailAvailable
            ? "Auth:RequireEmailConfirmation is true, but this server cannot send e-mail (Email:Provider and Email:From are not set), " +
              "so new organizations are not asked to confirm their e-mail address until it can."
            : null;
}
