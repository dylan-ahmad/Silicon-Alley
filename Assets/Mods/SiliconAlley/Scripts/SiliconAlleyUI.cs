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
        AddShadow(image.gameObject, SiliconAlleyTheme.Elevation.PanelAlpha);
        return image;
    }

    // A rounded raised card surface for grouping content.
    public static Image MakeCard(Transform parent, string name)
    {
        var image = MakeImage(parent, name, SiliconAlleyTheme.Card);
        ApplySlice(image, SiliconAlleyTheme.CardSprite);
        AddShadow(image.gameObject, SiliconAlleyTheme.Elevation.CardAlpha);
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

    // #143: render an Image as a true capsule at any size. The pill sprite's 9-slice border is scaled so
    // the effective corner radius = diameter/2 (diameter = the element's SMALLER dimension). Replaces the
    // old ButtonSprite-as-Type.Simple hack that stretched the corner radius with the element. No-op (flat
    // fallback) when the pill sprite is absent (pre-0.6.0 bundle).
    public static void ApplyPill(Image image, float diameter)
    {
        if (SiliconAlleyTheme.PillSprite == null)
            return;
        image.sprite = SiliconAlleyTheme.PillSprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2f * SiliconAlleyTheme.Radius.PillBorder / Mathf.Max(diameter, 1f);
    }

    // #143: a soft drop shadow under a sprite-backed surface, so cards/panels visibly lift off what's
    // behind them. The shadow ring sprite has a fully transparent interior, so although the child Image
    // draws ON TOP of its host, only the region outside the host's body is tinted. The rect is expanded
    // by exactly Elevation.ShadowPad — the pad baked into the sprite — so the ring's inner edge aligns
    // with the host edge. ignoreLayout keeps it invisible to the host's LayoutGroup (no layout impact).
    // Skipped entirely when the shadow sprite is absent (pre-0.6.0 bundle) — the old flat look.
    private static void AddShadow(GameObject host, float alpha)
    {
        if (SiliconAlleyTheme.ShadowSprite == null)
            return;
        var color = SiliconAlleyTheme.Shadow;
        color.a = alpha;
        var shadow = MakeImage(host.transform, "Shadow", color);
        shadow.sprite = SiliconAlleyTheme.ShadowSprite;
        shadow.type = Image.Type.Sliced;
        shadow.raycastTarget = false;
        shadow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var rt = shadow.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        var pad = SiliconAlleyTheme.Elevation.ShadowPad;
        rt.offsetMin = new Vector2(-pad, -pad);
        rt.offsetMax = new Vector2(pad, pad);
        rt.SetAsFirstSibling();
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
        text.alignment = anchor switch // full 9-way mapping (#143) — Upper/Lower and Right anchors included
        {
            TextAnchor.UpperLeft    => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter  => TextAlignmentOptions.Top,
            TextAnchor.UpperRight   => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight  => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft    => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter  => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight   => TextAlignmentOptions.BottomRight,
            _                       => TextAlignmentOptions.Left,
        };
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

    // A section header: a localization key resolved + accent-coloured. Returns the label (#143) so callers
    // can restyle, re-text or hide it later.
    public static TMP_Text MakeHeader(Transform parent, string key)
    {
        var header = MakeText(parent, "Header", SiliconAlleyTheme.Sizes.Header, TextAnchor.MiddleLeft, FontStyle.Bold);
        header.color = SiliconAlleyTheme.Header;
        header.text = key.GetLocalization();
        return header;
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
        button.colors = SiliconAlleyTheme.Interaction; // shared hover/press/disabled tint set (#143)
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = SiliconAlleyTheme.Height.Control;
        le.preferredHeight = SiliconAlleyTheme.Height.Control;
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
        // #146: present only on checkable cards (multi-select pickers) — driven via SetCardChecked.
        public Image? CheckBox;
        public SiliconAlleyCheckPop? Check;
    }

    public static CardItem MakeCardItem(Transform parent, UnityAction onClick, int chipCapacity = 3, bool checkable = false)
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
            button.colors = SiliconAlleyTheme.Interaction; // shared hover/press/disabled tint set (#143)
            button.onClick.AddListener(onClick);
            item.Button = button;
            go.AddComponent<SiliconAlleyHoverScale>().Gate = button; // issue #61: hover/press scale (clickable cards)
        }
        AddShadow(go, SiliconAlleyTheme.Elevation.CardAlpha);

        var row = go.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(SiliconAlleyTheme.Space.Medium, SiliconAlleyTheme.Space.Medium,
                                     SiliconAlleyTheme.Space.Base, SiliconAlleyTheme.Space.Base);
        row.spacing = SiliconAlleyTheme.Space.Medium;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleLeft;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = SiliconAlleyTheme.Height.Card;
        le.flexibleWidth = 1f;

        // #146: a real checkbox slot for multi-select picker cards — replaces the old 30% colour-lerp tint
        // as the selection signal (the card face stays Card; SetCardChecked drives the tick).
        if (checkable)
        {
            MakeCheckboxVisual(go.transform, out var box, out var check);
            item.CheckBox = box;
            item.Check = check;
        }

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
        chipsH.spacing = SiliconAlleyTheme.Space.Small;
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

    // A small capsule pill that auto-sizes to its text (chips + state badges). No ContentSizeFitter: the
    // pill's own HorizontalLayoutGroup reports its preferred width to the parent layout group. Public so
    // other screens reuse it (e.g. the dashboard's demand-trend pill, issue #59). #143: rendered via the
    // dedicated pill sprite (true capsule at any width) with a min width + ellipsis so a chip in a narrow
    // wizard card can no longer squash to nothing while its label spills out.
    public static Image MakeChip(Transform parent, Color bg, Color fg, out TMP_Text label)
    {
        var go = new GameObject("Chip", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = bg;
        img.raycastTarget = false;
        ApplyPill(img, SiliconAlleyTheme.Height.Chip);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(SiliconAlleyTheme.Space.Base, SiliconAlleyTheme.Space.Base,
                                   SiliconAlleyTheme.Space.Hairline, SiliconAlleyTheme.Space.Hairline);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleCenter;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 44f; // ≈ four caption glyphs + the pill's end caps — the floor under layout pressure
        le.flexibleWidth = 0f;

        label = MakeText(go.transform, "ChipLabel", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleCenter);
        label.color = fg;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis; // clipped, not spilled, when squeezed to min width
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

    // #146: tick/untick a checkable card. Selection reads from the checkbox, not a card tint, so the card
    // face stays Card; idempotent (the pop animator no-ops at its target), safe on the 1 Hz Refresh. No-op
    // on cards built without checkable: true.
    public static void SetCardChecked(CardItem c, bool on)
    {
        if (c.CheckBox == null || c.Check == null)
            return;
        SetCheckVisual(c.CheckBox, c.Check, on);
    }

    // ---- Review primitives (issue #58). A rounded card panel with a vertical content layout + scannable
    // "[icon] label …… value" stat rows. Reused by the summary review card (and later dashboard/sections). ----

    // A rounded card ready to hold stacked content (padding + vertical layout). Returns the card GameObject.
    public static GameObject MakeCardPanel(Transform parent, string name, int pad = SiliconAlleyTheme.Space.Large)
    {
        var card = MakeCard(parent, name);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(pad, pad, pad, pad);
        v.spacing = SiliconAlleyTheme.Space.Small;
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
        h.spacing = SiliconAlleyTheme.Space.Base;
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

        var value = MakeText(go.transform, "Value", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleRight, FontStyle.Bold);
        value.GetComponent<LayoutElement>().flexibleWidth = 1f; // fill the rest; wraps if the value is long

        return new StatRow { Root = go, Icon = icon, Label = label, Value = value };
    }

    // ---- Progress bar (issue #59). A capsule track + accent fill for a 0..1 fraction. The fill is a
    // single Image whose anchorMax.x is the fraction (#143) — NOT a layout-sized child — so SetProgress on
    // the 1s refresh tick never feeds the VerticalLayoutGroup/ContentSizeFitter (no layout rebuild / height
    // jitter, the same layout-safety reason #56 keeps alpha/scale off the layout path; anchors on a child
    // outside any LayoutGroup are just as layout-inert as fillAmount was). Sliced pills keep both caps
    // truly round at every width, where Type.Filled used to stretch the left cap and chop the right one. ----

    public sealed class ProgressBar
    {
        public GameObject Root = null!;
        public Image Track = null!; // background
        public Image Fill = null!;  // accent fill (anchorMax.x = fraction, driven by Anim)
        public SiliconAlleyAnimatedFill Anim = null!; // issue #61: tweens the fill toward the target
    }

    // A full-width progress bar of fixed height. Track tinted Elevated, fill tinted Accent; both render as
    // true capsules via the pill sprite (flat-colour fallback when the bundle lacks it).
    public static ProgressBar MakeProgressBar(Transform parent, float height = SiliconAlleyTheme.Height.Bar)
    {
        var go = new GameObject("ProgressBar", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
        le.flexibleWidth = 1f;

        var track = MakeImage(go.transform, "Track", SiliconAlleyTheme.Elevated);
        track.raycastTarget = false;
        ApplyPill(track, height);
        Stretch(track.rectTransform);

        var fill = MakeImage(go.transform, "Fill", SiliconAlleyTheme.Accent);
        fill.raycastTarget = false;
        ApplyPill(fill, height);
        Stretch(fill.rectTransform);
        fill.rectTransform.anchorMax = new Vector2(0f, 1f); // start empty; the animator drives anchorMax.x
        fill.enabled = false;                               // hidden until the fraction clears the sliver floor

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
        le.minHeight = le.preferredHeight = SiliconAlleyTheme.Space.Hairline;
    }

    // #143: an empty flexible gap that pushes row content apart (replaces the old invisible size-1 text hack).
    public static GameObject MakeSpacer(Transform parent, float flexibleWidth = 1f)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().flexibleWidth = flexibleWidth;
        return go;
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
        v.spacing = SiliconAlleyTheme.Space.Base;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        return go;
    }

    public static GameObject MakeRow(Transform parent, float spacing = SiliconAlleyTheme.Space.Base, int minHeight = (int)SiliconAlleyTheme.Height.Row)
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
    public static GameObject MakeColumns(Transform parent, float spacing = SiliconAlleyTheme.Space.Gutter)
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
        le.minHeight = SiliconAlleyTheme.Height.Slider;
        le.flexibleWidth = 1f;

        // Track band = the middle 30% of the control (~7px) — a capsule like the progress-bar track (#143).
        var trackHeight = SiliconAlleyTheme.Height.Slider * 0.3f;
        var background = MakeImage(go.transform, "Background", SiliconAlleyTheme.Elevated);
        ApplyPill(background, trackHeight);
        StretchBand(background.rectTransform, 0.35f, 0.65f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fillAreaRt = (RectTransform)fillArea.transform;
        StretchBand(fillAreaRt, 0.35f, 0.65f);
        fillAreaRt.offsetMin = new Vector2(8f, fillAreaRt.offsetMin.y);
        fillAreaRt.offsetMax = new Vector2(-8f, fillAreaRt.offsetMax.y);
        var fill = MakeImage(fillArea.transform, "Fill", SiliconAlleyTheme.Accent);
        ApplyPill(fill, trackHeight);
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
        var handle = MakeImage(handleArea.transform, "Handle", SiliconAlleyTheme.Text);
        ApplyPill(handle, 16f); // 16 wide × full height ⇒ a vertical capsule (corner radius = width/2)
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
        le.minHeight = le.preferredHeight = SiliconAlleyTheme.Height.Control;
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

    // ---- Scrollbar (#146). The missing "there is more below" affordance for the window's ScrollRect.
    // Standard track/sliding-area/handle hierarchy styled like MakeSlider (capsule track + capsule handle
    // via the pill sprite; flat fallback when absent). The bar OVERLAYS the window's right content padding
    // as a non-layout child, so showing/hiding it never reflows the content — which is also why visibility
    // stays Permanent on the ScrollRect: AutoHideAndExpandViewport resizes the viewport, exactly the
    // per-second reflow flicker this bar must not add. Auto-hide is instead a pure CanvasGroup alpha fade
    // driven by SiliconAlleyScrollbarAutoHide (hysteresis over the live overflow), never SetActive. ----

    public static Scrollbar MakeScrollbar(ScrollRect scroll, float width = 8f)
    {
        var go = new GameObject("Scrollbar", typeof(RectTransform));
        go.transform.SetParent(scroll.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        // Inset from the right edge, clear of the panel's rounded corners top + bottom.
        var corner = (float)SiliconAlleyTheme.Radius.Panel;
        rt.offsetMin = new Vector2(-5f - width, corner);
        rt.offsetMax = new Vector2(-5f, -corner);

        var track = go.AddComponent<Image>();
        track.color = SiliconAlleyTheme.Elevated;
        ApplyPill(track, width);

        var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)slidingArea.transform);

        var handle = MakeImage(slidingArea.transform, "Handle", SiliconAlleyTheme.TextMuted);
        ApplyPill(handle, width);
        Stretch(handle.rectTransform);

        var bar = go.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.colors = SiliconAlleyTheme.Interaction;
        bar.direction = Scrollbar.Direction.BottomToTop;

        // From here the ScrollRect drives the value + handle size each frame (layout-inert: only the
        // scrollbar's own internal rects move, never anything a LayoutGroup measures).
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f; // hidden until content actually overflows
        group.blocksRaycasts = false;
        group.interactable = false;
        var autoHide = go.AddComponent<SiliconAlleyScrollbarAutoHide>();
        autoHide.Scroll = scroll;
        autoHide.Group = group;
        return bar;
    }

    // ---- Tabs / segmented control (#146). A capsule track holding N pill tabs; single-select, with an
    // optional click-the-active-tab-to-clear mode (allowDeselect — the server-role selector's semantics,
    // migrating in #148). Clicks only report the chosen index through onSelect; the screen writes state and
    // its Refresh re-asserts the visuals via SetTabSelected (the write-state-then-Refresh flow every control
    // here follows), so the 1 Hz tick keeps tabs honest for free. Tab children are named Label/Icon exactly
    // like MakeButton's, so SetButtonIcon works on a tab button unchanged. ----

    public sealed class TabBar
    {
        public GameObject Root = null!;
        public Button[] Buttons = null!;
        public Image[] Images = null!;
        public TMP_Text[] Labels = null!;
        public int Selected = -1; // last index passed to SetTabSelected (-1 = none)
    }

    public static TabBar MakeTabs(Transform parent, string[] labels, Action<int> onSelect, bool allowDeselect = false)
    {
        var go = new GameObject("Tabs", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var track = go.AddComponent<Image>();
        track.color = SiliconAlleyTheme.Elevated;
        ApplyPill(track, SiliconAlleyTheme.Height.Control);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(3, 3, 3, 3);
        h.spacing = 4f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true; // tabs share the width equally
        h.childForceExpandHeight = true;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = SiliconAlleyTheme.Height.Control;
        le.flexibleWidth = 1f;

        var bar = new TabBar
        {
            Root = go,
            Buttons = new Button[labels.Length],
            Images = new Image[labels.Length],
            Labels = new TMP_Text[labels.Length],
        };
        var tabHeight = SiliconAlleyTheme.Height.Control - 6f; // inside the 3px track padding
        for (var i = 0; i < labels.Length; i++)
        {
            var tab = new GameObject("Tab", typeof(RectTransform));
            tab.transform.SetParent(go.transform, false);
            var image = tab.AddComponent<Image>();
            ApplyPill(image, tabHeight);
            var button = tab.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = SiliconAlleyTheme.Interaction;
            var index = i; // per-tab copy for the click closure
            button.onClick.AddListener(() => onSelect(allowDeselect && bar.Selected == index ? -1 : index));
            tab.AddComponent<SiliconAlleyHoverScale>().Gate = button;

            var label = MakeText(tab.transform, "Label", SiliconAlleyTheme.Sizes.Button, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.text = labels[i];
            Stretch(label.rectTransform);

            bar.Buttons[i] = button;
            bar.Images[i] = image;
            bar.Labels[i] = label;
        }
        SetTabSelected(bar, -1);
        return bar;
    }

    // Recolour the bar to the selected index (-1 = none). Idempotent — call from Refresh each tick.
    public static void SetTabSelected(TabBar bar, int index)
    {
        bar.Selected = index;
        var off = SiliconAlleyTheme.Slate;
        off.a = 0.45f; // near-transparent on the Elevated track so the active pill pops
        for (var i = 0; i < bar.Images.Length; i++)
        {
            bar.Images[i].color = i == index ? SiliconAlleyTheme.Accent : off;
            bar.Labels[i].color = i == index ? SiliconAlleyTheme.Text : SiliconAlleyTheme.TextMuted;
        }
    }

    // ---- Toggle / checkbox (#146). A real box-and-tick instead of the ON/OFF label + colour-lerp buttons.
    // The box is the #143 outline sprite (a solid Elevated well as the flat fallback); the tick is an accent
    // fill + the optional ui_check glyph, popped in by SiliconAlleyCheckPop (scale + alpha, unscaled,
    // layout-inert inside the fixed-size box). State lives in SiliconAlleyState as before — clicks report
    // through the handler and the screen's Refresh re-asserts visuals via SetToggle/SetCardChecked. ----

    public sealed class ToggleRow
    {
        public GameObject Root = null!;
        public Button Button = null!;
        public Image Box = null!;
        public SiliconAlleyCheckPop Check = null!;
        public TMP_Text Label = null!;
    }

    public static ToggleRow MakeToggle(Transform parent, string label, UnityAction onToggled)
    {
        var go = new GameObject("Toggle", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var surface = go.AddComponent<Image>();
        surface.color = new Color(0f, 0f, 0f, 0f); // invisible, but the WHOLE row catches the click
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(SiliconAlleyTheme.Space.Tight, SiliconAlleyTheme.Space.Tight, 0, 0);
        h.spacing = SiliconAlleyTheme.Space.Medium;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = (int)SiliconAlleyTheme.Height.Row;
        le.flexibleWidth = 1f;

        MakeCheckboxVisual(go.transform, out var box, out var check);

        var button = go.AddComponent<Button>();
        button.targetGraphic = box; // hover/press tint lands on the box frame (the row surface is invisible)
        button.colors = SiliconAlleyTheme.Interaction;
        if (onToggled != null)
            button.onClick.AddListener(onToggled);
        go.AddComponent<SiliconAlleyHoverScale>().Gate = button;

        var text = MakeText(go.transform, "Label", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft);
        text.text = label;
        text.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var row = new ToggleRow { Root = go, Button = button, Box = box, Check = check, Label = text };
        SetToggle(row, false);
        return row;
    }

    // Re-assert a toggle's visual state. Idempotent — call from Refresh each tick.
    public static void SetToggle(ToggleRow t, bool on) => SetCheckVisual(t.Box, t.Check, on);

    // The shared 22×22 box + tick visual behind MakeToggle and checkable CardItems. Box frame = outline
    // sprite (solid Elevated well when the bundle predates it); tick = accent fill + the ui_check glyph
    // (glyph hidden when the icon isn't bundled — the fill alone still reads as checked).
    private static void MakeCheckboxVisual(Transform parent, out Image box, out SiliconAlleyCheckPop check)
    {
        box = MakeImage(parent, "CheckBox", SiliconAlleyTheme.TextMuted);
        box.raycastTarget = false;
        if (SiliconAlleyTheme.OutlineSprite != null)
        {
            box.sprite = SiliconAlleyTheme.OutlineSprite;
            box.type = Image.Type.Sliced;
        }
        var le = box.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 22f;
        le.minHeight = le.preferredHeight = 22f;
        le.flexibleWidth = 0f;

        var checkGo = new GameObject("Check", typeof(RectTransform));
        checkGo.transform.SetParent(box.transform, false);
        Stretch((RectTransform)checkGo.transform);
        var group = checkGo.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        var fill = MakeImage(checkGo.transform, "Fill", SiliconAlleyTheme.Accent);
        fill.raycastTarget = false;
        Stretch(fill.rectTransform);
        fill.rectTransform.offsetMin = new Vector2(4f, 4f);
        fill.rectTransform.offsetMax = new Vector2(-4f, -4f);

        var glyph = MakeImage(checkGo.transform, "Glyph", SiliconAlleyTheme.Text);
        glyph.preserveAspect = true;
        glyph.raycastTarget = false;
        Stretch(glyph.rectTransform);
        glyph.rectTransform.offsetMin = new Vector2(3f, 3f);
        glyph.rectTransform.offsetMax = new Vector2(-3f, -3f);
        SetIconSprite(glyph, SiliconAlleyTheme.IconFor("ui_check"));

        check = checkGo.AddComponent<SiliconAlleyCheckPop>();
        check.Group = group;
    }

    // Frame colour + tick for either checkbox host: accent frame when on; muted outline (or the flat
    // fallback's solid well, left at its build colour) when off.
    private static void SetCheckVisual(Image box, SiliconAlleyCheckPop check, bool on)
    {
        box.color = on ? SiliconAlleyTheme.Accent
            : box.sprite != null ? SiliconAlleyTheme.TextMuted : SiliconAlleyTheme.Elevated;
        check.Set(on);
    }

    // ---- Collapsible section (#146). A header row (title + chevron, whole row clickable) over a content
    // section the caller fills. The reveal follows the layout-animation rule to the letter: expanding
    // SNAPS the height (one SetActive → one layout rebuild on the click) and animates ALPHA only via
    // SiliconAlleyFadeIn; collapsing hides instantly. The chevron rotates (layout-inert) and is hidden
    // gracefully when the ui_chevron_down icon isn't bundled — the header stays fully usable text-only.
    // The expanded flag is plain UI state the 1 Hz Refresh never writes, so it can't fight the player. ----

    public sealed class Collapsible
    {
        public GameObject Root = null!;
        public Button Header = null!;
        public TMP_Text Title = null!;
        public Image Chevron = null!;
        public GameObject Content = null!; // parent your rows/cards here
        public SiliconAlleyFadeIn Fade = null!;
        public bool Expanded;
    }

    public static Collapsible MakeCollapsible(Transform parent, string titleKey, bool startExpanded = true, Action? onToggled = null)
    {
        var root = MakeSection(parent);
        root.name = "Collapsible";

        var headerGo = new GameObject("Header", typeof(RectTransform));
        headerGo.transform.SetParent(root.transform, false);
        var surface = headerGo.AddComponent<Image>();
        surface.color = new Color(0f, 0f, 0f, 0f); // invisible, but the whole header row catches the click
        var h = headerGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = SiliconAlleyTheme.Space.Base;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        headerGo.AddComponent<LayoutElement>().minHeight = 28f;

        var title = MakeText(headerGo.transform, "Title", SiliconAlleyTheme.Sizes.Header, TextAnchor.MiddleLeft, FontStyle.Bold);
        title.color = SiliconAlleyTheme.Header;
        title.text = titleKey.GetLocalization();
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var chevron = MakeIcon(headerGo.transform, SiliconAlleyTheme.IconFor("ui_chevron_down"), 18f, SiliconAlleyTheme.TextMuted);

        var button = headerGo.AddComponent<Button>();
        button.transition = Selectable.Transition.None; // feedback comes from the hover scale + the chevron flip
        headerGo.AddComponent<SiliconAlleyHoverScale>().Gate = button;

        var content = MakeSection(root.transform);
        content.name = "Content";
        var fade = content.AddComponent<SiliconAlleyFadeIn>();

        var c = new Collapsible
        {
            Root = root,
            Header = button,
            Title = title,
            Chevron = chevron,
            Content = content,
            Fade = fade,
        };
        button.onClick.AddListener(() =>
        {
            SetCollapsibleExpanded(c, !c.Expanded);
            onToggled?.Invoke(); // e.g. the screen's ClampHeight, so the window resizes on the click, not a second later
        });
        // Initial state without the reveal fade (a freshly built screen shouldn't animate).
        c.Expanded = startExpanded;
        content.SetActive(startExpanded);
        chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, startExpanded ? 180f : 0f);
        return c;
    }

    // Expand/collapse programmatically. Height snaps; only the alpha of the revealed content animates.
    public static void SetCollapsibleExpanded(Collapsible c, bool expanded)
    {
        var was = c.Content.activeSelf;
        c.Expanded = expanded;
        c.Content.SetActive(expanded);
        if (expanded && !was)
            c.Fade.Play();
        // Down = "click to reveal below", up = "click to fold away" — rotation is layout-inert.
        c.Chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, expanded ? 180f : 0f);
    }

    // ---- Standalone badge (#146). The CardItem state badge's semantics (SetCardBadge's hide-on-empty),
    // freed from the card: a named state pill any row can host. A thin wrapper over MakeChip — which the
    // loose call sites (trend pill, server-count chips, history review chip) already used ad hoc — giving
    // that pattern a refresh-friendly API. ----

    public sealed class Badge
    {
        public Image Root = null!;
        public TMP_Text Label = null!;
    }

    public static Badge MakeBadge(Transform parent, Color bg, Color fg)
    {
        var root = MakeChip(parent, bg, fg, out var label);
        return new Badge { Root = root, Label = label };
    }

    // Set the badge's text + background colour; hide it entirely on null/empty (mirrors SetCardBadge).
    public static void SetBadge(Badge b, string? text, Color bg)
    {
        var on = !string.IsNullOrEmpty(text);
        b.Root.gameObject.SetActive(on);
        if (!on)
            return;
        b.Root.color = bg;
        b.Label.text = text;
    }

    // ---- Delta readout + disabled-reason line (#146). Two small "explain the number" primitives the
    // hub/detail/wizard tickets (#147–#150) consume: a before › after comparison with a signed, coloured
    // delta, and a caption that says WHY a button is disabled instead of leaving it silently grey. ----

    public sealed class DeltaReadout
    {
        public GameObject Root = null!;
        public TMP_Text Before = null!;
        public TMP_Text After = null!;
        public TMP_Text Delta = null!;
    }

    // "before › after (+delta)" — before muted, after bold, delta colour-coded by sign. The separator is
    // the codebase's proven "›" glyph (the wizard nav's), not "→", which the game font may lack.
    public static DeltaReadout MakeDeltaReadout(Transform parent)
    {
        var go = new GameObject("DeltaReadout", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = SiliconAlleyTheme.Space.Small;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.MiddleLeft;
        go.AddComponent<LayoutElement>().minHeight = 24f;

        var before = MakeText(go.transform, "Before", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft);
        before.color = SiliconAlleyTheme.TextMuted;
        var arrow = MakeText(go.transform, "Arrow", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleCenter);
        arrow.color = SiliconAlleyTheme.TextMuted;
        arrow.text = "›";
        var after = MakeText(go.transform, "After", SiliconAlleyTheme.Sizes.Body, TextAnchor.MiddleLeft, FontStyle.Bold);
        var delta = MakeText(go.transform, "Delta", SiliconAlleyTheme.Sizes.Caption, TextAnchor.MiddleLeft, FontStyle.Bold);

        return new DeltaReadout { Root = go, Before = before, After = after, Delta = delta };
    }

    // Fill the readout. `format` renders the SIGNED delta (e.g. SignedPct, or a "+0.6" lambda); colour
    // follows the established sign precedent — Ok positive / Warn negative / TextMuted zero (Danger stays
    // destructive-only, #143).
    public static void SetDelta(DeltaReadout d, string before, string after, float delta, Func<float, string> format)
    {
        d.Before.text = before;
        d.After.text = after;
        d.Delta.text = "(" + format(delta) + ")";
        d.Delta.color = Mathf.Approximately(delta, 0f) ? SiliconAlleyTheme.TextMuted
            : delta > 0f ? SiliconAlleyTheme.Ok : SiliconAlleyTheme.Warn;
    }

    // A muted-amber caption explaining WHY its control is disabled ("You have $2,400 of $6,000"); hidden
    // whenever the control is usable. Build it right under the button it explains.
    public static TMP_Text MakeDisabledReason(Transform parent)
    {
        var text = MakeText(parent, "DisabledReason", SiliconAlleyTheme.Sizes.Status, TextAnchor.MiddleLeft);
        text.color = SiliconAlleyTheme.Warn; // caution, not danger — nothing destructive about "can't afford"
        text.gameObject.SetActive(false);
        return text;
    }

    // Gate the button AND surface the reason in one call: enabled hides the line, disabled shows it with
    // the caller's explanation. Idempotent — call from Refresh each tick.
    public static void SetDisabledReason(Button button, TMP_Text reason, bool enabled, string? reasonText)
    {
        button.interactable = enabled;
        var show = !enabled && !string.IsNullOrEmpty(reasonText);
        reason.gameObject.SetActive(show);
        if (show)
            reason.text = reasonText;
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

// Tweens a progress-bar fill toward Target each frame, so bars glide instead of snapping. #143: the fill
// is a sliced pill whose width is its anchorMax.x (not fillAmount) — still layout-inert, see MakeProgressBar.
// The Image is hidden below a small floor so a near-empty bar doesn't render a crushed sub-corner sliver.
[DisallowMultipleComponent]
public sealed class SiliconAlleyAnimatedFill : MonoBehaviour
{
    public Image? Fill;
    public float Target;

    private const float Speed = 9f;
    private const float MinVisible = 0.02f;

    private float _current;

    private void Update()
    {
        if (Fill == null)
            return;
        if (Mathf.Abs(_current - Target) < 0.001f)
        {
            if (!Mathf.Approximately(_current, Target))
                Apply(Target);
            return;
        }
        Apply(Mathf.Lerp(_current, Target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime)));
    }

    private void Apply(float fraction)
    {
        _current = fraction;
        Fill!.rectTransform.anchorMax = new Vector2(fraction, 1f);
        Fill.enabled = fraction > MinVisible;
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

// ---- Issue #146: component-kit MonoBehaviours. Same conventions as the #61 set above: self-contained,
// manual unscaled-dt exponential lerps, and only alpha/localScale/anchor writes — never a layout input. ----

// #146: fades the window scrollbar in only while the content actually overflows the viewport. The check is
// hysteretic (show above 4px of overflow, hide below 1px; in between keep the current state) so the 1 Hz
// ClampHeight re-measure — which can jitter the content height by a pixel or two — can never flip the bar
// on and off (the "no flicker" acceptance criterion). Visibility is a CanvasGroup ALPHA fade, never
// SetActive, so neither direction triggers a layout rebuild.
[DisallowMultipleComponent]
public sealed class SiliconAlleyScrollbarAutoHide : MonoBehaviour
{
    public ScrollRect? Scroll;
    public CanvasGroup? Group;

    private const float ShowAbove = 4f; // px of overflow that reveal the bar
    private const float HideBelow = 1f; // px of overflow that dismiss it
    private const float Speed = 14f;

    private float _target; // 0 or 1

    private void Update()
    {
        if (Scroll == null || Group == null || Scroll.content == null || Scroll.viewport == null)
            return;
        var overflow = Scroll.content.rect.height - Scroll.viewport.rect.height;
        if (overflow > ShowAbove)
            _target = 1f;
        else if (overflow < HideBelow)
            _target = 0f;

        var a = Group.alpha;
        if (Mathf.Abs(a - _target) < 0.005f)
        {
            if (!Mathf.Approximately(a, _target))
                Group.alpha = _target;
        }
        else
        {
            Group.alpha = Mathf.Lerp(a, _target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
        }
        // A faded-out bar must not eat clicks/drags along the window edge.
        var visible = _target > 0.5f;
        Group.blocksRaycasts = visible;
        Group.interactable = visible;
    }
}

// #146: a one-shot CanvasGroup fade-in (alpha 0 → 1). Play() (re)starts it. Used where content SNAPS into
// place (SetActive / reposition) and only the alpha animates — the collapsible reveal and the tooltip
// panel — per the #56 layout-safety rule (alpha never feeds a LayoutGroup).
[DisallowMultipleComponent]
public sealed class SiliconAlleyFadeIn : MonoBehaviour
{
    private const float Speed = 10f;

    private CanvasGroup? _group;
    private bool _playing;

    public void Play()
    {
        if (_group == null)
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();
        }
        _group.alpha = 0f;
        _playing = true;
    }

    private void Update()
    {
        if (!_playing || _group == null)
            return;
        var a = Mathf.Lerp(_group.alpha, 1f, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
        if (a >= 0.995f)
        {
            a = 1f;
            _playing = false;
        }
        _group.alpha = a;
    }
}

// #146: the checkbox tick's pop — localScale + CanvasGroup alpha toward on/off, unscaled, layout-inert
// (the tick is a non-layout child inside the fixed-size box). Set() is idempotent so the 1 Hz Refresh can
// re-assert state every tick; the FIRST Set snaps, so a freshly shown control doesn't replay the pop.
// Group is wired by the builder (not looked up in Awake) so Set works before the host ever activates.
[DisallowMultipleComponent]
public sealed class SiliconAlleyCheckPop : MonoBehaviour
{
    public CanvasGroup? Group;

    private const float Speed = 14f;

    private float _target = -1f; // -1 = never set
    private float _t;

    public void Set(bool on)
    {
        var target = on ? 1f : 0f;
        if (_target < 0f)
        {
            _target = _t = target;
            Apply(target);
            return;
        }
        _target = target;
    }

    private void Update()
    {
        if (_target < 0f)
            return;
        if (Mathf.Abs(_t - _target) < 0.005f)
        {
            if (!Mathf.Approximately(_t, _target))
            {
                _t = _target;
                Apply(_t);
            }
            return;
        }
        _t = Mathf.Lerp(_t, _target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
        Apply(_t);
    }

    private void Apply(float t)
    {
        transform.localScale = new Vector3(t, t, 1f);
        if (Group != null)
            Group.alpha = t;
    }
}
