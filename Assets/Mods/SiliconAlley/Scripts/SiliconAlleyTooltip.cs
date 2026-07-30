#nullable enable
using System;
using Localizor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static SiliconAlleyUI;

// Issue #146 (epic #142): the mod's tooltip system. A reimplementation of the game's Tooltip.TooltipSystem
// pattern on the MOD canvas — the game's own tooltip canvas is scene-authored with no code-set sortingOrder,
// so it is expected to draw UNDER our overlay (but see #165: a source grep cannot see an Inspector-authored
// sortingOrder, which is how the Notifications panel got above us); rendering our own panel on the same canvas is the
// only way a tooltip can sit above the window. The shape mirrors the game's (verified in the decompiled
// TooltipTarget/TooltipSystem): a 0.1s unscaled hover delay, one global current target so a new hover steals
// the panel, Hide on pointer-exit AND on OnDisable (no stale tooltip surviving its hidden owner), and a
// cursor-following panel that flips below the cursor near the top edge and clamps to the canvas rect.
//
// The raycast opt-in lives HERE: MakeText/MakeIcon stay raycastTarget = false (decorative graphics must not
// eat clicks), and Attach() flips raycastTarget on for exactly the graphic that wants hover. Presentation
// only — no gameplay or save state.
public static class SiliconAlleyTooltip
{
    public const float Delay = 0.1f; // matches the game's TooltipSystem.Delay

    private static SiliconAlleyTooltipView? _view;
    private static SiliconAlleyTooltipTarget? _current;

    // Attach a live-evaluated tooltip to a graphic (the raycast opt-in). The provider runs on show and on
    // the 1 Hz refresh while visible, so per-second values stay current; return null/empty for "no tooltip
    // right now". Idempotent — re-attaching just swaps the provider.
    public static SiliconAlleyTooltipTarget Attach(Graphic graphic, Func<string?> text)
    {
        graphic.raycastTarget = true;
        var target = graphic.gameObject.GetComponent<SiliconAlleyTooltipTarget>();
        if (target == null)
            target = graphic.gameObject.AddComponent<SiliconAlleyTooltipTarget>();
        target.Text = text;
        return target;
    }

    // Convenience: a fixed localized tooltip. The key resolves at show time (language-change safe).
    public static SiliconAlleyTooltipTarget Attach(Graphic graphic, string localeKey) =>
        Attach(graphic, () => localeKey.GetLocalization());

    // A hover dwelled past the delay — show the panel for this target (stealing it from any previous owner).
    internal static void Show(SiliconAlleyTooltipTarget target)
    {
        if (target.Text == null)
            return;
        var canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        var root = canvas.rootCanvas;
        // Lazily build one view per root canvas; rebuild if the canvas was destroyed (screen teardown) or
        // the target lives on a different canvas than the current view.
        if (_view == null || _view.transform.parent != root.transform)
        {
            if (_view != null)
                UnityEngine.Object.Destroy(_view.gameObject);
            _view = SiliconAlleyTooltipView.Build(root);
        }
        _current = target;
        _view.ShowFor(target.Text);
    }

    // Pointer-exit / owner-disable. Only the panel's current owner may dismiss it — a stale Hide from the
    // PREVIOUS target (its OnDisable firing after a new hover stole the panel) must not close the new one.
    internal static void Hide(SiliconAlleyTooltipTarget target)
    {
        if (_current != target)
            return;
        _current = null;
        if (_view != null)
            _view.HideNow();
    }
}

// #146: the per-graphic hover trigger. Counts unscaled hover time in Update (the mod's no-coroutine
// convention; the game's TooltipTarget uses WaitForSecondsRealtime for the same effect) and shows once per
// hover. OnDisable hides — a refresh that hides the owning card must take its tooltip down with it.
[DisallowMultipleComponent]
public sealed class SiliconAlleyTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Func<string?>? Text;

    private bool _hovering;
    private bool _shown;
    private float _t;

    public void OnPointerEnter(PointerEventData e)
    {
        _hovering = true;
        _shown = false;
        _t = 0f;
    }

    public void OnPointerExit(PointerEventData e)
    {
        _hovering = false;
        _shown = false;
        SiliconAlleyTooltip.Hide(this);
    }

    private void OnDisable()
    {
        _hovering = false;
        _shown = false;
        SiliconAlleyTooltip.Hide(this);
    }

    private void Update()
    {
        if (!_hovering || _shown)
            return;
        _t += Time.unscaledDeltaTime;
        if (_t < SiliconAlleyTooltip.Delay)
            return;
        _shown = true;
        SiliconAlleyTooltip.Show(this);
    }
}

// #146: the single tooltip panel — a card-backed, raycast-transparent rect that follows the cursor. Sized
// in code from the text's preferred values (no LayoutGroup/ContentSizeFitter: the panel lives outside every
// layout group and a tooltip resize must never wake the window's layout). Fades in via SiliconAlleyFadeIn;
// hiding is SetActive(false) — layout-inert here for the same reason.
internal sealed class SiliconAlleyTooltipView : MonoBehaviour
{
    private const float MaxWidth = 320f;
    private const float Pad = 10f;    // inner text padding
    private const float Offset = 24f; // gap between the cursor and the panel edge

    private RectTransform _rt = null!;
    private RectTransform _canvasRt = null!;
    private TMP_Text _body = null!;
    private SiliconAlleyFadeIn _fade = null!;
    private Func<string?>? _textFn;
    private string _shownText = "";
    private Vector2 _lastMouse;
    private float _refreshTimer;

    public static SiliconAlleyTooltipView Build(Canvas canvas)
    {
        // MakeCard gives the themed rounded surface + drop shadow; the panel itself must not catch rays.
        var panel = MakeCard(canvas.transform, "SiliconAlleyTooltip");
        panel.raycastTarget = false;
        var view = panel.gameObject.AddComponent<SiliconAlleyTooltipView>();
        view._rt = panel.rectTransform;
        view._canvasRt = (RectTransform)canvas.transform;

        // A subtle 2px inner stroke — the first consumer of the #143 outline sprite. Skipped when the
        // bundle predates it (flat card fallback, still perfectly readable).
        if (SiliconAlleyTheme.OutlineSprite != null)
        {
            var border = MakeImage(panel.transform, "Border", SiliconAlleyTheme.Divider);
            border.sprite = SiliconAlleyTheme.OutlineSprite;
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;
            Stretch(border.rectTransform);
        }

        view._body = MakeText(panel.transform, "Body", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        Stretch(view._body.rectTransform);
        view._body.rectTransform.offsetMin = new Vector2(Pad, Pad);
        view._body.rectTransform.offsetMax = new Vector2(-Pad, -Pad);

        view._fade = panel.gameObject.AddComponent<SiliconAlleyFadeIn>();
        panel.gameObject.SetActive(false);
        return view;
    }

    public void ShowFor(Func<string?> textFn)
    {
        _textFn = textFn;
        _shownText = "";
        if (!RefreshText())
        {
            HideNow(); // provider says "nothing to explain right now"
            return;
        }
        transform.SetAsLastSibling(); // above the window AND above any open modal
        gameObject.SetActive(true);
        _lastMouse = new Vector2(float.MinValue, float.MinValue); // force an immediate position
        FollowCursor();
        _refreshTimer = 1f;
        _fade.Play();
    }

    public void HideNow() => gameObject.SetActive(false);

    private void Update()
    {
        FollowCursor();
        // Re-evaluate the provider at 1 Hz so live values (demand, money) stay current while hovered.
        _refreshTimer -= Time.unscaledDeltaTime;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = 1f;
            RefreshText(); // an empty re-read keeps the last text — mid-hover blanking would just flicker
        }
    }

    // Pull the provider's current text; false when it has nothing to show. Resizes only on a real change.
    private bool RefreshText()
    {
        var text = _textFn?.Invoke();
        if (string.IsNullOrEmpty(text))
            return false;
        if (text == _shownText)
            return true;
        _shownText = text!;
        _body.text = text;
        // Preferred size under the width cap; +2px slack so rounding never wraps the last word.
        var pref = _body.GetPreferredValues(text, MaxWidth - 2f * Pad, 0f);
        _rt.sizeDelta = new Vector2(
            Mathf.Ceil(Mathf.Min(MaxWidth - 2f * Pad, pref.x) + 2f * Pad) + 2f,
            Mathf.Ceil(pref.y + 2f * Pad));
        return true;
    }

    // The game's TooltipSystem.SetPosition, on the overlay canvas (null camera): only reposition when the
    // cursor actually moved, prefer sitting above the cursor, flip below near the top edge, clamp to the
    // canvas rect on all four sides.
    private void FollowCursor()
    {
        Vector2 mouse = Input.mousePosition;
        if (mouse == _lastMouse)
            return;
        _lastMouse = mouse;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out var local);
        var half = _rt.rect.size * 0.5f;
        var area = _canvasRt.rect;
        var y = local.y + Offset + half.y;
        if (y + half.y > area.yMax)
            y = local.y - Offset - half.y;
        var x = Mathf.Clamp(local.x, area.xMin + half.x, area.xMax - half.x);
        y = Mathf.Clamp(y, area.yMin + half.y, area.yMax - half.y);
        _rt.anchoredPosition = new Vector2(x, y);
    }
}
