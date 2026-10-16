// SPDX-License-Identifier: LicenseRef-BRIDGE
// Seeing what a robot controller knows: each joint's range drawn as an arc around it with a needle
// at its angle (red near the limits), the base and tool frames as red, green and blue axes (x, y, z
// as the Panda reports them), and the safety scanner's warning and protective fields on the floor.

using System.Collections.Generic;
using Bridge.UI;
using TMPro;
using UnityEngine;

namespace Bridge.Robot
{
    public class JointViz
    {
        readonly RobotArm arm;
        readonly List<Gauge> gauges = new();
        public bool ShowAll;
        public JogMode Mode;

        class Gauge
        {
            public Transform Frame, Needle;
            public GameObject Root;
            public TextMeshPro Label;
            public float Sign;
            public MeshRenderer NeedleRenderer;
            public float Shown;
        }

        public JointViz(Transform parent, RobotArm arm)
        {
            this.arm = arm;
            var d = RobotArm.Def;
            var range = Mats.Transparent(new Color(0.35f, 0.68f, 1f, 0.42f));
            var edge = Mats.Transparent(new Color(0.95f, 0.25f, 0.25f, 0.55f));
            var needle = Mats.Unlit(Pal.Brand * 1.4f);
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                int b = arm.JointBody[j];
                var body = d.bodies[b];
                var parentT = arm.Bodies[body.parent];
                var axis = new Vector3(body.joint.axis[0], body.joint.axis[1], body.joint.axis[2]);
                var rest = new Quaternion(body.rot[0], body.rot[1], body.rot[2], body.rot[3]);
                var g = new Gauge { Sign = body.joint.sign };
                g.Root = new GameObject("gauge J" + (j + 1));
                g.Root.layer = Layers.UI;
                g.Frame = g.Root.transform;
                g.Frame.SetParent(parentT, false);
                g.Frame.localPosition = new Vector3(body.pos[0], body.pos[1], body.pos[2]);
                g.Frame.localRotation = rest * Quaternion.FromToRotation(Vector3.up, axis);
                float r0 = 0.105f, r1 = 0.125f;
                float lo = -g.Sign * arm.QMax[j], hi = -g.Sign * arm.QMin[j];
                if (lo > hi) (lo, hi) = (hi, lo);
                float band = (hi - lo) * 0.05f;
                Go.Mesh("range", g.Frame, MeshKit.Arc(r0, r1, lo + band, hi - band), Cull(range), Vector3.zero, null, null, cast: false, layer: Layers.UI);
                Go.Mesh("limit low", g.Frame, MeshKit.Arc(r0, r1, lo, lo + band, 8), Cull(edge), Vector3.zero, null, null, cast: false, layer: Layers.UI);
                Go.Mesh("limit high", g.Frame, MeshKit.Arc(r0, r1, hi - band, hi, 8), Cull(edge), Vector3.zero, null, null, cast: false, layer: Layers.UI);
                g.Needle = new GameObject("needle").transform;
                g.Needle.SetParent(g.Frame, false);
                var stick = Go.Mesh("stick", g.Needle, MeshKit.Box(), needle, new Vector3(0.072f, 0, 0), null, new Vector3(0.068f, 0.004f, 0.006f), cast: false, layer: Layers.UI);
                g.NeedleRenderer = stick.GetComponent<MeshRenderer>();
                Go.Mesh("tip", g.Needle, MeshKit.Sphere(10), needle, new Vector3(0.115f, 0, 0), null, Vector3.one * 0.014f, cast: false, layer: Layers.UI);
                g.Label = Bridge.UI.Text.Make(g.Frame, "", 0.16f, Color.white, TextAlignmentOptions.Center, 0.2f, 0.04f, "Figtree-Bold", false);
                // The material first: the outline is set on (a copy of) it, and readable on light walls and a dark floor.
                g.Label.fontSharedMaterial = UIStyle.TextMaterial(g.Label.font, onTop: false);
                g.Label.outlineWidth = 0.18f;
                g.Label.outlineColor = new Color32(20, 24, 28, 255);
                g.Root.SetActive(false);
                gauges.Add(g);
            }
        }

        static readonly Dictionary<Material, Material> twoSided = new();

        static Material Cull(Material m)
        {
            if (twoSided.TryGetValue(m, out var c)) return c;
            c = new Material(m);
            c.SetFloat("_Cull", 0);
            twoSided[m] = c;
            return c;
        }

        public void Tick(float dt, Cell cell, bool driving)
        {
            for (int j = 0; j < gauges.Count; j++)
            {
                var g = gauges[j];
                bool show = ShowAll || (driving && cell.Mode == JogMode.Joint && cell.SelectedJoint == j);
                g.Shown = Mathf.MoveTowards(g.Shown, show ? 1 : 0, dt / 0.2f);
                bool on = g.Shown > 0.001f;
                if (g.Root.activeSelf != on) g.Root.SetActive(on);
                if (!on) continue;
                g.Frame.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Ease.OutCubic(g.Shown));
                float q = arm.Q[j];
                g.Needle.localRotation = Quaternion.AngleAxis(g.Sign * q * Mathf.Rad2Deg, Vector3.up);
                float t = Mathf.InverseLerp(arm.QMin[j], arm.QMax[j], q);
                bool edge = t < 0.05f || t > 0.95f;
                g.NeedleRenderer.sharedMaterial = edge ? Mats.Unlit(Pal.Err * 1.5f) : Mats.Unlit(Pal.Brand * 1.4f);
                g.Label.text = $"J{j + 1}  {q * Mathf.Rad2Deg:0}°";
                var head = Bridge.UI.Rig.Head;
                if (head != null)
                {
                    var tip = g.Needle.TransformPoint(new Vector3(0.16f, 0, 0));
                    g.Label.transform.position = tip + Vector3.up * 0.02f;
                    g.Label.transform.rotation = Quaternion.LookRotation(g.Label.transform.position - head.position, Vector3.up);
                }
            }
        }
    }

    /// <summary>Red, green and blue axes for the base frame and the tool frame.</summary>
    public class AxesViz
    {
        readonly Transform baseAxes, toolAxes;
        bool showBase, showTool;

        public AxesViz(Transform parent)
        {
            baseAxes = Make(parent, "base frame", 0.22f);
            toolAxes = Make(parent, "tool frame", 0.11f);
            baseAxes.gameObject.SetActive(false);
            toolAxes.gameObject.SetActive(false);
        }

        static Transform Make(Transform parent, string name, float length)
        {
            var root = new GameObject(name).transform;
            root.gameObject.layer = Layers.UI;
            root.SetParent(parent, false);
            // MuJoCo's x, y, z of the frame are Unity's local x, z, y.
            Axis(root, Vector3.right, Pal.AxisX, "X", length);
            Axis(root, Vector3.forward, Pal.AxisY, "Y", length);
            Axis(root, Vector3.up, Pal.AxisZ, "Z", length);
            Go.Mesh("origin", root, MeshKit.Sphere(12), Mats.Unlit(Color.white), Vector3.zero, null, Vector3.one * length * 0.08f, cast: false, layer: Layers.UI);
            return root;
        }

        static void Axis(Transform root, Vector3 dir, Color c, string letter, float length)
        {
            var m = Mats.Unlit(c * 1.3f);
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            float r = length * 0.035f;
            Go.Mesh(letter, root, MeshKit.Cylinder(r, length * 0.82f, 12), m, dir * length * 0.41f, rot, null, cast: false, layer: Layers.UI);
            Go.Mesh(letter + " head", root, MeshKit.Cone(r * 2.6f, length * 0.18f, 16), m, dir * length * 0.82f, rot, null, cast: false, layer: Layers.UI);
            var t = Bridge.UI.Text.Make(root, letter, 0.25f + length * 0.6f, c, TextAlignmentOptions.Center, 0.1f, 0.05f, "Figtree-ExtraBold", false);
            t.transform.localPosition = dir * (length + 0.03f);
            t.gameObject.AddComponent<Billboard>();
        }

        public void Show(bool baseFrame, bool toolFrame)
        {
            showBase = baseFrame;
            showTool = toolFrame;
            baseAxes.gameObject.SetActive(baseFrame);
            toolAxes.gameObject.SetActive(toolFrame);
        }

        public void Tick(RobotArm arm, Cell cell)
        {
            if (showBase)
            {
                baseAxes.position = arm.Root.transform.position + Vector3.up * 0.005f;
                baseAxes.rotation = arm.Root.transform.rotation;
            }
            if (showTool)
            {
                var frame = cell.Frame == JogFrame.Tool || !showBase ? arm.Tcp : arm.Tcp;
                toolAxes.position = frame.position;
                toolAxes.rotation = frame.rotation;
            }
        }
    }

    /// <summary>Turns a label to face the head.</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var head = Bridge.UI.Rig.Head;
            if (head == null) return;
            var d = transform.position - head.position;
            if (d.sqrMagnitude > 1e-5f) transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }

    /// <summary>The floor scanner's fields: amber (warning) and red (protective), drawn round the robot.</summary>
    public class SafetyZones
    {
        readonly Transform root;
        readonly Material warn, stop;
        readonly Safety safety;
        bool visible;
        float shown;
        readonly TextMeshPro warnText, stopText;

        public SafetyZones(Transform parent, Vector3 centre, Safety safety)
        {
            this.safety = safety;
            root = new GameObject("scanner fields").transform;
            root.SetParent(parent, false);
            root.position = new Vector3(centre.x, 0.006f, centre.z);
            warn = Mats.Clone("Zone");
            warn.SetColor("_Color", new Color(1.2f, 0.75f, 0.05f, 1));
            warn.SetFloat("_Inner", safety.ProtectiveRadius / safety.WarningRadius);
            warn.SetFloat("_Fill", 0.14f);
            warn.SetFloat("_EdgeWidth", 0.015f);
            warn.SetFloat("_Dashes", 90);
            stop = Mats.Clone("Zone");
            stop.SetColor("_Color", new Color(1.3f, 0.18f, 0.15f, 1));
            stop.SetFloat("_Inner", 0f);
            stop.SetFloat("_Fill", 0.16f);
            stop.SetFloat("_EdgeWidth", 0.02f);
            stop.SetFloat("_Dashes", 60);
            Go.Mesh("warning field", root, MeshKit.FloorQuad(), warn, Vector3.zero, null, new Vector3(safety.WarningRadius * 2, 1, safety.WarningRadius * 2), cast: false, layer: Layers.UI);
            Go.Mesh("protective field", root, MeshKit.FloorQuad(), stop, Vector3.up * 0.001f, null, new Vector3(safety.ProtectiveRadius * 2, 1, safety.ProtectiveRadius * 2), cast: false, layer: Layers.UI);
            warnText = Label("WARNING FIELD", new Color(1f, 0.75f, 0.1f), (safety.ProtectiveRadius + safety.WarningRadius) / 2);
            stopText = Label("PROTECTIVE FIELD", new Color(1f, 0.3f, 0.25f), safety.ProtectiveRadius * 0.55f);
            root.gameObject.SetActive(false);
        }

        TextMeshPro Label(string text, Color c, float radius)
        {
            var t = Bridge.UI.Text.Make(root, text, 0.55f, c, TextAlignmentOptions.Center, 1.2f, 0.1f, "Figtree-ExtraBold", false);
            t.transform.localPosition = new Vector3(radius, 0.003f, 0);
            t.transform.localRotation = Quaternion.Euler(90, -90, 0);
            return t;
        }

        public void Show(bool on) => visible = on;

        public void Tick(float dt, Vector3 head)
        {
            shown = Mathf.MoveTowards(shown, visible ? 1 : 0, dt / 0.4f);
            bool on = shown > 0.001f;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            warn.SetFloat("_Pulse", safety.Zone == "warning" ? 0.5f : 0f);
            stop.SetFloat("_Pulse", safety.Zone == "protective" ? 0.6f : 0f);
            var c = warn.GetColor("_Color");
            warn.SetColor("_Color", new Color(c.r, c.g, c.b, shown));
            var s = stop.GetColor("_Color");
            stop.SetColor("_Color", new Color(s.r, s.g, s.b, shown));
            // The lettering turns to face the person so it reads from where they stand.
            var d = new Vector3(head.x - root.position.x, 0, head.z - root.position.z);
            if (d.sqrMagnitude > 0.01f)
            {
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                warnText.transform.localPosition = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0.003f, (safety.ProtectiveRadius + safety.WarningRadius) / 2);
                warnText.transform.localRotation = Quaternion.Euler(90, yaw + 180, 0);
                stopText.transform.localPosition = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0.003f, safety.ProtectiveRadius * 0.6f);
                stopText.transform.localRotation = Quaternion.Euler(90, yaw + 180, 0);
            }
        }
    }
}
