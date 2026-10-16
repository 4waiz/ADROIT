// SPDX-License-Identifier: LicenseRef-BRIDGE
// Scripted checks, run with -autotest <scenario> [-shots <folder>] on the desktop (no headset):
//
//   robot    reach, jogging, joint limits, the pick and place, stops and resets, separation
//            monitoring, and the twin catching a collision; writes report.json, exits 0 or 1
//   shots    screenshots of the factory, the menus, the pendant, the twin and lesson panels
//   lessons  starts every module and steps through it, a screenshot of each first step
//   all      all three
//
// Each check prints "[autotest] PASS name: detail" or "[autotest] FAIL ...".

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bridge.Robot;
using Bridge.World;
using UnityEngine;
using Pose = Bridge.Robot.Pose;

namespace Bridge.App
{
    public class AutoTest : MonoBehaviour
    {
        BridgeApp app;
        string dir;
        readonly List<(string name, bool ok, string detail)> results = new();
        readonly List<float> frames = new();

        void Update()
        {
            if (app != null && app.Ready) frames.Add(Time.unscaledDeltaTime);
        }

        /// <summary>The frame times so far: the desktop window's rate, a floor for the headset's.</summary>
        void CheckFrames()
        {
            if (frames.Count < 30) return;
            var sorted = new List<float>(frames);
            sorted.Sort();
            float mean = 0;
            foreach (var f in frames) mean += f;
            mean /= frames.Count;
            float p95 = sorted[(int)(sorted.Count * 0.95f)];
            float worst = sorted[sorted.Count - 1];
            Check("frame rate", p95 < 0.05f, $"{frames.Count} frames, mean {mean * 1000:0.0} ms ({1 / mean:0} fps), 95th percentile {p95 * 1000:0.0} ms, worst {worst * 1000:0} ms on {SystemInfo.graphicsDeviceName}");
        }

        public void Run(string scenario, string shots)
        {
            app = BridgeApp.I;
            dir = shots ?? Path.Combine(Application.persistentDataPath, "autotest");
            Directory.CreateDirectory(dir);
            StartCoroutine(Main(scenario));
        }

        void Check(string name, bool ok, string detail = "")
        {
            results.Add((name, ok, detail));
            Debug.Log($"[autotest] {(ok ? "PASS" : "FAIL")} {name}: {detail}");
        }

        IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        IEnumerator Shot(string name, float settle = 0.6f)
        {
            yield return Wait(settle);
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(file);
            yield return null;
            yield return null;
            Debug.Log("[autotest] shot " + file);
        }

        void View(Vector3 eye, Vector3 look)
        {
            var d = look - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            app.XR.SetDesktopView(eye, yaw, pitch);
        }

        IEnumerator Main(string scenario)
        {
            while (!app.Ready) yield return null;
            yield return Wait(2.5f);
            app.XR.DesktopLook = false;
            if (scenario == "robot" || scenario == "all") yield return Robot();
            if (scenario == "shots" || scenario == "all") yield return Shots();
            if (scenario == "lessons" || scenario == "all") yield return Lessons();
            CheckFrames();
            Report();
            yield return Wait(0.5f);
            bool failed = results.Exists(r => !r.ok);
            Debug.Log($"[autotest] done: {results.FindAll(r => r.ok).Count} passed, {results.FindAll(r => !r.ok).Count} failed");
            Application.Quit(failed ? 1 : 0);
        }

        void Report()
        {
            var sb = new StringBuilder("{\n  \"checks\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.Append($"    {{\"name\": \"{Esc(r.name)}\", \"ok\": {(r.ok ? "true" : "false")}, \"detail\": \"{Esc(r.detail)}\"}}{(i + 1 < results.Count ? "," : "")}\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(dir, "report.json"), sb.ToString());
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ------------------------------------------------------------------ robot
        IEnumerator Robot()
        {
            var cell = app.Cell;
            var arm = app.Panda;
            app.Menus.Close();
            app.ResetCell();
            View(CellLayout.OperatorSpot + new Vector3(0, 1.6f, 0), CellLayout.BenchCentre + Vector3.up * 0.2f);
            yield return Wait(0.5f);

            // Reach: the places the lessons use.
            var q = new float[RobotArm.Dof];
            var down = cell.PickRotation();
            var spots = new (string name, Vector3 at)[]
            {
                ("above the cube", CellLayout.CubeSpot + Vector3.up * 0.15f),
                ("at the cube", CellLayout.CubeSpot + Vector3.up * 0.025f),
                ("at the place zone", CellLayout.PlaceZone + Vector3.up * 0.025f),
                ("at the feeder", CellLayout.FeederSpot + Vector3.up * 0.034f),
                ("tray pocket 1", Factory.TrayPockets()[0] + Vector3.up * 0.035f),
                ("tray pocket 4", Factory.TrayPockets()[3] + Vector3.up * 0.035f),
            };
            foreach (var s in spots)
            {
                var r = arm.Solve(new Pose(s.at, down), arm.Home, q, iterations: 160);
                bool clear = !arm.Collides(q, RobotArm.MaxOpening, Layers.EnvironmentMask, out string hit);
                Check("reach " + s.name, r.positionError < 0.003f && clear, $"error {r.positionError * 1000:0.0} mm, {(clear ? "clear" : "touches " + hit)}, hand travel {arm.ManipulabilityAt(q, positionOnly: true):0.0000}");
            }
            // How far from singular a few poses are: the pendant warns below Cell.SingularBelow.
            var probes = new (string name, float[] q)[]
            {
                ("home", arm.Home),
                ("elbow stretched (J4 -10)", new[] { 0f, 0.3f, 0f, -0.17f, 0f, 1.9f, 0.785f }),
                ("wrist straight (J6 0)", new[] { 0f, -0.3f, 0f, -2.2f, 0f, 0.0f, 0.785f }),
                ("wrist straight (J6 5)", new[] { 0f, -0.3f, 0f, -2.2f, 0f, 0.09f, 0.785f }),
                ("upright (J2 0, J4 -4)", new[] { 0f, 0f, 0f, -0.07f, 0f, 1.6f, 0.785f }),
                ("ready (J2 -45, J4 -135, J6 90)", new[] { 0f, -0.785f, 0f, -2.356f, 0f, 1.571f, 0.785f }),
            };
            foreach (var probe in probes)
            {
                Debug.Log($"[autotest] manipulability {probe.name}: {arm.ManipulabilityAt(probe.q):0.0000}, hand travel {arm.ManipulabilityAt(probe.q, positionOnly: true):0.0000}");
            }

            // Cartesian jogging, through the same path a controller's sticks take: Drive mode, right
            // stick up moves the hand straight up.
            var sticks = new XR.XRSystem.StickOverride();
            app.XR.Override = sticks;
            app.Interaction.SetMode(ControlMode.Drive);
            cell.SetMode(JogMode.Cartesian);
            cell.Frame = JogFrame.Base;
            cell.SpeedOverride = 0.5f;
            var p0 = arm.Tcp.position;
            sticks.RightStick = new Vector2(0, 1);
            int f0 = frames.Count;
            yield return Wait(1.0f);
            sticks.RightStick = Vector2.zero;
            yield return null;
            var lift = arm.Tcp.position - p0;
            float side = new Vector2(lift.x, lift.z).magnitude;
            // 0.25 m/s at 100 %, so about 125 mm in the second at 50 %.
            Check("jog up", lift.y > 0.09f && side < 0.004f, $"up {lift.y * 1000:0} mm, sideways {side * 1000:0.0} mm, {frames.Count - f0} frames");
            yield return Shot("robot-jog", 0.2f);

            // Joint jogging stops at a limit.
            cell.SetMode(JogMode.Joint);
            cell.SelectedJoint = 3;
            cell.SpeedOverride = 1f;
            bool limit = false;
            Action<string, string> onLimit = (n, d) => { if (n == "joint_limit") limit = true; };
            cell.Event += onLimit;
            sticks.RightStick = new Vector2(0, 1);
            float t = 0;
            while (!limit && t < 8) { t += Time.deltaTime; yield return null; }
            sticks.RightStick = Vector2.zero;
            cell.Event -= onLimit;
            Check("joint limit", limit && arm.Q[3] <= arm.QMax[3] + 1e-4f, $"J4 at {arm.Q[3] * Mathf.Rad2Deg:0.0} deg (max {arm.QMax[3] * Mathf.Rad2Deg:0.0})");

            // The bench stops the hand.
            app.ResetCell();
            app.Interaction.SetMode(ControlMode.Drive);
            cell.SetMode(JogMode.Cartesian);
            cell.Frame = JogFrame.Base;
            cell.SpeedOverride = 1f;
            bool blocked = false;
            Action<string, string> onBlock = (n, d) => { if (n == "blocked") blocked = true; };
            cell.Event += onBlock;
            sticks.RightStick = new Vector2(0, -1);
            t = 0;
            while (!blocked && t < 8) { t += Time.deltaTime; yield return null; }
            sticks.RightStick = Vector2.zero;
            cell.Event -= onBlock;
            app.Interaction.SetMode(ControlMode.Explore);
            app.XR.Override = null;
            Check("bench blocks the hand", blocked && arm.Tcp.position.y > CellLayout.BenchTop, $"stopped at {(arm.Tcp.position.y - CellLayout.BenchTop) * 1000:0} mm above the bench ({cell.BlockedBy})");

            // The pick and place, run as a program: the cube ends in the zone.
            app.ResetCell();
            app.QuickStartProgram();
            cell.SpeedOverride = 1f;
            cell.StartProgram();
            t = 0;
            while (cell.Running && t < 60) { t += Time.deltaTime; yield return null; }
            yield return Wait(1.0f);
            var c = app.Cube.transform.position;
            bool inZone = Mathf.Abs(c.x - CellLayout.PlaceZone.x) < CellLayout.ZoneHalf && Mathf.Abs(c.z - CellLayout.PlaceZone.z) < CellLayout.ZoneHalf && c.y < CellLayout.BenchTop + 0.06f;
            Check("pick and place", !cell.Running && inZone, $"cube {Flat(c, CellLayout.PlaceZone) * 1000:0} mm from the zone centre, cycle {cell.LastCycleTime:0.0} s");
            yield return Shot("robot-pickplace", 0.2f);

            // Emergency stop latches until released and reset.
            app.ResetCell();
            app.QuickStartProgram();
            cell.StartProgram(true);
            yield return Wait(1.0f);
            cell.Safety.PressEStop();
            var before = arm.Tcp.position;
            yield return Wait(0.6f);
            bool held = Vector3.Distance(before, arm.Tcp.position) < 0.0005f;
            bool refused = !cell.Safety.Reset();
            cell.Safety.ReleaseEStop();
            bool reset = cell.Safety.Reset();
            Check("emergency stop", held && refused && reset && cell.Safety.Stop == StopKind.None, $"held {held}, reset refused while pressed {refused}, reset after release {reset}");
            yield return Wait(0.5f);

            // Speed and separation monitoring: a person close slows, then stops, the cell.
            cell.Safety.SeparationMonitoring = true;
            app.SafetyZones.Show(true);
            var basePos = CellLayout.RobotBase;
            cell.HumanPresent = true;
            app.Interaction.Pointed = "";
            var near = new Vector3(basePos.x + 1.3f, 1.6f, basePos.z);
            var close = new Vector3(basePos.x + 0.8f, 1.6f, basePos.z);
            // The interaction tick sets the head every frame; drive the desktop camera instead.
            View(near, basePos);
            yield return Wait(0.8f);
            string z1 = cell.Safety.Zone;
            float s1 = cell.Safety.Scale;
            yield return Shot("robot-ssm-warning", 0.1f);
            View(close, basePos);
            yield return Wait(0.6f);
            string z2 = cell.Safety.Zone;
            var stop2 = cell.Safety.Stop;
            View(CellLayout.SafetySpot + Vector3.up * 1.6f, basePos);
            yield return Wait(0.8f);
            Check("separation monitoring", z1 == "warning" && s1 < 0.5f && z2 == "protective" && stop2 == StopKind.Protective && cell.Safety.Stop == StopKind.None,
                $"at 1.3 m: {z1} (scale {s1:0.00}); at 0.8 m: {z2} ({stop2}); back out: {cell.Safety.Stop}");
            cell.Safety.SeparationMonitoring = false;
            app.SafetyZones.Show(false);
            cell.StopProgram();

            // The twin catches the collision with the new fixture; a higher approach clears it.
            app.ResetCell();
            app.SetFixture(true);
            var drop = Factory.TrayPockets()[0] + Vector3.up * 0.037f;
            var low = cell.PickAndPlace(Bridge.Learn.Course.CubePick(), drop, 0.06f, "low");
            app.Twin.Preview(low, 4f);
            t = 0;
            while (app.Twin.Previewing && t < 40) { t += Time.deltaTime; yield return null; }
            bool caught = app.Twin.PreviewDone && !app.Twin.PreviewOk;
            string first = app.Twin.PreviewResult;
            LogTwin("low");
            yield return Shot("twin-collision", 0.1f);
            var high = cell.PickAndPlace(Bridge.Learn.Course.CubePick(), drop, 0.2f, "high");
            app.Twin.Preview(high, 4f);
            t = 0;
            while (app.Twin.Previewing && t < 60) { t += Time.deltaTime; yield return null; }
            LogTwin("high");
            Check("twin catches the collision", caught && app.Twin.PreviewOk, $"low: {first} | high: {app.Twin.PreviewResult}");
            app.SetFixture(false);

            // The capstone's batch program exists and runs clean on the twin.
            app.ResetCell();
            var batch = app.TrayProgram();
            bool built = batch != null;
            if (built)
            {
                app.Twin.Preview(batch, 4f);
                t = 0;
                while (app.Twin.Previewing && t < 120) { t += Time.deltaTime; yield return null; }
            }
            Check("batch program", built && app.Twin.PreviewOk, built ? app.Twin.PreviewResult : "could not build the feeder-to-tray program");
            app.ResetCell();
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>A Unity point in the robot's world frame (metres, z up), as the web app writes it.</summary>
        static string RobotWorld(Vector3 v) => $"({v.x + CellLayout.CellCentreX:0.000}, {v.z:0.000}, {v.y:0.000})";

        void LogTwin(string run)
        {
            var twin = app.Twin;
            var tcp = app.Panda.Forward(twin.Virtual.Arm.Q).position;
            string q = string.Join(", ", Array.ConvertAll(twin.Virtual.Arm.Q, a => (a * Mathf.Rad2Deg).ToString("0")));
            string fixture = app.Fixture != null ? RobotWorld(app.Fixture.position) : "none";
            Debug.Log($"[autotest] twin {run}: {twin.PreviewResult} TCP {RobotWorld(tcp)}, link {app.Panda.LastHitLink}, q [{q}], cube {RobotWorld(app.Cube.transform.position)}, fixture {fixture}");
        }

        // ------------------------------------------------------------------ screenshots
        IEnumerator Shots()
        {
            app.ResetCell();
            app.Menus.Close();
            var b = CellLayout.RobotBase;
            View(CellLayout.WelcomeSpot + Vector3.up * 1.62f, b + Vector3.up * 0.1f);
            app.Menus.Open(app.Menus.Home);
            yield return Shot("01-home", 1.5f);
            app.Menus.Open(app.Menus.Academy);
            yield return Shot("02-academy");
            app.Menus.Open(app.Menus.Settings);
            yield return Shot("03-settings");
            app.Menus.Close();
            yield return Shot("04-cell-from-welcome", 0.8f);
            View(CellLayout.OperatorSpot + Vector3.up * 1.62f, CellLayout.BenchCentre + Vector3.up * 0.25f);
            app.Interaction.SetMode(ControlMode.Drive);
            app.Pendant.Show(true);
            yield return Shot("05-operator-pendant", 1.0f);
            app.Pendant.SetTab("program");
            app.QuickStartProgram();
            yield return Shot("06-pendant-program");
            app.Pendant.SetTab("safety");
            yield return Shot("07-pendant-safety");
            app.Pendant.SetTab("jog");
            app.Interaction.SetMode(ControlMode.Explore);
            app.Pendant.Show(false);
            app.Joints.ShowAll = true;
            View(b + new Vector3(1.0f, 0.75f, -0.9f), b + Vector3.up * 0.1f);
            yield return Shot("08-joint-arcs");
            app.Joints.ShowAll = false;
            app.Axes.Show(true, true);
            yield return Shot("09-axes");
            app.Axes.Show(false, false);
            app.Cell.Safety.SeparationMonitoring = true;
            app.SafetyZones.Show(true);
            View(b + new Vector3(2.6f, 2.4f, -2.4f), b);
            yield return Shot("10-safety-fields", 1.0f);
            app.SafetyZones.Show(false);
            app.Cell.Safety.SeparationMonitoring = false;
            View(CellLayout.TwinSpot + Vector3.up * 1.65f, CellLayout.TwinBase + Vector3.up * 0.2f);
            app.QuickStartProgram();
            app.Cell.StartProgram(true);
            yield return Shot("11-twin", 2.0f);
            app.Cell.StopProgram();
            View(Web.P(-5.4f, 0.3f, 1.7f), Web.P(-7.3f, 3.0f, 1.0f));
            yield return Shot("12-palletizer", 1.0f);
            View(Web.P(-1.4f, 5.2f, 1.7f), Web.P(-1.4f, 6.65f, 1.0f));
            yield return Shot("13-delta", 0.8f);
            View(Web.P(8.7f, 2.4f, 1.7f), Web.P(10.7f, 2.4f, 1.0f));
            yield return Shot("14-cobots", 0.8f);
            View(Web.P(4.9f, 6.5f, 1.7f), Web.P(4.9f, 9.6f, 1.5f));
            yield return Shot("15-cnc-line", 0.8f);
            View(Web.P(6f, -9f, 3.2f), Web.P(-4f, 2f, 1.0f));
            yield return Shot("16-hall", 0.8f);
            View(Web.P(-11.5f, -3.0f, 1.7f), Web.P(-14.9f, -5f, 2.0f));
            yield return Shot("17-racking", 0.8f);
            View(Web.P(CellLayout.Bx1 + 1.7f, CellLayout.Hy - 0.6f, 1.55f), Web.P(CellLayout.Bx1 + 0.85f, CellLayout.Hy + 0.1f, 1.2f));
            yield return Shot("18-operator-panel", 0.6f);
        }

        // ------------------------------------------------------------------ lessons
        IEnumerator Lessons()
        {
            var runner = app.Lessons;
            for (int i = 0; i < runner.Curriculum.Count; i++)
            {
                bool ok = true;
                string why = "";
                try { runner.Start(i); }
                catch (Exception e) { ok = false; why = e.Message; }
                yield return Wait(1.2f);
                var head = app.XR.Head;
                yield return Shot($"lesson-{i + 1:00}-step-1", 0.6f);
                int steps = runner.Current != null ? runner.Current.Steps.Count : 0;
                for (int s = 0; s < steps && ok; s++)
                {
                    try { runner.Skip(); }
                    catch (Exception e) { ok = false; why = $"step {s + 1}: {e.Message}"; }
                    yield return Wait(0.15f);
                    if (s == 1) yield return Shot($"lesson-{i + 1:00}-step-3", 0.4f);
                }
                Check($"module {i + 1} {runner.Curriculum[i].Title}", ok && runner.Finished, ok ? $"{steps} steps" : why);
                yield return Shot($"lesson-{i + 1:00}-complete", 0.4f);
                runner.Exit(true);
                yield return Wait(0.3f);
            }
            app.Certificate.Show();
            yield return Shot("certificate", 0.8f);
        }
    }
}
