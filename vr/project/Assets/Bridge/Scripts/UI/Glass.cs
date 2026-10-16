// SPDX-License-Identifier: LicenseRef-BRIDGE
// BRIDGE VR's interface kit: white frosted glass with graphite ink and BRIDGE orange, sized for
// reading at arm's length in a headset. Panels are single quads drawn by the Glass shader (their
// shape travels in the vertex data, so every panel shares one material); text is TextMesh Pro in
// Figtree, the web app's typeface; icons are lucide's, from one atlas. Widgets answer the
// controllers' rays and a fingertip poke alike (see UIInput).
//
// Units are metres. A panel faces -z: its front is seen by a viewer looking along +z.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bridge.UI
{
    public static class UIStyle
    {
        // Text sizes (TextMesh Pro's 3D size: 1 = 0.1 m em). Readable at 0.6 to 1.2 m.
        public const float Display = 0.62f;
        public const float Title = 0.40f;
        public const float Heading = 0.30f;
        public const float Body = 0.21f;
        public const float Small = 0.165f;
        public const float Tiny = 0.13f;

        public const float Radius = 0.026f;
        public const float Pad = 0.03f;
        public const float Gap = 0.014f;
        public const float Lift = 0.0025f;   // how far an overlay stands off its panel

        static readonly Dictionary<string, Material> textMats = new();
        static Material glass, overlay, shadow, icons;

        public static TMP_FontAsset Font(string name) => Gen.Font(name);

        /// <summary>The font's material, drawn after panels and overlays (queue 3002).</summary>
        public static Material TextMaterial(TMP_FontAsset font, bool onTop = false)
        {
            string key = font.name + onTop;
            if (textMats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(font.material) { name = font.name + " (UI)" };
            m.renderQueue = (int)RenderQueue.Transparent + 2;
            if (onTop && m.HasProperty("unity_GUIZTestMode")) m.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            textMats[key] = m;
            return m;
        }

        public static Material Glass => glass ??= Gen.Template("Glass");
        public static Material Overlay => overlay ??= Gen.Template("GlassOverlay");
        public static Material Shadow => shadow ??= Gen.Template("Shadow");

        public static Material Icons
        {
            get
            {
                if (icons != null) return icons;
                icons = new Material(Gen.Template("UnlitTransparent")) { name = "icons" };
                icons.SetTexture("_BaseMap", Gen.IconAtlas);
                icons.SetFloat("_Fog", 0f);
                icons.renderQueue = (int)RenderQueue.Transparent + 2;
                return icons;
            }
        }
    }

    // ---------------------------------------------------------------------- text
    public static class Text
    {
        public static TextMeshPro Make(Transform parent, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft,
            float width = 0.5f, float height = 0.1f, string font = "Figtree-Medium", bool wrap = true, Vector3 pos = default)
        {
            var go = new GameObject("text");
            go.layer = parent != null ? parent.gameObject.layer : Layers.UI;
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            var fa = UIStyle.Font(font);
            if (fa != null)
            {
                tmp.font = fa;
                tmp.fontSharedMaterial = UIStyle.TextMaterial(fa);
            }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = true;
            tmp.extraPadding = true;
            tmp.rectTransform.sizeDelta = new Vector2(width, height);
            tmp.rectTransform.localPosition = pos + new Vector3(0, 0, -UIStyle.Lift * 2);
            var r = tmp.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tmp;
        }

        /// <summary>A rough width for a line of text in Figtree at a size (for laying out icons).</summary>
        public static float Approx(string s, float size) => (s?.Length ?? 0) * size * 0.1f * 0.52f;

        /// <summary>Lets a one-line label shrink (to <paramref name="min"/> of its size) rather than spill out of its box.</summary>
        public static void Fit(TextMeshPro t, float min = 0.72f)
        {
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = t.fontSize * min;
            t.enableAutoSizing = true;
        }

        /// <summary>The height a block of text needs at its width.</summary>
        public static float Height(TextMeshPro t)
        {
            t.ForceMeshUpdate();
            return t.GetPreferredValues(t.text, t.rectTransform.sizeDelta.x, 0).y;
        }
    }

    // ---------------------------------------------------------------------- glass
    /// <summary>A rounded pane of frosted glass (or a flat overlay), drawn as one quad.</summary>
    public class Pane : MonoBehaviour
    {
        public Vector2 Size;
        public float Radius;
        public Color Tint;
        public float Rim = 0.0016f;
        Mesh mesh;
        MeshRenderer mr;
        GameObject shadowGo;
        Mesh shadowMesh;

        public static Pane Make(Transform parent, string name, Vector2 size, Color tint, float radius = UIStyle.Radius, bool blur = true, bool shadow = false, Vector3 pos = default)
        {
            var go = new GameObject(name);
            go.layer = parent != null ? parent.gameObject.layer : Layers.UI;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var p = go.AddComponent<Pane>();
            p.mesh = new Mesh { name = name };
            go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            p.mr = go.AddComponent<MeshRenderer>();
            p.mr.sharedMaterial = blur ? UIStyle.Glass : UIStyle.Overlay;
            p.mr.shadowCastingMode = ShadowCastingMode.Off;
            p.mr.receiveShadows = false;
            p.Set(size, tint, radius);
            if (shadow) p.AddShadow();
            return p;
        }

        public void Set(Vector2 size, Color tint, float radius)
        {
            Size = size;
            Tint = tint;
            Radius = radius;
            Rebuild();
        }

        public void SetTint(Color tint)
        {
            if (tint == Tint) return;
            Tint = tint;
            var c = new Color[] { tint, tint, tint, tint };
            mesh.SetColors(c);
        }

        public void SetSize(Vector2 size)
        {
            if (size == Size) return;
            Size = size;
            Rebuild();
        }

        public void SetVisible(bool on)
        {
            mr.enabled = on;
            if (shadowGo != null) shadowGo.SetActive(on);
        }

        void Rebuild()
        {
            float w = Size.x / 2, h = Size.y / 2;
            mesh.Clear();
            mesh.SetVertices(new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            var size = new Vector2[] { Size, Size, Size, Size };
            mesh.SetUVs(1, size);
            var shape = new Vector4(Radius, 0, 0, Rim);
            mesh.SetUVs(2, new[] { shape, shape, shape, shape });
            mesh.SetColors(new[] { Tint, Tint, Tint, Tint });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(Size.x, Size.y, 0.01f));
            if (shadowMesh != null) RebuildShadow();
        }

        public void AddShadow(float soft = 0.035f, float strength = 0.28f)
        {
            shadowGo = new GameObject("shadow");
            shadowGo.layer = gameObject.layer;
            shadowGo.transform.SetParent(transform, false);
            shadowGo.transform.localPosition = new Vector3(0, -0.012f, 0.012f);
            shadowMesh = new Mesh { name = "shadow" };
            shadowGo.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
            var r = shadowGo.AddComponent<MeshRenderer>();
            r.sharedMaterial = UIStyle.Shadow;
            r.shadowCastingMode = ShadowCastingMode.Off;
            shadowSoft = soft;
            shadowStrength = strength;
            RebuildShadow();
        }

        float shadowSoft, shadowStrength;

        void RebuildShadow()
        {
            Vector2 s = Size + Vector2.one * shadowSoft * 2;
            float w = s.x / 2, h = s.y / 2;
            shadowMesh.Clear();
            shadowMesh.SetVertices(new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) });
            shadowMesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            shadowMesh.SetUVs(1, new[] { s, s, s, s });
            var shape = new Vector4(Radius + shadowSoft * 0.5f, shadowSoft, 0, 0);
            shadowMesh.SetUVs(2, new[] { shape, shape, shape, shape });
            var c = new Color(0.02f, 0.03f, 0.05f, shadowStrength);
            shadowMesh.SetColors(new[] { c, c, c, c });
            shadowMesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            shadowMesh.bounds = new Bounds(Vector3.zero, new Vector3(s.x, s.y, 0.01f));
        }
    }

    // ---------------------------------------------------------------------- icons
    public class Icon : MonoBehaviour
    {
        Mesh mesh;
        public string Name;
        Color color;

        public static Icon Make(Transform parent, string name, float size, Color color, Vector3 pos = default)
        {
            var go = new GameObject("icon:" + name);
            go.layer = parent != null ? parent.gameObject.layer : Layers.UI;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -UIStyle.Lift * 2);
            go.transform.localScale = new Vector3(size, size, 1);
            var icon = go.AddComponent<Icon>();
            icon.mesh = new Mesh { name = "icon" };
            go.AddComponent<MeshFilter>().sharedMesh = icon.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = UIStyle.Icons;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            icon.color = color;
            icon.Set(name);
            return icon;
        }

        public void Set(string name)
        {
            Name = name;
            var uv = Gen.Icon(name);
            mesh.Clear();
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) });
            mesh.SetUVs(0, new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) });
            mesh.SetColors(new[] { color, color, color, color });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateBounds();
        }

        public void SetColor(Color c)
        {
            if (c == color) return;
            color = c;
            mesh.SetColors(new[] { c, c, c, c });
        }
    }

    // ---------------------------------------------------------------------- widgets
    /// <summary>Something a pointer can hover and press. Its collider sits on the UI layer.</summary>
    public abstract class Widget : MonoBehaviour
    {
        public bool Interactable = true;
        public BoxCollider Hit;
        public string Tip;
        public float Hover;   // 0..1, animated
        public float Press;   // 0..1, animated
        public bool Hovered;
        public bool Pressed;
        public Action Clicked;

        protected void MakeHit(Vector2 size, float depth = 0.012f)
        {
            Hit = gameObject.AddComponent<BoxCollider>();
            Hit.size = new Vector3(size.x, size.y, depth);
            Hit.center = new Vector3(0, 0, -depth * 0.25f);
            Hit.isTrigger = true;
            gameObject.layer = Layers.UI;
        }

        public virtual void OnClick(Pointer p)
        {
            Clicked?.Invoke();
        }

        public virtual void OnDrag(Pointer p, Vector3 local) { }
        public virtual bool Draggable => false;

        protected virtual void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Hover = Ease.Damp(Hover, Hovered && Interactable ? 1 : 0, 18f, dt);
            Press = Ease.Damp(Press, Pressed && Interactable ? 1 : 0, 26f, dt);
            Animate();
        }

        protected abstract void Animate();
    }

    public enum ButtonStyle { Primary, Secondary, Ghost, Danger, Dark, Success }

    public class Button : Widget
    {
        public Pane Back;
        public TextMeshPro Label;
        public TextMeshPro Sub;
        public Icon Glyph;
        public ButtonStyle Style;
        public bool Selected;
        public Vector2 Size;
        Vector3 basePos;
        bool built;

        public static Button Make(Transform parent, string label, Vector2 size, Action onClick, ButtonStyle style = ButtonStyle.Secondary, string icon = null,
            float fontSize = UIStyle.Body, Vector3 pos = default, string sub = null, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("button:" + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -UIStyle.Lift);
            var b = go.AddComponent<Button>();
            b.Style = style;
            b.Size = size;
            b.basePos = go.transform.localPosition;
            b.Clicked = onClick;
            b.MakeHit(size);
            b.Back = Pane.Make(go.transform, "back", size, Color.clear, Mathf.Min(UIStyle.Radius * 0.8f, size.y * 0.5f), blur: false);
            // Small buttons get small margins.
            float pad = Mathf.Min(UIStyle.Pad, size.y * 0.45f);
            float iconSize = Mathf.Min(size.y * 0.5f, 0.05f);
            bool hasLabel = !string.IsNullOrEmpty(label);
            bool leftAligned = align == TextAlignmentOptions.Left;
            // A label too long for one line is set smaller (down to 72 %) before anything wraps.
            bool oneLine = sub == null && size.y < fontSize * 0.1f * 2.8f;
            // A square or tall button stacks its icon over its label.
            if (icon != null && hasLabel && sub == null && !leftAligned && size.y >= size.x * 0.75f && size.y > 0.07f)
            {
                b.Glyph = Icon.Make(go.transform, icon, Mathf.Min(size.y * 0.36f, 0.05f), Color.white, new Vector3(0, size.y * 0.13f, 0));
                b.Label = Text.Make(go.transform, label, fontSize, Color.white, TextAlignmentOptions.Center, size.x - pad, size.y * 0.3f, "Figtree-SemiBold", wrap: false, pos: new Vector3(0, -size.y * 0.24f, 0));
                Text.Fit(b.Label, 0.7f);
                b.built = true;
                b.Animate();
                return b;
            }
            if (hasLabel && sub == null)
            {
                float room = size.x - pad - (icon != null ? iconSize * 1.25f : 0);
                float need = Text.Approx(label, fontSize);
                if (need > room) fontSize *= Mathf.Max(0.72f, room / need);
            }
            if (icon != null)
            {
                float x = hasLabel ? (leftAligned ? -size.x / 2 + pad * 0.7f + iconSize / 2 : -Text.Approx(label, fontSize) / 2 - iconSize * 0.45f) : 0;
                b.Glyph = Icon.Make(go.transform, icon, iconSize, Color.white, new Vector3(x, 0, 0));
            }
            if (hasLabel)
            {
                float left = icon != null && leftAligned ? pad * 0.7f + iconSize + 0.012f : pad * 0.5f;
                float shift = icon != null && !leftAligned ? iconSize * 0.5f : 0;
                var labelAlign = leftAligned ? (sub != null ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.Left) : (sub != null ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Center);
                float w = size.x - left - pad * 0.5f - (icon != null && !leftAligned ? iconSize : 0);
                float cx = leftAligned ? -size.x / 2 + left + w / 2 : shift;
                float h = sub != null ? size.y * 0.5f : size.y;
                float cy = sub != null ? size.y * 0.25f - 0.002f : 0;
                b.Label = Text.Make(go.transform, label, fontSize, Color.white, labelAlign, w, h, "Figtree-SemiBold", wrap: true, pos: new Vector3(cx, cy, 0));
                if (oneLine) Text.Fit(b.Label, 0.85f);
                if (sub != null)
                {
                    b.Sub = Text.Make(go.transform, sub, UIStyle.Tiny, Color.white, align == TextAlignmentOptions.Left ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Top, w, size.y * 0.5f, "Figtree-Regular", pos: new Vector3(cx, -size.y * 0.25f + 0.004f, 0));
                }
            }
            b.built = true;
            b.Animate();
            return b;
        }

        public void SetLabel(string text)
        {
            if (Label != null && Label.text != text) Label.text = text;
        }

        public void SetIcon(string icon)
        {
            if (Glyph != null && Glyph.Name != icon) Glyph.Set(icon);
        }

        Color Fill(out Color ink)
        {
            float h = Hover;
            float p = Press;
            Color fill;
            switch (Style)
            {
                case ButtonStyle.Primary:
                    fill = Color.Lerp(Pal.Brand, Pal.BrandSoft, h * 0.6f);
                    ink = Color.white;
                    fill.a = 1f;
                    break;
                case ButtonStyle.Danger:
                    fill = Color.Lerp(Pal.Err, Color.Lerp(Pal.Err, Color.white, 0.25f), h * 0.6f);
                    ink = Color.white;
                    fill.a = 1f;
                    break;
                case ButtonStyle.Success:
                    fill = Color.Lerp(Pal.Ok, Color.Lerp(Pal.Ok, Color.white, 0.25f), h * 0.6f);
                    ink = Color.white;
                    fill.a = 1f;
                    break;
                case ButtonStyle.Dark:
                    fill = Color.Lerp(new Color(0.09f, 0.11f, 0.14f, 0.88f), new Color(0.16f, 0.19f, 0.23f, 0.94f), h);
                    ink = Color.white;
                    break;
                case ButtonStyle.Ghost:
                    fill = new Color(1, 1, 1, Mathf.Lerp(0f, 0.55f, h));
                    ink = Pal.Ink;
                    break;
                default:
                    fill = Color.Lerp(new Color(1, 1, 1, 0.62f), new Color(1, 1, 1, 0.96f), h);
                    ink = Pal.Ink;
                    break;
            }
            if (Selected && Style != ButtonStyle.Primary)
            {
                fill = Color.Lerp(fill, Pal.Brand, 0.9f);
                fill.a = 1f;
                ink = Color.white;
            }
            fill = Color.Lerp(fill, fill * 0.82f, p);
            fill.a = Mathf.Max(fill.a, 0);
            if (!Interactable)
            {
                fill.a *= 0.45f;
                ink = Color.Lerp(ink, new Color(0.5f, 0.55f, 0.6f), 0.6f);
            }
            return fill;
        }

        protected override void Animate()
        {
            if (!built) return;
            var fill = Fill(out var ink);
            Back.SetTint(fill);
            if (Label != null) Label.color = ink;
            if (Sub != null) Sub.color = new Color(ink.r, ink.g, ink.b, 0.75f);
            if (Glyph != null) Glyph.SetColor(ink);
            float s = 1f + Hover * 0.03f - Press * 0.04f;
            transform.localScale = new Vector3(s, s, 1);
            transform.localPosition = basePos + new Vector3(0, 0, -Hover * 0.004f + Press * 0.003f);
        }

        public void SetSelected(bool on) => Selected = on;
    }

    /// <summary>A labelled switch.</summary>
    public class Toggle : Widget
    {
        public bool On;
        public Action<bool> Changed;
        Pane track, knob;
        TextMeshPro label;
        float anim;

        public static Toggle Make(Transform parent, string label, float width, bool on, Action<bool> changed, Vector3 pos = default, float fontSize = UIStyle.Body)
        {
            var go = new GameObject("toggle:" + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -UIStyle.Lift);
            var t = go.AddComponent<Toggle>();
            t.On = on;
            t.anim = on ? 1 : 0;
            t.Changed = changed;
            float h = 0.05f;
            t.MakeHit(new Vector2(width, h));
            t.label = Text.Make(go.transform, label, fontSize, Pal.Ink, TextAlignmentOptions.Left, width - 0.09f, h, "Figtree-Medium", pos: new Vector3(-0.045f, 0, 0));
            Text.Fit(t.label);
            t.track = Pane.Make(go.transform, "track", new Vector2(0.068f, 0.036f), Color.white, 0.018f, blur: false, pos: new Vector3(width / 2 - 0.04f, 0, 0));
            t.knob = Pane.Make(t.track.transform, "knob", new Vector2(0.028f, 0.028f), Color.white, 0.014f, blur: false, pos: new Vector3(0, 0, -0.001f));
            t.Animate();
            return t;
        }

        public override void OnClick(Pointer p)
        {
            On = !On;
            Changed?.Invoke(On);
        }

        public void Set(bool on) => On = on;

        protected override void Animate()
        {
            if (track == null) return;
            anim = Ease.Damp(anim, On ? 1 : 0, 16f, Time.unscaledDeltaTime);
            track.SetTint(Color.Lerp(new Color(0.55f, 0.6f, 0.66f, 0.55f + Hover * 0.2f), Pal.Brand, anim));
            knob.transform.localPosition = new Vector3(Mathf.Lerp(-0.016f, 0.016f, anim), 0, -0.001f);
            knob.SetTint(new Color(1, 1, 1, 1));
        }
    }

    /// <summary>A horizontal slider, 0..1, dragged with the trigger held.</summary>
    public class Slider : Widget
    {
        public float Value;
        public Action<float> Changed;
        public Func<float, string> Format;
        Pane track, fill, knob;
        TextMeshPro label, readout;
        float width;
        string title;
        public override bool Draggable => true;

        public static Slider Make(Transform parent, string label, float width, float value, Action<float> changed, Func<float, string> format = null, Vector3 pos = default)
        {
            var go = new GameObject("slider:" + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -UIStyle.Lift);
            var s = go.AddComponent<Slider>();
            s.width = width;
            s.Value = Mathf.Clamp01(value);
            s.Changed = changed;
            s.Format = format ?? (v => $"{v * 100:0}%");
            s.title = label;
            s.MakeHit(new Vector2(width, 0.075f));
            s.label = Text.Make(go.transform, label, UIStyle.Small, Pal.InkSoft, TextAlignmentOptions.Left, width * 0.7f, 0.03f, "Figtree-SemiBold", pos: new Vector3(-width * 0.15f, 0.02f, 0));
            s.readout = Text.Make(go.transform, s.Format(s.Value), UIStyle.Small, Pal.Ink, TextAlignmentOptions.Right, width * 0.3f, 0.03f, "IBMPlexMono-SemiBold", pos: new Vector3(width * 0.35f, 0.02f, 0));
            s.track = Pane.Make(go.transform, "track", new Vector2(width, 0.012f), new Color(0.45f, 0.5f, 0.56f, 0.35f), 0.006f, blur: false, pos: new Vector3(0, -0.016f, 0));
            s.fill = Pane.Make(s.track.transform, "fill", new Vector2(0.01f, 0.012f), Pal.Brand, 0.006f, blur: false, pos: new Vector3(0, 0, -0.0008f));
            s.knob = Pane.Make(s.track.transform, "knob", new Vector2(0.03f, 0.03f), Color.white, 0.015f, blur: false, pos: new Vector3(0, 0, -0.0016f));
            s.Animate();
            return s;
        }

        public override void OnDrag(Pointer p, Vector3 local)
        {
            float v = Mathf.Clamp01(local.x / width + 0.5f);
            if (Mathf.Abs(v - Value) < 0.0005f) return;
            Value = v;
            Changed?.Invoke(v);
        }

        public override void OnClick(Pointer p) { }

        public void Set(float v) => Value = Mathf.Clamp01(v);

        protected override void Animate()
        {
            if (track == null) return;
            float w = Mathf.Max(0.012f, width * Value);
            fill.SetSize(new Vector2(w, 0.012f));
            fill.transform.localPosition = new Vector3(-width / 2 + w / 2, 0, -0.0008f);
            float k = 1f + Hover * 0.15f + Press * 0.1f;
            knob.transform.localPosition = new Vector3(-width / 2 + width * Value, 0, -0.0016f);
            knob.transform.localScale = new Vector3(k, k, 1);
            knob.SetTint(Color.Lerp(Color.white, new Color(1f, 0.93f, 0.9f), Press));
            string text = Format(Value);
            if (readout.text != text) readout.text = text;
        }
    }

    /// <summary>A bar that fills (progress, gauges); not interactive.</summary>
    public class Bar : MonoBehaviour
    {
        Pane track, fill;
        float width, height;
        public float Value;
        public Color Color = Pal.Brand;
        float shown;

        public static Bar Make(Transform parent, float width, float height, Color color, Vector3 pos = default)
        {
            var go = new GameObject("bar");
            go.layer = parent != null ? parent.gameObject.layer : Layers.UI;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -UIStyle.Lift);
            var b = go.AddComponent<Bar>();
            b.width = width;
            b.height = height;
            b.Color = color;
            b.track = Pane.Make(go.transform, "track", new Vector2(width, height), new Color(0.4f, 0.45f, 0.5f, 0.22f), height / 2, blur: false);
            b.fill = Pane.Make(go.transform, "fill", new Vector2(height, height), color, height / 2, blur: false, pos: new Vector3(0, 0, -0.0008f));
            return b;
        }

        void Update()
        {
            shown = Ease.Damp(shown, Mathf.Clamp01(Value), 10f, Time.unscaledDeltaTime);
            float w = Mathf.Max(height, width * shown);
            fill.SetSize(new Vector2(w, height));
            fill.transform.localPosition = new Vector3(-width / 2 + w / 2, 0, -0.0008f);
            fill.SetTint(Color);
        }
    }

    // ---------------------------------------------------------------------- windows
    /// <summary>
    /// A frosted panel with a title row, laid out top to bottom by a cursor. Windows can follow the
    /// head lazily (they stay put until you turn away, then glide back into view).
    /// </summary>
    public class Window : MonoBehaviour
    {
        public Pane Pane;
        public Vector2 Size;
        public TextMeshPro TitleText;
        public TextMeshPro Kicker;
        public Icon TitleIcon;
        public Transform Content;
        public float Cursor;       // y of the next row, from the top
        public bool Follow;
        public float FollowDistance = 0.95f;
        public float FollowYaw;       // degrees off the view direction
        public float FollowDrop = 0.18f;
        public float FollowCone = 32f;
        float shown = 1f;
        bool visible = true;
        Vector3 goal;
        Quaternion goalRot;
        bool placed;

        public static Window Make(Transform parent, string name, Vector2 size, string title = null, string icon = null, string kicker = null, bool shadow = true, Color? tint = null)
        {
            var go = new GameObject("window:" + name);
            go.layer = Layers.UI;
            go.transform.SetParent(parent, false);
            var w = go.AddComponent<Window>();
            w.Size = size;
            w.Pane = Pane.Make(go.transform, "pane", size, tint ?? Pal.Glass, UIStyle.Radius * 1.25f, blur: true, shadow: shadow);
            w.Content = new GameObject("content").transform;
            w.Content.gameObject.layer = Layers.UI;
            w.Content.SetParent(go.transform, false);
            w.Cursor = UIStyle.Pad;
            if (title != null)
            {
                float x = -size.x / 2 + UIStyle.Pad;
                if (kicker != null)
                {
                    w.Kicker = Text.Make(w.Content, kicker.ToUpperInvariant(), UIStyle.Tiny, Pal.Brand, TextAlignmentOptions.TopLeft, size.x - UIStyle.Pad * 2, 0.03f, "Figtree-Bold",
                        pos: new Vector3(0, size.y / 2 - w.Cursor - 0.015f, 0));
                    w.Kicker.characterSpacing = 8;
                    w.Cursor += 0.028f;
                }
                if (icon != null)
                {
                    w.TitleIcon = Icon.Make(w.Content, icon, 0.042f, Pal.Brand, new Vector3(x + 0.021f, size.y / 2 - w.Cursor - 0.024f, 0));
                    x += 0.056f;
                }
                float tw = size.x / 2 - UIStyle.Pad - x;
                w.TitleText = Text.Make(w.Content, title, UIStyle.Title, Pal.Ink, TextAlignmentOptions.Left, tw, 0.05f, "Figtree-Bold",
                    pos: new Vector3(x + tw / 2, size.y / 2 - w.Cursor - 0.024f, 0));
                w.Cursor += 0.062f;
            }
            return w;
        }

        public float Width => Size.x - UIStyle.Pad * 2;
        public float Top => Size.y / 2;

        /// <summary>The local position for a row of the given height, at the cursor, then moves past it.</summary>
        public Vector3 Row(float height, float gap = UIStyle.Gap, float x = 0)
        {
            var p = new Vector3(x, Top - Cursor - height / 2, 0);
            Cursor += height + gap;
            return p;
        }

        public TextMeshPro Paragraph(string text, float size = UIStyle.Body, Color? color = null, string font = "Figtree-Regular", float gap = UIStyle.Gap)
        {
            var t = Text.Make(Content, text, size, color ?? Pal.InkSoft, TextAlignmentOptions.TopLeft, Width, 0.05f, font);
            float h = Text.Height(t);
            t.rectTransform.sizeDelta = new Vector2(Width, h);
            t.rectTransform.localPosition = Row(h, gap) + new Vector3(0, 0, -UIStyle.Lift * 2);
            return t;
        }

        public void Clear()
        {
            foreach (Transform t in Content) Destroy(t.gameObject);
            Cursor = UIStyle.Pad;
            TitleText = null;
            Kicker = null;
            TitleIcon = null;
        }

        public void Resize(Vector2 size)
        {
            Size = size;
            Pane.SetSize(size);
        }

        /// <summary>Shrinks the window to what has been laid out.</summary>
        public void FitHeight(float min = 0.1f)
        {
            float h = Mathf.Max(min, Cursor + UIStyle.Pad - UIStyle.Gap);
            float dy = (Size.y - h) / 2;
            Resize(new Vector2(Size.x, h));
            Content.localPosition = new Vector3(0, -dy, 0) + Content.localPosition;
        }

        public void Show(bool on, bool instant = false)
        {
            visible = on;
            if (instant)
            {
                shown = on ? 1 : 0;
                Apply();
            }
            if (on) gameObject.SetActive(true);
        }

        public bool Visible => visible;

        /// <summary>Puts the window in front of the head now.</summary>
        public void PlaceInFront(Transform head, float distance, float yaw = 0, float drop = 0.15f)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd = Quaternion.AngleAxis(yaw, Vector3.up) * fwd;
            transform.position = head.position + fwd * distance + Vector3.down * drop;
            transform.rotation = Quaternion.LookRotation(transform.position - head.position, Vector3.up);
            goal = transform.position;
            goalRot = transform.rotation;
            placed = true;
        }

        public void FaceHead(Transform head)
        {
            var d = transform.position - head.position;
            d.y *= 0.6f;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float target = visible ? 1 : 0;
            if (Mathf.Abs(shown - target) > 0.001f)
            {
                shown = Mathf.MoveTowards(shown, target, dt / 0.18f);
                Apply();
                if (!visible && shown <= 0.001f) gameObject.SetActive(false);
            }
            if (Follow && visible)
            {
                var head = Rig.Head;
                if (head == null) return;
                Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                Vector3 to = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
                float angle = Vector3.Angle(Quaternion.AngleAxis(FollowYaw, Vector3.up) * fwd, to);
                float dist = to.magnitude;
                if (!placed || angle > FollowCone || dist > FollowDistance * 1.6f || dist < FollowDistance * 0.45f)
                {
                    Vector3 dir = Quaternion.AngleAxis(FollowYaw, Vector3.up) * fwd;
                    goal = head.position + dir * FollowDistance + Vector3.down * FollowDrop;
                    goalRot = Quaternion.LookRotation(goal - head.position, Vector3.up);
                    placed = true;
                }
                transform.position = Ease.Damp(transform.position, goal, 4.5f, dt);
                transform.rotation = Ease.Damp(transform.rotation, goalRot, 4.5f, dt);
            }
        }

        void Apply()
        {
            float s = Mathf.Lerp(0.92f, 1f, Ease.OutCubic(shown));
            transform.localScale = new Vector3(s, s, s);
        }
    }

    /// <summary>The scene's hook for "where is the head": set by the rig.</summary>
    public static class Rig
    {
        public static Transform Head;
    }
}
