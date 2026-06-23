using System;

namespace Quartz.Core;

public enum ReleaseChannel {
    Dev = -1, // below Alpha — "dev" builds only, never offered to other channels
    Alpha = 0,
    Beta = 1,
    ReleaseCandidate = 2,
    Stable = 3,
}

// A small SemVer-style version used for release ordering: MAJOR.MINOR.PATCH
// with an optional "channel.build" pre-release (alpha / beta / rc). Stable
// carries no pre-release and outranks every pre-release of the same core
// version. This exists so an updater can order builds correctly — a plain
// string compare gets cases like "alpha.10" vs "alpha.2" backwards.
public readonly struct SemVer : IComparable<SemVer> {
    public readonly int Major;
    public readonly int Minor;
    public readonly int Patch;
    public readonly ReleaseChannel Channel;
    public readonly int Build; // pre-release build number; ignored when Stable

    public SemVer(int major, int minor, int patch, ReleaseChannel channel, int build) {
        Major = major;
        Minor = minor;
        Patch = patch;
        Channel = channel;
        Build = build;
    }

    public bool IsPrerelease => Channel != ReleaseChannel.Stable;

    public static ReleaseChannel ParseChannel(string channel) =>
        (channel ?? string.Empty).Trim().ToLowerInvariant() switch {
            "dev" => ReleaseChannel.Dev,
            "alpha" => ReleaseChannel.Alpha,
            "beta" => ReleaseChannel.Beta,
            "rc" or "releasecandidate" or "release-candidate" => ReleaseChannel.ReleaseCandidate,
            _ => ReleaseChannel.Stable,
        };

    public static string ChannelTag(ReleaseChannel channel) => channel switch {
        ReleaseChannel.Dev => "dev",
        ReleaseChannel.Alpha => "alpha",
        ReleaseChannel.Beta => "beta",
        ReleaseChannel.ReleaseCandidate => "rc",
        _ => "",
    };

    // Parses "MAJOR.MINOR.PATCH" with an optional "-channel.build" suffix, e.g.
    // "2.0.0-alpha.1" or "2.0.0". A leading 'v' is tolerated.
    public static bool TryParse(string text, out SemVer version) {
        version = default;
        if(string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        string s = text.Trim();
        if(s.StartsWith("v") || s.StartsWith("V")) {
            s = s[1..];
        }

        ReleaseChannel channel = ReleaseChannel.Stable;
        int build = 0;

        int dash = s.IndexOf('-');
        if(dash >= 0) {
            string pre = s[(dash + 1)..];
            s = s[..dash];

            string chan = pre;
            int sep = pre.IndexOfAny(new[] { '-', '.' });
            if(sep >= 0) {
                chan = pre[..sep];
                int.TryParse(pre[(sep + 1)..], out build);
            }
            channel = ParseChannel(chan);
        }

        string[] core = s.Split('.');
        if(core.Length < 3
            || !int.TryParse(core[0], out int major)
            || !int.TryParse(core[1], out int minor)
            || !int.TryParse(core[2], out int patch)) {
            return false;
        }

        version = new SemVer(major, minor, patch, channel, build);
        return true;
    }

    public int CompareTo(SemVer other) {
        int c = Major.CompareTo(other.Major);
        if(c != 0) {
            return c;
        }
        c = Minor.CompareTo(other.Minor);
        if(c != 0) {
            return c;
        }
        c = Patch.CompareTo(other.Patch);
        if(c != 0) {
            return c;
        }
        // Alpha < Beta < ReleaseCandidate < Stable.
        c = Channel.CompareTo(other.Channel);
        if(c != 0) {
            return c;
        }
        // A stable release has no build component to compare.
        return Channel == ReleaseChannel.Stable ? 0 : Build.CompareTo(other.Build);
    }

    // > 0 when `a` is newer than `b`, < 0 when older, 0 when equal.
    // Unparseable input sorts as the oldest possible version.
    public static int Compare(string a, string b) {
        SemVer va = TryParse(a, out SemVer pa) ? pa : default;
        SemVer vb = TryParse(b, out SemVer pb) ? pb : default;
        return va.CompareTo(vb);
    }

    public override string ToString() =>
        Channel == ReleaseChannel.Stable
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{ChannelTag(Channel)}-{Build}";
}
