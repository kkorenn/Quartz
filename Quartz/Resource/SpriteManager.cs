using Quartz.Core;
using Quartz.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Resource;

public enum UISprite {
    QuartzLogo,
    OV5LogoOutline256,
    Circle256,
    X128,
    Monitor128,
    Gear128,
    Text128,
    Image128,
    Book128,
    Star128,
    ToggleCircle128,
    Triangle128,
    Power128,
    MagnifyingGlass128,
    Gamepad128,
    Wrench128,
    AdjustmentsHorizontal128,
    Users128,
    ClockRewind128,
}

public enum UISliceSprite {
    Circle256P1024,
    Circle256P2048,
    CircleHalf256P1024,
    CircleOutline256P1024,
    CircleOutline256P2048,
}

public sealed class SpriteManager(ResourceManager resource) : IDisposable {
    private readonly ResourceManager resource = resource;

    private readonly Dictionary<object, Sprite> cache = [];

    public static Sprite Create(Texture2D texture)
        => texture == null ? null : Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f)
        );

    public static Sprite CreateSliced(Texture2D texture, float ppui, Vector4 border)
        => texture == null ? null : Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            ppui,
            0,
            SpriteMeshType.FullRect,
            border
        );

    public Sprite Get(string assetName) {
        if(string.IsNullOrEmpty(assetName)) {
            return null;
        }

        if(cache.TryGetValue(assetName, out Sprite sprite)) {
            return sprite;
        }

        Texture2D tex = resource.Get<Texture2D>(assetName);
        if(tex == null) {
            return null;
        }

        sprite = Create(tex);
        cache[assetName] = sprite;

        return sprite;
    }

    public Sprite GetSliced(string assetName, float ppui, Vector4 border) {
        if(string.IsNullOrEmpty(assetName)) {
            return null;
        }

        object key = (assetName, ppui, border);

        if(cache.TryGetValue(key, out Sprite sprite)) {
            return sprite;
        }

        Texture2D tex = resource.Get<Texture2D>(assetName);
        if(tex == null) {
            return null;
        }

        sprite = CreateSliced(tex, ppui, border);
        cache[key] = sprite;

        return sprite;
    }

    public Sprite Get(Asset asset) {
        if(cache.TryGetValue(asset, out Sprite sprite)) {
            return sprite;
        }

        Texture2D tex = resource.Get<Texture2D>(asset);
        if(tex == null) {
            return null;
        }

        sprite = Create(tex);
        cache[asset] = sprite;

        return sprite;
    }

    public Sprite GetSliced(Asset asset, float ppui, Vector4 border) {
        object key = (asset, ppui, border);

        if(cache.TryGetValue(key, out Sprite sprite)) {
            return sprite;
        }

        Texture2D tex = resource.Get<Texture2D>(asset);
        if(tex == null) {
            return null;
        }

        sprite = CreateSliced(tex, ppui, border);
        cache[key] = sprite;

        return sprite;
    }

    public Sprite Get(UISprite sprite) => spriteMap.TryGetValue(sprite, out Asset asset) ? Get(asset) : null;

    // Sized icon: a one-time area-averaged copy of the 128px source baked at
    // the pixels the icon covers on screen, so sampling is 1:1 instead of
    // dropping 4-7x through the mip chain (which visibly blurs small icons).
    // `units` is the on-canvas size the Image is laid out at.
    public Sprite Get(UISprite sprite, float units) {
        object key = (sprite, units);

        if(cache.TryGetValue(key, out Sprite cached)) {
            return cached;
        }

        if(!spriteMap.TryGetValue(sprite, out Asset asset)) {
            return null;
        }

        Texture2D source = resource.Get<Texture2D>(asset);
        if(source == null) {
            return null;
        }

        int target = Mathf.Max(2, Mathf.RoundToInt(units * UIPixelScale()));

        // Only ever downscale — at or above source size the original
        // texture already samples 1:1 or better.
        if(target >= source.width) {
            return Get(sprite);
        }

        Texture2D tex = Downscale(source, target);
        generated.Add(tex);

        Sprite created = Create(tex);
        cache[key] = created;

        return created;
    }

    // Box (area-average) downscale with fractional edge weights, alpha-
    // weighted so transparent texels' RGB doesn't bleed into glyph edges.
    // Box is plenty for flat-color UI glyphs; runs once per (icon, size).
    private static Texture2D Downscale(Texture2D src, int size) {
        int sw = src.width;
        int sh = src.height;
        float rx = (float)sw / size;
        float ry = (float)sh / size;

        Texture2D tex = new(size, size, TextureFormat.RGBA32, false, true);

        for(int y = 0; y < size; y++) {
            float y0 = y * ry;
            float y1 = y0 + ry;
            int yi0 = (int)y0;
            int yi1 = Mathf.Min(sh, Mathf.CeilToInt(y1));

            for(int x = 0; x < size; x++) {
                float x0 = x * rx;
                float x1 = x0 + rx;
                int xi0 = (int)x0;
                int xi1 = Mathf.Min(sw, Mathf.CeilToInt(x1));

                float r = 0f, g = 0f, b = 0f, a = 0f, w = 0f;

                for(int sy = yi0; sy < yi1; sy++) {
                    float wy = Mathf.Min(y1, sy + 1) - Mathf.Max(y0, sy);

                    for(int sx = xi0; sx < xi1; sx++) {
                        float wx = Mathf.Min(x1, sx + 1) - Mathf.Max(x0, sx);
                        float ww = wx * wy;
                        Color c = src.GetPixel(sx, sy);

                        r += c.r * c.a * ww;
                        g += c.g * c.a * ww;
                        b += c.b * c.a * ww;
                        a += c.a * ww;
                        w += ww;
                    }
                }

                float invA = a > 0f ? 1f / a : 0f;
                tex.SetPixel(x, y, new Color(r * invA, g * invA, b * invA, a / w));
            }
        }

        tex.Apply(false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    // Slice sprites are generated procedurally (analytically antialiased)
    // instead of loaded from the old hard-edged 256px PNGs. The corner radius
    // in canvas units matches what the PNG variants drew (12.5 for the P1024
    // family, 6.25 for P2048), but the texture is sized to the pixels the
    // corner actually covers on screen, so sampling is ~1:1 and the 1px
    // coverage ramp lands on one screen pixel instead of smearing through
    // mip levels.
    private const float CORNER_UNITS_P1024 = 12.5f;
    private const float CORNER_UNITS_P2048 = 6.25f;

    public Sprite Get(UISliceSprite sprite) {
        if(cache.TryGetValue(sprite, out Sprite cached)) {
            return cached;
        }

        int r1 = CornerPixels(CORNER_UNITS_P1024);
        int r2 = CornerPixels(CORNER_UNITS_P2048);

        Sprite created = sprite switch {
            UISliceSprite.Circle256P1024 => SlicedShape(ProceduralTexture.Circle(r1), r1, CORNER_UNITS_P1024),
            UISliceSprite.Circle256P2048 => SlicedShape(ProceduralTexture.Circle(r2), r2, CORNER_UNITS_P2048),
            UISliceSprite.CircleHalf256P1024 => SlicedShape(ProceduralTexture.CircleHalfTop(r1), r1, CORNER_UNITS_P1024),
            UISliceSprite.CircleOutline256P1024 => SlicedShape(ProceduralTexture.CircleOutline(r1, Mathf.Max(1, r1 / 2)), r1, CORNER_UNITS_P1024),
            UISliceSprite.CircleOutline256P2048 => SlicedShape(ProceduralTexture.CircleOutline(r2, Mathf.Max(1, r2 / 2)), r2, CORNER_UNITS_P2048),
            _ => null,
        };

        if(created != null) {
            cache[sprite] = created;
        }

        return created;
    }

    // Screen pixels per canvas unit, mirroring the shared CanvasScaler setup
    // (1920x1080 reference, MatchWidthOrHeight 0.5 = geometric mean of the
    // axis ratios). Sampled at build time; if the resolution changes later,
    // existing sprites degrade through their mips rather than regenerate.
    private static float UIPixelScale() {
        Vector2 reference = UICore.ReferenceResolution;
        float scale = Mathf.Sqrt(Screen.width / reference.x * (Screen.height / reference.y));

        if(!loggedScale) {
            loggedScale = true;
            MainCore.Log.Msg($"[Sprites] screen {Screen.width}x{Screen.height}, {scale:F3} px/unit");
        }

        return scale;
    }

    private static bool loggedScale;

    private static int CornerPixels(float units)
        => Mathf.Max(2, Mathf.RoundToInt(units * UIPixelScale()));

    // Ring slice with an arbitrary corner radius and stroke width (canvas
    // units). Unlike the Outline component — which fakes a border by drawing
    // four diagonally shifted copies of the sprite, smearing the corner arcs
    // out of phase — this is a single analytically-antialiased ring, so
    // corners stay as crisp as the fill.
    public Sprite GetRing(float cornerUnits, float strokeUnits) {
        object key = (cornerUnits, strokeUnits);

        if(cache.TryGetValue(key, out Sprite cached)) {
            return cached;
        }

        int radius = CornerPixels(cornerUnits);
        int stroke = Mathf.Max(1, Mathf.RoundToInt(strokeUnits * UIPixelScale()));

        Sprite created = SlicedShape(ProceduralTexture.CircleOutline(radius, stroke), radius, cornerUnits);
        cache[key] = created;

        return created;
    }

    private Sprite SlicedShape(Texture2D texture, int border, float cornerUnits) {
        generated.Add(texture);
        // ppui derived from the rounded pixel radius so the corner's canvas-
        // unit size stays exact regardless of how the rounding landed.
        return CreateSliced(texture, border * 100f / cornerUnits, new Vector4(border, border, border, border));
    }

    // Procedural textures aren't owned by ResourceManager's cache, so this
    // manager has to destroy them itself.
    private readonly List<Texture2D> generated = [];

    public void Dispose() {
        foreach(Sprite sprite in cache.Values) {
            Object.Destroy(sprite);
        }

        cache.Clear();

        foreach(Texture2D texture in generated) {
            Object.Destroy(texture);
        }

        generated.Clear();
    }

    private readonly Dictionary<UISprite, Asset> spriteMap = new() {
        [UISprite.QuartzLogo] = Asset.QuartzLogo,
        [UISprite.OV5LogoOutline256] = Asset.OV5LogoOutline256,
        [UISprite.Circle256] = Asset.Circle256,
        [UISprite.X128] = Asset.X128,
        [UISprite.Monitor128] = Asset.Monitor128,
        [UISprite.Gear128] = Asset.Gear128,
        [UISprite.Text128] = Asset.Text128,
        [UISprite.Image128] = Asset.Image128,
        [UISprite.Book128] = Asset.Book128,
        [UISprite.Star128] = Asset.Star128,
        [UISprite.ToggleCircle128] = Asset.ToggleCircle128,
        [UISprite.Triangle128] = Asset.Triangle128,
        [UISprite.Power128] = Asset.Power128,
        [UISprite.MagnifyingGlass128] = Asset.MagnifyingGlass128,
        [UISprite.Gamepad128] = Asset.Gamepad128,
        [UISprite.Wrench128] = Asset.Wrench128,
        [UISprite.AdjustmentsHorizontal128] = Asset.AdjustmentsHorizontal128,
        [UISprite.Users128] = Asset.Users128,
        [UISprite.ClockRewind128] = Asset.ClockRewind128
    };

}