using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Koren.Core;
using Koren.Resource;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

using TMPro;

namespace Koren.Features.KeyViewer;

// The Unity-facing half of the DM Note custom-CSS feature. The engine-agnostic
// parser (KeyViewerCss.cs) turns a stylesheet into a typed model; this partial
// renders it: per-glyph gradients, box-shadow halos, :before/:after layers,
// runtime @font-face fonts, transform/filter/transition and a frosted
// backdrop approximation.
//
// Performance: per-frame work is confined to the `cssFx` list (boxes with an
// animated gradient, a transition in flight, or an animated pseudo layer).
// Everything else is applied once on a press/release. Gradient textures, the
// halo sprite and downloaded fonts are cached and shared; the per-glyph colour
// pass writes Color32s in place and uploads only the colour stream.
public static partial class KeyViewerOverlay {
    private static readonly List<Box> cssFx = [];

    private static string cssCacheKey;
    private static KeyViewerStylesheet cssCache;

    private static Sprite glowSprite;
    private static RectTransform cssGlowLayer;

    // Shared, content-keyed texture cache for fill / pseudo gradients.
    private static readonly Dictionary<string, Texture2D> gradTex = [];
    // Resolved @font-face / font-family assets, keyed by family name.
    private static readonly Dictionary<string, TMP_FontAsset> cssFonts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> cssFontPending = new(StringComparer.OrdinalIgnoreCase);
    // Set by a finished background download; the Updater consumes it to rebuild.
    private static volatile bool cssFontArrived;

    private static KeyViewerStylesheet GetStylesheet(string text) {
        if(cssCache == null || !string.Equals(cssCacheKey, text, StringComparison.Ordinal)) {
            cssCache = KeyViewerStylesheet.Parse(text);
            cssCacheKey = text;
        }
        return cssCache;
    }

    // Overlays the active stylesheet onto every parsed spec. Free when the CSS
    // layer is off or empty, so non-CSS presets are untouched.
    private static void ApplyCssToSpecs(List<DmNoteSpec> specs) {
        if(specs.Count == 0 || Conf == null || !Conf.DmCssEnabled
            || string.IsNullOrWhiteSpace(Conf.DmCssText)) {
            return;
        }

        try {
            KeyViewerStylesheet sheet = GetStylesheet(Conf.DmCssText);
            if(sheet.IsEmpty) {
                return;
            }

            EnsureFontFaces(sheet);

            foreach(DmNoteSpec spec in specs) {
                ApplyKeyStyle(spec, sheet.ResolveKey(spec.ClassName));
                ApplyCounterStyle(spec, sheet.ResolveCounter(spec.ClassName));
            }
        } catch(Exception ex) {
            MainCore.Log.Msg("[KeyViewer] CSS apply failed: " + ex.Message);
        }
    }

    private static void ApplyKeyStyle(DmNoteSpec spec, CssKeyStyleSet set) {
        CssKeyStyle i = set.Idle, a = set.Active;

        if(i.Radius.HasValue) { spec.BorderRadius = Mathf.Clamp(i.Radius.Value, 0f, 100f); }
        if(a.Radius.HasValue) { spec.BorderRadius = Mathf.Clamp(a.Radius.Value, 0f, 100f); }
        if(i.FontSize.HasValue) { spec.FontSize = Mathf.Max(1, Mathf.RoundToInt(i.FontSize.Value)); }
        if(a.FontSize.HasValue) { spec.FontSize = Mathf.Max(1, Mathf.RoundToInt(a.FontSize.Value)); }
        if(i.Bold.HasValue) { spec.Bold = i.Bold.Value; }
        if(a.Bold.HasValue) { spec.Bold = a.Bold.Value; }
        float? borderW = a.BorderWidth ?? i.BorderWidth;
        if(borderW.HasValue) { spec.BoxBorderWidth = Mathf.Clamp(borderW.Value, 0f, 20f); }

        if(i.Bg.Has) { spec.Bg = ToColor(i.Bg); }
        if(i.BorderColor.Has) { spec.Outline = ToColor(i.BorderColor); }
        if(i.TextColor.Has) { spec.Text = ToColor(i.TextColor); }
        spec.FillGradient = AssignGradient(ToGradient(i.BgGradient), ref spec.Bg);
        spec.LabelGradient = AssignGradient(ToGradient(i.TextGradient), ref spec.Text);
        if(i.TextShadow.On) { spec.LabelGlow = ToGlow(i.TextShadow); }
        if(i.BoxShadow.On) { spec.BoxGlow = ToGlow(i.BoxShadow); }

        if(a.Bg.Has) { spec.ActiveBg = ToColor(a.Bg); }
        if(a.BorderColor.Has) { spec.ActiveOutline = ToColor(a.BorderColor); }
        if(a.TextColor.Has) { spec.ActiveText = ToColor(a.TextColor); }
        spec.ActiveFillGradient = AssignGradient(ToGradient(a.BgGradient), ref spec.ActiveBg);
        spec.ActiveLabelGradient = AssignGradient(ToGradient(a.TextGradient), ref spec.ActiveText);
        if(a.TextShadow.On) { spec.ActiveLabelGlow = ToGlow(a.TextShadow); }
        if(a.BoxShadow.On) { spec.ActiveBoxGlow = ToGlow(a.BoxShadow); }

        // transform: translate folds into the offset; --key-offset-* adds on top.
        spec.IdleOffset = Translate(i) ;
        spec.ActiveOffset = Translate(a);
        if(a.OffsetX.HasValue) { spec.ActiveOffset.x += a.OffsetX.Value; }
        if(a.OffsetY.HasValue) { spec.ActiveOffset.y += a.OffsetY.Value; }
        // Legacy fields kept in sync so NeedsCssState / older paths still see them.
        spec.ActiveOffsetX = spec.ActiveOffset.x;
        spec.ActiveOffsetY = spec.ActiveOffset.y;
        spec.IdleScale = Scale(i);
        spec.ActiveScale = Scale(a);
        spec.IdleRot = i.Transform?.RotateDeg ?? 0f;
        spec.ActiveRot = a.Transform?.RotateDeg ?? 0f;
        spec.IdleFilter = FilterColor(i.Filter);
        spec.ActiveFilter = FilterColor(a.Filter);
        // filter: drop-shadow() reuses the box-shadow halo path when no
        // box-shadow already claims it.
        if(i.Filter?.DropShadow.On == true && !spec.BoxGlow.On) { spec.BoxGlow = ToGlow(i.Filter.DropShadow); }
        if(a.Filter?.DropShadow.On == true && !spec.ActiveBoxGlow.On) { spec.ActiveBoxGlow = ToGlow(a.Filter.DropShadow); }
        spec.IdleBackdrop = i.BackdropBlur ?? 0f;
        spec.ActiveBackdrop = a.BackdropBlur ?? 0f;
        spec.TransitionSec = Mathf.Max(a.TransitionSeconds ?? 0f, i.TransitionSeconds ?? 0f);

        spec.IdleBefore = ToLayer(i.Before);
        spec.ActiveBefore = ToLayer(a.Before);
        spec.IdleAfter = ToLayer(i.After);
        spec.ActiveAfter = ToLayer(a.After);

        // font-family / @font-face — active wins; only override when resolved.
        TMP_FontAsset font = ResolveFont(a.FontFamily) ?? ResolveFont(i.FontFamily);
        if(font != null) { spec.CssFont = font; }

        if(spec.BoxBorderWidth <= 0.01f) {
            spec.Outline.a = 0f;
            spec.ActiveOutline.a = 0f;
        }
    }

    private static void ApplyCounterStyle(DmNoteSpec spec, CssCounterStyleSet set) {
        CssCounterStyle i = set.Idle, a = set.Active;

        if(i.FontSize.HasValue) { spec.CounterFontSize = Mathf.Max(1, Mathf.RoundToInt(i.FontSize.Value)); }
        if(a.FontSize.HasValue) { spec.CounterFontSize = Mathf.Max(1, Mathf.RoundToInt(a.FontSize.Value)); }
        if(i.Bold.HasValue) { spec.CounterBold = i.Bold.Value; }
        if(a.Bold.HasValue) { spec.CounterBold = a.Bold.Value; }

        if(i.Color.Has) { spec.CounterText = ToColor(i.Color); }
        if(a.Color.Has) { spec.ActiveCounterText = ToColor(a.Color); }
        if(i.StrokeColor.Has) { spec.CounterStroke = ToColor(i.StrokeColor); }
        if(a.StrokeColor.Has) { spec.ActiveCounterStroke = ToColor(a.StrokeColor); }
        float? strokeW = a.StrokeWidth ?? i.StrokeWidth;
        if(strokeW.HasValue) { spec.CounterStrokeWidth = strokeW.Value; }

        spec.CounterGradient = AssignGradient(ToGradient(i.Gradient), ref spec.CounterText);
        spec.ActiveCounterGradient = AssignGradient(ToGradient(a.Gradient), ref spec.ActiveCounterText);
        if(i.TextShadow.On) { spec.CounterGlow = ToGlow(i.TextShadow); }
        if(a.TextShadow.On) { spec.ActiveCounterGlow = ToGlow(a.TextShadow); }
    }

    private static Vector2 Translate(CssKeyStyle s) =>
        s.Transform != null ? new Vector2(s.Transform.TranslateX, -s.Transform.TranslateY) : Vector2.zero;

    private static Vector2 Scale(CssKeyStyle s) =>
        s.Transform != null ? new Vector2(s.Transform.ScaleX, s.Transform.ScaleY) : Vector2.one;

    // brightness()/contrast() collapse to a single RGB multiply (the visible
    // part for solid key colours). saturate() can't truly be applied to a flat
    // tint without the source pixels, so it only nudges the multiply. white =
    // identity (no filter).
    private static Color FilterColor(CssFilter f) {
        if(f == null) {
            return Color.white;
        }
        float m = Mathf.Clamp(f.Brightness * f.Contrast, 0f, 4f);
        // A low saturate dims slightly toward grey; a high one brightens a touch.
        m *= Mathf.Lerp(0.92f, 1.05f, Mathf.Clamp01(f.Saturate * 0.5f));
        float v = Mathf.Clamp01(m);
        return new Color(v, v, v, 1f);
    }

    private static CssAnimGradient AssignGradient(CssAnimGradient grad, ref Color solidFallback) {
        if(grad != null && grad.Stops.Length > 0) {
            solidFallback = grad.Stops[0];
        }
        return grad;
    }

    private static Color ToColor(CssColor c) => new(c.R, c.G, c.B, c.A);

    private static CssGlow ToGlow(CssShadow s) =>
        new(s.X, -s.Y, s.Blur, new Color(s.Color.R, s.Color.G, s.Color.B, s.Color.A));

    private static CssAnimGradient ToGradient(CssGradient g) {
        if(g == null || g.Stops.Count == 0) {
            return null;
        }
        Color[] stops = new Color[g.Stops.Count];
        for(int i = 0; i < stops.Length; i++) {
            CssColor c = g.Stops[i];
            stops[i] = new Color(c.R, c.G, c.B, c.A);
        }
        return new CssAnimGradient { Stops = stops, Period = g.Animated ? g.AnimSeconds : 0f, AngleDeg = g.AngleDeg };
    }

    private static CssLayerRt ToLayer(CssLayer layer) {
        if(layer == null) {
            return null;
        }
        var rt = new CssLayerRt {
            Bg = layer.Bg.Has ? ToColor(layer.Bg) : new Color(0f, 0f, 0f, 0f),
            Radius = layer.Radius ?? -1f,
            InsetT = layer.InsetT, InsetR = layer.InsetR, InsetB = layer.InsetB, InsetL = layer.InsetL,
            Blur = layer.Blur,
            Z = layer.Z,
        };
        if(layer.Gradient is { Stops.Count: > 0 } g) {
            rt.GradStops = new Color[g.Stops.Count];
            for(int i = 0; i < rt.GradStops.Length; i++) {
                CssColor c = g.Stops[i];
                rt.GradStops[i] = new Color(c.R, c.G, c.B, c.A);
            }
            rt.GradPeriod = g.Animated ? g.AnimSeconds : 0f;
            rt.GradAngle = g.AngleDeg;
        }
        return rt;
    }

    // ---- per-box build ------------------------------------------------------

    private static void BuildCssFx(Box box, DmNoteSpec spec) {
        try {
            BuildCssFxInner(box, spec);
        } catch(Exception ex) {
            MainCore.Log.Msg("[KeyViewer] CSS fx build failed: " + ex.Message);
        }
    }

    private static void BuildCssFxInner(Box box, DmNoteSpec spec) {
        if(spec.CssFont != null) {
            if(box.Label != null) { box.Label.font = spec.CssFont; Exempt(box.Label); }
            if(box.Value != null) { box.Value.font = spec.CssFont; Exempt(box.Value); }
        }
        if(spec.Bold && box.Label != null) { box.Label.fontStyle |= FontStyles.Bold; }
        if(spec.CounterBold && box.Value != null) { box.Value.fontStyle |= FontStyles.Bold; }

        // A gradient owns its text's colour, so neutralise the base tint.
        if((spec.LabelGradient != null || spec.ActiveLabelGradient != null) && box.Label != null) {
            box.Label.color = Color.white;
        }
        if((spec.CounterGradient != null || spec.ActiveCounterGradient != null) && box.Value != null) {
            box.Value.color = Color.white;
        }

        BuildBoxGlow(box, spec);
        BuildFillGradient(box, spec);
        box.BeforeLayer = BuildPseudo(box, spec, spec.IdleBefore ?? spec.ActiveBefore, true);
        box.AfterLayer = BuildPseudo(box, spec, spec.IdleAfter ?? spec.ActiveAfter, false);

        bool animated = IsAnimated(spec)
            || spec.TransitionSec > 0.01f
            || LayerAnimated(spec.IdleBefore) || LayerAnimated(spec.ActiveBefore)
            || LayerAnimated(spec.IdleAfter) || LayerAnimated(spec.ActiveAfter);
        if(animated) {
            cssFx.Add(box);
        }
    }

    private static void Exempt(Component c) {
        if(c.GetComponent<FontExempt>() == null) {
            c.gameObject.AddComponent<FontExempt>();
        }
    }

    private static void BuildBoxGlow(Box box, DmNoteSpec spec) {
        if(!spec.BoxGlow.On && !spec.ActiveBoxGlow.On) {
            return;
        }
        float blur = Mathf.Max(spec.BoxGlow.Blur, spec.ActiveBoxGlow.Blur);
        float pad = Mathf.Max(2f, blur + spec.BoxBorderWidth);
        GameObject obj = new("CssGlow");
        obj.transform.SetParent(EnsureGlowLayer(), false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(spec.X - pad, -(spec.Y - pad));
        rt.sizeDelta = new Vector2(spec.W + pad * 2f, spec.H + pad * 2f);
        Image img = obj.AddComponent<Image>();
        img.sprite = GlowSprite();
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        box.Glow = img;
    }

    // A masked gradient child filling the box, clipped to its rounded shape, with
    // an oversized rotated quad so the (horizontally-baked) gradient runs along
    // the CSS angle and scrolls seamlessly.
    private static void BuildFillGradient(Box box, DmNoteSpec spec) {
        CssAnimGradient g = spec.FillGradient ?? spec.ActiveFillGradient;
        if(g == null || box.Fill == null) {
            return;
        }
        // Mask the rounded fill so the gradient keeps the corner radius.
        Mask mask = box.Fill.GetComponent<Mask>() ?? box.Fill.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject obj = new("CssFillGrad");
        obj.transform.SetParent(box.Fill.transform, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        // Oversize so rotation never exposes a corner.
        float diag = Mathf.Sqrt(spec.W * spec.W + spec.H * spec.H);
        rt.sizeDelta = new Vector2(diag, diag);
        rt.localRotation = Quaternion.Euler(0f, 0f, 90f - g.AngleDeg);

        RawImage ri = obj.AddComponent<RawImage>();
        ri.texture = GradientTexture(g.Stops, 0f);
        ri.raycastTarget = false;
        rt.SetAsFirstSibling(); // behind the border ring
        box.FillGrad = ri;
    }

    private static RawImage BuildPseudo(Box box, DmNoteSpec spec, CssLayerRt layer, bool isBefore) {
        if(layer == null) {
            return null;
        }
        bool behind = layer.Z < 0;
        GameObject obj = new(isBefore ? "CssBefore" : "CssAfter");
        // Behind the box (Z<0) → the shared glow layer, free to spill (a glow).
        // Over the box (Z>=0) → a child of the fill, clipped to the box.
        obj.transform.SetParent(behind ? EnsureGlowLayer() : box.Fill.transform, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        RawImage ri = obj.AddComponent<RawImage>();
        ri.raycastTarget = false;

        // CSS inset: negative grows the layer outward, positive shrinks it.
        float w = spec.W - layer.InsetL - layer.InsetR;
        float h = spec.H - layer.InsetT - layer.InsetB;

        if(behind && layer.HasGradient) {
            // A rotatable, oversized glow patch centred on the box.
            float cx = spec.X + spec.W * 0.5f + (layer.InsetL - layer.InsetR) * 0.5f;
            float cy = spec.Y + spec.H * 0.5f + (layer.InsetT - layer.InsetB) * 0.5f;
            float diag = Mathf.Sqrt(w * w + h * h);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, -cy);
            rt.sizeDelta = new Vector2(diag, diag);
            rt.localRotation = Quaternion.Euler(0f, 0f, 90f - layer.GradAngle);
            ri.texture = GradientTexture(layer.GradStops, layer.Blur);
            ri.color = Color.white;
        } else if(behind) {
            // A solid patch behind the box.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(spec.X + layer.InsetL, -(spec.Y + layer.InsetT));
            rt.sizeDelta = new Vector2(w, h);
            ri.color = layer.Bg;
        } else {
            // Over the box: stretch to the fill (minus inset) so it stays clipped.
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(layer.InsetL, layer.InsetB);
            rt.offsetMax = new Vector2(-layer.InsetR, -layer.InsetT);
            if(layer.HasGradient) {
                // Angle approximated horizontal so the stretched quad isn't
                // rotated out of the box; the scroll still animates it.
                ri.texture = GradientTexture(layer.GradStops, layer.Blur);
                ri.color = Color.white;
            } else {
                ri.color = layer.Bg;
            }
        }
        return ri;
    }

    private static bool LayerAnimated(CssLayerRt layer) => layer is { HasGradient: true, GradPeriod: > 0.01f };

    private static RectTransform EnsureGlowLayer() {
        if(cssGlowLayer != null) {
            return cssGlowLayer;
        }
        GameObject obj = new("CssGlowLayer");
        obj.transform.SetParent(root, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling();
        cssGlowLayer = rt;
        return cssGlowLayer;
    }

    private static bool IsAnimated(DmNoteSpec spec) =>
        Animated(spec.LabelGradient) || Animated(spec.ActiveLabelGradient)
        || Animated(spec.CounterGradient) || Animated(spec.ActiveCounterGradient)
        || Animated(spec.FillGradient) || Animated(spec.ActiveFillGradient);

    private static bool Animated(CssAnimGradient g) => g != null && g.Period > 0.01f && g.Stops.Length > 1;

    // ---- per-press state ----------------------------------------------------

    private static void ApplyCssState(Box box, bool pressed) {
        try {
            ApplyCssStateInner(box, pressed);
        } catch(Exception ex) {
            MainCore.Log.Msg("[KeyViewer] CSS state failed: " + ex.Message);
        }
    }

    private static void ApplyCssStateInner(Box box, bool pressed) {
        DmNoteSpec spec = box.Dm;

        if(box.Label != null && (spec.LabelGlow.On || spec.ActiveLabelGlow.On)) {
            CssGlow g = pressed ? spec.ActiveLabelGlow : spec.LabelGlow;
            TMPTextShadow.Apply(box.Label, g.On, g.X, g.Y, g.Blur, g.Color);
        }
        if(box.Value != null && (spec.CounterGlow.On || spec.ActiveCounterGlow.On)) {
            CssGlow g = pressed ? spec.ActiveCounterGlow : spec.CounterGlow;
            TMPTextShadow.Apply(box.Value, g.On, g.X, g.Y, g.Blur, g.Color);
        }
        if(box.Value != null && spec.CounterStrokeWidth > 0.01f) {
            Color stroke = pressed ? spec.ActiveCounterStroke : spec.CounterStroke;
            Material mat = box.Value.fontMaterial;
            if(stroke.a > 0.001f) {
                mat.SetColor(ShaderUtilities.ID_OutlineColor, stroke);
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp(spec.CounterStrokeWidth * 0.1f, 0f, 0.5f));
            } else {
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
            }
        }
        if(box.Glow != null) {
            CssGlow g = pressed ? spec.ActiveBoxGlow : spec.BoxGlow;
            box.Glow.enabled = g.On;
            if(g.On) { box.Glow.color = g.Color; }
        }

        // transform: offset + scale + rotation on the box.
        Vector2 off = pressed ? spec.ActiveOffset : spec.IdleOffset;
        Vector2 scl = pressed ? spec.ActiveScale : spec.IdleScale;
        float rot = pressed ? spec.ActiveRot : spec.IdleRot;
        box.Fill.rectTransform.anchoredPosition = new Vector2(spec.X + off.x, -(spec.Y + off.y));
        box.Fill.rectTransform.localScale = new Vector3(scl.x, scl.y, 1f);
        box.Fill.rectTransform.localRotation = rot == 0f ? Quaternion.identity : Quaternion.Euler(0f, 0f, -rot);

        // filter colour multiply on the box graphics.
        Color filter = pressed ? spec.ActiveFilter : spec.IdleFilter;
        if(filter != Color.white) {
            ApplyFilterTint(box, filter, pressed);
        }

        // backdrop-filter: blur → frosted fill (alpha scaled by blur radius).
        float backdrop = pressed ? spec.ActiveBackdrop : spec.IdleBackdrop;
        if(backdrop > 0f && box.Fill != null && spec.FillGradient == null && spec.ActiveFillGradient == null) {
            Color baseBg = pressed ? spec.ActiveBg : spec.Bg;
            float frost = Mathf.Clamp01(0.25f + backdrop * 0.03f);
            box.Fill.color = new Color(baseBg.r, baseBg.g, baseBg.b, Mathf.Max(baseBg.a, frost));
        }

        // Pseudo layers + fill gradient visibility for the current state.
        ApplyPseudoState(box.BeforeLayer, pressed ? spec.ActiveBefore : spec.IdleBefore);
        ApplyPseudoState(box.AfterLayer, pressed ? spec.ActiveAfter : spec.IdleAfter);
        if(box.FillGrad != null) {
            CssAnimGradient fg = pressed ? spec.ActiveFillGradient : spec.FillGradient;
            box.FillGrad.enabled = fg != null;
        }

        // Begin a colour/transform transition if one is configured.
        if(spec.TransitionSec > 0.01f) {
            box.TransStart = Time.unscaledTime;
        }
    }

    private static void ApplyFilterTint(Box box, Color f, bool pressed) {
        if(box.Label != null && (pressed ? box.Dm.ActiveLabelGradient : box.Dm.LabelGradient) == null) {
            box.Label.color = Mul(box.Label.color, f);
        }
        if(box.Value != null && (pressed ? box.Dm.ActiveCounterGradient : box.Dm.CounterGradient) == null) {
            box.Value.color = Mul(box.Value.color, f);
        }
        if(box.Border != null) { box.Border.color = Mul(box.Border.color, f); }
    }

    private static Color Mul(Color a, Color b) => new(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

    private static void ApplyPseudoState(RawImage layer, CssLayerRt rt) {
        if(layer == null) {
            return;
        }
        if(rt == null) {
            layer.enabled = false;
            return;
        }
        layer.enabled = true;
        if(!rt.HasGradient) {
            layer.color = rt.Bg;
        }
    }

    // ---- per-frame tick -----------------------------------------------------

    private static void CssTick(float time) {
        for(int i = 0; i < cssFx.Count; i++) {
            Box box = cssFx[i];
            if(box?.Dm == null) {
                continue;
            }
            try {
                TickBox(box, time);
            } catch {
                // A single bad box must never break the loop or the frame.
            }
        }
    }

    private static void TickBox(Box box, float time) {
        DmNoteSpec spec = box.Dm;
        bool pressed = box.Pressed;

        // Per-glyph gradient text.
        CssAnimGradient lg = pressed ? spec.ActiveLabelGradient : spec.LabelGradient;
        if(box.Label != null && lg != null) {
            ApplyGlyphGradient(box.Label, lg, time, ref box.GradLabelText);
        }
        CssAnimGradient cg = pressed ? spec.ActiveCounterGradient : spec.CounterGradient;
        if(box.Value != null && cg != null) {
            ApplyGlyphGradient(box.Value, cg, time, ref box.GradValueText);
        }

        // Fill gradient scroll.
        CssAnimGradient fg = pressed ? spec.ActiveFillGradient : spec.FillGradient;
        if(box.FillGrad != null && box.FillGrad.enabled && fg != null && fg.Period > 0.01f) {
            box.FillGrad.uvRect = new Rect((time / fg.Period) % 1f, 0f, 1f, 1f);
        }

        // Pseudo gradient scroll.
        ScrollLayer(box.BeforeLayer, pressed ? spec.ActiveBefore : spec.IdleBefore, time);
        ScrollLayer(box.AfterLayer, pressed ? spec.ActiveAfter : spec.IdleAfter, time);

        // Colour/transform transition.
        if(box.TransStart >= 0f && spec.TransitionSec > 0.01f) {
            TickTransition(box, spec, pressed, time);
        }
    }

    private static void ScrollLayer(RawImage layer, CssLayerRt rt, float time) {
        if(layer != null && layer.enabled && rt is { HasGradient: true, GradPeriod: > 0.01f }) {
            layer.uvRect = new Rect((time / rt.GradPeriod) % 1f, 0f, 1f, 1f);
        }
    }

    private static void TickTransition(Box box, DmNoteSpec spec, bool pressed, float time) {
        float t = Mathf.Clamp01((time - box.TransStart) / spec.TransitionSec);
        // Lerp from the opposite state's resolved values toward the current one.
        Color fillTo = pressed ? spec.ActiveBg : spec.Bg;
        Color fillFrom = pressed ? spec.Bg : spec.ActiveBg;
        if(box.Fill != null && spec.FillGradient == null && spec.ActiveFillGradient == null) {
            box.Fill.color = Color.Lerp(fillFrom, fillTo, t);
        }
        if(box.Border != null) {
            box.Border.color = Color.Lerp(pressed ? spec.Outline : spec.ActiveOutline,
                pressed ? spec.ActiveOutline : spec.Outline, t);
        }
        if(box.Label != null && spec.LabelGradient == null && spec.ActiveLabelGradient == null) {
            box.Label.color = Color.Lerp(pressed ? spec.Text : spec.ActiveText,
                pressed ? spec.ActiveText : spec.Text, t);
        }
        if(t >= 1f) {
            box.TransStart = -1f;
        }
    }

    // Writes per-glyph gradient colours into the text mesh. Only forces a mesh
    // rebuild when the string changed; otherwise it recolours the existing verts
    // and uploads just the colour stream.
    private static void ApplyGlyphGradient(TMP_Text tmp, CssAnimGradient g, float time, ref string lastText) {
        if(g.Stops.Length == 0) {
            return;
        }
        string text = tmp.text;
        if(!string.Equals(text, lastText, StringComparison.Ordinal)) {
            tmp.ForceMeshUpdate();
            lastText = text;
        }

        TMP_TextInfo info = tmp.textInfo;
        if(info == null || info.characterCount == 0) {
            return;
        }

        float scroll = g.Period > 0.01f ? (time / g.Period) % 1f : 0f;
        int count = info.characterCount;
        // Sample across the visible character span so the gradient spreads over
        // the whole word rather than per character.
        for(int i = 0; i < count; i++) {
            TMP_CharacterInfo ch = info.characterInfo[i];
            if(!ch.isVisible) {
                continue;
            }
            float u = count > 1 ? (float)i / (count - 1) : 0f;
            Color32 col = SampleGradient(g.Stops, u + scroll);
            int mat = ch.materialReferenceIndex;
            int vi = ch.vertexIndex;
            Color32[] cols = info.meshInfo[mat].colors32;
            cols[vi] = col;
            cols[vi + 1] = col;
            cols[vi + 2] = col;
            cols[vi + 3] = col;
        }
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    // Samples a looping stop list at position p (wraps), last→first seamless.
    private static Color SampleGradient(Color[] stops, float p) {
        int n = stops.Length;
        if(n == 1) {
            return stops[0];
        }
        p -= Mathf.Floor(p);
        float scaled = p * n;
        int idx = (int)scaled % n;
        int next = (idx + 1) % n;
        return Color.Lerp(stops[idx], stops[next], scaled - Mathf.Floor(scaled));
    }

    // ---- texture generation -------------------------------------------------

    // A horizontal repeating multi-stop gradient strip, optionally box-blurred,
    // cached by content. Width 256, height 8 (cheap; stretched by the quad).
    private static Texture2D GradientTexture(Color[] stops, float blur) {
        string key = GradKey(stops, blur);
        if(gradTex.TryGetValue(key, out Texture2D cached) && cached != null) {
            return cached;
        }

        const int w = 256, h = 8;
        Color[] row = new Color[w];
        int n = stops.Length;
        for(int x = 0; x < w; x++) {
            float p = (float)x / w * n;
            int idx = (int)p % n;
            int next = (idx + 1) % n;
            row[x] = Color.Lerp(stops[idx], stops[next], p - Mathf.Floor(p));
        }
        if(blur > 0.5f) {
            row = BoxBlur(row, Mathf.Clamp(Mathf.RoundToInt(blur * 2f), 1, 32));
        }

        Color[] px = new Color[w * h];
        for(int y = 0; y < h; y++) {
            Array.Copy(row, 0, px, y * w, w);
        }
        Texture2D tex = new(w, h, TextureFormat.RGBA32, false) {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels(px);
        tex.Apply(false, false);
        gradTex[key] = tex;
        return tex;
    }

    private static Color[] BoxBlur(Color[] src, int radius) {
        int n = src.Length;
        Color[] dst = new Color[n];
        for(int i = 0; i < n; i++) {
            float r = 0f, g = 0f, b = 0f, a = 0f;
            int cnt = 0;
            for(int k = -radius; k <= radius; k++) {
                int j = ((i + k) % n + n) % n; // wrap (the strip repeats)
                r += src[j].r; g += src[j].g; b += src[j].b; a += src[j].a;
                cnt++;
            }
            dst[i] = new Color(r / cnt, g / cnt, b / cnt, a / cnt);
        }
        return dst;
    }

    private static string GradKey(Color[] stops, float blur) {
        var sb = new StringBuilder(stops.Length * 8 + 4);
        foreach(Color c in stops) {
            sb.Append(ColorUtility.ToHtmlStringRGBA(c));
        }
        sb.Append('|').Append(Mathf.RoundToInt(blur));
        return sb.ToString();
    }

    private static Sprite GlowSprite() {
        if(glowSprite != null) {
            return glowSprite;
        }
        const int size = 64, margin = 22;
        Texture2D tex = new(size, size, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color[] px = new Color[size * size];
        for(int y = 0; y < size; y++) {
            float ay = EdgeAlpha(y, size, margin);
            for(int x = 0; x < size; x++) {
                px[y * size + x] = new Color(1f, 1f, 1f, ay * EdgeAlpha(x, size, margin));
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, false);
        glowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(margin, margin, margin, margin));
        return glowSprite;
    }

    private static float EdgeAlpha(int i, int size, int margin) {
        float t = Mathf.Clamp01(Mathf.Min(i, size - 1 - i) / (float)margin);
        return t * t * (3f - 2f * t);
    }

    // ---- @font-face / font-family -------------------------------------------

    private static readonly object cssFontLock = new();

    private static void EnsureFontFaces(KeyViewerStylesheet sheet) {
        foreach(CssFontFace face in sheet.FontFaces) {
            try {
                if(cssFonts.ContainsKey(face.Family)) {
                    continue;
                }
                lock(cssFontLock) {
                    if(cssFontPending.Contains(face.Family)) {
                        continue;
                    }
                }
                string path = CachedFontPath(face);
                if(path != null && File.Exists(path)) {
                    BuildFont(face.Family, path);
                } else {
                    StartFontDownload(face);
                }
            } catch(Exception ex) {
                MainCore.Log.Msg($"[KeyViewer] CSS @font-face '{face.Family}' failed: {ex.Message}");
            }
        }
    }

    private static TMP_FontAsset ResolveFont(string family) {
        if(string.IsNullOrEmpty(family)) {
            return null;
        }
        if(cssFonts.TryGetValue(family, out TMP_FontAsset asset)) {
            return asset;
        }
        // A bare font-family (no @font-face) may name a font already in the mod's
        // catalogue; reuse it rather than downloading.
        foreach(string name in FontManager.GetAvailableFonts()) {
            if(string.Equals(name, family, StringComparison.OrdinalIgnoreCase)) {
                TMP_FontAsset f = FontManager.GetFont(name);
                cssFonts[family] = f;
                return f;
            }
        }
        return null;
    }

    private static string FontCacheDir() {
        string dir = Path.Combine(MainCore.Paths.RootPath, "CssFonts");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Prefers a TTF/OTF source (Unity can build those); woff/woff2 can't be read
    // directly, so a woff2-only face is downloaded but only loads if the CDN also
    // serves the same path as .ttf.
    private static string CachedFontPath(CssFontFace face) {
        string url = PickFontUrl(face);
        if(url == null) {
            return null;
        }
        string ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if(ext != ".ttf" && ext != ".otf" && ext != ".ttc") {
            ext = ".ttf";
        }
        return Path.Combine(FontCacheDir(), Hash(face.Family + "|" + url) + ext);
    }

    private static string PickFontUrl(CssFontFace face) {
        foreach(string s in face.Srcs) {
            string e = s.ToLowerInvariant();
            if(e.EndsWith(".ttf") || e.EndsWith(".otf") || e.EndsWith(".ttc")) {
                return s;
            }
        }
        // No native source — take the first and try it as .ttf.
        return face.Srcs.Count > 0 ? SwapToTtf(face.Srcs[0]) : null;
    }

    private static string SwapToTtf(string url) {
        int dot = url.LastIndexOf('.');
        return dot > 0 ? url.Substring(0, dot) + ".ttf" : url;
    }

    private static void BuildFont(string family, string path) {
        try {
            Font font = new(path);
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
            asset.isMultiAtlasTexturesEnabled = true;
            cssFonts[family] = asset;
        } catch(Exception ex) {
            MainCore.Log.Msg($"[KeyViewer] CSS font '{family}' build failed: {ex.Message}");
            cssFonts[family] = null; // negative-cache so we don't retry every rebuild
        }
    }

    private static void StartFontDownload(CssFontFace face) {
        string url = PickFontUrl(face);
        string path = CachedFontPath(face);
        if(url == null || path == null) {
            return;
        }
        lock(cssFontLock) {
            cssFontPending.Add(face.Family);
        }

        var thread = new Thread(() => {
            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using var client = new WebClient();
                byte[] data = client.DownloadData(url);
                File.WriteAllBytes(path, data);
                cssFontArrived = true; // Updater rebuilds on the main thread
            } catch(Exception ex) {
                MainCore.Log.Msg($"[KeyViewer] CSS font download failed ({face.Family}): {ex.Message}");
            } finally {
                lock(cssFontLock) {
                    cssFontPending.Remove(face.Family);
                }
            }
        }) { IsBackground = true, Name = "KorenCssFont" };
        thread.Start();
    }

    private static string Hash(string s) {
        using var md5 = MD5.Create();
        byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(h.Length * 2);
        foreach(byte b in h) {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
