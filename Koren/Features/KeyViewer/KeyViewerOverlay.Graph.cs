using Koren.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.Features.KeyViewer;

// KPS-graph element, ported to match DM Note's GraphPanel exactly: a rolling
// history of a stat plotted as a line (filled area + 2px stroke + dashed average
// line) or rounded-top bars, with horizontal opacity gradients and a 150ms
// ease-out tween between samples. CSS --graph-bg/-border/-radius/-color override
// the preset's inline values.
//
// Performance: the mesh (a handful of history points) only rebuilds while the
// tween is running — i.e. briefly after each 50ms sample — and goes idle when
// the stat is flat. A typical preset has zero or one graph.
public static partial class KeyViewerOverlay {
    // DM Note constants (OverlayGraphItem): sample every 50ms, one history slot
    // per 100ms of window.
    private const float GraphTickMs = 50f;
    private const float GraphUpdateMs = 100f;
    private const float GraphAnimMs = 150f;

    private static void AddDmNoteGraph(int index, DmNoteSpec spec) {
        // Container: a rounded box with the graph background + border, clipping
        // its contents to the rounded shape (CSS overflow:hidden + radius).
        (Image fill, Image border) = NewBoxVisual(
            "DmNoteGraph_" + index, root, spec.X, spec.Y, spec.W, spec.H,
            spec.GraphBorderRadius, spec.GraphBorderWidth);
        fill.color = spec.GraphBg;
        border.color = spec.GraphBorderWidth <= 0.01f
            ? new Color(spec.GraphBorder.r, spec.GraphBorder.g, spec.GraphBorder.b, 0f)
            : spec.GraphBorder;

        Mask mask = fill.GetComponent<Mask>() ?? fill.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject obj = new("GraphPlot");
        obj.transform.SetParent(fill.transform, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        float inset = spec.GraphBorderWidth;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);

        KpsGraphGraphic plot = obj.AddComponent<KpsGraphGraphic>();
        plot.raycastTarget = false;
        plot.Init(spec);

        // Optional background image behind the plot (clipped by the mask).
        BuildGraphImage(fill.rectTransform, spec);
    }

    // Overrides the graph's inline colours from the resolved --graph-* CSS.
    private static void ApplyGraphCss(DmNoteSpec spec, CssGraphStyle g) {
        if(g.Bg.Has) { spec.GraphBg = ToColor(g.Bg); }
        if(g.BorderColor.Has) { spec.GraphBorder = ToColor(g.BorderColor); }
        if(g.BorderWidth.HasValue) { spec.GraphBorderWidth = Mathf.Clamp(g.BorderWidth.Value, 0f, 20f); }
        if(g.Radius.HasValue) { spec.GraphBorderRadius = Mathf.Clamp(g.Radius.Value, 0f, 100f); }
        if(g.Color.Has) { spec.GraphColor = ToColor(g.Color); }
    }

    // The plotted area: a UI mesh of the stat history. Updates itself each frame
    // while animating, sampling the live stat on DM Note's 50ms cadence.
    private sealed class KpsGraphGraphic : MaskableGraphic {
        private string _stat = "kps";
        private bool _bar;
        private float _speedMs = 1000f;
        private bool _showAvg = true;
        private bool _anim = true;
        private Color _color = Color.white;

        private float[] _history = new float[10];
        private float[] _animFrom = new float[10];
        private float[] _animTo = new float[10];
        private float _max = 1f;
        private long _sum;
        private int _samples;
        private int _avg;
        private float _nextSample;
        private float _animStart = -1f;

        public void Init(DmNoteSpec spec) {
            _stat = spec.GraphStat;
            _bar = string.Equals(spec.GraphType, "bar", StringComparison.OrdinalIgnoreCase);
            _speedMs = Mathf.Clamp(spec.GraphSpeed, 500f, 5000f);
            _showAvg = spec.GraphShowAvg;
            _anim = spec.GraphAnim;
            _color = spec.GraphColor;
            color = Color.white; // vertex colours carry the gradient

            int size = Mathf.Max(1, Mathf.CeilToInt(_speedMs / GraphUpdateMs));
            _history = new float[size];
            _animFrom = new float[size];
            _animTo = new float[size];
            _nextSample = 0f;
            SetVerticesDirty();
        }

        private void Update() {
            float now = Time.unscaledTime;

            if(now >= _nextSample) {
                Sample();
                _nextSample = now + GraphTickMs / 1000f;
            }

            if(_anim && _animStart >= 0f) {
                float t = Mathf.Clamp01((now - _animStart) / (GraphAnimMs / 1000f));
                float e = 1f - (1f - t) * (1f - t) * (1f - t); // easeOutCubic
                for(int i = 0; i < _history.Length; i++) {
                    _history[i] = Mathf.Lerp(_animFrom[i], _animTo[i], e);
                }
                SetVerticesDirty();
                if(t >= 1f) {
                    _animStart = -1f;
                }
            }
        }

        private void Sample() {
            int value = Mathf.Max(0, GraphStatValue(_stat));
            if(value > _max) {
                _max = value;
            }
            if(value > 0) {
                _sum += value;
                _samples++;
            }
            _avg = _samples > 0 ? Mathf.RoundToInt(_sum / (float)_samples) : 0;

            // Shift the target buffer and push the new sample at the end.
            for(int i = 0; i < _animTo.Length - 1; i++) {
                _animTo[i] = _animTo[i + 1];
            }
            _animTo[_animTo.Length - 1] = value;

            // Nothing changed (a flat, idle graph) → don't tween or rebuild the
            // mesh. This keeps a quiescent graph free of per-frame work.
            if(Equal(_history, _animTo)) {
                _animStart = -1f;
                return;
            }

            if(_anim) {
                Array.Copy(_history, _animFrom, _history.Length);
                _animStart = Time.unscaledTime;
            } else {
                Array.Copy(_animTo, _history, _history.Length);
                SetVerticesDirty();
            }
        }

        private static bool Equal(float[] a, float[] b) {
            for(int i = 0; i < a.Length; i++) {
                if(Mathf.Abs(a[i] - b[i]) > 0.001f) {
                    return false;
                }
            }
            return true;
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            float[] h = _history;
            if(h == null || h.Length == 0) {
                return;
            }

            Rect r = rectTransform.rect;
            float w = r.width, ht = r.height;
            if(w <= 1f || ht <= 1f) {
                return;
            }
            float ox = r.xMin, oy = r.yMin;
            float safeMax = Mathf.Max(_max, 1f);

            if(_bar) {
                BuildBars(vh, h, w, ht, ox, oy, safeMax);
            } else {
                BuildLine(vh, h, w, ht, ox, oy, safeMax);
            }
        }

        private void BuildLine(VertexHelper vh, float[] h, float w, float ht, float ox, float oy, float safeMax) {
            int n = h.Length;
            float denom = Mathf.Max(n - 1, 1);
            Span<float> px = n <= 256 ? stackalloc float[n] : new float[n];
            Span<float> py = n <= 256 ? stackalloc float[n] : new float[n];
            for(int i = 0; i < n; i++) {
                px[i] = ox + (i / denom) * w;
                float v = h[i];
                py[i] = v <= 0f ? oy : oy + Mathf.Min(v / safeMax, 1f) * ht;
            }

            // Filled area under the curve: per-segment quads, horizontal alpha
            // gradient 0.05 → 0.15 (matches DM Note's fillGradient).
            for(int i = 0; i < n - 1; i++) {
                float a0 = Mathf.Lerp(0.05f, 0.15f, i / denom);
                float a1 = Mathf.Lerp(0.05f, 0.15f, (i + 1) / denom);
                Color cb0 = Fade(a0), cb1 = Fade(a1);
                AddQuad(vh,
                    new Vector2(px[i], oy), new Vector2(px[i + 1], oy),
                    new Vector2(px[i + 1], py[i + 1]), new Vector2(px[i], py[i]),
                    cb0, cb1, cb1, cb0);
            }

            // Average line: dashed horizontal, opacity 0.5.
            if(_showAvg) {
                float ay = oy + Mathf.Min(_avg / safeMax, 1f) * ht;
                Color ac = Fade(0.5f);
                float dash = 4f, gap = 4f, x = ox;
                while(x < ox + w) {
                    float x2 = Mathf.Min(x + dash, ox + w);
                    AddQuad(vh,
                        new Vector2(x, ay - 0.5f), new Vector2(x2, ay - 0.5f),
                        new Vector2(x2, ay + 0.5f), new Vector2(x, ay + 0.5f),
                        ac, ac, ac, ac);
                    x += dash + gap;
                }
            }

            // The line itself: 2px stroke, horizontal alpha gradient 0.3 → 1.0.
            for(int i = 0; i < n - 1; i++) {
                Vector2 a = new(px[i], py[i]);
                Vector2 b = new(px[i + 1], py[i + 1]);
                Vector2 dir = (b - a);
                float len = dir.magnitude;
                if(len < 0.0001f) {
                    continue;
                }
                Vector2 nrm = new Vector2(-dir.y, dir.x) / len; // 1px each side
                Color ca = Fade(Mathf.Lerp(0.3f, 1f, i / denom));
                Color cb = Fade(Mathf.Lerp(0.3f, 1f, (i + 1) / denom));
                AddQuad(vh, a - nrm, b - nrm, b + nrm, a + nrm, ca, cb, cb, ca);
            }
        }

        private void BuildBars(VertexHelper vh, float[] h, float w, float ht, float ox, float oy, float safeMax) {
            int n = h.Length;
            float denom = Mathf.Max(n - 1, 1);
            float gap = 1f;
            float barW = Mathf.Max((w - gap * (n - 1)) / n, 0f);
            for(int i = 0; i < n; i++) {
                float v = h[i];
                float norm = Mathf.Min(v / safeMax, 1f);
                if(norm <= 0f) {
                    continue;
                }
                float barH = norm * ht;
                float x = ox + i * (barW + gap);
                Color c = Fade(Mathf.Lerp(0.3f, 1f, i / denom));
                AddQuad(vh,
                    new Vector2(x, oy), new Vector2(x + barW, oy),
                    new Vector2(x + barW, oy + barH), new Vector2(x, oy + barH),
                    c, c, c, c);
            }
        }

        private Color Fade(float a) => new(_color.r, _color.g, _color.b, _color.a * a);

        private static void AddQuad(VertexHelper vh, Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl,
            Color cbl, Color cbr, Color ctr, Color ctl) {
            int i = vh.currentVertCount;
            vh.AddVert(bl, cbl, Vector2.zero);
            vh.AddVert(br, cbr, Vector2.zero);
            vh.AddVert(tr, ctr, Vector2.zero);
            vh.AddVert(tl, ctl, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
