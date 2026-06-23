using UnityEngine;

namespace Quartz.Resource;

// Runtime-generated UI shape textures with analytic anti-aliasing: each
// pixel's alpha is its coverage of the shape (a 1px linear ramp at the edge).
// The old 256px PNGs had hard, un-antialiased edges and were minified ~10x
// on screen, which either aliased (no mips) or blurred (deep mip levels).
// Callers size these to the pixels the shape covers on screen (see
// SpriteManager.CornerPixels), so sampling is ~1:1: the coverage ramp lands
// on a single screen pixel and corners stay both smooth and sharp. Mips are
// still generated so the shapes only soften — never shimmer — if the
// resolution later drops below build size. All shapes are white — tint via
// Image.color.
public static class ProceduralTexture {
    public delegate float CoverageFn(float x, float y);

    // Filled circle of the given pixel radius (texture is radius*2 square).
    public static Texture2D Circle(int radius) {
        float c = radius - 0.5f;
        return Generate(radius * 2, (x, y) => CircleCoverage(x - c, y - c, radius));
    }

    // Top half rounded, bottom half a solid square (top-bar shape).
    public static Texture2D CircleHalfTop(int radius) {
        float c = radius - 0.5f;
        return Generate(radius * 2, (x, y) =>
            y <= c ? 1f : CircleCoverage(x - c, y - c, radius));
    }

    // Ring: outer radius `radius`, stroke width `stroke`, AA on both edges.
    public static Texture2D CircleOutline(int radius, int stroke) {
        float c = radius - 0.5f;
        return Generate(radius * 2, (x, y) => {
            float d = Mathf.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            float outer = Mathf.Clamp01(radius - d + 0.5f);
            float inner = Mathf.Clamp01(radius - stroke - d + 0.5f);
            return outer - inner;
        });
    }

    // The edge sits exactly on the texture border, so the pixels a sliced
    // sprite stretches along straight runs (the border midlines) stay fully
    // opaque — pulling the edge inward would tile a translucent seam.
    private static float CircleCoverage(float dx, float dy, float radius) {
        float d = Mathf.Sqrt((dx * dx) + (dy * dy));
        return Mathf.Clamp01(radius - d + 0.5f);
    }

    private static Texture2D Generate(int size, CoverageFn coverage) {
        Texture2D tex = new(size, size, TextureFormat.RGBA32, true, true);

        // SetPixel over SetPixels32: textures are small so scalar calls are fine.
        for(int y = 0; y < size; y++) {
            for(int x = 0; x < size; x++) {
                float a = Mathf.Clamp01(coverage(x, y));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        // makeNoLongerReadable: drop the CPU pixel copy — these are only ever
        // GPU-sampled as UI sprites, never GetPixel'd.
        tex.Apply(true, true);
        tex.filterMode = FilterMode.Trilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
}
