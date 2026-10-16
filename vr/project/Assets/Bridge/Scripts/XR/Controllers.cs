// SPDX-License-Identifier: LicenseRef-BRIDGE
// What you see of your hands: a model of each Touch controller (the Quest 2's, with its tracking
// ring; Quest 3's Touch Plus has the same buttons in the same places) whose buttons light up when a
// lesson points at them, labels beside each control saying what it does right now (they show when
// you look at the controller, or when a lesson asks), the pointer ray with its dot, and the fade and
// comfort vignette locomotion uses.

using System.Collections.Generic;
using Bridge.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bridge.XR
{
    public enum Control { Trigger, Grip, Primary, Secondary, Stick, Menu }

    public class ControllerModel
    {
        public readonly Hand Hand;
        public readonly Transform Root;
        readonly Dictionary<Control, Transform> parts = new();
        readonly Dictionary<Control, MeshRenderer[]> renderers = new();
        readonly Dictionary<Control, Hint> hints = new();
        readonly Material body, button, lit;
        readonly Transform stickTop;
        readonly LineRenderer ray;
        readonly Transform dot;
        readonly MeshRenderer dotRenderer;
        public bool HintsForced;
        public bool HintsAllowed = true;
        /// <summary>Shows the models without a headset (for screenshots of the controls).</summary>
        public static bool ShowInDesktop;
        float hintShow;
        readonly HashSet<Control> highlighted = new();
        public float RayLength = 2.5f;
        public bool RayVisible = true;
        public Color RayColor = new(1f, 1f, 1f, 0.9f);

        class Hint
        {
            public Transform Root;
            public TextMeshPro Text;
            public Pane Back;
            public LineRenderer Leader;
            public Vector3 Anchor;   // on the controller (local)
            public Vector3 Offset;   // where the label floats (local)
            public string Label = "";
        }

        public ControllerModel(Hand hand, Transform parent)
        {
            Hand = hand;
            Root = new GameObject(hand.Name + " controller model").transform;
            Root.SetParent(hand.Grip, false);
            Root.gameObject.layer = Layers.Hands;
            float side = hand.Index == 0 ? 1 : -1;   // the inner side, where the middle finger wraps
            body = Mats.Paint(0x23272c, 0.55f, 0.15f);
            button = Mats.Paint(0x3a4048, 0.4f, 0.1f);
            lit = Mats.Glow(Pal.Brand, 2.6f);

            // The handle along the grip's z, the head on top at the front, tilted back towards you.
            Go.Mesh("handle", Root, MeshKit.RoundedBox(new Vector3(0.034f, 0.04f, 0.105f), 0.016f), body, new Vector3(0, -0.012f, -0.012f), cast: false, layer: Layers.Hands);
            var head = Go.Empty("head", Root, new Vector3(0, 0.014f, 0.042f), Quaternion.Euler(-28, 0, 0)).transform;
            Go.Mesh("head shell", head, MeshKit.RoundedBox(new Vector3(0.058f, 0.024f, 0.064f), 0.011f), body, Vector3.zero, cast: false, layer: Layers.Hands);
            Go.Mesh("face", head, MeshKit.RoundedBox(new Vector3(0.05f, 0.002f, 0.054f), 0.009f), Mats.Paint(0x14171b, 0.3f, 0.1f), new Vector3(0, 0.012f, 0), cast: false, layer: Layers.Hands);

            // Thumbstick, outboard of the buttons.
            var stick = Go.Empty("stick", head, new Vector3(0.012f * side, 0.013f, 0.006f)).transform;
            Go.Mesh("well", stick, MeshKit.Cylinder(0.013f, 0.003f, 28), Mats.Paint(0x0e1013, 0.6f, 0), Vector3.zero, cast: false, layer: Layers.Hands);
            Go.Mesh("shaft", stick, MeshKit.Cylinder(0.004f, 0.012f, 12), button, new Vector3(0, 0.006f, 0), cast: false, layer: Layers.Hands);
            stickTop = Go.Mesh("cap", stick, MeshKit.Cylinder(0.0105f, 0.005f, 28, 0.0095f), button, new Vector3(0, 0.013f, 0), cast: false, layer: Layers.Hands).transform;
            parts[Control.Stick] = stick;
            renderers[Control.Stick] = new[] { stickTop.GetComponent<MeshRenderer>() };

            // Face buttons: A/B on the right controller, X/Y on the left.
            var primary = Go.Mesh("primary", head, MeshKit.Cylinder(0.0062f, 0.004f, 24), button, new Vector3(-0.012f * side, 0.014f, -0.014f), cast: false, layer: Layers.Hands).transform;
            var secondary = Go.Mesh("secondary", head, MeshKit.Cylinder(0.0062f, 0.004f, 24), button, new Vector3(-0.008f * side, 0.014f, 0.012f), cast: false, layer: Layers.Hands).transform;
            parts[Control.Primary] = primary;
            parts[Control.Secondary] = secondary;
            renderers[Control.Primary] = new[] { primary.GetComponent<MeshRenderer>() };
            renderers[Control.Secondary] = new[] { secondary.GetComponent<MeshRenderer>() };
            var letters = new[] { hand.PrimaryName, hand.SecondaryName };
            for (int i = 0; i < 2; i++)
            {
                var t = Bridge.UI.Text.Make(i == 0 ? primary : secondary, letters[i], 0.045f, new Color(0.8f, 0.82f, 0.85f), TextAlignmentOptions.Center, 0.012f, 0.012f, "Figtree-Bold", false);
                t.transform.localPosition = new Vector3(0, 0.0025f, 0);
                t.transform.localRotation = Quaternion.Euler(90, 0, 0);
                t.gameObject.layer = Layers.Hands;
            }
            // Menu (left) or the system button (right, kept by the headset).
            var menu = Go.Mesh("menu", head, MeshKit.Cylinder(0.0042f, 0.003f, 20), button, new Vector3(-0.016f * side, 0.0135f, 0.024f), cast: false, layer: Layers.Hands).transform;
            parts[Control.Menu] = menu;
            renderers[Control.Menu] = new[] { menu.GetComponent<MeshRenderer>() };

            // Trigger under the head, grip on the inner side of the handle.
            var trigger = Go.Mesh("trigger", Root, MeshKit.RoundedBox(new Vector3(0.016f, 0.026f, 0.014f), 0.006f), button, new Vector3(0, -0.012f, 0.062f), Quaternion.Euler(18, 0, 0), cast: false, layer: Layers.Hands).transform;
            var grip = Go.Mesh("grip", Root, MeshKit.RoundedBox(new Vector3(0.008f, 0.034f, 0.04f), 0.004f), button, new Vector3(0.018f * side, -0.016f, 0.004f), cast: false, layer: Layers.Hands).transform;
            parts[Control.Trigger] = trigger;
            parts[Control.Grip] = grip;
            // The tracking ring that loops over the back of the hand, round the thumb's buttons.
            Go.Mesh("ring", Root, MeshKit.Torus(0.05f, 0.0062f, 56, 12), Mats.Paint(0xe9eaec, 0.4f, 0.05f), new Vector3(-0.026f * side, 0.032f, 0.036f),
                Quaternion.Euler(-38, 0, 18f * side) * Quaternion.Euler(0, 0, 90), cast: false, layer: Layers.Hands);
            renderers[Control.Trigger] = new[] { trigger.GetComponent<MeshRenderer>() };
            renderers[Control.Grip] = new[] { grip.GetComponent<MeshRenderer>() };

            // Labels, floating outboard of each control.
            float o = hand.Index == 0 ? -1 : 1;   // outward from the body
            MakeHint(Control.Trigger, trigger.localPosition, new Vector3(0.0f, -0.07f, 0.11f));
            MakeHint(Control.Grip, grip.localPosition, new Vector3(0.09f * side, -0.07f, -0.03f));
            MakeHint(Control.Primary, Root.InverseTransformPoint(primary.position), new Vector3(0.1f * o, 0.06f, 0.0f));
            MakeHint(Control.Secondary, Root.InverseTransformPoint(secondary.position), new Vector3(0.1f * o, 0.1f, 0.05f));
            MakeHint(Control.Stick, Root.InverseTransformPoint(stick.position), new Vector3(-0.1f * o, 0.09f, 0.05f));
            if (hand.Index == 0) MakeHint(Control.Menu, Root.InverseTransformPoint(menu.position), new Vector3(0.1f * o, 0.14f, 0.1f));

            // The pointer ray and its dot.
            var rayGo = new GameObject(hand.Name + " ray");
            rayGo.layer = Layers.Hands;
            rayGo.transform.SetParent(parent, false);
            ray = rayGo.AddComponent<LineRenderer>();
            ray.useWorldSpace = true;
            ray.positionCount = 2;
            ray.widthCurve = new AnimationCurve(new Keyframe(0, 0.0045f), new Keyframe(1, 0.0016f));
            ray.numCapVertices = 4;
            var rayMat = Mats.Clone("UnlitTransparent");
            rayMat.renderQueue = (int)RenderQueue.Transparent + 3;
            rayMat.SetFloat("_Fog", 0);
            ray.sharedMaterial = rayMat;
            ray.shadowCastingMode = ShadowCastingMode.Off;
            ray.receiveShadows = false;
            var dotGo = Go.Mesh(hand.Name + " dot", parent, MeshKit.Sphere(16), Mats.Transparent(Color.white, true), Vector3.zero, null, Vector3.one * 0.012f, cast: false, layer: Layers.Hands);
            dot = dotGo.transform;
            dotRenderer = dotGo.GetComponent<MeshRenderer>();
        }

        void MakeHint(Control c, Vector3 anchor, Vector3 offset)
        {
            var h = new Hint { Anchor = anchor, Offset = offset };
            h.Root = new GameObject("hint " + c).transform;
            h.Root.gameObject.layer = Layers.Hands;
            h.Root.SetParent(Root, false);
            h.Root.localPosition = offset;
            h.Back = Pane.Make(h.Root, "back", new Vector2(0.09f, 0.026f), new Color(0.08f, 0.1f, 0.12f, 0.86f), 0.013f, blur: false);
            h.Back.Rim = 0.0008f;
            h.Text = Bridge.UI.Text.Make(h.Root, "", 0.11f, Color.white, TextAlignmentOptions.Center, 0.2f, 0.026f, "Figtree-SemiBold", false);
            var lr = new GameObject("leader").AddComponent<LineRenderer>();
            lr.transform.SetParent(h.Root, false);
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.0012f;
            lr.sharedMaterial = Mats.Transparent(new Color(1, 1, 1, 0.75f));
            lr.shadowCastingMode = ShadowCastingMode.Off;
            h.Leader = lr;
            hints[c] = h;
            foreach (var t in h.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Hands;
        }

        /// <summary>Sets what a control's label says ("" hides it).</summary>
        public void SetHint(Control c, string text)
        {
            if (!hints.TryGetValue(c, out var h)) return;
            if (h.Label == text) return;
            h.Label = text;
            string name = c switch
            {
                Control.Primary => Hand.PrimaryName,
                Control.Secondary => Hand.SecondaryName,
                Control.Stick => "Stick",
                Control.Trigger => "Trigger",
                Control.Grip => "Grip",
                _ => "Menu",
            };
            h.Text.text = string.IsNullOrEmpty(text) ? "" : $"<color=#FF8A5C>{name}</color>  {text}";
            float w = Bridge.UI.Text.Approx(name + "  " + text, 0.11f) + 0.026f;
            h.Back.SetSize(new Vector2(Mathf.Max(0.05f, w), 0.026f));
        }

        public void Highlight(Control c, bool on)
        {
            if (on) highlighted.Add(c); else highlighted.Remove(c);
        }

        public void ClearHighlights() => highlighted.Clear();

        public void Tick(float dt, Transform head, bool pointerOverUI, float uiDistance, Vector3? worldHit)
        {
            bool tracked = Hand.Tracked && XRSystem.I.XRActive || ShowInDesktop;
            Root.gameObject.SetActive(tracked);
            // Hints show while you look at this controller (or when asked to).
            Vector3 toCtl = Root.position - head.position;
            bool looking = tracked && toCtl.magnitude < 0.75f && Vector3.Angle(head.forward, toCtl) < 24f;
            hintShow = Mathf.MoveTowards(hintShow, (HintsForced || looking) && HintsAllowed ? 1 : 0, dt / 0.2f);
            foreach (var kv in hints)
            {
                var h = kv.Value;
                bool show = hintShow > 0.01f && !string.IsNullOrEmpty(h.Label);
                h.Root.gameObject.SetActive(show);
                if (!show) continue;
                h.Root.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, Ease.OutCubic(hintShow));
                var d = h.Root.position - head.position;
                h.Root.rotation = Quaternion.LookRotation(d, Vector3.up);
                h.Leader.SetPosition(0, Root.TransformPoint(h.Anchor));
                h.Leader.SetPosition(1, h.Root.position + (Root.TransformPoint(h.Anchor) - h.Root.position).normalized * 0.012f);
                bool hi = highlighted.Contains(kv.Key);
                h.Back.SetTint(hi ? new Color(Pal.Brand.r, Pal.Brand.g, Pal.Brand.b, 0.95f) : new Color(0.08f, 0.1f, 0.12f, 0.86f));
            }
            // Pulse highlighted controls.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var kv in renderers)
            {
                bool hi = highlighted.Contains(kv.Key);
                foreach (var r in kv.Value) r.sharedMaterial = hi && pulse > 0.35f ? lit : button;
            }
            // Pressed controls move a little.
            parts[Control.Trigger].localRotation = Quaternion.Euler(18 + Hand.Trigger * 14, 0, 0);
            stickTop.localPosition = new Vector3(Hand.Stick.x * 0.003f, 0.013f - (Hand.StickClick ? 0.0015f : 0), Hand.Stick.y * 0.003f);

            // The ray: to the panel it points at, or a short way into the room.
            bool showRay = tracked && RayVisible;
            ray.enabled = showRay;
            dotRenderer.enabled = showRay && (pointerOverUI || worldHit.HasValue);
            if (!showRay) return;
            var a = Hand.Aim;
            float len = pointerOverUI ? uiDistance : worldHit.HasValue ? Vector3.Distance(a.position, worldHit.Value) : RayLength;
            Vector3 end = a.position + a.forward * len;
            ray.SetPosition(0, a.position + a.forward * 0.02f);
            ray.SetPosition(1, end);
            var start = RayColor;
            var fade = new Color(start.r, start.g, start.b, pointerOverUI ? 0.85f : 0.05f);
            ray.startColor = new Color(start.r, start.g, start.b, 0.0f);
            ray.endColor = fade;
            ray.colorGradient = Gradient2(new Color(start.r, start.g, start.b, 0.05f), new Color(start.r, start.g, start.b, pointerOverUI ? 0.9f : 0.35f), fade);
            dot.position = end;
            float size = Mathf.Clamp(len * 0.012f, 0.006f, 0.02f) * (Hand.TriggerHeld ? 0.7f : 1f);
            dot.localScale = Vector3.one * size;
        }

        static Gradient Gradient2(Color a, Color b, Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 0.5f), new GradientColorKey(c, 1) },
                new[] { new GradientAlphaKey(a.a, 0), new GradientAlphaKey(b.a, 0.3f), new GradientAlphaKey(c.a, 1) });
            return g;
        }
    }

    /// <summary>Teleport, snap turn, smooth movement, and the fade and vignette that make them comfortable.</summary>
    public class Locomotion
    {
        readonly XRSystem xr;
        readonly LineRenderer arc;
        readonly Transform reticle;
        readonly Material reticleMat;
        readonly MeshRenderer fader;
        readonly Material faderMat;
        public bool Aiming { get; private set; }
        bool turnLatch;
        Vector3 target;
        bool valid;
        float fade;
        float fadeTarget;
        System.Action pending;
        /// <summary>The right stick walks (Settings: Walk with the right stick); off, it jumps and turns.</summary>
        public bool StickWalk = true;
        public float SnapAngle = 30f;
        public float MoveSpeed = 1.4f;
        /// <summary>Metres walked with the stick, for the lessons.</summary>
        public float Walked;
        public float Comfort;   // 0..1, the vignette while moving
        public bool Enabled = true;
        public int Teleports, Turns;
        public System.Action<Vector3> Teleported;
        public Rect Bounds = new(-15.4f, -12.4f, 34.8f, 24.8f);   // x, z
        static readonly RaycastHit[] hits = new RaycastHit[8];

        public Locomotion(XRSystem xr, Transform parent)
        {
            this.xr = xr;
            var go = new GameObject("teleport arc");
            go.transform.SetParent(parent, false);
            arc = go.AddComponent<LineRenderer>();
            arc.positionCount = 0;
            arc.widthMultiplier = 0.012f;
            arc.numCapVertices = 4;
            arc.sharedMaterial = Mats.Transparent(new Color(1, 1, 1, 1));
            arc.shadowCastingMode = ShadowCastingMode.Off;
            reticleMat = Mats.Clone("Zone");
            reticleMat.SetColor("_Color", new Color(1f, 0.55f, 0.3f, 1f));
            reticleMat.SetFloat("_Inner", 0.55f);
            reticleMat.SetFloat("_Fill", 0.35f);
            reticleMat.SetFloat("_Edge", 1.2f);
            reticleMat.SetFloat("_EdgeWidth", 0.08f);
            reticleMat.SetFloat("_Dashes", 18);
            reticleMat.SetFloat("_Speed", 0.6f);
            reticle = Go.Mesh("teleport target", parent, MeshKit.FloorQuad(), reticleMat, Vector3.zero, null, new Vector3(0.6f, 1, 0.6f), cast: false, layer: Layers.Hands).transform;
            reticle.gameObject.SetActive(false);
            // A black shell round the head for fades.
            faderMat = Mats.Clone("UnlitTransparent");
            faderMat.SetColor("_BaseColor", new Color(0, 0, 0, 0));
            faderMat.SetFloat("_ZTest", (float)CompareFunction.Always);
            faderMat.SetFloat("_Cull", (float)CullMode.Front);
            faderMat.SetFloat("_Fog", 0);
            faderMat.renderQueue = 4000;
            fader = Go.Mesh("fader", xr.Head, MeshKit.Sphere(20), faderMat, Vector3.zero, null, Vector3.one * 0.3f, cast: false, layer: Layers.Hands).GetComponent<MeshRenderer>();
            fader.enabled = false;
        }

        /// <summary>Fades out, runs the action (a teleport, a scene change), fades back in.</summary>
        public void FadeThrough(System.Action action, float seconds = 0.14f)
        {
            pending = action;
            fadeTarget = 1;
            fadeSpeed = 1f / Mathf.Max(seconds, 0.01f);
            if (!xr.XRActive) { pending?.Invoke(); pending = null; fadeTarget = 0; }
        }

        float fadeSpeed = 7f;

        public void Tick(float dt, Hand left, Hand right, bool sticksFree, bool pointerOnUI)
        {
            // Fade.
            fade = Mathf.MoveTowards(fade, fadeTarget, dt * fadeSpeed);
            if (fadeTarget >= 1 && fade >= 0.999f)
            {
                pending?.Invoke();
                pending = null;
                fadeTarget = 0;
            }
            fader.enabled = fade > 0.001f;
            faderMat.SetColor("_BaseColor", new Color(0, 0, 0, fade));
            Comfort = Mathf.MoveTowards(Comfort, 0, dt * 2.5f);

            if (!Enabled || !sticksFree)
            {
                Cancel();
                return;
            }
            // The right stick walks, and the left one turns and jumps. With walking off (Settings), the
            // right stick turns and jumps instead, as most comfort-first VR apps do.
            var jumpHand = StickWalk ? left : right;
            var s = jumpHand.Stick;
            // Snap turn.
            if (!Aiming && Mathf.Abs(s.x) > 0.72f && Mathf.Abs(s.y) < 0.5f)
            {
                if (!turnLatch)
                {
                    turnLatch = true;
                    xr.Turn(Mathf.Sign(s.x) * SnapAngle);
                    Turns++;
                    Comfort = 0.6f;
                }
            }
            else if (Mathf.Abs(s.x) < 0.3f) turnLatch = false;
            // Teleport: push forward to aim, let go to jump.
            if (!Aiming && s.y > 0.68f && !pointerOnUI) Aiming = true;
            if (Aiming)
            {
                Aim(jumpHand);
                if (s.magnitude < 0.25f)
                {
                    Aiming = false;
                    arc.positionCount = 0;
                    reticle.gameObject.SetActive(false);
                    if (valid)
                    {
                        var t = target;
                        var facing = Vector3.ProjectOnPlane(xr.Head.forward, Vector3.up);
                        FadeThrough(() => { xr.Place(t, facing); Teleports++; Teleported?.Invoke(t); });
                    }
                }
            }
            if (StickWalk) Walk(right.Stick, dt);
        }

        /// <summary>Walks where you look: forward and back, and sideways, sliding along what is in the way.</summary>
        void Walk(Vector2 stick, float dt)
        {
            float m = stick.magnitude;
            if (m < 0.15f) return;
            // A light push walks slowly, a full one at walking pace.
            var push = stick / m * Mathf.Pow(Mathf.Clamp01((m - 0.15f) / 0.85f), 1.4f);
            var fwd = Vector3.ProjectOnPlane(xr.Head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(xr.Head.up, Vector3.up);
            fwd.Normalize();
            var side = Vector3.Cross(Vector3.up, fwd);
            var move = (fwd * push.y + side * push.x) * (MoveSpeed * dt);
            var body = new Vector3(xr.Head.position.x, xr.Origin.position.y, xr.Head.position.z);
            var to = Clamp(Slide(body, move));
            xr.Origin.position += to - body;
            Walked += (to - body).magnitude;
            Comfort = Mathf.Max(Comfort, 0.55f * push.magnitude);
        }

        const int WalkMask = Layers.EnvironmentMask | Layers.RobotMask;

        /// <summary>A body 0.22 m round, from the knees to the chest, moved until it meets something and then along it.</summary>
        static Vector3 Slide(Vector3 from, Vector3 move)
        {
            for (int pass = 0; pass < 3 && move.sqrMagnitude > 1e-10f; pass++)
            {
                float dist = move.magnitude;
                var dir = move / dist;
                if (!Physics.CapsuleCast(from + Vector3.up * 0.45f, from + Vector3.up * 1.3f, 0.22f, dir, out var hit, dist + 0.02f, WalkMask, QueryTriggerInteraction.Ignore))
                    return from + move;
                float free = Mathf.Max(0, hit.distance - 0.02f);
                from += dir * free;
                var n = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (n.sqrMagnitude < 1e-6f) break;
                move = Vector3.ProjectOnPlane(dir * (dist - free), n.normalized);
            }
            return from;
        }

        Vector3 Clamp(Vector3 p) => new(Mathf.Clamp(p.x, Bounds.xMin, Bounds.xMax), p.y, Mathf.Clamp(p.z, Bounds.yMin, Bounds.yMax));

        public void Cancel()
        {
            if (!Aiming) return;
            Aiming = false;
            arc.positionCount = 0;
            reticle.gameObject.SetActive(false);
        }

        void Aim(Hand hand)
        {
            var points = new List<Vector3>();
            Vector3 p = hand.Aim.position;
            Vector3 v = hand.Aim.forward * 7.5f;
            valid = false;
            points.Add(p);
            for (int i = 0; i < 70; i++)
            {
                const float step = 0.025f;
                Vector3 next = p + v * step;
                v += Physics.gravity * step;
                var seg = next - p;
                int n = Physics.RaycastNonAlloc(p, seg.normalized, hits, seg.magnitude, Layers.TeleportMask | Layers.EnvironmentMask | Layers.RobotMask, QueryTriggerInteraction.Ignore);
                if (n > 0)
                {
                    RaycastHit best = hits[0];
                    for (int k = 1; k < n; k++) if (hits[k].distance < best.distance) best = hits[k];
                    points.Add(best.point);
                    bool floor = best.collider.gameObject.layer == Layers.Teleport && best.normal.y > 0.7f;
                    var c = Clamp(best.point);
                    valid = floor && Mathf.Abs(c.x - best.point.x) < 0.01f && Mathf.Abs(c.z - best.point.z) < 0.01f;
                    target = best.point;
                    break;
                }
                points.Add(next);
                p = next;
                if (p.y < -1) break;
            }
            arc.positionCount = points.Count;
            arc.SetPositions(points.ToArray());
            var col = valid ? new Color(1f, 0.55f, 0.3f, 0.95f) : new Color(1f, 0.3f, 0.3f, 0.55f);
            arc.startColor = new Color(col.r, col.g, col.b, 0.1f);
            arc.endColor = col;
            reticle.gameObject.SetActive(valid);
            if (valid) reticle.position = target + Vector3.up * 0.01f;
        }
    }
}
