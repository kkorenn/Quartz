using System.Collections;
using ADOFAI;
using Quartz.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Recorder;

// Press-Render-to-start helper for the EDITOR. A render from the editor does a full
// reload from disk (ADOBase.RestartScene re-opens the saved file via
// scnEditor.levelToOpenOnLoad) so the captured run is byte-for-byte pristine. The
// reload tears down and rebuilds scnEditor, so we can't call Play() inline — this
// MonoBehaviour (parented to the DontDestroyOnLoad root, so it survives the scene
// swap) waits for the freshly-loaded editor to settle, deselects everything so the
// playtest begins at floor 0, then starts it. scnEditor.Play -> scnGame.Play fires
// the recorder's BeginSession.
//
// Readiness is gated on having SEEN a load cycle (isLoading true -> false) so we never
// trigger on the outgoing scene's still-live editor before LoadScene swaps it out.
internal sealed class RecorderReload : MonoBehaviour {
    // Disk reload re-parses the level and decodes its song, which can take a couple of
    // seconds on large levels — generous timeout so a slow load still starts the render.
    private const float TimeoutSeconds = 15f;

    public static void PlayEditorFromStartWhenReady() {
        GameObject go = new("QuartzRecorderReload");
        go.transform.SetParent(MainCore.Root.transform, false);
        go.AddComponent<RecorderReload>();
    }

    private IEnumerator Start() {
        float waited = 0f;
        bool sawLoading = false;   // require a full load cycle on the NEW scene

        while(waited < TimeoutSeconds) {
            if(Recorder.Current != Recorder.State.Armed) {
                Object.Destroy(gameObject);   // cancelled / torn down mid-reload
                yield break;
            }
            bool ready = false;
            try {
                scnEditor ed = ADOBase.editor;
                if(ed != null) {
                    if(ed.isLoading) {
                        sawLoading = true;
                    }
                    ready = sawLoading && !ed.isLoading && !ed.playMode
                            && ed.floors != null && ed.floors.Count > 1;
                }
            } catch {
                ready = false;   // scene mid-rebuild; keep waiting
            }
            if(ready) {
                break;
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        try {
            scnEditor ed = ADOBase.editor;
            if(ed != null && !ed.playMode && ed.floors != null && ed.floors.Count > 1) {
                ed.DeselectFloors(skipSaving: true);   // play from floor 0, not the selection
                MainCore.Log.Msg("[Recorder] editor reloaded from disk — playtest from start");
                ed.Play();
            } else {
                throw new Exception("editor did not become ready after reload");
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] editor reload-play failed: {e.Message}");
            Recorder.Current = Recorder.State.Idle;
            Recorder.Reloading = false;
        }

        Object.Destroy(gameObject);
    }
}
