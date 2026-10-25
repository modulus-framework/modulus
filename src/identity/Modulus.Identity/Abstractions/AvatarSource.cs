namespace Modulus.Identity.Abstractions;

/// <summary>Where a profile picture comes from.</summary>
public enum AvatarSourceKind
{
    /// <summary>No usable picture: show the default avatar.</summary>
    Default,

    /// <summary>The controlled photo asset served by this application (<see cref="AvatarUrlPolicy.PhotoPath"/>).</summary>
    Controlled,

    /// <summary>A legacy https address that passed <see cref="AvatarUrlPolicy"/>. The browser loads it; the server never does.</summary>
    External,
}

/// <summary>The picture to show: its kind and, for a picture, the URL to put in the image element.</summary>
public readonly record struct AvatarSource(AvatarSourceKind Kind, string? Url);

/// <summary>
/// The rule for showing a profile picture (premodule 3, owner decision 2026-10-24).
/// <list type="bullet">
/// <item>A controlled photo asset takes precedence whenever the account has one.</item>
/// <item>A legacy <c>AvatarUrl</c> is shown only when it is an absolute <c>https</c> address with no embedded credentials, no whitespace or
/// control characters, and at most <see cref="MaxLength"/> characters. Every other scheme (<c>javascript:</c>, <c>data:</c>, <c>http:</c>,
/// <c>file:</c>, <c>blob:</c>) is refused, and so is a relative address.</item>
/// <item>Otherwise the default avatar is shown. The server never fetches a legacy address; the browser loads it directly, without
/// credentials (the page sets <c>referrerpolicy="no-referrer"</c>), and a picture that fails to load falls back to the default avatar.</item>
/// </list>
/// External hosts are permitted under this rule. An application that must restrict them can check <see cref="IsAllowedExternal"/> against its
/// own host list before rendering.
/// </summary>
public static class AvatarUrlPolicy
{
    /// <summary>The longest legacy address accepted.</summary>
    public const int MaxLength = 2048;

    /// <summary>The path of the controlled photo, relative to the API root.</summary>
    public const string PhotoPath = "/api/account/profile/photo";

    /// <summary>Chooses the picture for an account: the controlled photo first, then a legacy address that passes the policy.</summary>
    public static AvatarSource Resolve(bool hasPhoto, string? avatarUrl)
    {
        if (hasPhoto)
            return new AvatarSource(AvatarSourceKind.Controlled, PhotoPath);
        return IsAllowedExternal(avatarUrl) ? new AvatarSource(AvatarSourceKind.External, avatarUrl!.Trim()) : new AvatarSource(AvatarSourceKind.Default, null);
    }

    /// <summary>True when a legacy address may be shown under the policy.</summary>
    public static bool IsAllowedExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var value = url.Trim();
        if (value.Length > MaxLength)
            return false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                return false;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttps
            && uri.UserInfo.Length == 0
            && !string.IsNullOrEmpty(uri.Host);
    }
}
