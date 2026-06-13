using Koren.Core;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using Koren.Compat.OVC;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.Resource;

public enum Asset {
    SUIT_Regular,
    SUIT_Medium,

    KorenLogo,
    OV5LogoOutline256,
    Circle256,
    CircleHalf256,
    X128,
    Monitor128,
    Gear128,
    Image128,
    Text128,
    Book128,
    Star128,
    ToggleCircle128,
    CircleOutline256,
    Triangle128,
    Power128,
    MagnifyingGlass128,
    Gamepad128,
    Wrench128,
    AdjustmentsHorizontal128,
    Users128,
    OttoAuto,
}

public sealed class ResourceManager(Assembly assembly, string resourcePath) : IDisposable {
    private readonly Dictionary<string, object> cache = [];

    public byte[] Load(string path) {
        if(string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        try {
            using Stream stream = assembly.GetManifestResourceStream(resourcePath + path);

            if(stream == null) {
                return null;
            }

            if(stream.Length <= 0) {
                return [];
            }

            byte[] data = new byte[stream.Length];
            int offset = 0;

            while(offset < data.Length) {
                int read = stream.Read(data, offset, data.Length - offset);

                if(read <= 0) {
                    break;
                }

                offset += read;
            }

            return offset == data.Length ? data : null;
        } catch {
            return null;
        }
    }

    public Texture2D LoadTexture(string path, FilterMode filter = FilterMode.Trilinear) {
        if(cache.TryGetValue(path, out object cached)) {
            return cached as Texture2D;
        }

        byte[] data = Load(path);

        if(data == null || data.Length == 0) {
            return null;
        }

        // Mip chain on: the UI draws these (e.g. 256px circle corners) far
        // below native size, and minification without mips aliases into
        // visible stair-stepping on rounded corners. LoadImage rebuilds the
        // full chain after decoding; trilinear blends between mip levels.
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, true, true);

        if(!OVC_Texture2D.LoadImage(texture, data)) {
            Object.Destroy(texture);
            return null;
        }

        texture.filterMode = filter;
        texture.anisoLevel = 4;
        // Negative bias samples one notch sharper than the computed mip
        // level — counteracts the softness trilinear minification adds to
        // sprite edges without bringing the stair-stepping back.
        texture.mipMapBias = -0.7f;
        cache[path] = texture;
        return texture;
    }

    public TMP_FontAsset LoadFont(string path, string tempPath) {
        if(cache.TryGetValue(path, out object cached)) {
            return cached as TMP_FontAsset;
        }

        byte[] data = Load(path);

        if(data == null) {
            return null;
        }

        string directory = Path.GetDirectoryName(tempPath);
        if(!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(tempPath, data);

        Font font = new(tempPath);
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);

        cache[path] = asset;
        return asset;
    }

    public T Get<T>(Asset asset) where T : class {
        if(!assetMap.TryGetValue(asset, out string path)) {
            return null;
        }

        return GetInternal<T>(path, asset.ToString());
    }

    public T Get<T>(string path) where T : class {
        if(string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        return GetInternal<T>(path, fileName);
    }
    public TMP_FontAsset GetFont(string path, string customTempPath) => LoadFont(path, customTempPath);
    private T GetInternal<T>(string path, string assetNameForFont) where T : class {
        object result = null;

        if(typeof(T) == typeof(Texture2D)) {
            result = LoadTexture(path);
        } else if(typeof(T) == typeof(TMP_FontAsset)) {
            string tempPath = Path.Combine(MainCore.Paths.TempPath, assetNameForFont + ".otf");
            result = LoadFont(path, tempPath);
        }

        return result as T;
    }

    public void Dispose() {
        foreach(object item in cache.Values) {
            switch(item) {
                case Texture2D texture:
                    Object.Destroy(texture);
                    break;

                case TMP_FontAsset font:
                    Object.Destroy(font);
                    break;
            }
        }

        cache.Clear();
    }

    private readonly Dictionary<Asset, string> assetMap = new() {
        [Asset.SUIT_Regular] = "Font.SUIT-Regular.otf",
        [Asset.SUIT_Medium] = "Font.SUIT-Medium.otf",
        [Asset.KorenLogo] = "Image.KorenLogo.png",
        [Asset.OV5LogoOutline256] = "Image.OV5LogoOutline256.png",
        [Asset.Circle256] = "Image.Circle256.png",
        [Asset.CircleHalf256] = "Image.CircleHalf256.png",
        [Asset.X128] = "Image.X128.png",
        [Asset.Monitor128] = "Image.Monitor128.png",
        [Asset.Gear128] = "Image.Gear128.png",
        [Asset.Image128] = "Image.Image128.png",
        [Asset.Text128] = "Image.Text128.png",
        [Asset.Book128] = "Image.Book128.png",
        [Asset.Star128] = "Image.Star128.png",
        [Asset.ToggleCircle128] = "Image.ToggleCircle128.png",
        [Asset.CircleOutline256] = "Image.CircleOutline256.png",
        [Asset.Triangle128] = "Image.Triangle128.png",
        [Asset.Power128] = "Image.Power128.png",
        [Asset.MagnifyingGlass128] = "Image.MagnifyingGlass128.png",
        [Asset.Gamepad128] = "Image.Gamepad128.png",
        [Asset.Wrench128] = "Image.Wrench128.png",
        [Asset.AdjustmentsHorizontal128] = "Image.AdjustmentsHorizontal128.png",
        [Asset.Users128] = "Image.Users128.png",
        [Asset.OttoAuto] = "Image.OttoAuto.png"
    };
}