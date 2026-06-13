using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using Koren.Core;
using UnityEngine;

namespace Koren.UI.Objects;

public abstract class UIObject {
    private static readonly List<UIObject> _tickables = [];

    public string Id { get; }
    public RectTransform Rect { get; }

    public bool OnlyModOn {
        get;
        set {
            if(field == value) {
                return;
            }

            field = value;
            if(field) {
                SetBlocked(!MainCore.IsModEnabled, true);
                MainCore.OnModEnabledChanged += ApplyStateForAction;
            } else {
                MainCore.OnModEnabledChanged -= ApplyStateForAction;
            }
        }
    }

    protected CanvasGroup CanvasGroup {
        get {
            field ??= Rect.GetComponent<CanvasGroup>() ?? Rect.gameObject.AddComponent<CanvasGroup>();
            return field;
        }
    }
    private GTween blockSeq;

    protected UIObject(string id, RectTransform rect) {
        Id = id;
        Rect = rect;
    }

    private void ApplyStateForAction(bool enabled, bool isDispose) {
        if(!OnlyModOn || isDispose) {
            return;
        }

        SetBlocked(!enabled);
    }

    public virtual void SetBlocked(bool blocked, bool noAnimate = false) {
        blockSeq?.Kill();

        float targetAlpha = blocked ? 0.4f : 1f;

        CanvasGroup cg = CanvasGroup;

        cg.interactable = !blocked;
        cg.blocksRaycasts = !blocked;

        if(noAnimate) {
            cg.alpha = targetAlpha;
            return;
        }

        // Capture the CanvasGroup and null-check it inside the tween: a
        // profile switch (UICore.Rebuild) can destroy this widget mid-fade,
        // and a tween completing onto a destroyed CanvasGroup throws on every
        // tick (it never finishes, so it never leaves the alive-list).
        blockSeq = GTweenSequenceBuilder.New()
            .Join(
                GTweenExtensions.Tween(
                    () => cg == null ? targetAlpha : cg.alpha,
                    x => { if(cg != null) cg.alpha = x; },
                    targetAlpha,
                    0.2f
                ).SetEasing(Easing.OutSine)
            ).Build();
        MainCore.TC.Play(blockSeq);
    }

    public virtual void Dispose() {
        blockSeq?.Kill();
        UnregisterTick();
    }

    protected void RegisterTick() => _tickables.Add(this);

    protected void UnregisterTick() => _tickables.Remove(this);

    public virtual void Tick() {
    }

    public static void TickAll() {
        for(int i = 0; i < _tickables.Count; i++) {
            _tickables[i].Tick();
        }
    }

    // Drops every registered tickable. Must run when the whole UI canvas is
    // torn down (profile switch rebuild) — stale entries would Tick destroyed
    // components and throw every frame.
    public static void DisposeAll() {
        for(int i = _tickables.Count - 1; i >= 0; i--) {
            _tickables[i].Dispose();
        }

        _tickables.Clear();
    }
}
