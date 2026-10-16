// SPDX-License-Identifier: LicenseRef-BRIDGE
// Interface that lives in the room rather than on a panel: physical push buttons (the emergency
// stop), labels that point at parts of the robot, rings on the floor that say where to stand,
// targets for the robot's hand, and arrows that show where to look.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bridge.UI
{
    /// <summary>A round push button you can poke or point at. Latching buttons stay down until pressed again.</summary>
    public class PushButton : Widget
    {
        public Transform Cap;
        public bool Latching;
        public bool Down;
        public Action<bool> Toggled;
        float travel = 0.008f;
        float shown;

        public static PushButton Make(Transform parent, Transform cap, float radius, bool latching, Action<bool> toggled, Vector3 localCentre = default)
        {
            var go = new GameObject("push button");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCentre;
            var b = go.AddComponent<PushButton>();
            b.Cap = cap;
            b.Latching = latching;
            b.Toggled = toggled;
            b.MakeHit(new Vector2(radius * 2.6f, radius * 2.6f), radius * 2f);
            return b;
        }

        public override void OnClick(Pointer p)
        {
            Down = Latching ? !Down : false;
            Toggled?.Invoke(Latching ? Down : true);
            Clicked?.Invoke();
        }

        public void Set(bool down) => Down = down;

        protected override void Animate()
        {
            if (Cap == null) return;
            float target = (Down ? 1 : 0) * 0.8f + Press * 0.2f + Hover * 0.05f;
            shown = Ease.Damp(shown, target, 20, Time.unscaledDeltaTime);
            Cap.localPosition = new Vector3(0, 0, shown * travel);
        }
    }

    /// <summary>A label in the room: a glass chip with a line to what it names.</summary>
    public class WorldLabel : MonoBehaviour
    {
        public Transform Anchor;
        public Vector3 AnchorOffset;
        public Vector3 Offset = new(0, 0.18f, 0);
        TextMeshPro title, sub;
        Pane back;
        LineRenderer leader;
        Transform dot;
        float shown;
        public bool Visible = true;
        public Color Accent = Pal.Brand;

        public static WorldLabel Make(Transform parent, string titleText, string subText, Transform anchor, Vector3 offset, Vector3 anchorOffset = default, float width = 0.0f)
        {
            var go = new GameObject("label:" + titleText);
            go.layer = Layers.UI;
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<WorldLabel>();
            l.Anchor = anchor;
            l.Offset = offset;
            l.AnchorOffset = anchorOffset;
            float w = width > 0 ? width : Mathf.Max(Text.Approx(titleText, UIStyle.Body) + 0.05f, string.IsNullOrEmpty(subText) ? 0 : Text.Approx(subText, UIStyle.Tiny) + 0.05f);
            w = Mathf.Clamp(w, 0.1f, 0.36f);
            float h = string.IsNullOrEmpty(subText) ? 0.042f : 0.07f;
            l.back = Pane.Make(go.transform, "back", new Vector2(w, h), Pal.Glass, h * 0.32f, blur: true, shadow: false);
            l.title = Text.Make(go.transform, titleText, UIStyle.Body, Pal.Ink, TextAlignmentOptions.Left, w - 0.03f, 0.03f, "Figtree-Bold", false,
                new Vector3(0, string.IsNullOrEmpty(subText) ? 0 : 0.012f, 0));
            if (!string.IsNullOrEmpty(subText))
                l.sub = Text.Make(go.transform, subText, UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Left, w - 0.03f, 0.03f, "Figtree-Medium", true, new Vector3(0, -0.014f, 0));
            l.leader = new GameObject("leader").AddComponent<LineRenderer>();
            l.leader.transform.SetParent(go.transform, false);
            l.leader.useWorldSpace = true;
            l.leader.positionCount = 2;
            l.leader.widthMultiplier = 0.0018f;
            l.leader.sharedMaterial = Mats.Transparent(new Color(1, 1, 1, 0.9f));
            l.leader.shadowCastingMode = ShadowCastingMode.Off;
            l.dot = Go.Mesh("dot", go.transform, MeshKit.Sphere(12), Mats.Unlit(Pal.Brand * 1.4f), Vector3.zero, null, Vector3.one * 0.012f, cast: false, layer: Layers.UI).transform;
            return l;
        }

        public void SetText(string titleText, string subText = null)
        {
            title.text = titleText;
            if (sub != null && subText != null) sub.text = subText;
        }

        void LateUpdate()
        {
            shown = Mathf.MoveTowards(shown, Visible ? 1 : 0, Time.unscaledDeltaTime / 0.25f);
            bool on = shown > 0.001f && Anchor != null;
            back.gameObject.SetActive(on);
            title.gameObject.SetActive(on);
            if (sub != null) sub.gameObject.SetActive(on);
            leader.enabled = on;
            dot.gameObject.SetActive(on);
            if (!on) return;
            Vector3 a = Anchor.TransformPoint(AnchorOffset);
            transform.position = a + Offset;
            var head = Rig.Head;
            if (head != null)
            {
                var d = transform.position - head.position;
                d.y *= 0.4f;
                if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
            float s = Mathf.Lerp(0.6f, 1f, Ease.OutBack(shown));
            back.transform.localScale = new Vector3(s, s, 1);
            leader.SetPosition(0, a);
            leader.SetPosition(1, Vector3.Lerp(a, transform.position - transform.up * 0.02f, Ease.OutCubic(shown)));
            dot.position = a;
        }
    }

    /// <summary>A pulsing ring on the floor: "stand here".</summary>
    public class FloorMarker : MonoBehaviour
    {
        Material mat;
        TextMeshPro text;
        public float Radius = 0.4f;
        public Color Color = Pal.Brand;
        Transform label;

        public static FloorMarker Make(Transform parent, Vector3 at, float radius, string caption, Color color)
        {
            var go = new GameObject("marker:" + caption);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(at.x, 0.005f, at.z);
            var m = go.AddComponent<FloorMarker>();
            m.Radius = radius;
            m.Color = color;
            m.mat = Mats.Clone("Zone");
            m.mat.SetColor("_Color", color * 1.6f);
            m.mat.SetFloat("_Inner", 0.0f);
            m.mat.SetFloat("_Fill", 0.18f);
            m.mat.SetFloat("_Edge", 1.4f);
            m.mat.SetFloat("_EdgeWidth", 0.06f);
            m.mat.SetFloat("_Dashes", 36);
            m.mat.SetFloat("_Speed", 0.25f);
            m.mat.SetFloat("_Pulse", 0.25f);
            Go.Mesh("ring", go.transform, MeshKit.FloorQuad(), m.mat, Vector3.zero, null, new Vector3(radius * 2, 1, radius * 2), cast: false, layer: Layers.UI);
            if (!string.IsNullOrEmpty(caption))
            {
                m.label = new GameObject("caption").transform;
                m.label.SetParent(go.transform, false);
                m.label.localPosition = new Vector3(0, 0.02f, 0);
                m.text = Text.Make(m.label, caption.ToUpperInvariant(), 0.5f, color, TextAlignmentOptions.Center, radius * 3, 0.1f, "Figtree-ExtraBold", false);
                m.text.transform.localRotation = Quaternion.Euler(90, 0, 0);
                m.text.transform.localPosition = new Vector3(0, 0, -radius - 0.09f);
            }
            return m;
        }

        /// <summary>Whether a point (the head) stands inside the ring.</summary>
        public bool Contains(Vector3 p) => new Vector2(p.x - transform.position.x, p.z - transform.position.z).magnitude < Radius;

        void LateUpdate()
        {
            var head = Rig.Head;
            if (label != null && head != null)
            {
                var d = transform.position - head.position;
                d.y = 0;
                if (d.sqrMagnitude > 1e-4f) label.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }
    }

    /// <summary>A glowing sphere the robot's hand has to reach (a target point in a lesson).</summary>
    public class HandTarget : MonoBehaviour
    {
        public float Radius = 0.025f;
        public bool Reached;
        MeshRenderer shell;
        Material mat;
        TextMeshPro text;
        float pop;

        public static HandTarget Make(Transform parent, Vector3 at, string caption, float radius = 0.025f)
        {
            var go = new GameObject("target:" + caption);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var t = go.AddComponent<HandTarget>();
            t.Radius = radius;
            t.mat = Mats.Clone("Hologram");
            t.mat.SetColor("_Color", new Color(1.2f, 0.45f, 0.2f));
            t.mat.SetFloat("_Pulse", 0.6f);
            t.shell = Go.Mesh("shell", go.transform, MeshKit.Sphere(24), t.mat, Vector3.zero, null, Vector3.one * radius * 2, cast: false, layer: Layers.UI).GetComponent<MeshRenderer>();
            Go.Mesh("core", go.transform, MeshKit.Sphere(12), Mats.Unlit(new Color(1.6f, 0.6f, 0.25f)), Vector3.zero, null, Vector3.one * radius * 0.4f, cast: false, layer: Layers.UI);
            if (!string.IsNullOrEmpty(caption))
            {
                t.text = Text.Make(go.transform, caption, UIStyle.Small, Color.white, TextAlignmentOptions.Center, 0.2f, 0.03f, "Figtree-Bold", false, new Vector3(0, radius + 0.03f, 0));
            }
            return t;
        }

        public void MarkReached()
        {
            if (Reached) return;
            Reached = true;
            pop = 1;
            mat.SetColor("_Color", new Color(0.3f, 1.4f, 0.6f));
            mat.SetFloat("_Pulse", 0);
        }

        void LateUpdate()
        {
            pop = Mathf.MoveTowards(pop, 0, Time.unscaledDeltaTime * 3);
            shell.transform.localScale = Vector3.one * Radius * 2 * (1 + pop * 0.6f);
            var head = Rig.Head;
            if (text != null && head != null) text.transform.rotation = Quaternion.LookRotation(text.transform.position - head.position, Vector3.up);
        }
    }

    /// <summary>A bobbing arrow pointing down at something.</summary>
    public class PointerArrow : MonoBehaviour
    {
        public Transform Target;
        public Vector3 TargetOffset;
        float clock;

        public static PointerArrow Make(Transform parent, Transform target, Vector3 offset, Color color)
        {
            var go = new GameObject("arrow");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<PointerArrow>();
            a.Target = target;
            a.TargetOffset = offset;
            var mat = Mats.Unlit(color * 1.3f);
            Go.Mesh("head", go.transform, MeshKit.Cone(0.03f, 0.05f, 20), mat, new Vector3(0, 0, 0), Quaternion.Euler(180, 0, 0), cast: false, layer: Layers.UI);
            Go.Mesh("shaft", go.transform, MeshKit.Cylinder(0.011f, 0.06f, 16), mat, new Vector3(0, 0.08f, 0), cast: false, layer: Layers.UI);
            return a;
        }

        void LateUpdate()
        {
            if (Target == null) return;
            clock += Time.unscaledDeltaTime;
            transform.position = Target.TransformPoint(TargetOffset) + Vector3.up * (0.05f + 0.025f * Mathf.Sin(clock * 4));
            var head = Rig.Head;
            if (head != null)
            {
                var d = head.position - transform.position;
                d.y = 0;
                if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }
    }
}
