// SPDX-License-Identifier: LicenseRef-BRIDGE
// The app's screens: the home menu, the Academy's module list, settings and credits, the toasts that
// say what just happened, and the teach pendant you hold in your left hand.

using System;
using System.Collections.Generic;
using Bridge.App;
using Bridge.Robot;
using TMPro;
using UnityEngine;

namespace Bridge.UI
{
    // ====================================================================== menus
    public class Menus
    {
        readonly BridgeApp app;
        public Window Home, Academy, Settings, About;
        Window open;
        /// <summary>Whether a menu screen is up.</summary>
        public bool IsOpen => open != null;
        public bool AnyOpen => open != null && open.Visible;
        TextMeshPro status, progressText;
        Bar progress;

        public const string Title = "BRIDGE's Next-Gen Factory Digital Twin Simulation for Robotics";
        public const string Product = "Next-Generation Industry 5.0 Robotic Arm Digital Twin Simulation";
        public const string Author = "by Awaiz Ahmed";

        public Menus(BridgeApp app)
        {
            this.app = app;
            BuildHome();
            BuildAcademy();
            BuildSettings();
            BuildAbout();
            foreach (var w in new[] { Home, Academy, Settings, About }) w.Show(false, true);
            foreach (var w in new[] { Home, Academy, Settings, About }) w.gameObject.SetActive(false);
        }

        Transform Root => app.UIRoot;

        void BuildHome()
        {
            Home = Window.Make(Root, "home", new Vector2(1.02f, 0.66f));
            var c = Home.Content;
            float top = Home.Top;
            // The wordmark, then the product's title as the person who made it names it.
            var mark = Gen.Tex("wordmark_graphite");
            float mh = 0.05f, mw = mark != null ? mh * mark.width / mark.height : 0.2f;
            var logo = Go.Mesh("wordmark", c, MeshKit.Quad(), WordmarkMaterial(), new Vector3(-Home.Size.x / 2 + UIStyle.Pad + mw / 2, top - 0.055f, -0.003f), null, new Vector3(mw, mh, 1), cast: false, layer: Layers.UI);
            var kick = Text.Make(c, "DIGITAL TWIN  ·  VIRTUAL REALITY", UIStyle.Tiny, Pal.Brand, TextAlignmentOptions.Right, 0.5f, 0.03f, "Figtree-Bold", false, new Vector3(Home.Size.x / 2 - UIStyle.Pad - 0.25f, top - 0.055f, 0));
            kick.characterSpacing = 8;
            Text.Make(c, Title, UIStyle.Display * 0.82f, Pal.Ink, TextAlignmentOptions.TopLeft, Home.Size.x - UIStyle.Pad * 2, 0.14f, "Figtree-ExtraBold", true, new Vector3(0, top - 0.165f, 0));
            Text.Make(c, Product + "  ·  " + Author, UIStyle.Small, Pal.InkSoft, TextAlignmentOptions.TopLeft, Home.Size.x - UIStyle.Pad * 2, 0.03f, "Figtree-Medium", true, new Vector3(0, top - 0.262f, 0));

            // Four doors.
            var cards = new (string title, string sub, string icon, Action go, ButtonStyle style)[]
            {
                ("Robotics Academy", "10 hands-on modules", "graduation-cap", () => Open(Academy), ButtonStyle.Primary),
                ("Free Play", "Drive the cobot freely", "gamepad-2", () => { Close(); app.StartFreePlay(); }, ButtonStyle.Secondary),
                ("Factory Tour", "Walk the digital factory", "map", () => { Close(); app.Tour.Start(); }, ButtonStyle.Secondary),
                ("Settings", "Comfort and controls", "settings", () => Open(Settings), ButtonStyle.Secondary),
            };
            float cw = 0.225f, ch = 0.2f, gap = 0.016f;
            float x0 = -(cw * 4 + gap * 3) / 2 + cw / 2;
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                var b = Button.Make(c, "", new Vector2(cw, ch), card.go, card.style, null, UIStyle.Body, new Vector3(x0 + i * (cw + gap), top - 0.43f, 0));
                bool primary = card.style == ButtonStyle.Primary;
                var ink = primary ? Color.white : Pal.Ink;
                Icon.Make(b.transform, card.icon, 0.05f, primary ? Color.white : Pal.Brand, new Vector3(-cw / 2 + 0.045f, ch / 2 - 0.05f, 0));
                var t = Text.Make(b.transform, card.title, UIStyle.Body * 1.08f, ink, TextAlignmentOptions.BottomLeft, cw - 0.04f, 0.06f, "Figtree-Bold", true, new Vector3(0, -0.02f, 0));
                var s = Text.Make(b.transform, card.sub, UIStyle.Tiny, primary ? new Color(1, 1, 1, 0.85f) : Pal.InkSoft, TextAlignmentOptions.TopLeft, cw - 0.04f, 0.04f, "Figtree-Medium", true, new Vector3(0, -0.068f, 0));
                b.Label = t;
                b.Sub = s;
            }

            // Progress through the Academy, and what the headset is doing.
            progress = Bar.Make(c, 0.3f, 0.01f, Pal.Brand, new Vector3(-Home.Size.x / 2 + UIStyle.Pad + 0.15f, -Home.Top + 0.06f, 0));
            progressText = Text.Make(c, "", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Left, 0.4f, 0.03f, "Figtree-SemiBold", false, new Vector3(-Home.Size.x / 2 + UIStyle.Pad + 0.2f, -Home.Top + 0.088f, 0));
            status = Text.Make(c, "", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Right, 0.6f, 0.05f, "Figtree-Medium", true, new Vector3(Home.Size.x / 2 - UIStyle.Pad - 0.3f, -Home.Top + 0.07f, 0));
        }

        static Material wordmark;
        static Material WordmarkMaterial()
        {
            if (wordmark != null) return wordmark;
            wordmark = new Material(Gen.Template("UnlitTransparent"));
            wordmark.SetTexture("_BaseMap", Gen.Tex("wordmark_graphite"));
            wordmark.SetFloat("_Fog", 0);
            wordmark.renderQueue = 3002;
            return wordmark;
        }

        public static Material Wordmark => WordmarkMaterial();

        readonly List<(Button button, Icon done)> lessonCards = new();
        Button certificateButton;

        void BuildAcademy()
        {
            Academy = Window.Make(Root, "academy", new Vector2(1.0f, 0.78f), "Robotics Academy", "graduation-cap", "Ten modules, about 70 minutes");
            var c = Academy.Content;
            var lessons = app.Lessons.Curriculum;
            float cw = 0.455f, ch = 0.094f, gap = 0.012f;
            float startY = Academy.Top - Academy.Cursor - ch / 2 - 0.006f;
            for (int i = 0; i < lessons.Count; i++)
            {
                var lesson = lessons[i];
                int col = i % 2, row = i / 2;
                var pos = new Vector3((col == 0 ? -1 : 1) * (cw / 2 + gap / 2), startY - row * (ch + gap), 0);
                int index = i;
                var b = Button.Make(c, $"{i + 1}. {lesson.Title}", new Vector2(cw, ch), () => { Close(); app.Lessons.Start(index); }, ButtonStyle.Secondary, lesson.Icon, UIStyle.Body * 0.95f, pos, $"{lesson.Minutes} min  ·  {lesson.Summary}", TextAlignmentOptions.Left);
                var done = Icon.Make(b.transform, "circle-check", 0.03f, Pal.Ok, new Vector3(cw / 2 - 0.026f, ch / 2 - 0.024f, -0.001f));
                done.gameObject.SetActive(false);
                lessonCards.Add((b, done));
            }
            float by = startY - 5 * (ch + gap) + ch / 2 - 0.04f;
            Button.Make(c, "Back", new Vector2(0.16f, 0.055f), () => Open(Home), ButtonStyle.Ghost, "arrow-left", UIStyle.Small, new Vector3(-Academy.Size.x / 2 + UIStyle.Pad + 0.08f, by, 0));
            Button.Make(c, "Reset progress", new Vector2(0.22f, 0.055f), () => { app.Progress.ResetAll(); Refresh(); }, ButtonStyle.Ghost, "rotate-ccw", UIStyle.Small, new Vector3(Academy.Size.x / 2 - UIStyle.Pad - 0.11f, by, 0));
            // Shown once every module is complete.
            certificateButton = Button.Make(c, "Your certificate", new Vector2(0.26f, 0.055f), () => { Close(); app.Certificate.Show(); }, ButtonStyle.Primary, "award", UIStyle.Small, new Vector3(0, by, 0));
            certificateButton.gameObject.SetActive(false);
        }

        void BuildSettings()
        {
            Settings = Window.Make(Root, "settings", new Vector2(0.62f, 0.86f), "Settings", "settings", "Comfort and controls");
            var c = Settings.Content;
            var s = app.Settings;
            float w = Settings.Width;
            Toggle.Make(c, "Smooth movement (left stick)", w, s.SmoothMove, v => { s.SmoothMove = v; app.ApplySettings(); }, Settings.Row(0.05f));
            Toggle.Make(c, "Seated mode (raise the view)", w, s.Seated, v => { s.Seated = v; app.ApplySettings(); }, Settings.Row(0.05f));
            Toggle.Make(c, "Always show controller labels", w, s.AlwaysHints, v => { s.AlwaysHints = v; app.ApplySettings(); }, Settings.Row(0.05f));
            Toggle.Make(c, "Comfort vignette when moving", w, s.Vignette, v => { s.Vignette = v; app.ApplySettings(); }, Settings.Row(0.05f));
            Toggle.Make(c, "Hold the enabling switch to jog", w, s.RequireEnabling, v => { s.RequireEnabling = v; app.ApplySettings(); }, Settings.Row(0.05f));
            Settings.Row(0.004f);
            Slider.Make(c, "Snap turn", w, Mathf.InverseLerp(15, 90, s.SnapAngle), v => { s.SnapAngle = Mathf.Round(Mathf.Lerp(15, 90, v) / 15f) * 15f; app.ApplySettings(); }, v => $"{Mathf.Round(Mathf.Lerp(15, 90, v) / 15f) * 15f:0}°", Settings.Row(0.075f));
            Slider.Make(c, "Robot speed when you start", w, s.DefaultSpeed, v => { s.DefaultSpeed = Mathf.Max(0.05f, v); app.ApplySettings(); }, null, Settings.Row(0.075f));
            Slider.Make(c, "Volume", w, s.Volume, v => { s.Volume = v; app.ApplySettings(); }, null, Settings.Row(0.075f));
            Settings.Row(0.004f);
            float bw = (w - 0.02f) / 2;
            var row = Settings.Row(0.06f);
            Button.Make(c, "About and credits", new Vector2(bw, 0.06f), () => Open(About), ButtonStyle.Secondary, "info", UIStyle.Small, row + new Vector3(-bw / 2 - 0.01f, 0, 0));
            Button.Make(c, "Back", new Vector2(bw, 0.06f), () => Open(Home), ButtonStyle.Primary, "arrow-left", UIStyle.Small, row + new Vector3(bw / 2 + 0.01f, 0, 0));
            Settings.FitHeight();
        }

        void BuildAbout()
        {
            About = Window.Make(Root, "about", new Vector2(0.78f, 0.7f), "About", "info", Author);
            About.Paragraph($"<b>{Title}</b>\n{Product}, {Author}.", UIStyle.Small, Pal.Ink, "Figtree-Regular");
            About.Paragraph(
                "Built on the BRIDGE's Next-Generation Industry 5.0 Robotic Arm Digital Twin Simulation and RoboSimStudio (Apache 2.0).\n" +
                "Robot: Franka Emika Panda model from robosuite 1.4.0 (MIT), meshes from franka_description (Apache 2.0); link 5's lettering changed to read BRIDGE.\n" +
                "Factory photographs, props and HDRI: Poly Haven (CC0).\n" +
                "Typefaces: Figtree and IBM Plex Mono (SIL Open Font License). Icons: Lucide (ISC).\n" +
                "Readings in this app come from a simulation and are marked as simulated: no robot hardware is connected.",
                UIStyle.Tiny, Pal.InkSoft, "Figtree-Regular");
            Button.Make(About.Content, "Back", new Vector2(0.18f, 0.055f), () => Open(Settings), ButtonStyle.Primary, "arrow-left", UIStyle.Small, About.Row(0.055f));
            About.FitHeight();
        }

        public void Open(Window w)
        {
            if (open != null && open != w) open.Show(false);
            open = w;
            Refresh();
            w.gameObject.SetActive(true);
            w.Show(true);
            var head = Rig.Head;
            if (head != null) w.PlaceInFront(head, 1.05f, 0, 0.12f);
            Sfx.Play("open", 0.5f);
        }

        public void Close()
        {
            if (open != null) open.Show(false);
            open = null;
        }

        public void ToggleHome()
        {
            if (AnyOpen) Close();
            else Open(Home);
        }

        public void Refresh()
        {
            int done = 0;
            for (int i = 0; i < lessonCards.Count; i++)
            {
                bool d = app.Progress.Done(app.Lessons.Curriculum[i].Id);
                lessonCards[i].done.gameObject.SetActive(d);
                if (d) done++;
            }
            if (certificateButton != null) certificateButton.gameObject.SetActive(lessonCards.Count > 0 && done == lessonCards.Count);
            if (progress != null)
            {
                progress.Value = done / (float)Mathf.Max(1, lessonCards.Count);
                progressText.text = $"Academy: {done} of {lessonCards.Count} modules complete";
            }
        }

        public void Tick()
        {
            if (status != null && Home.Visible)
            {
                var xr = app.XR;
                status.text = xr.XRActive ? "<color=#1FA463>●</color> Meta Quest connected" : xr.XRStatus;
            }
        }
    }

    // ====================================================================== toasts
    /// <summary>Short notes near the top of your view: what just happened.</summary>
    public class Toasts
    {
        readonly Transform root;
        readonly List<(Transform t, Pane p, TextMeshPro text, Icon icon, float born, float life)> live = new();

        public Toasts(Transform parent)
        {
            root = new GameObject("toasts").transform;
            root.SetParent(parent, false);
        }

        public void Show(string text, string icon = "info", Color? tint = null, float life = 2.8f)
        {
            var go = new GameObject("toast");
            go.layer = Layers.UI;
            go.transform.SetParent(root, false);
            float w = Mathf.Clamp(Text.Approx(text, UIStyle.Small) + 0.09f, 0.18f, 0.6f);
            var p = Pane.Make(go.transform, "back", new Vector2(w, 0.05f), new Color(0.07f, 0.09f, 0.11f, 0.9f), 0.025f, blur: false);
            var i = Icon.Make(go.transform, icon, 0.026f, tint ?? Pal.BrandSoft, new Vector3(-w / 2 + 0.03f, 0, 0));
            var t = Text.Make(go.transform, text, UIStyle.Small, Color.white, TextAlignmentOptions.Left, w - 0.07f, 0.05f, "Figtree-SemiBold", false, new Vector3(0.018f, 0, 0));
            live.Insert(0, (go.transform, p, t, i, Time.unscaledTime, life));
            while (live.Count > 3)
            {
                UnityEngine.Object.Destroy(live[live.Count - 1].t.gameObject);
                live.RemoveAt(live.Count - 1);
            }
        }

        public void Tick(Transform head)
        {
            if (head == null) return;
            Vector3 fwd = head.forward;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            Vector3 anchor = head.position + flat * 0.95f + Vector3.up * 0.2f;
            root.position = Vector3.Lerp(root.position, anchor, 1 - Mathf.Exp(-6 * Time.unscaledDeltaTime));
            root.rotation = Quaternion.LookRotation(root.position - head.position, Vector3.up);
            for (int k = live.Count - 1; k >= 0; k--)
            {
                var e = live[k];
                float age = Time.unscaledTime - e.born;
                if (age > e.life)
                {
                    UnityEngine.Object.Destroy(e.t.gameObject);
                    live.RemoveAt(k);
                    continue;
                }
                float a = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01((e.life - age) / 0.3f);
                e.t.localPosition = Vector3.Lerp(e.t.localPosition, new Vector3(0, -k * 0.058f, 0), 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
                e.t.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, a);
                e.p.SetTint(new Color(0.07f, 0.09f, 0.11f, 0.9f * a));
                e.text.alpha = a;
            }
        }
    }

    // ====================================================================== the pendant
    /// <summary>
    /// The teach pendant, held in the left hand as a real cobot's tablet is: jog the arm, read its
    /// joints and tool position, teach and run programs, and see and reset its safety state.
    /// </summary>
    public class Pendant
    {
        readonly BridgeApp app;
        public readonly Window Window;
        public bool Visible { get; private set; }
        public string Tab = "jog";
        readonly List<Action> refresh = new();
        readonly Dictionary<string, Button> tabs = new();
        Transform page;
        const float W = 0.34f, H = 0.35f;

        public Pendant(BridgeApp app)
        {
            this.app = app;
            Window = Window.Make(app.UIRoot, "pendant", new Vector2(W, H), null, null, null, true);
            var c = Window.Content;
            // Header: the wordmark, the cell's state, and three tabs.
            var mark = Go.Mesh("wordmark", c, MeshKit.Quad(), Menus.Wordmark, new Vector3(-W / 2 + 0.05f, H / 2 - 0.022f, -0.003f), null, new Vector3(0.066f, 0.016f, 1), cast: false, layer: Layers.UI);
            var state = Text.Make(c, "", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Right, 0.2f, 0.02f, "Figtree-Bold", false, new Vector3(W / 2 - 0.11f, H / 2 - 0.022f, 0));
            refresh.Add(() => state.text = StateLine());
            string[] names = { "jog", "program", "safety" };
            string[] labels = { "Jog", "Program", "Safety" };
            string[] icons = { "move-3d", "list-ordered", "shield" };
            float tw = (W - 0.03f) / 3;
            for (int i = 0; i < 3; i++)
            {
                string n = names[i];
                tabs[n] = Button.Make(c, labels[i], new Vector2(tw - 0.006f, 0.032f), () => SetTab(n), ButtonStyle.Ghost, icons[i], UIStyle.Tiny, new Vector3(-W / 2 + 0.015f + tw / 2 + i * tw, H / 2 - 0.058f, 0));
            }
            Window.Show(false, true);
            Window.gameObject.SetActive(false);
            SetTab("jog");
        }

        string StateLine()
        {
            var cell = app.Cell;
            var s = cell.Safety;
            if (s.Stop == StopKind.Emergency) return "<color=#E5383B>E-STOP</color>";
            if (s.Stop == StopKind.Protective) return "<color=#E5383B>STOPPED</color>";
            if (s.Zone == "warning") return "<color=#E8A200>REDUCED SPEED</color>";
            if (cell.Running) return "<color=#1FA463>RUNNING</color>";
            return app.Interaction.Mode == ControlMode.Drive ? "<color=#2F7CF6>DRIVE</color>" : "READY";
        }

        public void Show(bool on)
        {
            Visible = on;
            if (on) Window.gameObject.SetActive(true);
            Window.Show(on);
        }

        public void SetTab(string tab)
        {
            Tab = tab;
            foreach (var kv in tabs) kv.Value.SetSelected(kv.Key == tab);
            if (page != null) UnityEngine.Object.Destroy(page.gameObject);
            refresh.RemoveRange(1, refresh.Count - 1);
            page = new GameObject("page").transform;
            page.gameObject.layer = Layers.UI;
            page.SetParent(Window.Content, false);
            if (tab == "jog") BuildJog();
            else if (tab == "program") BuildProgram();
            else BuildSafety();
        }

        /// <summary>A page row's height, measured down from just under the tabs.</summary>
        float Y(float fromTop) => H / 2 - 0.095f - fromTop;

        void BuildJog()
        {
            var cell = app.Cell;
            float x0 = -W / 2 + 0.015f;
            // Jog mode.
            string[] modes = { "Cartesian", "Joint", "Hand guide" };
            JogMode[] values = { JogMode.Cartesian, JogMode.Joint, JogMode.FreeDrive };
            float mw = (W - 0.03f) / 3;
            for (int i = 0; i < 3; i++)
            {
                var mode = values[i];
                var b = Button.Make(page, modes[i], new Vector2(mw - 0.006f, 0.03f), () => { cell.SetMode(mode); if (mode != JogMode.FreeDrive) app.Interaction.SetMode(ControlMode.Drive); }, ButtonStyle.Secondary, null, UIStyle.Tiny, new Vector3(x0 + mw / 2 + i * mw, Y(0.0f), 0));
                refresh.Add(() => b.SetSelected(cell.Mode == mode));
            }
            // Joints in two columns (J1 to J4, J5 to J7): a bar each and its value; the selected one in orange.
            float cw = (W - 0.03f) / 2;
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                int jj = j;
                float cx = x0 + (j / 4) * cw;
                float y = Y(0.038f + (j % 4) * 0.02f);
                var label = Text.Make(page, $"J{j + 1}", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Left, 0.024f, 0.02f, "Figtree-Bold", false, new Vector3(cx + 0.012f, y, 0));
                var bar = Bar.Make(page, 0.062f, 0.007f, Pal.InkFaint, new Vector3(cx + 0.059f, y, 0));
                var value = Text.Make(page, "", UIStyle.Tiny, Pal.Ink, TextAlignmentOptions.Right, 0.06f, 0.02f, "IBMPlexMono-Regular", false, new Vector3(cx + 0.121f, y, 0));
                Button.Make(page, "", new Vector2(cw - 0.004f, 0.019f), () => { cell.SelectedJoint = jj; if (cell.Mode != JogMode.Joint) cell.SetMode(JogMode.Joint); }, ButtonStyle.Ghost, null, UIStyle.Tiny, new Vector3(cx + cw / 2, y, 0.001f));
                refresh.Add(() =>
                {
                    float q = cell.Arm.Q[jj];
                    float t = Mathf.InverseLerp(cell.Arm.QMin[jj], cell.Arm.QMax[jj], q);
                    bar.Value = t;
                    bool sel = cell.Mode == JogMode.Joint && cell.SelectedJoint == jj;
                    bool edge = t < 0.04f || t > 0.96f;
                    bar.Color = edge ? Pal.Err : sel ? Pal.Brand : Pal.InkFaint;
                    value.text = $"{q * Mathf.Rad2Deg:0.0}°";
                    label.color = sel ? Pal.Brand : Pal.InkSoft;
                });
            }
            // What the arm is doing now, then the tool's position in the base frame.
            var flags = Text.Make(page, "", UIStyle.Tiny, Pal.Err, TextAlignmentOptions.Left, W - 0.03f, 0.02f, "Figtree-SemiBold", false, new Vector3(0, Y(0.12f), 0));
            Text.Fit(flags, 0.8f);
            refresh.Add(() =>
            {
                string f;
                if (cell.Blocked) f = "Blocked: contact with " + cell.BlockedBy;
                else if (cell.AtJointLimit) f = "Joint limit";
                else if (cell.OutOfReach) f = "Out of reach";
                else if (cell.NearSingularity) f = "Near a singular pose";
                else f = $"<color=#7C8794>Frame: {Interaction.FrameName(cell.Frame)}  ·  {(cell.GripperClosed ? (cell.Held != null ? "holding a part" : "gripper closed") : "gripper open")}</color>";
                flags.text = f;
            });
            var tcp = Text.Make(page, "", UIStyle.Tiny, Pal.Ink, TextAlignmentOptions.TopLeft, W - 0.03f, 0.036f, "IBMPlexMono-Regular", false, new Vector3(0, Y(0.153f), 0));
            refresh.Add(() =>
            {
                var p = cell.TcpInBase * 1000f;
                var r = cell.TcpRpyInBase;
                tcp.text = $"X {p.x,5:0}  Y {p.y,5:0}  Z {p.z,5:0} mm\nR {r.x,5:0}  P {r.y,5:0}  Y {r.z,5:0} °";
            });
            // Speed, gripper, home.
            var speed = Slider.Make(page, "Speed", 0.14f, cell.SpeedOverride, v => cell.SpeedOverride = Mathf.Max(0.05f, v), null, new Vector3(x0 + 0.07f, Y(0.215f), 0));
            refresh.Add(() => { if (!speed.Pressed) speed.Set(cell.SpeedOverride); });
            var grip = Button.Make(page, "Gripper", new Vector2(0.075f, 0.036f), () => { cell.ToggleGripper(); Sfx.Play(cell.GripperClosed ? "close" : "open"); }, ButtonStyle.Primary, "hand-grab", UIStyle.Tiny, new Vector3(W / 2 - 0.1225f, Y(0.222f), 0));
            refresh.Add(() => { grip.SetLabel(cell.GripperClosed ? "Open" : "Close"); grip.SetIcon(cell.GripperClosed ? "hand" : "hand-grab"); });
            Button.Make(page, "Home", new Vector2(0.077f, 0.036f), () => cell.Home(), ButtonStyle.Secondary, "house", UIStyle.Tiny, new Vector3(W / 2 - 0.0535f, Y(0.222f), 0));
        }

        int programPage;

        void BuildProgram()
        {
            var cell = app.Cell;
            float x0 = -W / 2 + 0.015f;
            var title = Text.Make(page, "", UIStyle.Small, Pal.Ink, TextAlignmentOptions.Left, W - 0.03f, 0.024f, "Figtree-Bold", false, new Vector3(0, Y(0.0f), 0));
            refresh.Add(() => title.text = $"{cell.Program.Name}  <size=70%><color=#7C8794>{cell.Program.Steps.Count} steps</color></size>");
            // The steps, six to a page, the running one marked.
            var rows = new List<(TextMeshPro t, Icon i, Pane back)>();
            for (int k = 0; k < 6; k++)
            {
                float y = Y(0.028f + k * 0.019f);
                var back = Pane.Make(page, "row", new Vector2(W - 0.03f, 0.02f), new Color(1, 1, 1, 0), 0.006f, blur: false, pos: new Vector3(0, y, -0.001f));
                var icon = Icon.Make(page, "circle", 0.014f, Pal.InkSoft, new Vector3(x0 + 0.012f, y, 0));
                var t = Text.Make(page, "", UIStyle.Tiny, Pal.Ink, TextAlignmentOptions.Left, W - 0.06f, 0.02f, "Figtree-Medium", false, new Vector3(0.012f, y, 0));
                rows.Add((t, icon, back));
            }
            refresh.Add(() =>
            {
                var steps = cell.Program.Steps;
                int pages = Mathf.Max(1, (steps.Count + 5) / 6);
                if (cell.Running && cell.StepIndex >= 0) programPage = cell.StepIndex / 6;
                programPage = Mathf.Clamp(programPage, 0, pages - 1);
                for (int k = 0; k < 6; k++)
                {
                    int i = programPage * 6 + k;
                    var row = rows[k];
                    bool has = i < steps.Count;
                    row.t.text = has ? $"{i + 1}. {steps[i].Label}" : (k == 0 && steps.Count == 0 ? "<color=#7C8794>Empty: jog the arm and press X to save a spot.</color>" : "");
                    row.i.gameObject.SetActive(has);
                    if (has) row.i.Set(steps[i].Icon);
                    bool now = cell.Running && cell.StepIndex == i;
                    row.back.SetTint(now ? new Color(Pal.Brand.r, Pal.Brand.g, Pal.Brand.b, 0.18f) : new Color(1, 1, 1, 0));
                    row.i.SetColor(now ? Pal.Brand : Pal.InkSoft);
                }
            });
            // Editing.
            float bw = (W - 0.03f) / 4;
            var edit = new (string label, string icon, Action act)[]
            {
                ("Save spot", "save", () => { var wp = cell.SaveWaypoint(); app.Toasts.Show("Waypoint " + wp.Name + " saved", "save", Pal.Ok); Sfx.Play("waypoint"); }),
                ("+ Close", "hand-grab", () => cell.AddStep(StepKind.Close)),
                ("+ Open", "hand", () => cell.AddStep(StepKind.Open)),
                ("+ Wait", "timer", () => cell.AddStep(StepKind.Wait, 1f)),
            };
            for (int i = 0; i < edit.Length; i++)
            {
                var e = edit[i];
                Button.Make(page, e.label, new Vector2(bw - 0.005f, 0.03f), e.act, ButtonStyle.Secondary, null, UIStyle.Tiny, new Vector3(x0 + bw / 2 + i * bw, Y(0.153f), 0));
            }
            var tools = new (string label, string icon, Action act)[]
            {
                ("Undo", "rotate-ccw", () => { if (cell.Program.Steps.Count > 0) cell.Program.Steps.RemoveAt(cell.Program.Steps.Count - 1); }),
                ("Clear", "x", () => cell.ClearProgram()),
                ("Quick start", "sparkles", () => app.QuickStartProgram()),
                ("Reset parts", "refresh-cw", () => app.ResetParts()),
            };
            for (int i = 0; i < tools.Length; i++)
            {
                var e = tools[i];
                Button.Make(page, e.label, new Vector2(bw - 0.005f, 0.03f), e.act, ButtonStyle.Ghost, null, UIStyle.Tiny, new Vector3(x0 + bw / 2 + i * bw, Y(0.186f), 0));
            }
            // Running.
            var run = Button.Make(page, "Run", new Vector2(0.075f, 0.036f), () => { if (cell.Running) cell.StopProgram(); else cell.StartProgram(false); }, ButtonStyle.Success, "play", UIStyle.Tiny, new Vector3(x0 + 0.04f, Y(0.222f), 0));
            var loop = Button.Make(page, "Loop", new Vector2(0.07f, 0.036f), () => { if (!cell.Running) cell.StartProgram(true); else cell.Program.Loop = !cell.Program.Loop; }, ButtonStyle.Secondary, "repeat", UIStyle.Tiny, new Vector3(x0 + 0.118f, Y(0.222f), 0));
            var stats = Text.Make(page, "", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Right, 0.15f, 0.04f, "IBMPlexMono-Regular", false, new Vector3(W / 2 - 0.09f, Y(0.222f), 0));
            refresh.Add(() =>
            {
                run.SetLabel(cell.Running ? "Stop" : "Run");
                run.SetIcon(cell.Running ? "square" : "play");
                run.Style = cell.Running ? ButtonStyle.Danger : ButtonStyle.Success;
                loop.SetSelected(cell.Running && cell.Program.Loop);
                stats.text = $"cycles {cell.Cycles}\nlast {(cell.LastCycleTime > 0 ? cell.LastCycleTime.ToString("0.0") + " s" : "-")}";
            });
        }

        void BuildSafety()
        {
            var cell = app.Cell;
            var s = cell.Safety;
            float x0 = -W / 2 + 0.015f;
            var chip = Pane.Make(page, "state", new Vector2(W - 0.03f, 0.05f), Pal.Ok, 0.012f, blur: false, pos: new Vector3(0, Y(0.012f), -0.001f));
            var head = Text.Make(page, "", UIStyle.Small, Color.white, TextAlignmentOptions.Left, W - 0.06f, 0.024f, "Figtree-Bold", false, new Vector3(0, Y(0.003f), 0));
            var why = Text.Make(page, "", UIStyle.Tiny, Color.white, TextAlignmentOptions.Left, W - 0.06f, 0.02f, "Figtree-Medium", false, new Vector3(0, Y(0.024f), 0));
            refresh.Add(() =>
            {
                Color c;
                string h, w;
                if (s.Stop == StopKind.Emergency) { c = Pal.Err; h = "Emergency stop"; w = s.EStopPressed ? "Twist the button to release it, then Reset" : "Released: press Reset to continue"; }
                else if (s.Stop == StopKind.Protective) { c = Pal.Err; h = "Protective stop"; w = s.Reason; }
                else if (s.Zone == "warning") { c = Pal.Warn; h = "Reduced speed"; w = $"A person is {s.HumanDistance:0.0} m from the robot"; }
                else { c = Pal.Ok; h = "Safe to run"; w = s.SeparationMonitoring ? $"Scanner on  ·  nearest person {Mathf.Min(s.HumanDistance, 9.9f):0.0} m" : "Collaborative mode: force limiting on"; }
                chip.SetTint(c);
                head.text = h;
                why.text = w;
            });
            // A pendant's own emergency stop, and the reset.
            var estop = Button.Make(page, "E-STOP", new Vector2(0.11f, 0.11f), () => { if (s.EStopPressed) s.ReleaseEStop(); else s.PressEStop(); }, ButtonStyle.Danger, "octagon-x", UIStyle.Small, new Vector3(x0 + 0.06f, Y(0.115f), 0));
            refresh.Add(() => estop.SetLabel(s.EStopPressed ? "Release" : "E-STOP"));
            Button.Make(page, "Reset", new Vector2(0.09f, 0.04f), () => { if (s.Reset()) { app.Toasts.Show("Stop reset", "rotate-ccw", Pal.Ok); Sfx.Play("success", 0.6f); } else app.Toasts.Show(cell.Message, "triangle-alert", Pal.Warn); }, ButtonStyle.Primary, "rotate-ccw", UIStyle.Tiny, new Vector3(x0 + 0.19f, Y(0.075f), 0));
            Toggle.Make(page, "Scanner fields", 0.18f, s.SeparationMonitoring, v => { s.SeparationMonitoring = v; app.SafetyZones.Show(v); }, new Vector3(x0 + 0.218f, Y(0.135f), 0), UIStyle.Tiny);
            Toggle.Make(page, "Enabling switch", 0.18f, cell.RequireEnabling, v => { cell.RequireEnabling = v; app.Settings.RequireEnabling = v; }, new Vector3(x0 + 0.218f, Y(0.188f), 0), UIStyle.Tiny);
            var dist = Text.Make(page, "", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Left, W - 0.03f, 0.03f, "Figtree-Medium", true, new Vector3(0, Y(0.232f), 0));
            refresh.Add(() => dist.text = cell.RequireEnabling ? (cell.EnablingHeld ? "<color=#1FA463>Enabling switch held: the arm may move</color>" : "Hold the left grip halfway to enable jogging") : "ISO 10218 (2025) collaborative applications");
        }

        public void Tick(Transform leftGrip, bool xr, Transform head)
        {
            if (!Visible) return;
            if (xr && leftGrip != null)
            {
                // Held like a tablet: above the left hand, tilted towards you.
                var target = leftGrip.position + leftGrip.rotation * new Vector3(0.0f, 0.075f, 0.11f);
                var look = Quaternion.LookRotation(target - head.position, Vector3.up);
                var rot = Quaternion.Slerp(leftGrip.rotation * Quaternion.Euler(52, 0, 0), look, 0.65f);
                // Raised by its extra height, so its lower edge stays clear of the controller.
                Window.transform.position = target + rot * new Vector3(0, 0.03f, 0);
                Window.transform.rotation = rot;
            }
            else if (head != null)
            {
                // On the desktop it sits at the lower left of the view.
                // Square to the screen, so the wide lens does not bend it.
                var target = head.position + head.rotation * new Vector3(-0.4f, -0.16f, 0.72f);
                Window.transform.position = target;
                Window.transform.rotation = head.rotation;
            }
            foreach (var r in refresh) r();
        }
    }
}
