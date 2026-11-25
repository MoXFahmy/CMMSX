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

    /// <summary><c>Auth:RequireEmailConfirmation</c>: unset = on when this server can send e-mail, else off; an explicit true/false wins.</summary>
    public static bool Required(IConfiguration config, EmailOutbox mail) =>
        config.GetValue<bool?>("Auth:RequireEmailConfirmation") ?? mail.Available;
}
