using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Panels;

// One stat placed on a panel: which catalog stat, and whether its line is
// currently shown (entries can be disabled without losing their spot).
public sealed class StatEntry {
    public string Id = "";
    public bool Enabled = true;

    // Show this stat's label before its value. Off = render only the number
    // (the panel's LabelSeparator is skipped too).
    public bool ShowLabel = true;

    // Optional per-stat value coloring (v1's ColorRange). null until the user
    // opens the stat's color settings.
    public StatColor Color;

    public StatEntry() { }
    public StatEntry(string id) => Id = id;

    // Lazily seeds the stat's color settings with the v1 defaults for its id.
    public StatColor EnsureColor() => Color ??= StatColor.DefaultFor(Id);

    public JToken Serialize() {
        JObject obj = new() {
            [nameof(Id)] = Id,
            [nameof(Enabled)] = Enabled,
            [nameof(ShowLabel)] = ShowLabel,
        };

        if(Color != null) {
            obj[nameof(Color)] = Color.Serialize();
        }

        return obj;
    }

    public static StatEntry Deserialize(JToken token) {
        // Legacy shape: a plain stat-id string.
        if(token is JValue) {
            return new StatEntry(token.ToString());
        }

        StatEntry e = new();
        e.Id = IOUtils.Read(token, nameof(Id), e.Id);
        e.Enabled = IOUtils.Read(token, nameof(Enabled), e.Enabled);
        e.ShowLabel = IOUtils.Read(token, nameof(ShowLabel), e.ShowLabel);
        if(token[nameof(Color)] is JObject color) {
            e.Color = StatColor.Deserialize(color);
        }
        return e;
    }
}

// Which screen corner/edge a panel hangs off. The panel's offset (PosX/PosY)
// is relative to this anchor, and the panel grows away from it.
public enum PanelAnchor {
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

// One user-created overlay panel: a named, draggable HUD box showing the
// stats the user put on it, with its own appearance settings.
public sealed class PanelConfig {
    public string Name = "Panel";

    // Screen anchor + offset from it; dragged around in Reorganize mode.
    public int Anchor = (int)PanelAnchor.TopLeft;
    public float PosX = 24f;
    public float PosY = -24f;

    // Default inset for an anchor: 24px in from each non-centered edge.
    public static Vector2 DefaultOffset(PanelAnchor anchor) {
        Vector2 a = AnchorVector(anchor);
        return new Vector2(
            a.x == 0f ? 24f : a.x == 1f ? -24f : 0f,
            a.y == 0f ? 24f : a.y == 1f ? -24f : 0f
        );
    }

    public static Vector2 AnchorVector(PanelAnchor anchor) => anchor switch {
        PanelAnchor.TopLeft => new Vector2(0f, 1f),
        PanelAnchor.TopCenter => new Vector2(0.5f, 1f),
        PanelAnchor.TopRight => new Vector2(1f, 1f),
        PanelAnchor.MiddleLeft => new Vector2(0f, 0.5f),
        PanelAnchor.MiddleCenter => new Vector2(0.5f, 0.5f),
        PanelAnchor.MiddleRight => new Vector2(1f, 0.5f),
        PanelAnchor.BottomLeft => new Vector2(0f, 0f),
        PanelAnchor.BottomCenter => new Vector2(0.5f, 0f),
        PanelAnchor.BottomRight => new Vector2(1f, 0f),
        _ => new Vector2(0f, 1f),
    };

    // Ordered stat entries (catalog ids); list order = display order.
    public List<StatEntry> Stats = [];

    public string Prefix = "";
    public int Decimals = 2;
    public float FontSize = 22f;
    // Drawn between label and value; a single character is auto-padded with a
    // space each side at render (see PanelsOverlay.EffectiveSeparator). "|" → " | ".
    public string LabelSeparator = "|";
    public float LineSpacing = 0f;
    public bool BackgroundEnabled = true;

    // Stat labels on this panel stay English by default; on = follow the UI
    // language. The settings UI always shows localized labels regardless.
    public bool LocalizeStatLabels = false;

    public float TextR = 1f;
    public float TextG = 1f;
    public float TextB = 1f;
    public float TextA = 1f;

    public bool TextShadowEnabled = true;
    public float TextShadowX = 2f;
    public float TextShadowY = -2f;
    public float TextShadowSoftness = 0f;
    public float TextShadowR = 0f;
    public float TextShadowG = 0f;
    public float TextShadowB = 0f;
    public float TextShadowA = 0.75f;

    public Color GetTextColor() => new(
        Mathf.Clamp01(TextR),
        Mathf.Clamp01(TextG),
        Mathf.Clamp01(TextB),
        Mathf.Clamp01(TextA)
    );

    public void SetTextColor(Color c) {
        TextR = Mathf.Clamp01(c.r);
        TextG = Mathf.Clamp01(c.g);
        TextB = Mathf.Clamp01(c.b);
        TextA = Mathf.Clamp01(c.a);
    }

    public Color GetTextShadowColor() => new(
        Mathf.Clamp01(TextShadowR),
        Mathf.Clamp01(TextShadowG),
        Mathf.Clamp01(TextShadowB),
        Mathf.Clamp01(TextShadowA)
    );

    public void SetTextShadowColor(Color c) {
        TextShadowR = Mathf.Clamp01(c.r);
        TextShadowG = Mathf.Clamp01(c.g);
        TextShadowB = Mathf.Clamp01(c.b);
        TextShadowA = Mathf.Clamp01(c.a);
    }

    public JToken Serialize() {
        JArray stats = [];
        foreach(StatEntry e in Stats) {
            stats.Add(e.Serialize());
        }

        return new JObject {
            [nameof(Name)] = Name,
            [nameof(Anchor)] = Anchor,
            [nameof(PosX)] = PosX,
            [nameof(PosY)] = PosY,
            [nameof(Stats)] = stats,
            [nameof(Prefix)] = Prefix,
            [nameof(Decimals)] = Decimals,
            [nameof(FontSize)] = FontSize,
            [nameof(LabelSeparator)] = LabelSeparator,
            [nameof(LineSpacing)] = LineSpacing,
            [nameof(BackgroundEnabled)] = BackgroundEnabled,
            [nameof(LocalizeStatLabels)] = LocalizeStatLabels,
            [nameof(TextR)] = TextR,
            [nameof(TextG)] = TextG,
            [nameof(TextB)] = TextB,
            [nameof(TextA)] = TextA,
            [nameof(TextShadowEnabled)] = TextShadowEnabled,
            [nameof(TextShadowX)] = TextShadowX,
            [nameof(TextShadowY)] = TextShadowY,
            [nameof(TextShadowSoftness)] = TextShadowSoftness,
            [nameof(TextShadowR)] = TextShadowR,
            [nameof(TextShadowG)] = TextShadowG,
            [nameof(TextShadowB)] = TextShadowB,
            [nameof(TextShadowA)] = TextShadowA,
        };
    }

    public static PanelConfig Deserialize(JToken token) {
        PanelConfig p = new();
        if(token == null) {
            return p;
        }

        p.Name = IOUtils.Read(token, nameof(Name), p.Name);
        p.Anchor = IOUtils.Read(token, nameof(Anchor), p.Anchor);
        p.PosX = IOUtils.Read(token, nameof(PosX), p.PosX);
        p.PosY = IOUtils.Read(token, nameof(PosY), p.PosY);
        if(token[nameof(Stats)] is JArray arr) {
            p.Stats = [];
            foreach(JToken t in arr) {
                StatEntry e = StatEntry.Deserialize(t);
                if(!string.IsNullOrEmpty(e.Id)) {
                    p.Stats.Add(e);
                }
            }
        }
        p.Prefix = IOUtils.Read(token, nameof(Prefix), p.Prefix);
        p.Decimals = IOUtils.Read(token, nameof(Decimals), p.Decimals);
        p.FontSize = IOUtils.Read(token, nameof(FontSize), p.FontSize);
        p.LabelSeparator = IOUtils.Read(token, nameof(LabelSeparator), p.LabelSeparator);
        p.LineSpacing = IOUtils.Read(token, nameof(LineSpacing), p.LineSpacing);
        p.BackgroundEnabled = IOUtils.Read(token, nameof(BackgroundEnabled), p.BackgroundEnabled);
        p.LocalizeStatLabels = IOUtils.Read(token, nameof(LocalizeStatLabels), p.LocalizeStatLabels);
        p.TextR = IOUtils.Read(token, nameof(TextR), p.TextR);
        p.TextG = IOUtils.Read(token, nameof(TextG), p.TextG);
        p.TextB = IOUtils.Read(token, nameof(TextB), p.TextB);
        p.TextA = IOUtils.Read(token, nameof(TextA), p.TextA);
        p.TextShadowEnabled = IOUtils.Read(token, nameof(TextShadowEnabled), p.TextShadowEnabled);
        p.TextShadowX = IOUtils.Read(token, nameof(TextShadowX), p.TextShadowX);
        p.TextShadowY = IOUtils.Read(token, nameof(TextShadowY), p.TextShadowY);
        p.TextShadowSoftness = IOUtils.Read(token, nameof(TextShadowSoftness), p.TextShadowSoftness);
        p.TextShadowR = IOUtils.Read(token, nameof(TextShadowR), p.TextShadowR);
        p.TextShadowG = IOUtils.Read(token, nameof(TextShadowG), p.TextShadowG);
        p.TextShadowB = IOUtils.Read(token, nameof(TextShadowB), p.TextShadowB);
        p.TextShadowA = IOUtils.Read(token, nameof(TextShadowA), p.TextShadowA);
        return p;
    }
}

// Persisted config for the panel overlay system. Replaces the old fixed
// Left/Right Status HUD (Status.json) — panels are user-created, named and
// freely composed instead. Lives in UserData/Koren/OverlayPanels.json.
public sealed class PanelsSettings : ISettingsFile {
    // Master switch for the whole overlay system ("Enable Overlays") —
    // ProgressBar / Combo / Judgement HUDs gate on it too, like they did on
    // the old Status master.
    public bool Enabled = true;

    // Default layout matches the shipped Default profile: three corner panels
    // (top-left run stats, top-right tempo, bottom-right attempts), all sharing
    // the " | " separator, no background, and a soft drop shadow.
    public List<PanelConfig> Panels = [
        new PanelConfig {
            Name = "left",
            Anchor = (int)PanelAnchor.TopLeft,
            PosX = 22.7f,
            PosY = -19.5f,
            Stats = [new("progress"), new("best"), new("xaccuracy"), new("maxaccuracy"), new("fps")],
            LabelSeparator = "|",
            BackgroundEnabled = false,
            TextShadowX = 1.5f,
            TextShadowY = -1.5f,
            TextShadowA = 0.5f,
        },
        new PanelConfig {
            Name = "right",
            Anchor = (int)PanelAnchor.TopRight,
            PosX = -19.2f,
            PosY = -28.5f,
            Stats = [new("tbpm"), new("cbpm"), new("kps")],
            LabelSeparator = "|",
            BackgroundEnabled = false,
            TextShadowX = 1.5f,
            TextShadowY = -1.5f,
            TextShadowA = 0.5f,
        },
        new PanelConfig {
            Name = "attempts",
            Anchor = (int)PanelAnchor.BottomRight,
            PosX = 1.5f,
            PosY = 63.8f,
            Stats = [new("attempt"), new("totalattempts")],
            LabelSeparator = "|",
            BackgroundEnabled = false,
            TextShadowX = 1.5f,
            TextShadowY = -1.5f,
        },
    ];

    public JToken Serialize() {
        JArray panels = [];
        foreach(PanelConfig p in Panels) {
            panels.Add(p.Serialize());
        }

        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(Panels)] = panels,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);

        if(token?[nameof(Panels)] is JArray arr) {
            Panels = [];
            foreach(JToken t in arr) {
                Panels.Add(PanelConfig.Deserialize(t));
            }
        }
    }
}
