using Quartz.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Nostalgia;

// Lazy-loaded sprites extracted from BackToThePast's AssetBundle and embedded
// as PNGs under Resource/Embedded/Image/Bttp. The swirl/arrow sprites are
// gameplay sprites (100 px/unit, centred pivot) used by Legacy Twirl; the
// editor button fill is a 9-sliced UI sprite (1000 px/unit, 77 px border) used
// by Legacy CLS.
public static class NostalgiaImages {
    public static Sprite SwirlCw { get; private set; }
    public static Sprite SwirlCcw { get; private set; }
    public static Sprite SwirlCwOutline { get; private set; }
    public static Sprite SwirlCcwOutline { get; private set; }
    public static Sprite ArrowCw { get; private set; }
    public static Sprite ArrowCcw { get; private set; }
    public static Sprite ArrowCwOutline { get; private set; }
    public static Sprite ArrowCcwOutline { get; private set; }
    public static Sprite EditorButtonFill { get; private set; }

    private static bool loaded;

    public static void EnsureLoaded() {
        if(loaded) {
            return;
        }
        loaded = true;

        SwirlCw = MakeSprite("swirl_cw", 100f);
        SwirlCcw = MakeSprite("swirl_ccw", 100f);
        SwirlCwOutline = MakeSprite("swirl_cw_outline", 100f);
        SwirlCcwOutline = MakeSprite("swirl_ccw_outline", 100f);
        ArrowCw = MakeSprite("arrow_cw", 100f);
        ArrowCcw = MakeSprite("arrow_ccw", 100f);
        ArrowCwOutline = MakeSprite("arrow_cw_outline", 100f);
        ArrowCcwOutline = MakeSprite("arrow_ccw_outline", 100f);
        EditorButtonFill = MakeSprite("editor_button_fill", 1000f, new Vector4(77, 77, 77, 77));
    }

    private static Sprite MakeSprite(string name, float pixelsPerUnit, Vector4 border = default) {
        byte[] data = MainCore.Res.Load("Image.Bttp." + name + ".png");
        if(data == null || data.Length == 0) {
            MainCore.Log.Wrn($"[Nostalgia] missing sprite '{name}'");
            return null;
        }

        Texture2D tex = new(2, 2, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        if(!tex.LoadImage(data)) {
            Object.Destroy(tex);
            return null;
        }

        Rect rect = new(0, 0, tex.width, tex.height);
        Vector2 pivot = new(0.5f, 0.5f);
        Sprite sprite = border == default
            ? Sprite.Create(tex, rect, pivot, pixelsPerUnit)
            : Sprite.Create(tex, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        sprite.name = name;
        return sprite;
    }
}
