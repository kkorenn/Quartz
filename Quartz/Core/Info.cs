namespace Quartz.Core;

public static class Info {
    public const string Name = "Quartz";
    public const string Author = "koren";

    // Core version (MAJOR.MINOR.PATCH). Parsed into a System.Version at runtime,
    // so keep it dotted numbers only.
    public const string Version = "2.0.0";

    // Release channel. Move it forward as the project matures:
    //   "alpha" -> "beta" -> "rc" -> "stable".
    public const string Channel = "alpha";

    // Pre-release build number. The single source of truth is build.json
    // (per version+channel); tools/release.sh bumps it there. At compile time
    // the csproj's GenerateBuildInfo target reads build.json for this
    // version+channel and emits BuildInfo.Number (obj/BuildInfo.g.cs), so the
    // DLL always carries whatever build.json says — nothing is hand-edited or
    // baked into this file. Defaults to 0 when build.json has no entry yet.
    public static readonly int Build = BuildInfo.Number;

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
    public const string GithubLink = "https://github.com/kkorenn/Quartz";

    // GitHub repo that hosts the release builds the updater pulls from.
    // (Releases are created by tools/release.sh.)
    public const string RepoOwner = "kkorenn";
    public const string RepoName = "Quartz";
}
