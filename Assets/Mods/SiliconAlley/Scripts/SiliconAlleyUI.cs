#nullable enable
using Localizor;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
        public GameObject Root;
        public Button Button;       // null for a read-only card
        public Image Card;          // background — set its colour for the state tint
        public Image Icon;
        public TMP_Text Title;
        public Image[] Chips;
        public TMP_Text[] ChipLabels;
        public Image Badge;
        public TMP_Text BadgeLabel;
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
    // pill's own HorizontalLayoutGroup reports its preferred width to the parent layout group.
    private static Image MakeChip(Transform parent, Color bg, Color fg, out TMP_Text label)
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
                c.ChipLabels[i].text = texts[i];
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
        public GameObject Root;
        public Image Icon;
        public TMP_Text Label;
        public TMP_Text Value;
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
}
