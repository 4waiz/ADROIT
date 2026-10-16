// SPDX-License-Identifier: LicenseRef-BRIDGE
// The factory tour: thirteen stops round the hall, each with where to stand, what to look at and a
// few plain sentences about it, from the cobot cell and its safety kit to the lab's robots, the
// machine line and the digital twin.

using System.Collections.Generic;
using Bridge.App;
using Bridge.UI;
using Bridge.World;
using TMPro;
using UnityEngine;

namespace Bridge.Learn
{
    public class Tour
    {
        readonly BridgeApp app;
        readonly List<(string title, string text, Vector3 spot, Vector3 look, string icon)> stops = new();
        public bool Active { get; private set; }
        public int Index { get; private set; }
        public int Count => stops.Count;
        public string CurrentTitle => Active ? stops[Index].title : "";
        Window window;
        PointerArrow arrow;
        Transform lookAt;

        public Tour(BridgeApp app)
        {
            this.app = app;
            float bx0 = CellLayout.Bx0, by0 = CellLayout.By0;
            Add("Cell 01, a collaborative robot cell",
                "In front of you is a Franka Emika Panda on its pedestal beside the work bench. The yellow line on the floor marks the cell; the striped band is where an operator steps in. There is no fence: this robot is built to work next to people.",
                Web.P(3.4f, -2.6f, 0), CellLayout.RobotBase, "bot");
            Add("Control cabinet and stack light",
                "The cabinet holds the robot's controller. The stack light on top tells everyone the cell's state at a glance: green running, blue in manual mode, amber reduced speed, red stopped.",
                Web.P(bx0 + 0.9f, by0 - 1.4f, 0), Web.P(bx0 - 0.75f, by0 + 0.35f, 1.6f), "siren");
            Add("Operator panel and emergency stop",
                "The panel says the same in words. The red mushroom button is the emergency stop: press it and everything stops, and it stays stopped until a person releases it and resets the cell.",
                Web.P(3.25f, 0.15f, 0), Web.P(CellLayout.Bx1 + 0.85f, CellLayout.Hy + 0.1f, 1.2f), "octagon-x");
            Add("Safety laser scanner",
                "The small yellow scanner at the bench's corner sweeps the floor round the cell. When someone walks into its warning field the robot slows; in its protective field the robot stops.",
                Web.P(1.7f, -2.2f, 0), Web.P(CellLayout.Hx - 0.02f, -CellLayout.Hy - 0.02f, 0.15f), "radar");
            Add("Infeed conveyor",
                "Cartons arrive on the roller conveyor behind the cell. On a real line, sensors along it tell the cell when the next part is ready.",
                Web.P(1.6f, 4.7f, 0), Web.P(1.6f, CellLayout.By1 + 1.3f, 0.8f), "package");
            Add("The digital twin",
                "Beside the cell stands its digital twin: a live virtual copy fed by the cell's data. Engineers use it to watch the cell, predict problems and test changes before touching the real robot. The Academy's module 9 shows you how.",
                CellLayout.TwinSpot, CellLayout.TwinBase + Vector3.up * 0.3f, "layers");
            Add("Palletizing robot",
                "A six-axis industrial robot stacks cartons from the infeed onto a pallet. It is fast and strong, so it works in a guarded zone where people do not go while it runs.",
                Web.P(-5.4f, 0.3f, 0), Web.P(-7.3f, 3.0f, 0.9f), "boxes");
            Add("Delta picker",
                "Three light arms move one small platform: a parallel-kinematic delta robot. It snatches parts off a moving belt and sorts them into a tray, many times a minute.",
                Web.P(-1.4f, 5.35f, 0), Web.P(-1.4f, 6.65f, 1.1f), "zap");
            Add("Cobot workstation",
                "Two collaborative arms hand parts to each other across a fixture. Cobots like these share space with people thanks to force limiting and monitored speed.",
                Web.P(8.85f, 2.4f, 0), Web.P(10.7f, 2.4f, 1.0f), "users");
            Add("Autonomous mobile robot",
                "This mobile robot carries a carton round the lab on its own. Stand in its path: its scanner sees you, it stops and its lights turn amber until the way is clear.",
                Web.P(2.0f, -3.55f, 0), Web.P(4.0f, -4.4f, 0.4f), "truck");
            Add("CNC machine line",
                "Machining centres, each with its own stack light. Loading and unloading machines like these, machine tending, is a classic job for a cobot.",
                Web.P(4.9f, 6.9f, 0), Web.P(4.9f, 9.6f, 1.4f), "cog");
            Add("Pallet racking",
                "Stock waits here on pallets. Mobile robots, cobots and the digital twin together let a factory follow material from the shelf to the machine and out again.",
                Web.P(-12.2f, -4.2f, 0), Web.P(-14.9f, -4.2f, 2.0f), "warehouse");
            Add("That's the factory",
                "Ready to take control? Open the Robotics Academy from the main menu (the left controller's Menu button) and start with module 1.",
                Web.P(3.4f, -2.6f, 0), CellLayout.RobotBase, "graduation-cap");
        }

        void Add(string title, string text, Vector3 spot, Vector3 look, string icon) => stops.Add((title, text, spot, look, icon));

        public void Start()
        {
            app.Lessons.Exit(silent: true);
            Active = true;
            Index = 0;
            if (lookAt == null) lookAt = new GameObject("tour look").transform;
            Show();
        }

        public void Stop()
        {
            if (!Active) return;
            Active = false;
            if (window != null) window.Show(false);
            if (arrow != null) Object.Destroy(arrow.gameObject);
        }

        public void Next()
        {
            if (!Active) return;
            if (Index + 1 >= stops.Count) { Stop(); app.Menus.Open(app.Menus.Home); return; }
            Index++;
            Show();
        }

        public void Back()
        {
            if (!Active || Index == 0) return;
            Index--;
            Show();
        }

        void Show()
        {
            var s = stops[Index];
            var facing = CellLayout.Facing(s.spot, s.look);
            app.GoTo(s.spot, facing);
            lookAt.position = s.look;
            if (arrow != null) Object.Destroy(arrow.gameObject);
            arrow = PointerArrow.Make(app.WorldRoot, lookAt, new Vector3(0, 0.45f, 0), Pal.Brand);
            if (window != null) Object.Destroy(window.gameObject);
            window = Window.Make(app.UIRoot, "tour", new Vector2(0.62f, 0.4f), s.title, s.icon, $"Factory tour  ·  stop {Index + 1} of {stops.Count}");
            window.Paragraph(s.text, UIStyle.Body, Pal.InkSoft, "Figtree-Regular");
            var row = window.Row(0.055f, 0);
            float w = window.Width;
            Button.Make(window.Content, "Exit tour", new Vector2(0.16f, 0.05f), () => { Stop(); app.Menus.Open(app.Menus.Home); }, ButtonStyle.Ghost, "x", UIStyle.Small, row + new Vector3(-w / 2 + 0.08f, 0, 0));
            Button.Make(window.Content, "", new Vector2(0.06f, 0.05f), Back, ButtonStyle.Ghost, "arrow-left", UIStyle.Small, row + new Vector3(w / 2 - 0.2f, 0, 0));
            Button.Make(window.Content, Index + 1 < stops.Count ? "Next stop" : "Finish", new Vector2(0.17f, 0.05f), Next, ButtonStyle.Primary, "arrow-right", UIStyle.Small, row + new Vector3(w / 2 - 0.085f, 0, 0));
            window.FitHeight();
            window.Follow = true;
            window.FollowYaw = 24f;
            window.FollowDistance = 0.95f;
            window.FollowDrop = 0.12f;
            window.Show(true, true);
            app.StartCoroutine(PlaceSoon());
            Sfx.Play("step", 0.6f);
        }

        System.Collections.IEnumerator PlaceSoon()
        {
            yield return new WaitForSeconds(0.25f);
            if (window != null && Bridge.UI.Rig.Head != null) window.PlaceInFront(Bridge.UI.Rig.Head, window.FollowDistance, window.FollowYaw, window.FollowDrop);
        }

        public void Tick(float dt) { }
    }

    /// <summary>The Academy's certificate, shown when the last module is done (and from the menu).</summary>
    public class Certificate
    {
        readonly BridgeApp app;
        public Window Window;

        public Certificate(BridgeApp app) { this.app = app; }

        public void Show()
        {
            if (Window != null) Object.Destroy(Window.gameObject);
            Window = Window.Make(app.UIRoot, "certificate", new Vector2(1.0f, 0.7f), null, null, null, true);
            var c = Window.Content;
            float top = Window.Top;
            Icon.Make(c, "award", 0.09f, Pal.Brand, new Vector3(0, top - 0.08f, 0));
            var k = Bridge.UI.Text.Make(c, "CERTIFICATE OF COMPLETION", UIStyle.Small, Pal.Brand, TextAlignmentOptions.Center, 0.9f, 0.03f, "Figtree-Bold", false, new Vector3(0, top - 0.15f, 0));
            k.characterSpacing = 10;
            Bridge.UI.Text.Make(c, "Robotics Academy: Cobot Operation, Safety and the Digital Twin", UIStyle.Title, Pal.Ink, TextAlignmentOptions.Center, 0.92f, 0.1f, "Figtree-ExtraBold", true, new Vector3(0, top - 0.22f, 0));
            int done = app.Progress.Count;
            int total = app.Lessons.Curriculum.Count;
            string list = "";
            for (int i = 0; i < total; i++)
            {
                var l = app.Lessons.Curriculum[i];
                list += (app.Progress.Done(l.Id) ? "<color=#1FA463>✓</color> " : "<color=#7C8794>○</color> ") + l.Title + (i % 2 == 1 ? "\n" : "     ");
            }
            Bridge.UI.Text.Make(c, list, UIStyle.Small, Pal.InkSoft, TextAlignmentOptions.Center, 0.92f, 0.2f, "Figtree-Medium", true, new Vector3(0, top - 0.39f, 0));
            string when = string.IsNullOrEmpty(app.Progress.FirstCompleted) ? System.DateTime.Now.ToString("d MMMM yyyy") : System.DateTime.Now.ToString("d MMMM yyyy");
            Bridge.UI.Text.Make(c, $"{done} of {total} modules completed  ·  {when}\n{Menus.Title}  ·  {Menus.Author}", UIStyle.Tiny, Pal.InkSoft, TextAlignmentOptions.Center, 0.92f, 0.06f, "Figtree-Medium", true, new Vector3(0, -top + 0.12f, 0));
            Button.Make(c, "Save as a picture", new Vector2(0.24f, 0.05f), () => app.StartCoroutine(Save()), ButtonStyle.Primary, "download", UIStyle.Small, new Vector3(-0.13f, -top + 0.05f, 0));
            Button.Make(c, "Close", new Vector2(0.16f, 0.05f), () => Window.Show(false), ButtonStyle.Secondary, "x", UIStyle.Small, new Vector3(0.11f, -top + 0.05f, 0));
            Window.Show(true, true);
            var head = Bridge.UI.Rig.Head;
            if (head != null) Window.PlaceInFront(head, 1.1f, 0, 0.1f);
            Sfx.Play("complete");
        }

        public string LastSaved;

        System.Collections.IEnumerator Save()
        {
            string dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "BRIDGE VR");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, $"Certificate {System.DateTime.Now:yyyy-MM-dd HHmm}.png");
            app.Spectator.CertificateShot = true;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(file);
            yield return null;
            yield return null;
            app.Spectator.CertificateShot = false;
            LastSaved = file;
            app.Toasts.Show("Saved to Documents\\BRIDGE VR", "download", Pal.Ok, 4f);
        }

        public void Tick(float dt) { }
    }
}
