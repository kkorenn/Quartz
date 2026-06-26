using System.Collections;
using ADOFAI;
using Quartz.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Quartz.Features.Recorder;

// Bridges the two render passes. The realtime AUDIO pass ends with the level on its
// finished/results state; to run the offline VIDEO pass the level must replay so the
// scnGame.Play patch fires BeginSession again. How we replay depends on context:
//
//   editor playtest -> scnEditor.Play()         (controller.Restart drops back to the
//                                                 blank edit view here, so the video
//                                                 pass never starts and the captured
//                                                 audio buffer is orphaned)
//   normal play     -> scrController.Restart()   (reloads + replays the scene)
//
// Deferred until the level has settled out of the just-finished run, and bounded by a
// timeout so an unexpected state can't hang the handoff or strand the buffer.
internal sealed class RecorderHandoff : MonoBehaviour {
    private const float TimeoutSeconds = 3f;

    public static void BeginVideoPass() {
        GameObject go = new("QuartzRecorderHandoff");
        go.transform.SetParent(MainCore.Root.transform, false);
        go.AddComponent<RecorderHandoff>();
    }

    private IEnumerator Start() {
        bool editor = ADOBase.isLevelEditor && ADOBase.editor != null;

        // Wait for a replayable state: editor back in edit view (out of play mode), or a
        // live controller for normal play. Timeout-bounded so we still attempt the replay
        // rather than hanging if the readiness signal never flips.
        float waited = 0f;
        while(waited < TimeoutSeconds) {
            if(Recorder.Current != Recorder.State.Armed || Recorder.PrepassAudio == null) {
                Object.Destroy(gameObject);   // cancelled or torn down mid-handoff
                yield break;
            }
            bool ready;
            try {
                ready = editor
                    ? ADOBase.editor != null && !ADOBase.editor.playMode
                    : ADOBase.controller != null;
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
            if(editor && ADOBase.editor != null) {
                MainCore.Log.Msg("[Recorder] video pass: replaying via editor.Play()");
                ADOBase.editor.Play();
            } else if(ADOBase.controller != null) {
                MainCore.Log.Msg("[Recorder] video pass: replaying via controller.Restart()");
                ADOBase.controller.Restart(false);
            } else {
                throw new Exception("no editor or controller available to replay the level");
            }
        } catch(Exception e) {
            MainCore.Log.Wrn($"[Recorder] video-pass replay failed: {e.Message}");
            Recorder.ClearPrepass();
            Recorder.Current = Recorder.State.Idle;
            Recorder.OnSessionEnded();
        }

        Object.Destroy(gameObject);
    }
}
