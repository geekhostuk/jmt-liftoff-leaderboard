using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// Builds uGUI in code, in the JMT site's idiom: dark cards with a 1px rule, rounded
/// corners, orange for action and "you", metals for podiums.
///
/// The plugin ships no assets. Rounded corners are sprites drawn into textures at
/// runtime and 9-sliced. The site's fonts can't be loaded from a .ttf at runtime, so
/// they are used only when the pilot has them installed; otherwise the game's own menu
/// font carries headings and body text, and the OS's monospace carries times.
///
/// One rule keeps the layout honest: Unity's layout groups pass "flexible" upwards, so
/// a group that force-expands its children makes itself, and everything holding it,
/// greedy for space. Every group built here says exactly what it expands, and anything
/// given a fixed size by <see cref="Size"/> refuses to grow on that axis.
/// </summary>
internal static class UiKit
{
    public enum ButtonKind
    {
        Primary,
        Outline,
        Ghost,
    }

    private static readonly Dictionary<int, Sprite> RoundedSprites = new();
    private static Sprite? _circle;
    private static Font? _gameFont;
    private static Font? _heading;
    private static Font? _body;
    private static Font? _mono;

    /// <summary>The game's menu font, taken from one of its own buttons, once one is seen.</summary>
    public static void AdoptGameFont(Font? font)
    {
        if (font == null || _gameFont == font)
            return;
        _gameFont = font;
        _heading = _body = _mono = null;
    }

    private static Font GameFont =>
        _gameFont != null ? _gameFont : _gameFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    public static Font Heading => _heading != null ? _heading : _heading = Installed(GameFont, "Rajdhani", "Rajdhani SemiBold", "Rajdhani Bold");
    public static Font Body => _body != null ? _body : _body = Installed(GameFont, "Inter", "Inter Regular");
    public static Font Mono => _mono != null ? _mono : _mono = Installed(GameFont,
        "JetBrains Mono", "Consolas", "DejaVu Sans Mono", "Liberation Mono", "Menlo", "Courier New");

    private static Font Installed(Font fallback, params string[] names)
    {
        try
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (installed.Contains(name))
                    return Font.CreateDynamicFontFromOSFont(name, 16);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Font lookup failed: {ex.Message}");
        }
        return fallback;
    }

    // ── Sprites ─────────────────────────────────────────────────────────────

    /// <summary>A white rounded rectangle, 9-sliced so it stretches to any size.</summary>
    public static Sprite Rounded(int radius)
    {
        if (RoundedSprites.TryGetValue(radius, out var cached) && cached != null)
            return cached;

        var size = radius * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // Distance from the nearest corner's centre, only inside the corner squares.
                var cx = x < radius ? radius - 0.5f : x >= size - radius ? size - radius - 0.5f : x;
                var cy = y < radius ? radius - 0.5f : y >= size - radius ? size - radius - 0.5f : y;
                var distance = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                if (cx == x || cy == y)
                    alpha = 1f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        RoundedSprites[radius] = sprite;
        return sprite;
    }

    /// <summary>A white disc, for avatar masks and dots.</summary>
    public static Sprite Circle()
    {
        if (_circle != null)
            return _circle;
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color32[size * size];
        const float centre = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                var alpha = Mathf.Clamp01(size / 2f - distance);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        _circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _circle.hideFlags = HideFlags.HideAndDontSave;
        return _circle;
    }

    // ── Layout primitives ───────────────────────────────────────────────────

    public static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>Stretch to fill the parent, inset by the given margins.</summary>
    public static RectTransform Fill(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    /// <summary>
    /// Sizes an element for its parent's layout group. A fixed width or height also
    /// stops it growing on that axis, unless a flexible amount is given too; -1 leaves
    /// a value as the element's own layout would have it.
    /// </summary>
    public static LayoutElement Size(Component target, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1)
    {
        var element = Element(target);
        if (width >= 0)
        {
            element.minWidth = width;
            element.preferredWidth = width;
            if (flexWidth < 0)
                element.flexibleWidth = 0;
        }
        if (height >= 0)
        {
            element.minHeight = height;
            element.preferredHeight = height;
            if (flexHeight < 0)
                element.flexibleHeight = 0;
        }
        if (flexWidth >= 0)
            element.flexibleWidth = flexWidth;
        if (flexHeight >= 0)
            element.flexibleHeight = flexHeight;
        return element;
    }

    /// <summary>Holds an element at its natural size: it never grows to take spare room.</summary>
    public static LayoutElement Rigid(Component target)
    {
        var element = Element(target);
        element.flexibleWidth = 0;
        element.flexibleHeight = 0;
        return element;
    }

    private static LayoutElement Element(Component target)
    {
        var element = target.GetComponent<LayoutElement>();
        return element != null ? element : target.gameObject.AddComponent<LayoutElement>();
    }

    private static HorizontalLayoutGroup HGroup(GameObject go, float spacing, RectOffset padding, TextAnchor align,
        bool expandWidth = false, bool expandHeight = false)
    {
        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding;
        layout.childAlignment = align;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = expandWidth;
        layout.childForceExpandHeight = expandHeight;
        return layout;
    }

    public static RectTransform Row(Transform parent, float spacing = 8, int padX = 0, int padY = 0, TextAnchor align = TextAnchor.MiddleLeft, string name = "Row")
    {
        var rect = Node(name, parent);
        HGroup(rect.gameObject, spacing, new RectOffset(padX, padX, padY, padY), align);
        return rect;
    }

    /// <summary>A stack whose children span its width; it grows no taller than they need.</summary>
    public static RectTransform Column(Transform parent, float spacing = 8, int padX = 0, int padY = 0, string name = "Column")
    {
        var rect = Node(name, parent);
        Stack(rect.gameObject, spacing, new RectOffset(padX, padX, padY, padY));
        return rect;
    }

    /// <summary>A vertical layout on an existing object: children span the width, heights as they need.</summary>
    public static VerticalLayoutGroup Stack(GameObject go, float spacing, RectOffset padding)
    {
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    /// <summary>A horizontal layout on an existing object, expanding nothing.</summary>
    public static HorizontalLayoutGroup Line(GameObject go, float spacing, RectOffset padding, TextAnchor align = TextAnchor.MiddleLeft) =>
        HGroup(go, spacing, padding, align);

    /// <summary>Makes a layout container size itself to its content, for use inside scroll lists.</summary>
    public static void FitHeight(RectTransform rect)
    {
        var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public static void Clear(Transform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
    }

    // ── Surfaces ────────────────────────────────────────────────────────────

    public static Image Panel(Transform parent, Color color, int radius = 0, string name = "Panel")
    {
        var rect = Node(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        if (radius > 0)
        {
            image.sprite = Rounded(radius);
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    /// <summary>
    /// A site card: a 1px ink-700 rule around an ink-850 fill. Content goes straight onto
    /// the returned card, whose layout group should pad by at least 1px to clear the rule;
    /// the fill is a child the layout ignores, so it adds nothing to the card's size.
    /// </summary>
    public static RectTransform Card(Transform parent, Color? fill = null, int radius = Theme.RadiusCard, string name = "Card")
    {
        var rule = Panel(parent, Theme.Ink700, radius, name);
        var inner = Panel(rule.transform, fill ?? Theme.Ink850, radius, "Fill");
        Fill(inner.rectTransform, 1, 1, 1, 1);
        inner.raycastTarget = false;
        Element(inner).ignoreLayout = true;
        return rule.rectTransform;
    }

    /// <summary>
    /// A coloured stripe along one edge: the site's orange top rule, a row's place marker.
    /// On a rounded card, inset it by the corner radius so it runs along the straight edge.
    /// </summary>
    public static Image Stripe(Transform parent, Color color, RectTransform.Edge edge, float thickness, float inset = 0)
    {
        var image = Panel(parent, color, 0, "Stripe");
        image.raycastTarget = false;
        Element(image).ignoreLayout = true;
        var rect = image.rectTransform;
        rect.SetInsetAndSizeFromParentEdge(edge, 0, thickness);
        if (edge is RectTransform.Edge.Top or RectTransform.Edge.Bottom)
        {
            rect.anchorMin = new Vector2(0, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1, rect.anchorMax.y);
            rect.offsetMin = new Vector2(inset, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-inset, rect.offsetMax.y);
        }
        else
        {
            rect.anchorMin = new Vector2(rect.anchorMin.x, 0);
            rect.anchorMax = new Vector2(rect.anchorMax.x, 1);
            rect.offsetMin = new Vector2(rect.offsetMin.x, inset);
            rect.offsetMax = new Vector2(rect.offsetMax.x, -inset);
        }
        return image;
    }

    // ── Text ────────────────────────────────────────────────────────────────

    public static Text Label(Transform parent, string text, int size, Color color, Font? font = null,
        FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft, string name = "Label")
    {
        var rect = Node(name, parent);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font ?? Body;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = align;
        label.supportRichText = true;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.text = text;
        return label;
    }

    /// <summary>The site's small caps label: Rajdhani, bold, upper case, muted.</summary>
    public static Text Caption(Transform parent, string text, Color? color = null, int size = 13) =>
        Label(parent, text.ToUpperInvariant(), size, color ?? Theme.Ink600, Heading, FontStyle.Bold);

    /// <summary>A label that stays on one line at its natural width.</summary>
    public static Text OneLine(Text label)
    {
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        Rigid(label);
        return label;
    }

    // ── Controls ────────────────────────────────────────────────────────────

    public static Button Button(Transform parent, string text, UnityAction onClick, ButtonKind kind = ButtonKind.Outline,
        int fontSize = 16, float height = 38, string name = "Button")
    {
        var (fill, border, textColor) = kind switch
        {
            ButtonKind.Primary => (Theme.Accent, Theme.Accent, Theme.Ink950),
            ButtonKind.Ghost => (new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), Theme.Ink400),
            _ => (Theme.Ink850, Theme.Ink700, Theme.Ink200),
        };

        var outer = Panel(parent, border, Theme.RadiusControl, name);
        var inner = Panel(outer.transform, fill, Theme.RadiusControl, "Fill");
        Fill(inner.rectTransform, 1, 1, 1, 1);
        inner.raycastTarget = false;

        var label = Label(outer.transform, text, fontSize, textColor, Heading, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(label.rectTransform, 12, 0, 12, 0);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;

        var button = outer.gameObject.AddComponent<Button>();
        button.targetGraphic = inner;
        var colors = button.colors;
        colors.normalColor = Color.white;
        // Tints above 1 brighten the fill on hover; the multiplier stays 1, since it
        // applies to the resting state too.
        colors.highlightedColor = kind == ButtonKind.Primary ? new Color(1.15f, 1.15f, 1.15f) : new Color(1.6f, 1.6f, 1.7f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        Size(outer, label.preferredWidth + 28, height);
        return button;
    }

    /// <summary>
    /// Makes any surface clickable, with a hover tint, without restyling it. On a
    /// <see cref="Card"/> the tint goes on the card's fill rather than its rule.
    /// </summary>
    public static Button Clickable(Graphic surface, UnityAction onClick)
    {
        var button = surface.gameObject.AddComponent<Button>();
        var fill = surface.transform.Find("Fill");
        var tinted = fill != null ? fill.GetComponent<Graphic>() : null;
        button.targetGraphic = tinted != null ? tinted : surface;
        surface.raycastTarget = true;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.5f, 1.5f, 1.6f);
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        return button;
    }

    /// <summary>A small rounded pill: divisions, licenses, "In lobby".</summary>
    public static RectTransform Pill(Transform parent, string text, Color fill, Color textColor, int size = 13)
    {
        var pill = Panel(parent, fill, 10, "Pill");
        pill.raycastTarget = false;
        Line(pill.gameObject, 0, new RectOffset(10, 10, 4, 4), TextAnchor.MiddleCenter);
        Rigid(pill);
        OneLine(Label(pill.transform, text, size, textColor, Heading, FontStyle.Bold));
        return pill.rectTransform;
    }

    /// <summary>A square-cornered tag: "You", "Guest".</summary>
    public static RectTransform Badge(Transform parent, string text, Color fill, Color textColor)
    {
        var badge = Panel(parent, fill, Theme.RadiusBadge, "Badge");
        badge.raycastTarget = false;
        Line(badge.gameObject, 0, new RectOffset(6, 6, 2, 2), TextAnchor.MiddleCenter);
        Rigid(badge);
        OneLine(Label(badge.transform, text.ToUpperInvariant(), 11, textColor, Heading, FontStyle.Bold));
        return badge.rectTransform;
    }

    public static InputField Search(Transform parent, string placeholder, Action<string> onChanged, float width = 240)
    {
        var outer = Panel(parent, Theme.Ink700, Theme.RadiusControl, "Search");
        var inner = Panel(outer.transform, Theme.Ink900, Theme.RadiusControl, "Fill");
        Fill(inner.rectTransform, 1, 1, 1, 1);

        var hint = Label(outer.transform, placeholder, 15, Theme.Ink600);
        Fill(hint.rectTransform, 12, 0, 12, 0);
        var typed = Label(outer.transform, "", 15, Theme.Ink200);
        Fill(typed.rectTransform, 12, 0, 12, 0);
        typed.supportRichText = false;

        var field = outer.gameObject.AddComponent<InputField>();
        field.targetGraphic = inner;
        field.textComponent = typed;
        field.placeholder = hint;
        field.caretColor = Theme.Accent;
        field.selectionColor = Theme.Alpha(Theme.Accent, 0.35f);
        field.characterLimit = 80;
        field.lineType = InputField.LineType.SingleLine;
        field.onValueChanged.AddListener(value => onChanged(value));

        Size(outer, width, 38);
        return field;
    }

    /// <summary>
    /// The site's segmented control ("Top 10 / Top 25 / All"). Returns a setter that
    /// moves the highlight without firing <paramref name="onPick"/>.
    /// </summary>
    public static Action<int> Segmented(Transform parent, string[] options, int selected, Action<int> onPick)
    {
        var frame = Panel(parent, Theme.Ink900, Theme.RadiusControl, "Segmented");
        Line(frame.gameObject, 2, new RectOffset(3, 3, 3, 3));
        Rigid(frame);

        var fills = new List<Image>();
        var labels = new List<Text>();
        void Paint(int active)
        {
            for (var i = 0; i < fills.Count; i++)
            {
                fills[i].color = i == active ? Theme.Ink700 : new Color(0, 0, 0, 0);
                labels[i].color = i == active ? Theme.Ink200 : Theme.Ink600;
            }
        }

        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var fill = Panel(frame.transform, new Color(0, 0, 0, 0), 6, options[i]);
            Line(fill.gameObject, 0, new RectOffset(12, 12, 6, 6), TextAnchor.MiddleCenter);
            Rigid(fill);
            var label = OneLine(Label(fill.transform, options[i], 14, Theme.Ink600, Heading, FontStyle.Bold));
            fills.Add(fill);
            labels.Add(label);
            Clickable(fill, () =>
            {
                Paint(index);
                onPick(index);
            });
        }

        Paint(selected);
        return Paint;
    }

    /// <summary>A round avatar: the Steam picture when the pilot has one, their initials until then.</summary>
    public static RectTransform Avatar(Transform parent, float size, PilotRef pilot, SiteClient site, Color? ring = null)
    {
        var holder = Panel(parent, ring ?? Theme.Ink800, 0, "Avatar");
        holder.sprite = Circle();
        holder.type = Image.Type.Simple;
        holder.raycastTarget = false;
        Size(holder, size, size);
        holder.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var initials = Label(holder.transform, Theme.Initials(pilot.Name), Mathf.RoundToInt(size * 0.4f), Theme.Ink400,
            Heading, FontStyle.Bold, TextAnchor.MiddleCenter);
        Fill(initials.rectTransform);

        if (!string.IsNullOrEmpty(pilot.AvatarUrl))
        {
            var picture = Node("Picture", holder.transform).gameObject.AddComponent<RawImage>();
            Fill(picture.rectTransform);
            picture.raycastTarget = false;
            picture.enabled = false;
            site.Texture(pilot.AvatarUrl, texture =>
            {
                if (picture == null || texture == null)
                    return;
                picture.texture = texture;
                picture.enabled = true;
            });
        }
        return holder.rectTransform;
    }

    /// <summary>A picture from a URL at a fixed size, cropped to fill it (the site's object-cover).</summary>
    public static RawImage Picture(Transform parent, string? url, SiteClient site, float width, float height)
    {
        var aspect = width / height;
        var frame = Panel(parent, Theme.Ink800, Theme.RadiusControl, "PictureFrame");
        frame.raycastTarget = false;
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        Size(frame, width, height);

        var picture = Node("Picture", frame.transform).gameObject.AddComponent<RawImage>();
        Fill(picture.rectTransform);
        picture.raycastTarget = false;
        picture.enabled = false;
        site.Texture(url, texture =>
        {
            if (picture == null || texture == null)
                return;
            picture.texture = texture;
            picture.enabled = true;
            // Crop the texture's UVs to the frame's aspect, centred.
            var textureAspect = texture.width / (float)Math.Max(1, texture.height);
            picture.uvRect = textureAspect > aspect
                ? new Rect((1 - aspect / textureAspect) / 2, 0, aspect / textureAspect, 1)
                : new Rect(0, (1 - textureAspect / aspect) / 2, 1, textureAspect / aspect);
        });
        return picture;
    }

    /// <summary>
    /// A vertical scroll list. Returns the content, a column that grows with what is put
    /// in it; the viewport clips to the given rect.
    /// </summary>
    public static RectTransform Scroll(Transform parent, out ScrollRect scroll, float spacing = 0, int pad = 0, string name = "Scroll")
    {
        var viewport = Node(name, parent);
        viewport.gameObject.AddComponent<RectMask2D>();
        // An invisible surface so the wheel scrolls anywhere over the list, not only over rows.
        var catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = new Color(0, 0, 0, 0);

        var content = Column(viewport, spacing, pad, pad, "Content");
        // Room below the last card, so its bottom edge isn't cut by the viewport.
        content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(pad, pad, pad, pad + 24);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = content.offsetMax = Vector2.zero;
        FitHeight(content);

        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40;
        scroll.inertia = true;
        return content;
    }

    /// <summary>A thin horizontal rule, the site's inner divider.</summary>
    public static Image Rule(Transform parent, Color? color = null)
    {
        var rule = Panel(parent, color ?? Theme.Ink750, 0, "Rule");
        rule.raycastTarget = false;
        Size(rule, -1, 1);
        return rule;
    }

    /// <summary>A flexible gap that pushes what follows it to the far end of a row.</summary>
    public static void Spacer(Transform parent)
    {
        var gap = Node("Spacer", parent);
        Size(gap, 0, 0, 1);
    }
}
