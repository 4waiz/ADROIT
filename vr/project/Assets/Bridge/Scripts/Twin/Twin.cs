// SPDX-License-Identifier: LicenseRef-BRIDGE
// The cell's digital twin, standing beside it on its own stage: a translucent copy of the Panda and
// its bench that follows the real one (a moment late, as data from a controller arrives), with the
// parts where the real parts are. Its dashboard shows what a twin is for: cycles and cycle time,
// energy, OEE, motor temperatures and power over time, and a prediction from the temperature trend.
// It can also run a program ahead of the real cell (virtual commissioning) and say where the path
// would collide, before anything moves on the real robot.

using System;
using System.Collections.Generic;
using Bridge.App;
using Bridge.Robot;
using Bridge.UI;
using Bridge.World;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bridge.Twin
{
    public class DigitalTwin
    {
        readonly BridgeApp app;
        public readonly Transform Root;
        public readonly RobotArm Ghost;
        public readonly Cell Virtual;
        public readonly Dashboard Board;
        public float Latency = 0.12f;
        public bool Previewing { get; private set; }
        public string PreviewResult = "";
        public bool PreviewOk;
        public bool PreviewDone;
        public int PreviewRuns;
        public Action<bool, string> PreviewFinished;
        readonly Queue<(float t, float[] q, float open)> buffer = new();
        readonly List<(Graspable real, Transform ghost)> parts = new();
        readonly Material holo, holoAlert;
        readonly Vector3 offset;
        readonly List<Transform> pulses = new();
        readonly Vector3 linkFrom, linkTo, linkMid;
        float clock;
        float previewScale = 2f;
        Transform fixtureGhost;
        readonly TextMeshPro tag;

        public DigitalTwin(BridgeApp app)
        {
            this.app = app;
            Root = new GameObject("Digital twin").transform;
            Root.SetParent(app.WorldRoot, false);
            offset = CellLayout.TwinBase - CellLayout.RobotBase;
            holo = Mats.Hologram(new Color(0.25f, 0.75f, 1.4f), 1.4f, 0.1f);
            holoAlert = Mats.Hologram(new Color(1.6f, 0.25f, 0.2f), 1.8f, 0.25f);

            // The stage: a glowing disc, the twin's bench as an outline, the floor lettering.
            var stage = new Vector3(CellLayout.TwinBase.x + 0.3f, 0, CellLayout.TwinBase.z);
            Go.Mesh("stage", Root, MeshKit.Cylinder(1.25f, 0.04f, 64), Mats.Paint(0x1b1f24, 0.35f, 0.4f), stage + Vector3.up * 0.02f, cast: false);
            Go.Mesh("stage ring", Root, MeshKit.Cylinder(1.27f, 0.012f, 64, 1.27f), Mats.Glow(new Color(0.25f, 0.7f, 1f), 2.5f), stage + Vector3.up * 0.036f, cast: false);
            var floorText = Go.FloorDecal("DIGITAL TWIN", Root, Mats.Decal("floor_twin", 0.85f), 2.0f, 0.5f, 0, 0, 0, 0.045f);
            floorText.transform.position = stage + new Vector3(0, 0.045f, -1.0f);
            var bench = CellLayout.BenchCentre + offset;
            Go.Mesh("twin bench", Root, MeshKit.Box(), holo, new Vector3(bench.x, CellLayout.BenchTop / 2 + 0.04f, bench.z), null, new Vector3(CellLayout.BenchSize.x, CellLayout.BenchTop - 0.08f, CellLayout.BenchSize.z), cast: false);
            Go.Mesh("twin pedestal", Root, MeshKit.Box(), holo, new Vector3(CellLayout.TwinBase.x, CellLayout.TwinBase.y / 2, CellLayout.TwinBase.z), null, new Vector3(0.2f, CellLayout.TwinBase.y - 0.01f, 0.2f), cast: false);
            var zone = CellLayout.PlaceZone + offset;
            Go.Mesh("twin zone", Root, MeshKit.FloorQuad(), Mats.Transparent(new Color(0.3f, 0.9f, 1f, 0.35f)), zone + Vector3.up * 0.002f, null, new Vector3(0.08f, 1, 0.08f), cast: false);

            Ghost = new RobotArm(Root, "twin panda", CellLayout.TwinBase, Quaternion.identity, holo, colliders: false);
            Virtual = new Cell(Ghost) { CollisionArm = app.Panda, HumanPresent = false };
            Virtual.Safety.ForceLimiting = false;

            tag = Bridge.UI.Text.Make(Root, "DIGITAL TWIN", 0.32f, new Color(0.55f, 0.85f, 1f), TextAlignmentOptions.Center, 1.2f, 0.06f, "Figtree-ExtraBold", false);
            tag.transform.position = CellLayout.TwinBase + new Vector3(0, 0.95f, 0);

            // The data link from the cell's controller (the cabinet) to the twin.
            linkFrom = Web.P(CellLayout.Bx0 - 0.57f, CellLayout.By0 + 0.63f, 2.35f);
            linkTo = CellLayout.TwinBase + new Vector3(0, 0.75f, 0);
            linkMid = (linkFrom + linkTo) / 2 + Vector3.up * 2.2f;
            var pulseMat = Mats.Additive(new Color(0.35f, 0.85f, 1.6f, 1));
            for (int i = 0; i < 14; i++) pulses.Add(Go.Mesh("pulse", Root, MeshKit.Sphere(10), pulseMat, Vector3.zero, null, Vector3.one * 0.035f, cast: false).transform);

            Board = new Dashboard(app, this);
        }

        /// <summary>Puts a ghost beside every real part, so the twin shows the parts too.</summary>
        public void TrackPart(Graspable g)
        {
            var mesh = g.GetComponentInChildren<MeshFilter>().sharedMesh;
            var t = Go.Mesh("twin " + g.Id, Root, mesh, holo, Vector3.zero, null, g.transform.lossyScale, cast: false).transform;
            parts.Add((g, t));
        }

        public void SetFixture(Transform real)
        {
            if (fixtureGhost != null) UnityEngine.Object.Destroy(fixtureGhost.gameObject);
            fixtureGhost = null;
            if (real == null) return;
            fixtureGhost = Go.Mesh("twin fixture", Root, MeshKit.Box(), holo, real.position + offset, real.rotation, real.lossyScale, cast: false).transform;
        }

        /// <summary>Runs a program on the twin only, faster than real time, checking for collisions.</summary>
        public void Preview(RobotProgram program, float speed = 2f)
        {
            Previewing = true;
            PreviewDone = false;
            PreviewOk = false;
            PreviewResult = "Simulating on the twin...";
            previewScale = speed;
            Virtual.Safety.Reset();
            Virtual.Safety.Stop = StopKind.None;
            Virtual.Arm.CopyFrom(app.Panda);
            Virtual.Program = program;
            Virtual.SpeedOverride = 1f;
            Virtual.StartProgram(false);
            foreach (var r in Ghost.AllRenderers()) r.sharedMaterial = holo;
            Sfx.Play("whoosh", 0.6f);
        }

        void EndPreview(bool ok, string message)
        {
            Previewing = false;
            PreviewDone = true;
            PreviewOk = ok;
            PreviewResult = message;
            PreviewRuns++;
            PreviewFinished?.Invoke(ok, message);
            Sfx.Play(ok ? "success" : "error");
        }

        public void Tick(float dt)
        {
            clock += dt;
            var real = app.Panda;
            buffer.Enqueue((clock, (float[])real.Q.Clone(), real.Opening));
            while (buffer.Count > 2 && buffer.Peek().t < clock - Latency - 0.5f) buffer.Dequeue();
            if (Previewing)
            {
                // The twin runs ahead on its own; a collision on its path ends the run.
                for (int k = 0; k < 3; k++) Virtual.Tick(dt * previewScale / 3f);
                if (Virtual.Safety.Stop != StopKind.None)
                {
                    string step = Virtual.StepIndex >= 0 && Virtual.StepIndex < Virtual.Program.Steps.Count ? Virtual.Program.Steps[Virtual.StepIndex].Label : "a move";
                    foreach (var r in Ghost.AllRenderers()) r.sharedMaterial = holoAlert;
                    Virtual.StopProgram("collision");
                    EndPreview(false, $"Collision predicted during \"{step}\": {Virtual.Safety.Reason.Replace("Contact: the path runs into ", "the path runs into the ")}.");
                }
                else if (!Virtual.Running)
                {
                    EndPreview(true, "The program ran on the twin with no collisions. It is safe to deploy.");
                }
            }
            else
            {
                // Mirror the real arm as it was Latency seconds ago.
                (float t, float[] q, float open) sample = default;
                bool found = false;
                foreach (var s in buffer)
                {
                    if (s.t > clock - Latency) break;
                    sample = s;
                    found = true;
                }
                if (found)
                {
                    Array.Copy(sample.q, Ghost.Q, RobotArm.Dof);
                    Ghost.Opening = sample.open;
                    Ghost.Apply();
                }
            }
            foreach (var (g, t) in parts)
            {
                bool on = g.gameObject.activeInHierarchy;
                t.gameObject.SetActive(on);
                if (!on) continue;
                t.position = g.transform.position + offset;
                t.rotation = g.transform.rotation;
            }
            // Data pulses along the link.
            for (int i = 0; i < pulses.Count; i++)
            {
                float u = (clock * 0.45f + i / (float)pulses.Count) % 1f;
                var p = Bezier(linkFrom, linkMid, linkTo, u);
                pulses[i].position = p;
                pulses[i].localScale = Vector3.one * (0.02f + 0.02f * Mathf.Sin(u * Mathf.PI));
            }
            var head = Bridge.UI.Rig.Head;
            if (head != null)
            {
                var d = tag.transform.position - head.position;
                d.y = 0;
                if (d.sqrMagnitude > 0.01f) tag.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
            Board.Tick(dt);
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

        public void SetLinkVisible(bool on)
        {
            foreach (var p in pulses) p.gameObject.SetActive(on);
        }
    }

    // ====================================================================== charts
    /// <summary>A small line chart on a glass card: one or more series from ring buffers.</summary>
    public class Chart
    {
        readonly Transform root;
        readonly List<(Ring data, LineRenderer line)> series = new();
        readonly Vector2 size;
        public float Min, Max;
        readonly TextMeshPro title, top, bottom;
        readonly LineRenderer limit;
        public float? LimitValue;

        public Chart(Transform parent, string name, Vector2 size, Vector3 pos, float min, float max)
        {
            this.size = size;
            Min = min;
            Max = max;
            root = new GameObject("chart " + name).transform;
            root.gameObject.layer = Layers.UI;
            root.SetParent(parent, false);
            root.localPosition = pos;
            Pane.Make(root, "back", size, new Color(1, 1, 1, 0.42f), 0.012f, blur: false);
            title = Bridge.UI.Text.Make(root, name, UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.TopLeft, size.x - 0.02f, 0.02f, "Figtree-Bold", false, new Vector3(0, size.y / 2 - 0.014f, 0));
            top = Bridge.UI.Text.Make(root, "", UIStyle.Tiny * 0.9f, Pal.InkFaint, TextAlignmentOptions.TopRight, size.x - 0.02f, 0.02f, "IBMPlexMono-Regular", false, new Vector3(0, size.y / 2 - 0.014f, 0));
            bottom = Bridge.UI.Text.Make(root, "", UIStyle.Tiny * 0.9f, Pal.InkFaint, TextAlignmentOptions.BottomRight, size.x - 0.02f, 0.02f, "IBMPlexMono-Regular", false, new Vector3(0, -size.y / 2 + 0.012f, 0));
            limit = Line(new Color(0.9f, 0.22f, 0.23f, 0.85f), 0.0018f);
            limit.positionCount = 2;
            limit.enabled = false;
        }

        LineRenderer Line(Color c, float width)
        {
            var go = new GameObject("line");
            go.layer = Layers.UI;
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0, 0, -UIStyle.Lift * 2);
            var l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = false;
            l.widthMultiplier = width;
            l.alignment = LineAlignment.TransformZ;
            var m = Mats.Clone("UnlitTransparent");
            m.SetFloat("_Fog", 0);
            m.renderQueue = (int)RenderQueue.Transparent + 2;
            l.sharedMaterial = m;
            l.startColor = l.endColor = c;
            l.shadowCastingMode = ShadowCastingMode.Off;
            l.receiveShadows = false;
            return l;
        }

        public void Add(Ring data, Color c) => series.Add((data, Line(c, 0.0022f)));

        public void Tick(string unit)
        {
            float w = size.x - 0.03f, h = size.y - 0.05f;
            float y0 = -size.y / 2 + 0.02f;
            foreach (var (data, line) in series)
            {
                int n = data.Count;
                line.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float x = -w / 2 + w * i / Mathf.Max(1, data.Capacity - 1);
                    float y = y0 + h * Mathf.InverseLerp(Min, Max, data[i]);
                    line.SetPosition(i, new Vector3(x, Mathf.Clamp(y, y0, y0 + h), 0));
                }
            }
            if (LimitValue.HasValue)
            {
                limit.enabled = true;
                float y = y0 + h * Mathf.InverseLerp(Min, Max, LimitValue.Value);
                limit.SetPosition(0, new Vector3(-w / 2, y, 0));
                limit.SetPosition(1, new Vector3(w / 2, y, 0));
            }
            top.text = $"{Max:0}{unit}";
            bottom.text = $"{Min:0}{unit}";
        }
    }

    // ====================================================================== dashboard
    /// <summary>The twin's dashboard: a large glass screen by the twin stage.</summary>
    public class Dashboard
    {
        readonly BridgeApp app;
        readonly DigitalTwin twin;
        public readonly Window Window;
        readonly TextMeshPro live, cycles, cycleTime, energy, oee, prediction, commission;
        readonly Chart temps, power;
        public readonly Button Simulate, Fix, Deploy;
        public Action OnSimulate, OnFix, OnDeploy;
        float slow;
        static readonly Color[] JointColors =
        {
            Pal.Hex(0x2f7cf6), Pal.Hex(0x1fa463), Pal.Hex(0x8b5cf6), Pal.Hex(0xff4713), Pal.Hex(0xe8a200), Pal.Hex(0x0ea5e9), Pal.Hex(0x64748b),
        };

        public Dashboard(BridgeApp app, DigitalTwin twin)
        {
            this.app = app;
            this.twin = twin;
            Window = Window.Make(twin.Root, "twin dashboard", new Vector2(1.1f, 0.72f), "Digital twin  ·  Cell 01", "layers", "Live data (simulated)");
            var c = Window.Content;
            Window.transform.position = CellLayout.TwinBase + new Vector3(1.55f, 0.55f, -0.35f);
            Window.transform.rotation = Quaternion.LookRotation(Window.transform.position - (CellLayout.TwinSpot + Vector3.up * 1.6f), Vector3.up);
            live = Bridge.UI.Text.Make(c, "", UIStyle.Small, Pal.Ok, TextAlignmentOptions.Right, 0.4f, 0.03f, "Figtree-Bold", false, new Vector3(Window.Size.x / 2 - UIStyle.Pad - 0.2f, Window.Top - 0.065f, 0));
            float y = Window.Top - Window.Cursor;
            // KPI tiles.
            float tw = (Window.Width - 0.03f) / 4;
            TextMeshPro Tile(int i, string label, string icon)
            {
                var pos = new Vector3(-Window.Width / 2 + tw / 2 + i * (tw + 0.01f), y - 0.05f, 0);
                Pane.Make(c, "tile", new Vector2(tw, 0.1f), new Color(1, 1, 1, 0.5f), 0.016f, blur: false, pos: pos + new Vector3(0, 0, -0.001f));
                Icon.Make(c, icon, 0.024f, Pal.Brand, pos + new Vector3(-tw / 2 + 0.024f, 0.026f, 0));
                Bridge.UI.Text.Make(c, label, UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Left, tw - 0.06f, 0.02f, "Figtree-SemiBold", false, pos + new Vector3(0.012f, 0.026f, 0));
                return Bridge.UI.Text.Make(c, "-", UIStyle.Heading, Pal.Ink, TextAlignmentOptions.Left, tw - 0.03f, 0.04f, "Figtree-ExtraBold", false, pos + new Vector3(0, -0.015f, 0));
            }
            cycles = Tile(0, "Cycles", "repeat");
            cycleTime = Tile(1, "Cycle time", "timer");
            energy = Tile(2, "Energy per cycle", "zap");
            oee = Tile(3, "OEE", "gauge");
            float cy = y - 0.21f;
            float cw = (Window.Width - 0.01f) / 2;
            temps = new Chart(c, "Motor temperature by joint", new Vector2(cw, 0.17f), new Vector3(-cw / 2 - 0.005f, cy, 0), 20, 80) { LimitValue = 70 };
            for (int j = 0; j < RobotArm.Dof; j++) temps.Add(app.Cell.Telemetry.TempHistory[j], JointColors[j]);
            power = new Chart(c, "Electrical power", new Vector2(cw, 0.17f), new Vector3(cw / 2 + 0.005f, cy, 0), 0, 400);
            power.Add(app.Cell.Telemetry.PowerHistory, Pal.Brand);
            prediction = Bridge.UI.Text.Make(c, "", UIStyle.Small, Pal.InkSoft, TextAlignmentOptions.TopLeft, Window.Width, 0.05f, "Figtree-Medium", true, new Vector3(0, cy - 0.115f, 0));
            // Virtual commissioning.
            float by = -Window.Top + 0.06f;
            Simulate = Button.Make(c, "Simulate on twin", new Vector2(0.26f, 0.055f), () => OnSimulate?.Invoke(), ButtonStyle.Primary, "play", UIStyle.Small, new Vector3(-Window.Width / 2 + 0.13f, by, 0));
            Fix = Button.Make(c, "Raise clearance", new Vector2(0.24f, 0.055f), () => OnFix?.Invoke(), ButtonStyle.Secondary, "arrow-up", UIStyle.Small, new Vector3(-Window.Width / 2 + 0.39f, by, 0));
            Deploy = Button.Make(c, "Deploy to cell", new Vector2(0.24f, 0.055f), () => OnDeploy?.Invoke(), ButtonStyle.Success, "send", UIStyle.Small, new Vector3(-Window.Width / 2 + 0.64f, by, 0));
            commission = Bridge.UI.Text.Make(c, "", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Left, 0.26f, 0.06f, "Figtree-Medium", true, new Vector3(Window.Width / 2 - 0.13f, by, 0));
            ShowCommissioning(false);
        }

        public void ShowCommissioning(bool on)
        {
            Simulate.gameObject.SetActive(on);
            Fix.gameObject.SetActive(on);
            Deploy.gameObject.SetActive(on);
            commission.gameObject.SetActive(on);
        }

        public void Tick(float dt)
        {
            var cell = app.Cell;
            var tel = cell.Telemetry;
            live.text = twin.Previewing ? "<color=#2F7CF6>●</color> Running ahead of the cell" : $"<color=#1FA463>●</color> Live  ·  {twin.Latency * 1000:0} ms behind";
            slow += dt;
            if (slow < 0.25f) return;
            slow = 0;
            cycles.text = cell.Cycles.ToString();
            cycleTime.text = cell.LastCycleTime > 0 ? $"{cell.LastCycleTime:0.0} s" : "-";
            energy.text = tel.LastCycleEnergy > 0 ? $"{tel.LastCycleEnergy / 3600f:0.00} Wh" : $"{tel.TotalPower:0} W";
            tel.Oee(out float a, out float p, out float q);
            oee.text = $"{a * p * q * 100:0}%";
            temps.Tick("°");
            power.Tick(" W");
            // A prediction from the trend: how long until the hottest motor reaches its limit.
            int hot = 0;
            for (int j = 1; j < RobotArm.Dof; j++) if (tel.Temperature[j] > tel.Temperature[hot]) hot = j;
            var hist = tel.TempHistory[hot];
            string text = $"Hottest motor: J{hot + 1} at {tel.Temperature[hot]:0.0}°C (limit 70°C).";
            if (hist.Count > 40)
            {
                float slope = (hist[hist.Count - 1] - hist[hist.Count - 41]) / 10f;   // per second, over the last 10 s
                if (slope > 0.01f)
                {
                    float seconds = (70 - tel.Temperature[hot]) / slope;
                    float perCycle = cell.LastCycleTime > 0 ? cell.LastCycleTime : 10f;
                    text += seconds > 0 ? $"  Trend: rising {slope * 60:0.0}°C/min; the twin predicts the limit in about {Mathf.Max(1, seconds / perCycle):0} cycles." : "  At the limit.";
                }
                else text += "  Trend: steady.";
            }
            prediction.text = text;
            commission.text = twin.PreviewResult;
            commission.color = twin.PreviewDone ? (twin.PreviewOk ? Pal.Ok : Pal.Err) : Pal.InkSoft;
        }
    }
}
