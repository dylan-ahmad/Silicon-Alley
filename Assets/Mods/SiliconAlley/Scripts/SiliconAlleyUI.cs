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
