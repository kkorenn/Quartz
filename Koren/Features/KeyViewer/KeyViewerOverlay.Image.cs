using System.Net;
using System.Threading;
using Koren.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.Features.KeyViewer;

// Per-state background images for keys, stats and graphs — a port of DM Note's
// inactiveImage/activeImage + object-fit handling (useKeyElementStyles). Sources
// may be data: URIs, http(s) URLs (downloaded + disk-cached like fonts) or local
// file paths; tauri:/asset:/blob: (DM Note-internal) can't be resolved outside
// the app and are skipped. Textures are cached and shared by source string.
public static partial class KeyViewerOverlay {
    private static readonly Dictionary<string, Texture2D> cssImages = new(StringComparer.Ordinal);
    private static readonly HashSet<string> cssImagePending = new(StringComparer.Ordinal);
    private static readonly object cssImageLock = new();
    private static volatile bool cssImageArrived;

    // Resolves a source string to a texture, or null if absent / still loading /
    // unsupported. Main-thread only (Texture2D.LoadImage).
    private static Texture2D ResolveImage(string src) {
        if(string.IsNullOrWhiteSpace(src)) {
            return null;
        }
        string key = src.Trim();
        if(cssImages.TryGetValue(key, out Texture2D cached)) {
            return cached; // may be a negative-cached null
        }

        try {
            if(key.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
                int comma = key.IndexOf(',');
                int b64 = key.IndexOf("base64", StringComparison.OrdinalIgnoreCase);
                if(comma > 0 && b64 > 0 && b64 < comma) {
                    return Cache(key, LoadTex(Convert.FromBase64String(key.Substring(comma + 1))));
                }
                return Cache(key, null);
            }

            if(key.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                string path = ImageCachePath(key);
                if(File.Exists(path)) {
                    return Cache(key, LoadTex(File.ReadAllBytes(path)));
                }
                StartImageDownload(key, path);
                return null; // arrives later; Rebuild re-resolves from disk
            }

            // Local file: file:// scheme or an absolute path.
            string file = key.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(key).LocalPath
                : key;
            if(IsLikelyLocalPath(file) && File.Exists(file)) {
                return Cache(key, LoadTex(File.ReadAllBytes(file)));
            }
        } catch(Exception ex) {
            MainCore.Log.Msg("[KeyViewer] CSS image load failed: " + ex.Message);
        }

        return Cache(key, null); // unsupported (tauri:/asset:/blob:/relative)
    }

    private static bool IsLikelyLocalPath(string v) =>
        v.Length > 1 && (v[0] == '/' || v.StartsWith("\\\\", StringComparison.Ordinal)
            || (char.IsLetter(v[0]) && v[1] == ':'));

    private static Texture2D Cache(string key, Texture2D tex) {
        cssImages[key] = tex;
        return tex;
    }

    private static Texture2D LoadTex(byte[] bytes) {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        // ImageConversion.LoadImage auto-detects PNG/JPG and resizes the texture.
        return tex.LoadImage(bytes) ? tex : null;
    }

    private static string ImageCachePath(string url) {
        string dir = Path.Combine(MainCore.Paths.RootPath, "CssImages");
        Directory.CreateDirectory(dir);
        string ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if(ext.Length is < 2 or > 5) {
            ext = ".png";
        }
        return Path.Combine(dir, Hash(url) + ext);
    }

    private static void StartImageDownload(string url, string path) {
        lock(cssImageLock) {
            if(!cssImagePending.Add(url)) {
                return;
            }
        }
        var thread = new Thread(() => {
            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using var client = new WebClient();
                File.WriteAllBytes(path, client.DownloadData(url));
                cssImageArrived = true;
            } catch(Exception ex) {
                MainCore.Log.Msg("[KeyViewer] CSS image download failed: " + ex.Message);
            } finally {
                lock(cssImageLock) {
                    cssImagePending.Remove(url);
                }
            }
        }) { IsBackground = true, Name = "KorenCssImage" };
        thread.Start();
    }

    // Builds the key's image layer: a rounded-clipped child behind the text,
    // resolving both state textures up front. DM Note draws the image over the
    // background fill and under the label.
    private static void BuildKeyImage(Box box, DmNoteSpec spec) {
        if(!spec.HasImage || box.Fill == null) {
            return;
        }
        spec.IdleTex = ResolveImage(spec.InactiveImage);
        spec.ActiveTex = ResolveImage(spec.ActiveImage);
        if(spec.IdleTex == null && spec.ActiveTex == null) {
            return; // nothing resolved (yet) — Rebuild retries on arrival
        }

        Mask mask = box.Fill.GetComponent<Mask>() ?? box.Fill.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject obj = new("KeyImage");
        obj.transform.SetParent(box.Fill.transform, false);
        RawImage ri = obj.AddComponent<RawImage>();
        ri.raycastTarget = false;
        obj.transform.SetAsFirstSibling(); // behind the border ring and the text
        box.KeyImage = ri;
    }

    // Builds a single background image for a graph (always inactive).
    private static void BuildGraphImage(RectTransform parent, DmNoteSpec spec) {
        if(!spec.HasImage) {
            return;
        }
        Texture2D tex = ResolveImage(spec.InactiveImage) ?? ResolveImage(spec.ActiveImage);
        if(tex == null) {
            return;
        }
        GameObject obj = new("GraphImage");
        obj.transform.SetParent(parent, false);
        RawImage ri = obj.AddComponent<RawImage>();
        ri.raycastTarget = false;
        obj.transform.SetAsFirstSibling(); // behind the plotted mesh
        string fit = spec.ImageFitDefault.Length > 0 ? spec.ImageFitDefault : "cover";
        ApplyImageFit(ri, tex, fit, spec.W, spec.H);
        ri.color = Color.white;
    }

    // State-driven image swap on a press/release: pick the texture, fit it, and
    // dim the idle image when pressed with no dedicated active image.
    private static void ApplyImageState(Box box, DmNoteSpec spec, bool pressed) {
        if(box.KeyImage == null) {
            return;
        }
        bool usingActive = pressed && spec.ActiveTex != null;
        Texture2D tex = usingActive ? spec.ActiveTex : spec.IdleTex;
        if(tex == null) {
            box.KeyImage.enabled = false;
            return;
        }
        box.KeyImage.enabled = true;

        string fit = usingActive
            ? Pick(spec.ActiveImageFit, spec.ImageFitDefault)
            : Pick(spec.IdleImageFit, spec.ImageFitDefault);
        ApplyImageFit(box.KeyImage, tex, fit, spec.W, spec.H);

        // fallbackImageDimmed: pressed, no active image, idle image shown.
        bool dimmed = pressed && spec.ActiveTex == null && spec.IdleTex != null;
        box.KeyImage.color = dimmed ? new Color(0.62f, 0.62f, 0.62f, 1f) : Color.white;
    }

    private static string Pick(string specific, string fallback) =>
        specific.Length > 0 ? specific : fallback.Length > 0 ? fallback : "cover";

    // Lays out a RawImage to emulate CSS object-fit within a rect of rw x rh.
    // cover/fill stretch to the rect (cover crops via uvRect); contain/none size
    // the quad and centre it, relying on the parent Mask to clip.
    private static void ApplyImageFit(RawImage ri, Texture2D tex, string fit, float rw, float rh) {
        ri.texture = tex;
        RectTransform rt = ri.rectTransform;
        float tw = Mathf.Max(tex.width, 1), th = Mathf.Max(tex.height, 1);

        switch(fit?.ToLowerInvariant()) {
            case "fill":
                Stretch(rt);
                ri.uvRect = new Rect(0f, 0f, 1f, 1f);
                break;
            case "contain": {
                float scale = Mathf.Min(rw / tw, rh / th);
                Center(rt, tw * scale, th * scale);
                ri.uvRect = new Rect(0f, 0f, 1f, 1f);
                break;
            }
            case "none":
                Center(rt, tw, th); // native size, clipped by the mask
                ri.uvRect = new Rect(0f, 0f, 1f, 1f);
                break;
            default: { // cover
                Stretch(rt);
                float ra = rw / rh, ta = tw / th;
                ri.uvRect = ta > ra
                    ? new Rect((1f - ra / ta) * 0.5f, 0f, ra / ta, 1f)
                    : new Rect(0f, (1f - ta / ra) * 0.5f, 1f, ta / ra);
                break;
            }
        }
    }

    private static void Stretch(RectTransform rt) {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localRotation = Quaternion.identity;
    }

    private static void Center(RectTransform rt, float w, float h) {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        rt.localRotation = Quaternion.identity;
    }
}
