namespace Koren.Core;

public static class Info {
    public const string Name = "KorenResourcePack v2";
    public const string Author = "koren";

    // Core version (MAJOR.MINOR.PATCH). Parsed into a System.Version at runtime,
    // so keep it dotted numbers only.
    public const string Version = "2.0.0";

    // Release channel. Move it forward as the project matures:
    //   "alpha" -> "beta" -> "rc" -> "stable".
    public const string Channel = "alpha";

    // Pre-release build number. Auto-managed by tools/release.sh, which tracks
    // it per (version, channel) in build.json and bakes the next value here on
    // each release (it resets implicitly when Version changes). Don't hand-edit.
    public const int Build = 2;

    // Typed channel + flags for runtime and update-check logic.
    public static ReleaseChannel ChannelKind => SemVer.ParseChannel(Channel);
    public static bool IsPrerelease => ChannelKind != ReleaseChannel.Stable;

    // True only for "dev" builds. Gates developer-only UI (the Developer tab).
    public static bool IsDev => ChannelKind == ReleaseChannel.Dev;

    // Full display version. Pre-release builds carry the channel + build number
    // ("2.0.0-alpha-1"); a Stable release drops both and shows just the core
    // ("2.0.0"), so going Stable needs no other edits. Use this as the git
    // release tag too (prefixed with 'v').
    public static string DisplayVersion => IsPrerelease
        ? $"{Version}-{SemVer.ChannelTag(ChannelKind)}-{Build}"
        : Version;

    public static SemVer Current => SemVer.TryParse(DisplayVersion, out SemVer v) ? v : default;

    public const string Description = ":thumbs_up:";
    public const string GithubLink = "https://github.com/kkorenn/KorenResourcePack";

    // GitHub repo that hosts the release builds the updater pulls from.
    // (Releases are created by tools/release.sh.)
    public const string RepoOwner = "kkorenn";
    public const string RepoName = "KorenResourcePack_v2";
}
