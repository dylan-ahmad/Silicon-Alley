#nullable enable
using System;
using Localizor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SiliconAlleyUI;

// Issue #146 (epic #142): the in-canvas confirm modal for destructive/irreversible actions. Deliberately
// NOT the game's HudConfirm (docs/CAPABILITIES.md Q3): that dialog's scene canvas has no code-set
// sortingOrder so it draws UNDER our sortingOrder-5000 overlay, its labels are localization-key-only (no
// dynamic strings like the product name), and it silently auto-confirms when its host UI is absent or the
// skip modifier is held — all three disqualify it for a destructive confirm on top of our window.
//
// Shape: a full-canvas scrim (raycast-on, so everything behind it is physically unclickable) + a centred
// panel with [Cancel (Slate)] [confirm (Danger)]. Esc and a backdrop click cancel; the screen's Update
// routes Esc here first via TryHandleEscape so one press closes the modal, not the modal AND the window.
// Opened last-sibling on the caller's canvas; one modal at a time (re-opening replaces the content).
// Presentation only — the confirmed action runs via the caller's onConfirm.
public static class SiliconAlleyModal
{
    private static SiliconAlleyModalView? _view;

    public static bool IsOpen => _view != null && _view.gameObject.activeSelf;

    // Open (or retarget) the confirm dialog. Strings arrive pre-localized so callers can Compose dynamic
    // bodies; the confirm button is Danger-styled — use this for destructive/irreversible actions only.
    public static void Confirm(Transform canvasRoot, string title, string body, string confirmLabel, Action onConfirm)
    {
        if (canvasRoot == null)
            return;
        // Lazily build one view per canvas; rebuild if the canvas was torn down or the caller's differs.
        if (_view == null || _view.transform.parent != canvasRoot)
        {
            if (_view != null)
                UnityEngine.Object.Destroy(_view.gameObject);
            _view = SiliconAlleyModalView.Build(canvasRoot);
        }
        _view.Open(title, body, confirmLabel, onConfirm);
    }

    // The screen's Esc handler calls this FIRST: an open modal consumes the press (cancel) and the screen
    // stays put; false = nothing open, the screen may treat Esc as its own close.
    public static bool TryHandleEscape()
    {
        if (!IsOpen)
            return false;
        _view!.Cancel();
        return true;
    }

    // Hard close without confirming — the screen's Close/teardown path, so a hidden window never strands
    // a floating modal.
    public static void CloseAll()
    {
        if (IsOpen)
            _view!.Cancel();
    }
}

// #146: the modal's view — scrim + panel, built once per canvas and reused. Opens with the project
// screen's transition feel (CanvasGroup alpha + 0.96→1 scale pop, SmoothStep over unscaled time); both
// are layout-inert. The panel sizes itself via a ContentSizeFitter, which is safe here because the view
// lives directly under the canvas, outside every layout group.
internal sealed class SiliconAlleyModalView : MonoBehaviour
{
    private const float PanelWidth = 420f;
    private const float TransitionDuration = 0.16f; // matches the screen's page transition (#56)
    private const float ScalePopFrom = 0.96f;

    private CanvasGroup _group = null!;
    private RectTransform _panelRt = null!;
    private TMP_Text _title = null!, _body = null!, _confirmLabel = null!;
    private Action? _onConfirm;
    private float _animT = 1f;

    public static SiliconAlleyModalView Build(Transform canvasRoot)
    {
        var go = new GameObject("SiliconAlleyModal", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        Stretch((RectTransform)go.transform);
        var view = go.AddComponent<SiliconAlleyModalView>();
        view._group = go.AddComponent<CanvasGroup>();

        // Full-canvas scrim: raycast-ON — while the modal shows, nothing behind it (including the screen's
        // own backdrop-close button) can be clicked. Clicking it cancels.
        var scrim = MakeImage(go.transform, "Scrim", SiliconAlleyTheme.Scrim);
        Stretch(scrim.rectTransform);
        var backdrop = scrim.gameObject.AddComponent<Button>();
        backdrop.transition = Selectable.Transition.None;
        backdrop.onClick.AddListener(view.Cancel);

        var panel = MakePanel(go.transform, "ModalPanel");
        view._panelRt = panel.rectTransform;
        view._panelRt.anchorMin = view._panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        view._panelRt.sizeDelta = new Vector2(PanelWidth, 0f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(20, 20, 18, 18);
        v.spacing = SiliconAlleyTheme.Space.Medium;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        view._title = MakeText(panel.transform, "Title", SiliconAlleyTheme.Sizes.Subtitle, TextAnchor.MiddleLeft, FontStyle.Bold);
        view._title.color = SiliconAlleyTheme.Header;
        view._body = MakeText(panel.transform, "Body", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        view._body.color = SiliconAlleyTheme.TextMuted;

        var row = MakeRow(panel.transform, 10f, 40);
        MakeButton(row.transform, "siliconalley:ui_cancel".GetLocalization(), view.Cancel);
        var confirm = MakeButton(row.transform, "", view.ConfirmPressed, primary: true);
        confirm.GetComponent<Image>().color = SiliconAlleyTheme.Danger; // destructive = red, never amber (#143)
        view._confirmLabel = confirm.GetComponentInChildren<TMP_Text>();

        go.SetActive(false);
        return view;
    }

    public void Open(string title, string body, string confirmLabel, Action onConfirm)
    {
        _title.text = title;
        _body.text = body;
        _confirmLabel.text = confirmLabel;
        _onConfirm = onConfirm;
        transform.SetAsLastSibling(); // above the window (the tooltip re-lasts itself above us on show)
        gameObject.SetActive(true);
        _animT = 0f;
        _group.alpha = 0f;
        _panelRt.localScale = new Vector3(ScalePopFrom, ScalePopFrom, 1f);
    }

    public void Cancel() => HideNow();

    public void ConfirmPressed()
    {
        var confirmed = _onConfirm;
        HideNow(); // close FIRST so the callback's Refresh sees the modal gone
        confirmed?.Invoke();
    }

    private void HideNow()
    {
        _onConfirm = null;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        // The open transition — the screen's page-transition math on unscaled time (#56 conventions).
        if (_animT >= 1f)
            return;
        _animT += Time.unscaledDeltaTime / TransitionDuration;
        var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_animT));
        _group.alpha = t;
        var s = Mathf.Lerp(ScalePopFrom, 1f, t);
        _panelRt.localScale = new Vector3(s, s, 1f);
        if (_animT >= 1f)
        {
            _group.alpha = 1f;
            _panelRt.localScale = Vector3.one;
        }
    }
}
