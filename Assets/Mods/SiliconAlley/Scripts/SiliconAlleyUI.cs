#nullable enable
using System;
using Localizor;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Issue #54 (epic #53 — UI overhaul foundation): the reusable styled-component layer. These are the
// Make* helpers, lifted out of SiliconAlleyProjectScreen so every screen (#56–#61) builds on one set of
// primitives, and re-backed by the bundled 9-slice sprite kit in SiliconAlleyTheme: MakePanel/MakeCard/
// MakeButton now draw rounded sprites (Image.type = Sliced) tinted by the palette instead of flat navy
// boxes. Each sprite-backed helper degrades to the old flat-colour box when its sprite is null (older
// bundle / load failure), so nothing breaks. Backdrop + divider stay deliberately flat.
//
// Presentation only — these build GameObjects/Components and never touch gameplay or save state.
public static class SiliconAlleyUI
{
    // ---- Sprite-backed surfaces. ----

    // A rounded window/panel background (the outer surface).
    public static Image MakePanel(Transform parent, string name)
    {
        var image = MakeImage(parent, name, SiliconAlleyTheme.Surface);
        ApplySlice(image, SiliconAlleyTheme.PanelSprite);
        return image;
    }

    // A rounded raised card surface for grouping content.
    public static Image MakeCard(Transform parent, string name)
    {
        var image = MakeImage(parent, name, SiliconAlleyTheme.Card);
        ApplySlice(image, SiliconAlleyTheme.CardSprite);
        return image;
    }

    // Turn a flat Image into a 9-sliced sprite render; no-op (flat fallback) when the sprite is absent.
    private static void ApplySlice(Image image, Sprite? sprite)
    {
        if (sprite == null)
            return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f; // keep the authored corner radius at its native size
    }

    // ---- Text. ----

    public static TMP_Text MakeText(Transform parent, string name, int size, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        var font = SiliconAlleyTheme.Font;
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.fontStyle = style == FontStyle.Bold ? FontStyles.Bold
            : style == FontStyle.Italic ? FontStyles.Italic
            : FontStyles.Normal;
        text.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        text.color = SiliconAlleyTheme.Text;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        go.AddComponent<LayoutElement>().minHeight = size + 10;
        return text;
    }

    // A standalone label inside a horizontal row (no button behaviour).
    public static TMP_Text MakeTextButtonless(Transform parent, string value)
    {
        var t = MakeText(parent, "Label", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleCenter);
        t.text = value;
        return t;
    }

    // A section header: a localization key resolved + accent-coloured.
    public static void MakeHeader(Transform parent, string key)
    {
        var header = MakeText(parent, "Header", SiliconAlleyTheme.Sizes.Header, TextAnchor.MiddleLeft, FontStyle.Bold);
        header.color = SiliconAlleyTheme.Header;
        header.text = key.GetLocalization();
    }

    // ---- Buttons. ----

    public static Button MakeButton(Transform parent, string label, UnityAction onClick, bool primary = false)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = primary ? SiliconAlleyTheme.Accent : SiliconAlleyTheme.Slate;
        ApplySlice(image, SiliconAlleyTheme.ButtonSprite);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        // Hover/press/disabled feedback. normalColor stays white so each button's own image.color shows
        // through (the scope/overtime/hold buttons recolour their image to signal state); the others tint.
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.pressedColor = new Color(0.66f, 0.66f, 0.66f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 38f;
        le.preferredHeight = 38f;
        le.flexibleWidth = 1f;

        var text = MakeText(go.transform, "Label", SiliconAlleyTheme.Sizes.Button, TextAnchor.MiddleCenter, FontStyle.Bold);
        text.text = label;
        Stretch(text.rectTransform);

        // Issue #61: subtle hover/press scale on top of the colour tint (gated on the button's interactable state).
        go.AddComponent<SiliconAlleyHoverScale>().Gate = button;

        if (onClick != null)
            button.onClick.AddListener(onClick);
        return button;
    }

    // ---- Icons (issue #55). Concept icons are tinted, non-9-sliced (Simple) images; a null sprite is
    // hidden (Image disabled) so no white box shows. ----

    // A standalone icon image for a header/row (phase indicator, business-type selector). Sized via a
    // LayoutElement so it behaves inside the Make* layout groups; null sprite ⇒ hidden, no broken sprite.
    public static Image MakeIcon(Transform parent, Sprite? sprite, float size, Color? tint = null)
    {
        var image = MakeImage(parent, "Icon", tint ?? SiliconAlleyTheme.Text);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var le = image.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = size;
        le.minHeight = le.preferredHeight = size;
        le.flexibleWidth = 0f;
        SetIconSprite(image, sprite);
        return image;
    }

    // Swap an icon image's sprite, hiding the whole image when the sprite is null (so no default white box
    // renders). Cheap + idempotent — safe to call every refresh.
    public static void SetIconSprite(Image image, Sprite? sprite)
    {
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    // Place (or update) a left-aligned icon inside a button built by MakeButton, and inset its "Label" so the
    // text clears the icon. Idempotent: finds-or-creates the "Icon" child, so it's safe to call every refresh
    // (the feature/tool/platform pools are relabelled per business type). A null icon hides it + un-insets the
    // label, reverting to the text-only look (graceful fallback).
    public static void SetButtonIcon(Button button, Sprite? icon, float size = 20f, Color? tint = null)
    {
        var existing = button.transform.Find("Icon");
        Image image;
        if (existing != null)
        {
            image = existing.GetComponent<Image>();
        }
        else
        {
            image = MakeImage(button.transform, "Icon", tint ?? SiliconAlleyTheme.Text);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(10f, 0f); // left padding inside the button
        }

        SetIconSprite(image, icon);

        var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
        if (label != null)
        {
            var inset = icon != null ? size + 16f : 0f; // keep centred text clear of the icon when present
            label.margin = new Vector4(inset, label.margin.y, label.margin.z, label.margin.w);
        }
    }

    // ---- Cards (issue #57). A design-document card: [icon] [title + cost/benefit chips] [state badge].
    // Reusable across the wizard pickers. onClick optional — pass null for a read-only card (no Button, no
    // hover), e.g. the Dependencies coverage rows. The screen tints c.Card + sets the badge per state. ----

    public sealed class CardItem
    {
        public GameObject Root = null!;
        public Button? Button;      // null for a read-only card
        public Image Card = null!;  // background — set its colour for the state tint
        public Image Icon = null!;
        public TMP_Text Title = null!;
        public Image[] Chips = null!;
        public TMP_Text[] ChipLabels = null!;
        public Image Badge = null!;
        public TMP_Text BadgeLabel = null!;
    }

    public static CardItem MakeCardItem(Transform parent, UnityAction onClick, int chipCapacity = 3)
    {
        var go = new GameObject("CardItem", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var card = go.AddComponent<Image>();
        card.color = SiliconAlleyTheme.Card;
        ApplySlice(card, SiliconAlleyTheme.CardSprite);

        var item = new CardItem { Root = go, Card = card };
        if (onClick != null)
        {
            var button = go.AddComponent<Button>();
            button.targetGraphic = card;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            item.Button = button;
            go.AddComponent<SiliconAlleyHoverScale>().Gate = button; // issue #61: hover/press scale (clickable cards)
        }

        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(10, 10, 8, 8);
        row.spacing = 10f;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleLeft;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 52f;
        le.flexibleWidth = 1f;

        // Icon (left, fixed).
        item.Icon = MakeImage(go.transform, "Icon", SiliconAlleyTheme.Text);
        item.Icon.type = Image.Type.Simple;
        item.Icon.preserveAspect = true;
        item.Icon.raycastTarget = false;
        var iconLe = item.Icon.gameObject.AddComponent<LayoutElement>();
        iconLe.minWidth = iconLe.preferredWidth = 30f;
        iconLe.minHeight = iconLe.preferredHeight = 30f;

        // Body (title + chips), flexible width so the badge is pushed to the right.
        var body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(go.transform, false);
        var bodyV = body.AddComponent<VerticalLayoutGroup>();
        bodyV.spacing = 4f;
        bodyV.childControlWidth = bodyV.childControlHeight = true;
        bodyV.childForceExpandWidth = true;   // children fill the body width (so long titles wrap, not overflow)
        bodyV.childForceExpandHeight = false;
        bodyV.childAlignment = TextAnchor.MiddleLeft;
        body.AddComponent<LayoutElement>().flexibleWidth = 1f;

        item.Title = MakeText(body.transform, "Title", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft, FontStyle.Bold);
        item.Title.raycastTarget = false;

        var chipsGo = new GameObject("Chips", typeof(RectTransform));
        chipsGo.transform.SetParent(body.transform, false);
        var chipsH = chipsGo.AddComponent<HorizontalLayoutGroup>();
        chipsH.spacing = 6f;
        chipsH.childControlWidth = chipsH.childControlHeight = true;
        chipsH.childForceExpandWidth = chipsH.childForceExpandHeight = false;
        chipsH.childAlignment = TextAnchor.MiddleLeft;
        chipsGo.AddComponent<LayoutElement>().minHeight = 18f;

        item.Chips = new Image[chipCapacity];
        item.ChipLabels = new TMP_Text[chipCapacity];
        for (var i = 0; i < chipCapacity; i++)
            item.Chips[i] = MakeChip(chipsGo.transform, SiliconAlleyTheme.Elevated, SiliconAlleyTheme.TextMuted, out item.ChipLabels[i]);

        // State badge (right, fixed to its content).
        item.Badge = MakeChip(go.transform, SiliconAlleyTheme.Slate, SiliconAlleyTheme.Text, out item.BadgeLabel);
        return item;
    }

    // A small rounded pill that auto-sizes to its text (chips + state badges). No ContentSizeFitter: the
    // pill's own HorizontalLayoutGroup reports its preferred width to the parent layout group. Public so
    // other screens reuse it (e.g. the dashboard's demand-trend pill, issue #59).
    public static Image MakeChip(Transform parent, Color bg, Color fg, out TMP_Text label)
    {
        var go = new GameObject("Chip", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = false;
        if (SiliconAlleyTheme.ButtonSprite != null)
        {
            img.sprite = SiliconAlleyTheme.ButtonSprite;
            img.type = Image.Type.Simple; // Simple — the 9-slice border doesn't fit a ~18px pill
        }
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 2, 2);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleCenter;

        label = MakeText(go.transform, "ChipLabel", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleCenter);
        label.color = fg;
        label.enableWordWrapping = false;
        label.GetComponent<LayoutElement>().minHeight = 16f; // trim the chip height
        return img;
    }

    // Fill the first texts.Length chips with text; hide the remaining chip slots.
    public static void SetCardChips(CardItem c, params string[] texts)
    {
        for (var i = 0; i < c.Chips.Length; i++)
        {
            var on = texts != null && i < texts.Length && !string.IsNullOrEmpty(texts[i]);
            c.Chips[i].gameObject.SetActive(on);
            if (on)
                c.ChipLabels[i].text = texts![i]; // `on` already guarantees texts != null
        }
    }

    // Set the state badge's text + colour; hide it entirely when text is null/empty.
    public static void SetCardBadge(CardItem c, string text, Color color)
    {
        var on = !string.IsNullOrEmpty(text);
        c.Badge.gameObject.SetActive(on);
        if (!on)
            return;
        c.Badge.color = color;
        c.BadgeLabel.text = text;
    }

    // ---- Review primitives (issue #58). A rounded card panel with a vertical content layout + scannable
    // "[icon] label …… value" stat rows. Reused by the summary review card (and later dashboard/sections). ----

    // A rounded card ready to hold stacked content (padding + vertical layout). Returns the card GameObject.
    public static GameObject MakeCardPanel(Transform parent, string name, int pad = 12)
    {
        var card = MakeCard(parent, name);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(pad, pad, pad, pad);
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return card.gameObject;
    }

    public sealed class StatRow
    {
        public GameObject Root = null!;
        public Image Icon = null!;
        public TMP_Text Label = null!;
        public TMP_Text Value = null!;
    }

    // "[icon 22px] label (muted, hugs left) …… value (bold, right-aligned, fills + wraps)".
    public static StatRow MakeStatRow(Transform parent)
    {
        var go = new GameObject("StatRow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        go.AddComponent<LayoutElement>().minHeight = 26f;

        var icon = MakeImage(go.transform, "Icon", SiliconAlleyTheme.TextMuted);
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var il = icon.gameObject.AddComponent<LayoutElement>();
        il.minWidth = il.preferredWidth = 22f;
        il.minHeight = il.preferredHeight = 22f;

        var label = MakeText(go.transform, "Label", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        label.color = SiliconAlleyTheme.TextMuted;
        label.enableWordWrapping = false;
        label.GetComponent<LayoutElement>().flexibleWidth = 0f; // hug content on the left

        var value = MakeText(go.transform, "Value", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft, FontStyle.Bold);
        value.alignment = TextAlignmentOptions.Right;
        value.GetComponent<LayoutElement>().flexibleWidth = 1f; // fill the rest; wraps if the value is long

        return new StatRow { Root = go, Icon = icon, Label = label, Value = value };
    }

    // ---- Progress bar (issue #59). A rounded track + accent fill for a 0..1 fraction. The fill is a
    // single Image driven by fillAmount (Image.Type.Filled) — NOT a width-sized child — so SetProgress on
    // the 1s refresh tick never feeds the VerticalLayoutGroup/ContentSizeFitter (no layout rebuild / height
    // jitter, the same layout-safety reason #56 keeps alpha/scale off the layout path). ----

    public sealed class ProgressBar
    {
        public GameObject Root = null!;
        public Image Track = null!; // background
        public Image Fill = null!;  // accent fill (fillAmount = fraction)
        public SiliconAlleyAnimatedFill Anim = null!; // issue #61: tweens Fill.fillAmount toward the target
    }

    // A full-width progress bar of fixed height. Track tinted Elevated, fill tinted Accent; both reuse the
    // rounded ButtonSprite when present (flat-colour fallback when the bundle lacks it).
    public static ProgressBar MakeProgressBar(Transform parent, float height = 10f)
    {
        var go = new GameObject("ProgressBar", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
        le.flexibleWidth = 1f;

        var track = MakeImage(go.transform, "Track", SiliconAlleyTheme.Elevated);
        track.raycastTarget = false;
        if (SiliconAlleyTheme.ButtonSprite != null)
        {
            track.sprite = SiliconAlleyTheme.ButtonSprite;
            track.type = Image.Type.Simple;
        }
        Stretch(track.rectTransform);

        var fill = MakeImage(go.transform, "Fill", SiliconAlleyTheme.Accent);
        fill.raycastTarget = false;
        if (SiliconAlleyTheme.ButtonSprite != null)
            fill.sprite = SiliconAlleyTheme.ButtonSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        Stretch(fill.rectTransform);

        // Issue #61: tween the fill toward its target each frame instead of snapping.
        var anim = fill.gameObject.AddComponent<SiliconAlleyAnimatedFill>();
        anim.Fill = fill;
        return new ProgressBar { Root = go, Track = track, Fill = fill, Anim = anim };
    }

    // Set the bar's 0..1 target fraction (the AnimatedFill tweens to it); optionally recolour the fill
    // instantly (e.g. amber while a contract pauses the product).
    public static void SetProgress(ProgressBar bar, float fraction01, Color? fillColor = null)
    {
        bar.Anim.Target = Mathf.Clamp01(fraction01);
        if (fillColor.HasValue)
            bar.Fill.color = fillColor.Value;
    }

    // Issue #61: get-or-add a number animator on a label and point it at a new target + formatter. The value
    // counts toward the target each frame (snaps on first assignment to avoid a count-from-zero on first reveal).
    public static void AnimateNumber(TMP_Text text, float target, Func<float, string> format)
    {
        var anim = text.GetComponent<SiliconAlleyAnimatedNumber>() ?? text.gameObject.AddComponent<SiliconAlleyAnimatedNumber>();
        anim.Set(text, target, format);
    }

    // ---- Flat primitives + layout containers (unchanged behaviour; backdrop/divider stay flat). ----

    // A thin separator line for visual grouping between sections.
    public static void MakeDivider(Transform parent)
    {
        var divider = MakeImage(parent, "Divider", SiliconAlleyTheme.Divider);
        var le = divider.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 2f;
    }

    // A plain colour rectangle (no sprite) — used for the dim backdrop, dividers, slider parts.
    public static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    public static void FixWidth(Component control, float width)
    {
        var le = control.GetComponent<LayoutElement>() ?? control.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.flexibleWidth = 0f;
    }

    // A vertical sub-container the screen toggles per phase (SetActive). It reports its own preferred
    // height, so the panel's ContentSizeFitter shrinks to whichever section is active.
    public static GameObject MakeSection(Transform parent)
    {
        var go = new GameObject("Section", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        return go;
    }

    public static GameObject MakeRow(Transform parent, float spacing = 8f, int minHeight = 36)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleCenter;
        go.AddComponent<LayoutElement>().minHeight = minHeight;
        return go;
    }

    // Issue #81: a row of equal-width, TOP-aligned columns for a wide multi-column layout (the design wizard's
    // Software-Inc-scale phases; reusable by the dependencies/market screens). Unlike MakeRow, columns are not
    // vertically centred and there's no forced min-height — each column sizes to its own content and the row's
    // height is the tallest column. Put a MakeSection in each column to stack that column's controls.
    public static GameObject MakeColumns(Transform parent, float spacing = 14f)
    {
        var go = new GameObject("Columns", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;   // equal-width columns share the wide window
        h.childForceExpandHeight = false; // each column hugs its content height
        h.childAlignment = TextAnchor.UpperCenter;
        return go;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void StretchBand(RectTransform rt, float minY, float maxY)
    {
        rt.anchorMin = new Vector2(0f, minY);
        rt.anchorMax = new Vector2(1f, maxY);
        rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
        rt.offsetMax = new Vector2(rt.offsetMax.x, 0f);
    }

    // A functional uGUI slider built from the standard background / fill / handle hierarchy.
    public static Slider MakeSlider(Transform parent)
    {
        var go = new GameObject("FocusSlider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var slider = go.AddComponent<Slider>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 24f;
        le.flexibleWidth = 1f;

        var background = MakeImage(go.transform, "Background", new Color(0.12f, 0.12f, 0.15f, 1f));
        StretchBand(background.rectTransform, 0.35f, 0.65f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        StretchBand(fillAreaRt, 0.35f, 0.65f);
        fillAreaRt.offsetMin = new Vector2(8f, fillAreaRt.offsetMin.y);
        fillAreaRt.offsetMax = new Vector2(-8f, fillAreaRt.offsetMax.y);
        var fill = MakeImage(fillArea.transform, "Fill", SiliconAlleyTheme.Accent);
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        fillRt.sizeDelta = new Vector2(10f, 0f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.anchorMin = Vector2.zero;
        handleAreaRt.anchorMax = Vector2.one;
        handleAreaRt.offsetMin = new Vector2(8f, 0f);
        handleAreaRt.offsetMax = new Vector2(-8f, 0f);
        var handle = MakeImage(handleArea.transform, "Handle", new Color(0.88f, 0.90f, 0.96f, 1f));
        var handleRt = handle.rectTransform;
        handleRt.anchorMin = new Vector2(0f, 0f);
        handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.sizeDelta = new Vector2(16f, 0f);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 0.5f;
        return slider;
    }

    public static TMP_InputField MakeInputField(Transform parent, string name, string placeholderText, int characterLimit = 64)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = SiliconAlleyTheme.Elevated;
        ApplySlice(image, SiliconAlleyTheme.ButtonSprite);
        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.characterLimit = characterLimit;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.textViewport = (RectTransform)go.transform;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 38f;
        le.flexibleWidth = 1f;

        var text = MakeText(go.transform, "Text", SiliconAlleyTheme.Sizes.Button, TextAnchor.MiddleLeft);
        text.margin = new Vector4(12f, 7f, 12f, 0f);
        Stretch(text.rectTransform);
        input.textComponent = text;

        var placeholder = MakeText(go.transform, "Placeholder", SiliconAlleyTheme.Sizes.Button, TextAnchor.MiddleLeft, FontStyle.Italic);
        placeholder.text = placeholderText;
        placeholder.color = SiliconAlleyTheme.TextMuted;
        placeholder.margin = new Vector4(12f, 7f, 12f, 0f);
        Stretch(placeholder.rectTransform);
        input.placeholder = placeholder;

        return input;
    }
}

// ---- Issue #61: interaction-polish components. Self-contained MonoBehaviours that drive their own per-frame
// animation (the #56 manual-lerp convention, Time.unscaledDeltaTime) so they work on any screen with no central
// Update loop. Presentation only — they animate transforms / fillAmount / text, never gameplay or save state. ----

// A subtle hover/press scale on top of a control's colour tint. Lerps localScale toward 1 / Hover / Press.
// Gate (optional) is the control's Selectable: while it is non-interactable (e.g. an unaffordable button),
// the element is held at scale 1 so greyed controls don't react. localScale doesn't feed the LayoutGroup.
[DisallowMultipleComponent]
public sealed class SiliconAlleyHoverScale : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Selectable? Gate;

    private const float HoverScale = 1.03f;
    private const float PressScale = 0.97f;
    private const float Speed = 14f; // exponential approach rate (framerate-independent)

    private bool _hover, _press;

    public void OnPointerEnter(PointerEventData e) => _hover = true;
    public void OnPointerExit(PointerEventData e) { _hover = false; _press = false; }
    public void OnPointerDown(PointerEventData e) => _press = true;
    public void OnPointerUp(PointerEventData e) => _press = false;

    private void OnDisable()
    {
        // A re-shown element shouldn't keep a stale hover scale.
        _hover = _press = false;
        transform.localScale = Vector3.one;
    }

    private void Update()
    {
        var interactable = Gate == null || Gate.IsInteractable();
        var target = !interactable ? 1f : _press ? PressScale : _hover ? HoverScale : 1f;
        var s = transform.localScale.x;
        if (Mathf.Abs(s - target) < 0.0005f)
        {
            if (!Mathf.Approximately(s, target))
                transform.localScale = new Vector3(target, target, 1f);
            return;
        }
        s = Mathf.Lerp(s, target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }
}

// Tweens an Image.fillAmount toward Target each frame, so progress bars glide instead of snapping.
[DisallowMultipleComponent]
public sealed class SiliconAlleyAnimatedFill : MonoBehaviour
{
    public Image? Fill;
    public float Target;

    private const float Speed = 9f;

    private void Update()
    {
        if (Fill == null)
            return;
        var cur = Fill.fillAmount;
        if (Mathf.Abs(cur - Target) < 0.001f)
        {
            if (!Mathf.Approximately(cur, Target))
                Fill.fillAmount = Target;
            return;
        }
        Fill.fillAmount = Mathf.Lerp(cur, Target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
    }
}

// Counts a TMP_Text's number toward Target, formatting each frame. Snaps on the first assignment (no
// count-from-zero on first reveal); animates on later changes.
[DisallowMultipleComponent]
public sealed class SiliconAlleyAnimatedNumber : MonoBehaviour
{
    private TMP_Text? _text;
    private Func<float, string>? _format;
    private float _current, _target;
    private bool _has;

    private const float Speed = 11f;

    public void Set(TMP_Text text, float target, Func<float, string> format)
    {
        _text = text;
        _format = format;
        _target = target;
        if (!_has)
        {
            _has = true;
            _current = target;
            _text.text = format(_current);
        }
    }

    private void Update()
    {
        if (_text == null || _format == null)
            return;
        if (Mathf.Abs(_current - _target) < 0.01f)
        {
            if (!Mathf.Approximately(_current, _target))
            {
                _current = _target;
                _text.text = _format(_current);
            }
            return;
        }
        _current = Mathf.Lerp(_current, _target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
        _text.text = _format(_current);
    }
}
