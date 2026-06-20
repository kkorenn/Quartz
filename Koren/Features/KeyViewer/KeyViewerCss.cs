#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Koren.Features.KeyViewer;

// Unity-free parser for DM Note "custom CSS". It implements the DM Note CSS
// contract (github.com/DmNote-App/DmNote, docs/custom-css) plus the wider set of
// web effects the key viewer reproduces: per-glyph gradients, :before/:after
// pseudo layers, @font-face fonts, transform, filter, transition, mix-blend-mode
// and backdrop-filter. The output is an engine-agnostic style model the
// overlay's runtime layer turns into meshes, materials and animations. Keeping
// this free of UnityEngine lets it run under the plain-.NET unit tests.

// An RGBA colour in 0..1 components. Has=false marks "not specified".
public readonly struct CssColor {
    public readonly float R, G, B, A;
    public readonly bool Has;

    public CssColor(float r, float g, float b, float a) {
        R = r; G = g; B = b; A = a; Has = true;
    }

    public static readonly CssColor Unset = default;
    public static readonly CssColor Transparent = new(0f, 0f, 0f, 0f);

    public CssColor WithAlpha(float a) => new(R, G, B, a);
}

// A parsed linear-gradient(): ordered colour stops, the angle, and the optional
// scrolling animation pulled from an accompanying `animation:` shorthand.
public sealed class CssGradient {
    public readonly List<CssColor> Stops = new();
    // CSS angle convention: 0deg = to top, 90deg = to right, 180deg = to bottom.
    public float AngleDeg = 180f;
    public float AnimSeconds;   // 0 = static
    public bool Animated;
    // background-clip:text — the gradient paints the text, not the box.
    public bool ClipText;
}

// One text-shadow / box-shadow / drop-shadow layer. On=false means "none".
public readonly struct CssShadow {
    public readonly bool On;
    public readonly float X, Y, Blur;
    public readonly CssColor Color;

    public CssShadow(float x, float y, float blur, CssColor color) {
        On = true; X = x; Y = y; Blur = blur; Color = color;
    }
}

public enum CssBlend { Normal, Multiply, Screen, Additive, Darken, Lighten }

// transform: scale()/translate()/rotate(). Identity until something is set.
public sealed class CssTransform {
    public float ScaleX = 1f, ScaleY = 1f, TranslateX, TranslateY, RotateDeg;
    public bool Has;
}

// filter: brightness()/saturate()/contrast() fold into a colour multiply; blur()
// and drop-shadow() are realised by the runtime.
public sealed class CssFilter {
    public float Brightness = 1f, Saturate = 1f, Contrast = 1f, Blur;
    public CssShadow DropShadow;
    public bool Has;
}

// A :before / :after pseudo-element rendered as its own layer behind/over the box.
public sealed class CssLayer {
    public CssColor Bg = CssColor.Unset;
    public CssGradient? Gradient;
    public float? Radius;
    // CSS `inset`: positive shrinks the layer inside the box, negative grows it.
    public float InsetT, InsetR, InsetB, InsetL;
    public float Blur;
    public CssBlend Blend = CssBlend.Normal;
    public int Z;
    public bool Has;
}

public sealed class CssFontFace {
    public string Family = "";
    public readonly List<string> Srcs = new();
}

// Resolved key style for one state (idle or active). Absent fields = "leave the
// preset value untouched".
public sealed class CssKeyStyle {
    public float? Radius;
    public CssColor Bg = CssColor.Unset;
    public CssGradient? BgGradient;
    public float? BorderWidth;
    public CssColor BorderColor = CssColor.Unset;
    public CssColor TextColor = CssColor.Unset;
    public CssGradient? TextGradient;
    public float? FontSize;
    public bool? Bold;
    public string? FontFamily;
    public float? OffsetX, OffsetY;
    public CssShadow TextShadow;
    public CssShadow BoxShadow;
    public CssTransform? Transform;
    public CssFilter? Filter;
    public float? TransitionSeconds;
    public CssBlend Blend = CssBlend.Normal;
    public float? BackdropBlur;
    public CssLayer? Before;
    public CssLayer? After;

    public bool Any =>
        Radius.HasValue || Bg.Has || BgGradient != null || BorderWidth.HasValue
        || BorderColor.Has || TextColor.Has || TextGradient != null
        || FontSize.HasValue || Bold.HasValue || FontFamily != null
        || OffsetX.HasValue || OffsetY.HasValue || TextShadow.On || BoxShadow.On
        || Transform != null || Filter != null || TransitionSeconds.HasValue
        || Blend != CssBlend.Normal || BackdropBlur.HasValue
        || Before != null || After != null;
}

public sealed class CssKeyStyleSet {
    public readonly CssKeyStyle Idle = new();
    public readonly CssKeyStyle Active = new();
    public bool Any => Idle.Any || Active.Any;
}

public sealed class CssCounterStyle {
    public CssColor Color = CssColor.Unset;
    public CssGradient? Gradient;
    public CssColor StrokeColor = CssColor.Unset;
    public float? StrokeWidth;
    public float? FontSize;
    public bool? Bold;
    public CssShadow TextShadow;

    public bool Any =>
        Color.Has || Gradient != null || StrokeColor.Has || StrokeWidth.HasValue
        || FontSize.HasValue || Bold.HasValue || TextShadow.On;
}

public sealed class CssCounterStyleSet {
    public readonly CssCounterStyle Idle = new();
    public readonly CssCounterStyle Active = new();
    public bool Any => Idle.Any || Active.Any;
}

// Declaration store for one (target, state, pseudo) slot: global declarations
// plus class-scoped rules matched by subset so both `.blue` and `.blue.special`
// resolve with proper specificity.
internal sealed class CssBucket {
    public readonly Dictionary<string, string> Global = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<(string[] classes, Dictionary<string, string> decls)> Classes = new();

    public void Add(string[]? classes, Dictionary<string, string> decls) {
        if(classes == null || classes.Length == 0) {
            KeyViewerStylesheet.Overlay(Global, decls);
        } else {
            Classes.Add((classes, decls));
        }
    }

    // Global first, then every class rule whose classes are all present on the
    // key, lowest specificity (fewest classes) first so the most specific wins.
    public Dictionary<string, string> Flatten(HashSet<string> keyClasses) {
        var merged = new Dictionary<string, string>(Global, StringComparer.OrdinalIgnoreCase);
        if(Classes.Count > 0) {
            foreach((string[] classes, Dictionary<string, string> decls) in Classes.OrderBy(c => c.classes.Length)) {
                if(AllPresent(classes, keyClasses)) {
                    KeyViewerStylesheet.Overlay(merged, decls);
                }
            }
        }
        return merged;
    }

    private static bool AllPresent(string[] classes, HashSet<string> have) {
        for(int i = 0; i < classes.Length; i++) {
            if(!have.Contains(classes[i])) {
                return false;
            }
        }
        return true;
    }
}

public sealed class KeyViewerStylesheet {
    private readonly CssBucket _keyIdle = new(), _keyActive = new();
    private readonly CssBucket _beforeIdle = new(), _beforeActive = new();
    private readonly CssBucket _afterIdle = new(), _afterActive = new();
    private readonly CssBucket _ctrIdle = new(), _ctrActive = new();

    public List<CssFontFace> FontFaces { get; } = new();
    public bool IsEmpty { get; private set; } = true;

    public static KeyViewerStylesheet Parse(string? css) {
        var sheet = new KeyViewerStylesheet();
        if(string.IsNullOrWhiteSpace(css)) {
            return sheet;
        }

        foreach((string prelude, string body) in CssReader.Rules(StripComments(css!))) {
            if(prelude.StartsWith("@", StringComparison.Ordinal)) {
                string at = prelude.TrimStart('@').TrimStart();
                if(at.StartsWith("font-face", StringComparison.OrdinalIgnoreCase)) {
                    sheet.AddFontFace(ParseDeclarations(body));
                } else if(at.StartsWith("media", StringComparison.OrdinalIgnoreCase)
                    || at.StartsWith("supports", StringComparison.OrdinalIgnoreCase)) {
                    foreach((string p2, string b2) in CssReader.Rules(body)) {
                        if(!p2.StartsWith("@", StringComparison.Ordinal)) {
                            sheet.AddRule(p2, ParseDeclarations(b2));
                        }
                    }
                }
                continue;
            }
            sheet.AddRule(prelude, ParseDeclarations(body));
        }

        return sheet;
    }

    private void AddFontFace(Dictionary<string, string> decls) {
        var face = new CssFontFace();
        if(decls.TryGetValue("font-family", out string? fam)) {
            face.Family = fam.Trim().Trim('"', '\'').Trim();
        }
        if(decls.TryGetValue("src", out string? src)) {
            foreach(string part in SplitTopLevel(src, ',')) {
                string url = ExtractUrl(part);
                if(url.Length > 0) {
                    face.Srcs.Add(url);
                }
            }
        }
        if(face.Family.Length > 0 && face.Srcs.Count > 0) {
            FontFaces.Add(face);
            IsEmpty = false;
        }
    }

    private static string ExtractUrl(string part) {
        int u = part.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if(u < 0) {
            return "";
        }
        int lp = u + 3;
        int rp = MatchParen(part, lp);
        if(rp < 0) {
            return "";
        }
        return part.Substring(lp + 1, rp - lp - 1).Trim().Trim('"', '\'').Trim();
    }

    private void AddRule(string selectorList, Dictionary<string, string> decls) {
        if(decls.Count == 0) {
            return;
        }

        foreach(string raw in SplitTopLevel(selectorList, ',')) {
            string sel = raw.Trim();
            if(sel.Length == 0) {
                continue;
            }

            // Pseudo-element → a :before/:after layer on the key it decorates.
            int pseudo = 0; // 0 none, 1 before, 2 after
            string baseSel = sel;
            int dc = sel.IndexOf("::", StringComparison.Ordinal);
            int sc = dc >= 0 ? dc : sel.IndexOf(':', StringComparison.Ordinal);
            if(sc >= 0) {
                string tail = sel.Substring(sc).ToLowerInvariant();
                if(tail.Contains("before")) {
                    pseudo = 1;
                } else if(tail.Contains("after")) {
                    pseudo = 2;
                } else {
                    // A :hover/:focus or similar — not modelled; drop it.
                    continue;
                }
                baseSel = sel.Substring(0, sc);
            }

            string lower = baseSel.ToLowerInvariant();
            bool counter = lower.IndexOf("counter", StringComparison.Ordinal) >= 0;
            bool hasState = lower.IndexOf("data-state", StringComparison.Ordinal) >= 0;
            if(!counter && !hasState) {
                continue; // unrecognised selector (e.g. .kps-graph)
            }

            int state = -1; // -1 both, 0 inactive, 1 active
            if(lower.IndexOf("inactive", StringComparison.Ordinal) >= 0) {
                state = 0;
            } else if(lower.IndexOf("active", StringComparison.Ordinal) >= 0) {
                state = 1;
            }

            string[] classes = ExtractClasses(baseSel);

            if(counter) {
                AddTo(_ctrIdle, _ctrActive, state, classes, decls);
            } else if(pseudo == 1) {
                AddTo(_beforeIdle, _beforeActive, state, classes, decls);
            } else if(pseudo == 2) {
                AddTo(_afterIdle, _afterActive, state, classes, decls);
            } else {
                AddTo(_keyIdle, _keyActive, state, classes, decls);
            }

            IsEmpty = false;
        }
    }

    private static void AddTo(CssBucket idle, CssBucket active, int state, string[] classes, Dictionary<string, string> decls) {
        if(state != 1) {
            idle.Add(classes, decls);
        }
        if(state != 0) {
            active.Add(classes, decls);
        }
    }

    // Every class token in the selector except the structural ".counter".
    private static string[] ExtractClasses(string selector) {
        List<string>? names = null;
        for(int i = 0; i < selector.Length; i++) {
            if(selector[i] != '.') {
                continue;
            }
            int j = i + 1;
            while(j < selector.Length && (char.IsLetterOrDigit(selector[j]) || selector[j] is '-' or '_')) {
                j++;
            }
            string name = selector.Substring(i + 1, j - i - 1);
            if(name.Length > 0 && !name.Equals("counter", StringComparison.OrdinalIgnoreCase)) {
                (names ??= new List<string>()).Add(name);
            }
            i = j - 1;
        }
        return names == null ? Array.Empty<string>() : names.ToArray();
    }

    internal static void Overlay(Dictionary<string, string> into, Dictionary<string, string> from) {
        foreach(KeyValuePair<string, string> kv in from) {
            into[kv.Key] = kv.Value; // later declaration wins (cascade order)
        }
    }

    public CssKeyStyleSet ResolveKey(string? className) {
        HashSet<string> classes = ClassSet(className);
        var set = new CssKeyStyleSet();
        MapKey(_keyIdle.Flatten(classes), set.Idle);
        MapKey(_keyActive.Flatten(classes), set.Active);
        set.Idle.Before = MapLayer(_beforeIdle.Flatten(classes));
        set.Active.Before = MapLayer(_beforeActive.Flatten(classes));
        set.Idle.After = MapLayer(_afterIdle.Flatten(classes));
        set.Active.After = MapLayer(_afterActive.Flatten(classes));
        return set;
    }

    public CssCounterStyleSet ResolveCounter(string? className) {
        HashSet<string> classes = ClassSet(className);
        var set = new CssCounterStyleSet();
        MapCounter(_ctrIdle.Flatten(classes), set.Idle);
        MapCounter(_ctrActive.Flatten(classes), set.Active);
        return set;
    }

    private static HashSet<string> ClassSet(string? className) {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(!string.IsNullOrWhiteSpace(className)) {
            foreach(string c in className!.Split(Space, StringSplitOptions.RemoveEmptyEntries)) {
                set.Add(c);
            }
        }
        return set;
    }

    private static void MapKey(Dictionary<string, string> d, CssKeyStyle s) {
        bool clipText = false;
        CssGradient? bgGradient = null;

        foreach(KeyValuePair<string, string> kv in d) {
            string prop = kv.Key;
            string val = kv.Value;
            switch(prop) {
                case "--key-radius":
                case "border-radius":
                    if(TryLen(val, out float r)) { s.Radius = r; }
                    break;
                case "--key-bg":
                case "background-color":
                    if(TryParseColor(val, out CssColor bg)) { s.Bg = bg; }
                    break;
                case "background":
                case "background-image":
                    if(ParseGradient(val) is { } g) { bgGradient = g; }
                    break;
                case "--key-border":
                case "border":
                    ParseBorder(val, s);
                    break;
                case "border-width":
                    if(TryLen(val, out float bw)) { s.BorderWidth = bw; }
                    break;
                case "border-color":
                    if(TryParseColor(val, out CssColor bc)) { s.BorderColor = bc; }
                    break;
                case "--key-text-color":
                case "color":
                    if(TryParseColor(val, out CssColor tc)) { s.TextColor = tc; }
                    break;
                case "background-clip":
                case "-webkit-background-clip":
                    if(val.Trim().Equals("text", StringComparison.OrdinalIgnoreCase)) { clipText = true; }
                    break;
                case "-webkit-text-fill-color":
                    if(val.Trim().Equals("transparent", StringComparison.OrdinalIgnoreCase)) { clipText = true; }
                    break;
                case "font-size":
                    if(TryLen(val, out float fs)) { s.FontSize = fs; }
                    break;
                case "font-weight":
                    s.Bold = IsBold(val);
                    break;
                case "font-family":
                    s.FontFamily = FirstFamily(val);
                    break;
                case "--key-offset-x":
                    if(TryLen(val, out float ox)) { s.OffsetX = ox; }
                    break;
                case "--key-offset-y":
                    if(TryLen(val, out float oy)) { s.OffsetY = oy; }
                    break;
                case "text-shadow":
                    if(ParseShadow(val) is { } ts) { s.TextShadow = ts; }
                    break;
                case "box-shadow":
                    if(ParseShadow(val) is { } bs) { s.BoxShadow = bs; }
                    break;
                case "transform":
                    s.Transform = ParseTransform(val);
                    break;
                case "filter":
                    s.Filter = ParseFilter(val);
                    break;
                case "transition":
                case "transition-duration":
                    if(TryDuration(val, out float tr)) { s.TransitionSeconds = tr; }
                    break;
                case "mix-blend-mode":
                    s.Blend = ParseBlend(val);
                    break;
                case "backdrop-filter":
                case "-webkit-backdrop-filter":
                    if(FilterBlur(val, out float bb)) { s.BackdropBlur = bb; }
                    break;
            }
        }

        ApplyAnimation(d, bgGradient);
        if(clipText && bgGradient != null) {
            bgGradient.ClipText = true;
            s.TextGradient = bgGradient;
        } else {
            s.BgGradient = bgGradient;
        }
    }

    private static void MapCounter(Dictionary<string, string> d, CssCounterStyle s) {
        CssGradient? gradient = null;
        foreach(KeyValuePair<string, string> kv in d) {
            switch(kv.Key) {
                case "--counter-color":
                case "color":
                    if(TryParseColor(kv.Value, out CssColor c)) { s.Color = c; }
                    break;
                case "background":
                case "background-image":
                    if(ParseGradient(kv.Value) is { } g) { gradient = g; }
                    break;
                case "--counter-stroke-color":
                    if(TryParseColor(kv.Value, out CssColor sc)) { s.StrokeColor = sc; }
                    break;
                case "--counter-stroke-width":
                    if(TryLen(kv.Value, out float sw)) { s.StrokeWidth = sw; }
                    break;
                case "font-size":
                    if(TryLen(kv.Value, out float fs)) { s.FontSize = fs; }
                    break;
                case "font-weight":
                    s.Bold = IsBold(kv.Value);
                    break;
                case "text-shadow":
                    if(ParseShadow(kv.Value) is { } ts) { s.TextShadow = ts; }
                    break;
            }
        }
        ApplyAnimation(d, gradient);
        if(gradient != null) {
            gradient.ClipText = true;
            s.Gradient = gradient;
        }
    }

    // :before / :after layer.
    private static CssLayer? MapLayer(Dictionary<string, string> d) {
        if(d.Count == 0) {
            return null;
        }
        var layer = new CssLayer();
        CssGradient? grad = null;
        foreach(KeyValuePair<string, string> kv in d) {
            string val = kv.Value;
            switch(kv.Key) {
                case "background":
                case "background-image":
                    if(ParseGradient(val) is { } g) { grad = g; } else if(TryParseColor(val, out CssColor bc)) { layer.Bg = bc; layer.Has = true; }
                    break;
                case "background-color":
                    if(TryParseColor(val, out CssColor c)) { layer.Bg = c; layer.Has = true; }
                    break;
                case "border-radius":
                    if(TryLen(val, out float r)) { layer.Radius = r; layer.Has = true; }
                    break;
                case "inset":
                    ParseInset(val, layer);
                    break;
                case "top": if(TryLen(val, out float t)) { layer.InsetT = t; layer.Has = true; } break;
                case "right": if(TryLen(val, out float rr)) { layer.InsetR = rr; layer.Has = true; } break;
                case "bottom": if(TryLen(val, out float b)) { layer.InsetB = b; layer.Has = true; } break;
                case "left": if(TryLen(val, out float l)) { layer.InsetL = l; layer.Has = true; } break;
                case "filter":
                    if(FilterBlur(val, out float blur)) { layer.Blur = blur; layer.Has = true; }
                    break;
                case "mix-blend-mode":
                    layer.Blend = ParseBlend(val); layer.Has = true;
                    break;
                case "z-index":
                    if(int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) { layer.Z = z; layer.Has = true; }
                    break;
            }
        }
        ApplyAnimation(d, grad);
        if(grad != null) {
            layer.Gradient = grad;
            layer.Has = true;
        }
        return layer.Has ? layer : null;
    }

    private static void ApplyAnimation(Dictionary<string, string> d, CssGradient? gradient) {
        if(gradient == null) {
            return;
        }
        if((d.TryGetValue("animation", out string? a) || d.TryGetValue("animation-duration", out a))
            && TryDuration(a, out float seconds) && seconds > 0.01f) {
            gradient.Animated = true;
            gradient.AnimSeconds = seconds;
        }
    }

    // ---- value parsing ------------------------------------------------------

    private static readonly char[] Space = { ' ', '\t', '\n', '\r' };

    private static string? FirstFamily(string v) {
        foreach(string part in SplitTopLevel(v, ',')) {
            string fam = part.Trim().Trim('"', '\'').Trim();
            // Skip generic fallbacks; the runtime can't synthesise them anyway.
            if(fam.Length > 0
                && !fam.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
                && !fam.Equals("serif", StringComparison.OrdinalIgnoreCase)
                && !fam.Equals("monospace", StringComparison.OrdinalIgnoreCase)) {
                return fam;
            }
        }
        return null;
    }

    private static bool IsBold(string v) {
        string t = v.Trim();
        if(t.Equals("bold", StringComparison.OrdinalIgnoreCase) || t.Equals("bolder", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w >= 600;
    }

    private static bool TryLen(string v, out float px) {
        px = 0f;
        string t = v.Trim();
        int i = 0;
        if(i < t.Length && (t[i] == '+' || t[i] == '-')) {
            i++;
        }
        int start = i;
        bool dot = false;
        while(i < t.Length && (char.IsDigit(t[i]) || (t[i] == '.' && !dot))) {
            if(t[i] == '.') { dot = true; }
            i++;
        }
        if(i == start) {
            return false;
        }
        string num = (t.Length > 0 && t[0] == '-' ? "-" : "") + t.Substring(start, i - start);
        return float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out px);
    }

    private static bool TryDuration(string v, out float seconds) {
        seconds = 0f;
        foreach(string tok in v.Split(Space, StringSplitOptions.RemoveEmptyEntries)) {
            string t = tok.Trim();
            if(t.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) {
                if(float.TryParse(t[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ms)) {
                    seconds = ms / 1000f; return true;
                }
            } else if(t.EndsWith("s", StringComparison.OrdinalIgnoreCase)) {
                if(float.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) {
                    seconds = s; return true;
                }
            }
        }
        return false;
    }

    private static void ParseBorder(string v, CssKeyStyle s) {
        string t = v.Trim();
        if(t.Equals("none", StringComparison.OrdinalIgnoreCase) || t.Length == 0) {
            s.BorderWidth = 0f;
            return;
        }
        bool gotWidth = false;
        foreach(string tok in SplitTopLevel(t, ' ')) {
            string p = tok.Trim();
            if(p.Length == 0 || p.Equals("solid", StringComparison.OrdinalIgnoreCase)
                || p.Equals("dashed", StringComparison.OrdinalIgnoreCase)
                || p.Equals("dotted", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if(!gotWidth && TryLen(p, out float w) && !LooksLikeColor(p)) {
                s.BorderWidth = w;
                gotWidth = true;
            } else if(TryParseColor(p, out CssColor c)) {
                s.BorderColor = c;
            }
        }
    }

    private static void ParseInset(string v, CssLayer layer) {
        var vals = new List<float>();
        foreach(string tok in v.Split(Space, StringSplitOptions.RemoveEmptyEntries)) {
            if(TryLen(tok, out float n)) {
                vals.Add(n);
            }
        }
        if(vals.Count == 0) {
            return;
        }
        // CSS shorthand: 1 = all, 2 = T/B + L/R, 3 = T + L/R + B, 4 = T R B L.
        float top = vals[0];
        float right = vals.Count >= 2 ? vals[1] : vals[0];
        float bottom = vals.Count >= 3 ? vals[2] : vals[0];
        float left = vals.Count >= 4 ? vals[3] : (vals.Count >= 2 ? vals[1] : vals[0]);
        layer.InsetT = top; layer.InsetR = right; layer.InsetB = bottom; layer.InsetL = left;
        layer.Has = true;
    }

    private static CssShadow? ParseShadow(string v) {
        string t = v.Trim();
        if(t.Equals("none", StringComparison.OrdinalIgnoreCase) || t.Length == 0) {
            return null;
        }
        CssShadow? best = null;
        foreach(string layer in SplitTopLevel(t, ',')) {
            float x = 0f, y = 0f, blur = 0f;
            int lenIdx = 0;
            CssColor color = CssColor.Unset;
            foreach(string tok in SplitTopLevel(layer.Trim(), ' ')) {
                string p = tok.Trim();
                if(p.Length == 0 || p.Equals("inset", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if(LooksLikeColor(p)) {
                    if(TryParseColor(p, out CssColor c)) { color = c; }
                    continue;
                }
                if(TryLen(p, out float len)) {
                    switch(lenIdx) {
                        case 0: x = len; break;
                        case 1: y = len; break;
                        case 2: blur = len; break;
                    }
                    lenIdx++;
                }
            }
            if(lenIdx == 0) {
                continue;
            }
            var shadow = new CssShadow(x, y, blur, color.Has ? color : new CssColor(0f, 0f, 0f, 1f));
            if(best == null || blur > best.Value.Blur) {
                best = shadow;
            }
        }
        return best;
    }

    private static CssTransform? ParseTransform(string v) {
        var t = new CssTransform();
        foreach((string name, string args) in Functions(v)) {
            List<float> nums = Nums(args);
            switch(name.ToLowerInvariant()) {
                case "scale":
                    if(nums.Count >= 1) { t.ScaleX = nums[0]; t.ScaleY = nums.Count >= 2 ? nums[1] : nums[0]; t.Has = true; }
                    break;
                case "scalex": if(nums.Count >= 1) { t.ScaleX = nums[0]; t.Has = true; } break;
                case "scaley": if(nums.Count >= 1) { t.ScaleY = nums[0]; t.Has = true; } break;
                case "translate":
                    if(nums.Count >= 1) { t.TranslateX = nums[0]; t.TranslateY = nums.Count >= 2 ? nums[1] : 0f; t.Has = true; }
                    break;
                case "translatex": if(nums.Count >= 1) { t.TranslateX = nums[0]; t.Has = true; } break;
                case "translatey": if(nums.Count >= 1) { t.TranslateY = nums[0]; t.Has = true; } break;
                case "rotate": if(nums.Count >= 1) { t.RotateDeg = nums[0]; t.Has = true; } break;
            }
        }
        return t.Has ? t : null;
    }

    private static CssFilter? ParseFilter(string v) {
        var f = new CssFilter();
        foreach((string name, string args) in Functions(v)) {
            switch(name.ToLowerInvariant()) {
                case "brightness": if(TryAmount(args, out float br)) { f.Brightness = br; f.Has = true; } break;
                case "saturate": if(TryAmount(args, out float sa)) { f.Saturate = sa; f.Has = true; } break;
                case "contrast": if(TryAmount(args, out float co)) { f.Contrast = co; f.Has = true; } break;
                case "blur": if(TryLen(args, out float bl)) { f.Blur = bl; f.Has = true; } break;
                case "drop-shadow": if(ParseShadow(args) is { } ds) { f.DropShadow = ds; f.Has = true; } break;
            }
        }
        return f.Has ? f : null;
    }

    // filter:/backdrop-filter: blur(Npx) → N.
    private static bool FilterBlur(string v, out float px) {
        px = 0f;
        foreach((string name, string args) in Functions(v)) {
            if(name.Equals("blur", StringComparison.OrdinalIgnoreCase) && TryLen(args, out px)) {
                return true;
            }
        }
        return false;
    }

    // brightness(1.2) or brightness(120%) → 1.2.
    private static bool TryAmount(string v, out float amount) {
        string t = v.Trim();
        if(t.EndsWith("%", StringComparison.Ordinal)) {
            if(float.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct)) {
                amount = pct / 100f; return true;
            }
        }
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
    }

    private static CssBlend ParseBlend(string v) => v.Trim().ToLowerInvariant() switch {
        "multiply" => CssBlend.Multiply,
        "screen" => CssBlend.Screen,
        "plus-lighter" or "lighter" or "add" or "additive" => CssBlend.Additive,
        "darken" => CssBlend.Darken,
        "lighten" => CssBlend.Lighten,
        _ => CssBlend.Normal,
    };

    private static IEnumerable<(string name, string args)> Functions(string v) {
        int i = 0, n = v.Length;
        while(i < n) {
            while(i < n && (char.IsWhiteSpace(v[i]) || v[i] == ',')) {
                i++;
            }
            int start = i;
            while(i < n && (char.IsLetterOrDigit(v[i]) || v[i] == '-')) {
                i++;
            }
            if(i >= n || v[i] != '(') {
                if(i == start) { i++; }
                continue;
            }
            string name = v.Substring(start, i - start);
            int rp = MatchParen(v, i);
            if(rp < 0) {
                yield break;
            }
            string args = v.Substring(i + 1, rp - i - 1);
            yield return (name, args);
            i = rp + 1;
        }
    }

    private static List<float> Nums(string args) {
        var list = new List<float>();
        foreach(string tok in SplitTopLevel(args, ',')) {
            foreach(string sub in tok.Split(Space, StringSplitOptions.RemoveEmptyEntries)) {
                if(TryLen(sub, out float f)) {
                    list.Add(f);
                }
            }
        }
        return list;
    }

    private static CssGradient? ParseGradient(string v) {
        int idx = v.IndexOf("linear-gradient", StringComparison.OrdinalIgnoreCase);
        bool radial = false;
        if(idx < 0) {
            idx = v.IndexOf("radial-gradient", StringComparison.OrdinalIgnoreCase);
            radial = true;
        }
        if(idx < 0) {
            return null;
        }
        int lp = v.IndexOf('(', idx);
        if(lp < 0) {
            return null;
        }
        int rp = MatchParen(v, lp);
        if(rp < 0) {
            return null;
        }

        string inner = v.Substring(lp + 1, rp - lp - 1);
        var grad = new CssGradient();
        bool first = true;
        foreach(string partRaw in SplitTopLevel(inner, ',')) {
            string part = partRaw.Trim();
            if(part.Length == 0) {
                continue;
            }
            if(first && !radial && IsDirection(part)) {
                grad.AngleDeg = DirectionToAngle(part);
                first = false;
                continue;
            }
            first = false;
            if(TryParseColor(FirstColorToken(part), out CssColor c)) {
                grad.Stops.Add(c);
            }
        }
        return grad.Stops.Count >= 2 ? grad : null;
    }

    private static string FirstColorToken(string part) {
        string t = part.Trim();
        if(t.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || t.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)) {
            int lp = t.IndexOf('(');
            int rp = lp >= 0 ? MatchParen(t, lp) : -1;
            return rp > 0 ? t.Substring(0, rp + 1) : t;
        }
        int sp = t.IndexOf(' ');
        return sp > 0 ? t.Substring(0, sp) : t;
    }

    private static bool IsDirection(string p) {
        string t = p.Trim();
        if(t.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return t.EndsWith("deg", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("turn", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("rad", StringComparison.OrdinalIgnoreCase);
    }

    private static float DirectionToAngle(string p) {
        string t = p.Trim();
        if(t.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) {
            return t.Substring(3).Trim().ToLowerInvariant() switch {
                "top" => 0f,
                "right" => 90f,
                "bottom" => 180f,
                "left" => 270f,
                "top right" or "right top" => 45f,
                "bottom right" or "right bottom" => 135f,
                "bottom left" or "left bottom" => 225f,
                "top left" or "left top" => 315f,
                _ => 180f,
            };
        }
        if(t.EndsWith("deg", StringComparison.OrdinalIgnoreCase) && TryLen(t, out float deg)) {
            return deg;
        }
        if(t.EndsWith("turn", StringComparison.OrdinalIgnoreCase) && TryLen(t, out float turn)) {
            return turn * 360f;
        }
        if(t.EndsWith("rad", StringComparison.OrdinalIgnoreCase) && TryLen(t, out float rad)) {
            return rad * 57.29578f;
        }
        return 180f;
    }

    private static bool LooksLikeColor(string p) {
        string t = p.Trim();
        return t.Length > 0
            && (t[0] == '#'
                || t.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)
                || NamedColor(t, out _));
    }

    public static bool TryParseColor(string v, out CssColor color) {
        color = CssColor.Unset;
        string s = v.Trim();
        if(s.Length == 0) {
            return false;
        }
        if(s.Equals("transparent", StringComparison.OrdinalIgnoreCase)) {
            color = CssColor.Transparent;
            return true;
        }
        if(NamedColor(s, out CssColor named)) {
            color = named;
            return true;
        }
        if(s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) {
            int lp = s.IndexOf('(');
            int rp = lp >= 0 ? s.IndexOf(')', lp) : -1;
            if(lp < 0 || rp < 0) {
                return false;
            }
            string[] parts = s.Substring(lp + 1, rp - lp - 1).Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length < 3) {
                return false;
            }
            color = new CssColor(Comp(parts[0], 255f), Comp(parts[1], 255f), Comp(parts[2], 255f),
                parts.Length >= 4 ? Alpha(parts[3]) : 1f);
            return true;
        }
        if(s.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)) {
            int lp = s.IndexOf('(');
            int rp = lp >= 0 ? s.IndexOf(')', lp) : -1;
            if(lp < 0 || rp < 0) {
                return false;
            }
            string[] parts = s.Substring(lp + 1, rp - lp - 1).Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length < 3) {
                return false;
            }
            float h = float.TryParse(parts[0].Trim().Replace("deg", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float hv) ? hv : 0f;
            float sl = Pct(parts[1]);
            float ll = Pct(parts[2]);
            float a = parts.Length >= 4 ? Alpha(parts[3]) : 1f;
            HslToRgb(h, sl, ll, out float r, out float g, out float b);
            color = new CssColor(r, g, b, a);
            return true;
        }
        string h2 = s.TrimStart('#');
        try {
            switch(h2.Length) {
                case 3:
                case 4:
                    color = new CssColor(Hex(h2[0]) / 15f, Hex(h2[1]) / 15f, Hex(h2[2]) / 15f,
                        h2.Length == 4 ? Hex(h2[3]) / 15f : 1f);
                    return true;
                case 6:
                case 8:
                    color = new CssColor(
                        Convert.ToInt32(h2.Substring(0, 2), 16) / 255f,
                        Convert.ToInt32(h2.Substring(2, 2), 16) / 255f,
                        Convert.ToInt32(h2.Substring(4, 2), 16) / 255f,
                        h2.Length == 8 ? Convert.ToInt32(h2.Substring(6, 2), 16) / 255f : 1f);
                    return true;
            }
        } catch {
            return false;
        }
        return false;
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b) {
        h = ((h % 360f) + 360f) % 360f / 360f;
        if(s <= 0f) {
            r = g = b = l;
            return;
        }
        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        r = HueToRgb(p, q, h + 1f / 3f);
        g = HueToRgb(p, q, h);
        b = HueToRgb(p, q, h - 1f / 3f);
    }

    private static float HueToRgb(float p, float q, float t) {
        if(t < 0f) { t += 1f; }
        if(t > 1f) { t -= 1f; }
        if(t < 1f / 6f) { return p + (q - p) * 6f * t; }
        if(t < 1f / 2f) { return q; }
        if(t < 2f / 3f) { return p + (q - p) * (2f / 3f - t) * 6f; }
        return p;
    }

    private static int Hex(char c) => Convert.ToInt32(c.ToString(), 16);

    private static float Comp(string s, float scale) {
        string t = s.Trim();
        if(t.EndsWith("%", StringComparison.Ordinal)) {
            return float.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct) ? Clamp01(pct / 100f) : 1f;
        }
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Clamp01(v / scale) : 1f;
    }

    private static float Pct(string s) {
        string t = s.Trim().TrimEnd('%').Trim();
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Clamp01(v / 100f) : 0f;
    }

    private static float Alpha(string s) {
        string t = s.Trim();
        if(t.EndsWith("%", StringComparison.Ordinal)) {
            return float.TryParse(t.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct) ? Clamp01(pct / 100f) : 1f;
        }
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Clamp01(v <= 1f ? v : v / 255f) : 1f;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    private static bool NamedColor(string name, out CssColor color) {
        switch(name.Trim().ToLowerInvariant()) {
            case "white": color = new CssColor(1f, 1f, 1f, 1f); return true;
            case "black": color = new CssColor(0f, 0f, 0f, 1f); return true;
            case "red": color = new CssColor(1f, 0f, 0f, 1f); return true;
            case "green": color = new CssColor(0f, 0.5f, 0f, 1f); return true;
            case "lime": color = new CssColor(0f, 1f, 0f, 1f); return true;
            case "blue": color = new CssColor(0f, 0f, 1f, 1f); return true;
            case "yellow": color = new CssColor(1f, 1f, 0f, 1f); return true;
            case "cyan": case "aqua": color = new CssColor(0f, 1f, 1f, 1f); return true;
            case "magenta": case "fuchsia": color = new CssColor(1f, 0f, 1f, 1f); return true;
            case "gray": case "grey": color = new CssColor(0.5f, 0.5f, 0.5f, 1f); return true;
            case "silver": color = new CssColor(0.75f, 0.75f, 0.75f, 1f); return true;
            case "orange": color = new CssColor(1f, 0.647f, 0f, 1f); return true;
            case "pink": color = new CssColor(1f, 0.753f, 0.796f, 1f); return true;
            case "purple": color = new CssColor(0.5f, 0f, 0.5f, 1f); return true;
            default: color = CssColor.Unset; return false;
        }
    }

    // ---- declaration / structural parsing -----------------------------------

    internal static Dictionary<string, string> ParseDeclarations(string body) {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach(string declRaw in SplitTopLevel(body, ';')) {
            string decl = declRaw.Trim();
            if(decl.Length == 0) {
                continue;
            }
            int colon = IndexOfTopLevel(decl, ':');
            if(colon <= 0) {
                continue;
            }
            string name = decl.Substring(0, colon).Trim().ToLowerInvariant();
            string value = decl.Substring(colon + 1).Trim();
            int bang = value.IndexOf('!');
            if(bang >= 0) {
                value = value.Substring(0, bang).Trim();
            }
            if(name.Length > 0 && value.Length > 0) {
                d[name] = value;
            }
        }
        return d;
    }

    internal static string StripComments(string css) {
        var sb = new StringBuilder(css.Length);
        for(int i = 0; i < css.Length; i++) {
            if(i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*') {
                int end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if(end < 0) {
                    break;
                }
                i = end + 1;
                continue;
            }
            sb.Append(css[i]);
        }
        return sb.ToString();
    }

    internal static List<string> SplitTopLevel(string s, char sep) {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for(int i = 0; i < s.Length; i++) {
            char c = s[i];
            if(c is '(' or '[') {
                depth++;
            } else if(c is ')' or ']') {
                if(depth > 0) { depth--; }
            } else if(c == sep && depth == 0) {
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(s.Substring(start));
        return parts;
    }

    private static int IndexOfTopLevel(string s, char target) {
        int depth = 0;
        for(int i = 0; i < s.Length; i++) {
            char c = s[i];
            if(c is '(' or '[') { depth++; } else if(c is ')' or ']') { if(depth > 0) { depth--; } } else if(c == target && depth == 0) {
                return i;
            }
        }
        return -1;
    }

    private static int MatchParen(string s, int open) {
        int depth = 0;
        for(int i = open; i < s.Length; i++) {
            if(s[i] == '(') { depth++; } else if(s[i] == ')') {
                depth--;
                if(depth == 0) { return i; }
            }
        }
        return -1;
    }
}

// Walks a CSS string into (prelude, body) rule pairs, honouring nested braces so
// @media/@keyframes blocks are returned whole rather than split mid-block.
internal static class CssReader {
    public static IEnumerable<(string prelude, string body)> Rules(string css) {
        int i = 0, n = css.Length;
        while(i < n) {
            while(i < n && char.IsWhiteSpace(css[i])) {
                i++;
            }
            if(i >= n) {
                yield break;
            }

            int start = i;
            int depth = 0;
            int braceOpen = -1;
            while(i < n) {
                char c = css[i];
                if(c == '{') {
                    if(depth == 0) {
                        braceOpen = i;
                    }
                    depth++;
                } else if(c == '}') {
                    depth--;
                    if(depth == 0) {
                        break;
                    }
                } else if(c == ';' && depth == 0) {
                    break;
                }
                i++;
            }

            if(braceOpen < 0) {
                i++;
                continue;
            }

            string prelude = css.Substring(start, braceOpen - start).Trim();
            int bodyStart = braceOpen + 1;
            int bodyEnd = i < n ? i : n;
            string body = css.Substring(bodyStart, Math.Max(0, bodyEnd - bodyStart));
            i++;
            yield return (prelude, body);
        }
    }
}
