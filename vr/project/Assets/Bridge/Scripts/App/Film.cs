// SPDX-License-Identifier: LicenseRef-BRIDGE
// Films the factory for videos. Run the player with -film <folder> [-take a,b,...]: each take is a
// scripted camera move (and, in some, the trainee's controllers) over the app as it runs, rendered at
// 2560 x 1440 to numbered JPEGs at exactly 30 frames a second of app time (Time.captureFramerate), so
// the motion is smooth however long a frame takes to save. Nothing in the factory is staged: the robot
// runs its own programs, and the twin, the lessons, the safety functions and the lab work as always.
// The controllers are the app's own models, posed by the script where a person's hands would be.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Bridge.Learn;
using Bridge.Robot;
using Bridge.UI;
using Bridge.World;
using Bridge.XR;
using UnityEngine;

namespace Bridge.App
{
    public class Film : MonoBehaviour
    {
        const int Width = 2560, Height = 1440, Fps = 30;

        BridgeApp app;
        string dir;
        HashSet<string> only;
        RenderTexture rt;
        Texture2D tex;
        readonly HashSet<string> fired = new();

        // The camera this frame.
        Vector3 eye, look;
        float fov = 55f;

        // The trainee's hands, when a take shows them.
        bool hands;
        float aimBlend, reachBlend, rightTrigger;
        bool rightGrip, prevTrigger, prevGrip;
        Vector3 aimPoint, reachPoint;
        readonly XRSystem.StickOverride sticks = new();

        class Take
        {
            public string Name;
            public float Seconds;
            public float Warm = 0.6f;        // app time run before the first kept frame
            public int Sub = 1;              // app frames per kept frame: a finer clock for what is checked once a frame
            public Action Setup;
            public Action<float> Frame;      // seconds into the take (negative while warming up)
        }

        public void Run(string folder, string takes)
        {
            app = BridgeApp.I;
            dir = folder;
            Directory.CreateDirectory(dir);
            if (!string.IsNullOrEmpty(takes)) only = new HashSet<string>(takes.Split(','));
            StartCoroutine(Main());
        }

        IEnumerator Main()
        {
            while (!app.Ready) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            app.Spectator.Overlay = false;
            Toasts.Quiet = true;
            app.XR.DesktopLook = false;
            app.XR.Puppet = Pose;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var cam = app.XR.Cam;
            Time.captureFramerate = Fps;
            foreach (var take in Takes())
            {
                if (only != null && !only.Contains(take.Name)) continue;
                float t0 = Time.realtimeSinceStartup;
                yield return Record(take, cam);
                Debug.Log($"[film] {take.Name}: {Mathf.RoundToInt(take.Seconds * Fps)} frames in {Time.realtimeSinceStartup - t0:0} s");
            }
            Time.captureFramerate = 0;
            cam.targetTexture = null;
            Debug.Log("[film] done");
            Application.Quit(0);
        }

        IEnumerator Record(Take take, Camera cam)
        {
            Clean();
            // The camera goes to its first spot before the take sets up, so panels open in front of it.
            Step(take, -take.Warm, cam);
            yield return null;
            take.Setup?.Invoke();
            int sub = Mathf.Max(1, take.Sub);
            int rate = Fps * sub;
            Time.captureFramerate = rate;
            int warm = Mathf.RoundToInt(take.Warm * rate);
            for (int i = -warm; i < 0; i++)
            {
                Step(take, i / (float)rate, cam);
                yield return null;
            }
            string folder = Path.Combine(dir, take.Name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            int n = Mathf.RoundToInt(take.Seconds * Fps);
            cam.targetTexture = rt;
            double next = Time.realtimeSinceStartupAsDouble;
            for (int f = 0; f < n * sub; f++)
            {
                Step(take, f / (float)rate, cam);
                yield return new WaitForEndOfFrame();
                if (f == 10 * sub) Debug.Log($"[film] clock: dt {Time.deltaTime:0.0000} s, unscaled {Time.unscaledDeltaTime:0.0000} s");
                if (f % sub == 0) Save(Path.Combine(folder, $"f_{f / sub:00000}.jpg"));
                // Real time keeps pace with the film's clock, so what animates on real time (panels,
                // buttons) plays at its true speed too. The frames are encoded off this thread.
                next += 1.0 / rate;
                while (Time.realtimeSinceStartupAsDouble < next) System.Threading.Thread.Sleep(1);
            }
            Time.captureFramerate = Fps;
            cam.targetTexture = null;
            while (System.Threading.Volatile.Read(ref pending) > 0) yield return null;
        }

        int pending;

        /// <summary>Reads this frame back from the GPU and writes it as a JPEG on a worker thread.</summary>
        void Save(string path)
        {
            while (System.Threading.Volatile.Read(ref pending) > 48) System.Threading.Thread.Sleep(2);
            System.Threading.Interlocked.Increment(ref pending);
            UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError)
                {
                    Debug.LogWarning("[film] a frame could not be read back: " + path);
                    System.Threading.Interlocked.Decrement(ref pending);
                    return;
                }
                var data = req.GetData<byte>().ToArray();
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var jpg = ImageConversion.EncodeArrayToJPG(data, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, Width, Height, 0, 93);
                        File.WriteAllBytes(path, jpg);
                    }
                    finally { System.Threading.Interlocked.Decrement(ref pending); }
                });
            });
        }

        /// <summary>Every take starts from a quiet cell: no lesson, menu, tour, program or overlay.</summary>
        void Clean()
        {
            app.Lessons.Exit(silent: true);
            app.Tour.Stop();
            app.Menus.Close();
            if (app.Certificate.Window != null) { Destroy(app.Certificate.Window.gameObject); app.Certificate.Window = null; }
            app.Pendant.Show(false);
            app.XR.Override = null;
            app.Interaction.SetMode(ControlMode.Explore);
            app.ResetCell();
            app.Cell.Safety.SeparationMonitoring = false;
            app.SafetyZones.Show(false);
            app.Joints.ShowAll = false;
            app.Axes.Show(false, false);
            app.SetFixture(false);
            app.Twin.Board.ShowCommissioning(false);
            hands = false;
            ControllerModel.ShowInDesktop = false;
            aimBlend = reachBlend = rightTrigger = 0;
            rightGrip = false;
            sticks.LeftStick = sticks.RightStick = Vector2.zero;
            fired.Clear();
        }

        void Step(Take take, float t, Camera cam)
        {
            take.Frame(t);
            var d = look - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            app.XR.SetDesktopView(eye, yaw, pitch);
            cam.fieldOfView = fov;
        }

        /// <summary>The trainee's controllers, where a person's hands would be (called after the desktop input).</summary>
        void Pose(XRSystem xr)
        {
            xr.Mouse.Active = false;
            if (!hands) return;
            var head = xr.Head;
            var flat = Quaternion.Euler(0, head.eulerAngles.y, 0);
            var l = xr.Left;
            l.Tracked = true;
            l.Grip.position = head.position + flat * new Vector3(-0.19f, -0.36f, 0.36f);
            l.Grip.rotation = flat * Quaternion.Euler(40f, 14f, 0);
            l.Aim.position = l.Grip.position;
            l.Aim.rotation = l.Grip.rotation * Quaternion.Euler(-30f, 0, 0);
            l.Pointer.Active = false;

            var r = xr.Right;
            r.Tracked = true;
            Vector3 rest = head.position + flat * new Vector3(0.2f, -0.34f, 0.38f);
            Vector3 raised = head.position + flat * new Vector3(0.15f, -0.2f, 0.44f);
            Vector3 pos = Vector3.Lerp(rest, raised, aimBlend);
            if (reachBlend > 0) pos = Vector3.Lerp(pos, reachPoint, reachBlend);
            Quaternion restAim = flat * Quaternion.Euler(28f, -4f, 0);
            Quaternion aimRot = aimBlend > 0 && (aimPoint - pos).sqrMagnitude > 1e-4f ? Quaternion.LookRotation(aimPoint - pos, Vector3.up) : restAim;
            Quaternion aim = Quaternion.Slerp(restAim, aimRot, aimBlend);
            if (reachBlend > 0) aim = Quaternion.Slerp(aim, flat * Quaternion.Euler(50f, -8f, 0), reachBlend);
            r.Aim.position = pos + aim * new Vector3(0, 0, 0.05f);
            r.Aim.rotation = aim;
            r.Grip.position = pos;
            r.Grip.rotation = aim * Quaternion.Euler(30f, 0, 0);
            bool trig = rightTrigger > 0.5f;
            r.Trigger = rightTrigger;
            r.TriggerDown = trig && !prevTrigger;
            r.TriggerUp = !trig && prevTrigger;
            r.TriggerHeld = trig;
            prevTrigger = trig;
            r.Squeeze = rightGrip ? 1 : 0;
            r.GripDown = rightGrip && !prevGrip;
            r.GripUp = !rightGrip && prevGrip;
            r.GripHeld = rightGrip;
            prevGrip = rightGrip;
            var p = r.Pointer;
            p.Active = aimBlend > 0.5f;
            p.Ray = new Ray(r.Aim.position, r.Aim.forward);
            p.Select = trig;
            p.SelectDown = r.TriggerDown;
            p.SelectUp = r.TriggerUp;
            p.Tip = null;
        }

        // ------------------------------------------------------------------ helpers
        static Vector3 P(float x, float y, float z) => Web.P(x, y, z);
        static float U(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));
        static float Smooth(float u) => u * u * (3 - 2 * u);
        static float Smoother(float u) => u * u * u * (u * (u * 6 - 15) + 10);
        static Vector3 Up(float y) => new(0, y, 0);

        static Vector3 Orbit(Vector3 c, float r, float y, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
        }

        /// <summary>A point straight ahead of an eye, turned by yaw and tilted by pitch from a base direction.</summary>
        static Vector3 Gaze(Vector3 eye, Vector3 toward, float yaw, float pitch)
        {
            var flat = toward - eye;
            flat.y = 0;
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
            return eye + dir * 3f;
        }

        /// <summary>Piecewise smooth between (time, value) keys.</summary>
        static float Keys(float t, params float[] kv)
        {
            if (t <= kv[0]) return kv[1];
            for (int i = 0; i + 3 < kv.Length; i += 2)
                if (t <= kv[i + 2]) return Mathf.Lerp(kv[i + 1], kv[i + 3], Smooth(U(t, kv[i], kv[i + 2])));
            return kv[kv.Length - 1];
        }

        void At(float t, float when, string key, Action act)
        {
            if (t >= when && fired.Add(key)) act();
        }

        void RunPick(bool loop)
        {
            app.ResetParts();
            app.QuickStartProgram();
            app.Cell.SpeedOverride = 1f;
            app.Cell.StartProgram(loop);
        }

        static Transform FindButton(Transform root, string label)
        {
            if (root == null) return null;
            foreach (var b in root.GetComponentsInChildren<Bridge.UI.Button>(true))
                if (b.Label != null && b.Label.text.Trim() == label) return b.transform;
            return null;
        }

        /// <summary>Module 9 at its "test on the twin first" step: the new fixture is on the bench and the new program is loaded.</summary>
        void TwinTest()
        {
            app.Lessons.Start(8);
            app.Lessons.Skip();
            app.Lessons.Skip();
            app.Lessons.Skip();
        }

        /// <summary>The lesson panel stays out of the twin's two angles, which show the dashboard and the twin instead.</summary>
        void HideLessonPanel()
        {
            var w = app.Lessons.Panel.Window;
            if (w != null && w.gameObject.activeSelf) w.gameObject.SetActive(false);
        }

        void HandsOn()
        {
            hands = true;
            ControllerModel.ShowInDesktop = true;
        }

        // ------------------------------------------------------------------ the takes
        IEnumerable<Take> Takes()
        {
            var b = CellLayout.RobotBase;
            var robot = b + Up(0.2f);

            // The hall from high up, settling on Cell 01 while the robot works.
            yield return new Take
            {
                Name = "hall_reveal", Seconds = 14,
                Setup = () => RunPick(true),
                Frame = t =>
                {
                    float u = Smoother(U(t, 0, 14));
                    eye = Vector3.Lerp(P(9.2f, -10.4f, 5.8f), P(4.8f, -5.6f, 2.8f), u);
                    look = Vector3.Lerp(P(-3.2f, 3.6f, 1.3f), P(-0.3f, 0.1f, 0.95f), u);
                    fov = 46;
                },
            };

            // Walking in at eye height, towards the robot.
            yield return new Take
            {
                Name = "walk_in", Seconds = 12,
                Setup = () => RunPick(true),
                Frame = t =>
                {
                    float u = Smoother(U(t, 0, 12));
                    eye = Vector3.Lerp(P(3.6f, -2.9f, 1.64f), P(1.9f, -0.8f, 1.62f), u) + Up(Mathf.Sin(t * Mathf.PI * 1.7f) * 0.006f);
                    look = Vector3.Lerp(P(0.4f, -0.4f, 1.0f), P(-0.2f, -0.05f, 1.0f), Smooth(U(t, 0, 12)));
                    fov = 60;
                },
            };

            // Standing near the bench and looking around, as a person in the headset does.
            yield return new Take
            {
                Name = "look_around", Seconds = 13,
                Setup = () => RunPick(true),
                Frame = t =>
                {
                    eye = P(1.75f, -0.55f, 1.63f);
                    float yaw = Keys(t, 0, 0, 1.0f, 0, 3.2f, -58, 4.8f, -58, 7.0f, 4, 8.2f, 4, 10.4f, 62, 13, 62);
                    float pitch = Keys(t, 0, 7, 3.2f, 2, 7.0f, 9, 10.4f, 4, 13, 6);
                    var flat = robot - eye;
                    flat.y = 0;
                    var dir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
                    look = eye + dir * 3f;
                    fov = 68;
                },
            };

            // Round the robot as it picks and places the cube.
            yield return new Take
            {
                Name = "cell_orbit", Seconds = 14,
                Setup = () => RunPick(false),
                Frame = t =>
                {
                    eye = Orbit(b, 2.35f, 1.6f, Mathf.Lerp(-62, 38, Smoother(U(t, 0, 14))));
                    look = b + new Vector3(0.22f, 0.05f, 0);
                    fov = 40;
                },
            };

            // Close on the gripper: down to the cube, grip, lift, across, set it down.
            yield return new Take
            {
                Name = "gripper_macro", Seconds = 11, Warm = 0.2f,
                Setup = () => RunPick(false),
                Frame = t =>
                {
                    var cube = CellLayout.CubeSpot;
                    var zone = CellLayout.PlaceZone;
                    float u = Smooth(U(t, 0, 11));
                    eye = cube + Vector3.Lerp(new Vector3(0.66f, 0.33f, -0.5f), new Vector3(0.58f, 0.29f, -0.38f), u);
                    look = Vector3.Lerp(cube + Up(0.07f), (cube + zone) * 0.5f + Up(0.09f), Smooth(U(t, 4.2f, 8.2f)));
                    fov = 30;
                },
            };

            // The teach pendant in the left hand while the right thumbstick drives the robot's hand.
            yield return new Take
            {
                Name = "pendant_drive", Seconds = 12,
                Setup = () =>
                {
                    HandsOn();
                    app.Interaction.SetMode(ControlMode.Drive);
                    app.Cell.SetMode(JogMode.Cartesian);
                    app.Cell.SpeedOverride = 0.6f;
                    app.Pendant.Show(true);
                    app.Pendant.SetTab("jog");
                    app.XR.Override = sticks;
                },
                Frame = t =>
                {
                    eye = P(1.32f, 0.05f, 1.62f) + Up(Mathf.Sin(t * 1.1f) * 0.004f);
                    look = P(0.05f, 0.08f, 0.92f);
                    fov = 72;
                    sticks.RightStick = new Vector2(Keys(t, 7.6f, 0, 8.2f, 0.65f, 10.2f, 0.65f, 10.8f, 0), Keys(t, 0.8f, 0, 1.4f, 0.85f, 3.4f, 0.85f, 4.0f, 0));
                    sticks.LeftStick = new Vector2(Keys(t, 4.2f, 0, 4.8f, 0.7f, 6.8f, 0.7f, 7.4f, 0), 0);
                },
            };

            // Module 1: the first step, then pointing the right controller's beam at the panel and pulling the trigger.
            yield return new Take
            {
                Name = "lesson_point", Seconds = 14,
                Setup = () => { HandsOn(); app.Lessons.Start(0); },
                Frame = t =>
                {
                    eye = CellLayout.WelcomeSpot + Up(1.63f);
                    var ahead = Gaze(eye, robot, -12f, 6f);
                    var marker = P(1.8f, -1.2f, 0);
                    look = Vector3.Lerp(ahead, marker, 0.55f * Smooth(U(t, 10.2f, 12.6f)));
                    fov = 74;
                    At(t, 2.6f, "next1", () => app.Lessons.Next(fromButton: true));
                    var target = FindButton(app.Lessons.Panel.Window.transform, "Pull the trigger here");
                    if (target != null) aimPoint = target.position;
                    aimBlend = Mathf.Min(Smooth(U(t, 3.2f, 5.0f)), 1 - Smooth(U(t, 9.6f, 10.8f)));
                    rightTrigger = t > 6.6f && t < 6.95f ? 1 : 0;
                    At(t, 9.4f, "next2", () => app.Lessons.Next(fromButton: true));
                },
            };

            // The main menu: the beam picks the Robotics Academy, then points at module 5.
            yield return new Take
            {
                Name = "academy_menu", Seconds = 12,
                Setup = () => { HandsOn(); app.Menus.Open(app.Menus.Home); },
                Frame = t =>
                {
                    eye = P(3.0f, -2.0f, 1.63f);
                    look = Gaze(eye, robot, 0, 7f);
                    fov = 72;
                    var card = FindButton(app.Menus.Home.transform, "Robotics Academy");
                    var module = FindButton(app.Menus.Academy.transform, "5. Pick and place");
                    if (t < 5f && card != null) aimPoint = card.position;
                    else if (module != null) aimPoint = Vector3.Lerp(aimPoint, module.position, 0.12f);
                    aimBlend = Smooth(U(t, 1.4f, 3.2f));
                    rightTrigger = t > 4.1f && t < 4.4f ? 1 : 0;
                },
            };

            // Module 5: the task, with the part and the place zone marked on the bench, and the robot moving.
            yield return new Take
            {
                Name = "lesson_pick", Seconds = 12,
                Setup = () =>
                {
                    app.Lessons.Start(4);
                    app.Interaction.SetMode(ControlMode.Drive);
                    app.Cell.SetMode(JogMode.Cartesian);
                    app.Cell.SpeedOverride = 0.5f;
                    app.XR.Override = sticks;
                },
                Frame = t =>
                {
                    eye = CellLayout.OperatorSpot + Up(1.63f) + new Vector3(-0.1f * Smooth(U(t, 0, 12)), 0, 0);
                    look = Gaze(eye, CellLayout.BenchCentre, -10f, 22f);
                    fov = 64;
                    sticks.RightStick = new Vector2(0, Keys(t, 1.5f, 0, 2.1f, -0.6f, 4.6f, -0.6f, 5.2f, 0));
                    sticks.LeftStick = new Vector2(Keys(t, 6.0f, 0, 6.6f, -0.55f, 8.6f, -0.55f, 9.2f, 0), 0);
                },
            };

            // The joints' ranges as arcs, their needles moving as the program runs.
            yield return new Take
            {
                Name = "joint_arcs", Seconds = 11,
                Setup = () => { app.Joints.ShowAll = true; RunPick(false); },
                Frame = t =>
                {
                    eye = Orbit(b, 1.55f, 1.38f, Mathf.Lerp(-35, 25, Smoother(U(t, 0, 11))));
                    look = b + new Vector3(0.1f, 0.18f, 0);
                    fov = 42;
                },
            };

            // From the real robot to its digital twin, which follows it a moment later.
            yield return new Take
            {
                Name = "twin_mirror", Seconds = 13,
                Setup = () => RunPick(true),
                Frame = t =>
                {
                    eye = Vector3.Lerp(P(1.9f, -2.0f, 1.75f), P(4.9f, -1.7f, 1.85f), Smoother(U(t, 0, 13)));
                    look = Vector3.Lerp(robot, CellLayout.TwinBase + Up(0.25f), Smooth(U(t, 2f, 11f)));
                    fov = 46;
                },
            };

            // The twin's dashboard: live readings from the cell.
            yield return new Take
            {
                Name = "twin_dashboard", Seconds = 9,
                Setup = () => RunPick(true),
                Frame = t =>
                {
                    var d = app.Twin.Board.Window.transform.position;
                    var away = (CellLayout.TwinSpot + Up(1.6f) - d).normalized;
                    eye = d + away * Mathf.Lerp(2.3f, 1.45f, Smoother(U(t, 0, 9)));
                    look = d;
                    fov = 36;
                },
            };

            // Module 9: test a new program on the twin, see it predict a collision, raise the path, test again.
            // Two angles on the same run (the twin, then its dashboard), at 90 app frames a second as in the
            // headset, since the twin checks its path once a frame.
            yield return new Take
            {
                Name = "twin_test_ghost", Seconds = 20, Sub = 3,
                Setup = TwinTest,
                Frame = t =>
                {
                    HideLessonPanel();
                    var off = CellLayout.TwinBase - CellLayout.RobotBase;
                    var work = CellLayout.FromRobotWorld(0.42f, 0.0f, CellLayout.BenchTop + 0.12f) + off;
                    eye = Vector3.Lerp(work + new Vector3(-0.95f, 0.55f, -0.85f), work + new Vector3(-0.8f, 0.5f, -0.72f), Smooth(U(t, 0, 20)));
                    look = work;
                    fov = 42;
                    At(t, 1.2f, "simulate1", () => app.Twin.Board.OnSimulate?.Invoke());
                    At(t, 9.0f, "fix", () => app.Twin.Board.OnFix?.Invoke());
                    At(t, 10.6f, "simulate2", () => app.Twin.Board.OnSimulate?.Invoke());
                },
            };
            yield return new Take
            {
                Name = "twin_test_dash", Seconds = 20, Sub = 3,
                Setup = () => { TwinTest(); HandsOn(); },
                Frame = t =>
                {
                    HideLessonPanel();
                    var board = app.Twin.Board.Window.transform;
                    var d = board.position;
                    var away = (CellLayout.TwinSpot + Up(1.6f) - d).normalized;
                    eye = d + away * Mathf.Lerp(1.25f, 1.1f, Smooth(U(t, 0, 20))) + Vector3.down * 0.12f;
                    look = d + Vector3.down * 0.14f;
                    fov = 40;
                    var simulate = FindButton(board, "Simulate on twin");
                    var raise = FindButton(board, "Raise clearance");
                    var target = t < 8.2f || t > 9.8f ? simulate : raise;
                    if (target != null) aimPoint = Vector3.Lerp(aimPoint == Vector3.zero ? target.position : aimPoint, target.position, 0.18f);
                    aimBlend = Mathf.Min(Smooth(U(t, 0.2f, 0.9f)), 1 - Smooth(U(t, 12.4f, 13.4f)));
                    rightTrigger = (t > 1.2f && t < 1.45f) || (t > 9.0f && t < 9.25f) || (t > 10.6f && t < 10.85f) ? 1 : 0;
                },
            };

            // Walking into the scanner's fields: the robot slows, then stops, then carries on as you leave.
            yield return new Take
            {
                Name = "safety_walk", Seconds = 13,
                Setup = () =>
                {
                    app.Cell.Safety.SeparationMonitoring = true;
                    app.SafetyZones.Show(true);
                    RunPick(true);
                },
                Frame = t =>
                {
                    var a = P(3.1f, -1.7f, 1.65f);
                    var near = P(0.45f, -0.45f, 1.63f);
                    var back = P(2.4f, -1.4f, 1.64f);
                    eye = t < 6.5f ? Vector3.Lerp(a, near, Smoother(U(t, 0, 6.5f))) : Vector3.Lerp(near, back, Smoother(U(t, 9.0f, 13f)));
                    look = robot;
                    fov = 66;
                },
            };

            // The scanner's fields seen from above.
            yield return new Take
            {
                Name = "safety_overhead", Seconds = 10,
                Setup = () =>
                {
                    app.Cell.Safety.SeparationMonitoring = true;
                    app.SafetyZones.Show(true);
                    RunPick(true);
                },
                Frame = t =>
                {
                    eye = Orbit(b, 3.5f, 3.3f, Mathf.Lerp(-58, -26, Smoother(U(t, 0, 10))));
                    look = b;
                    fov = 42;
                },
            };

            // The robotics lab.
            yield return new Take
            {
                Name = "lab_palletizer", Seconds = 8,
                Frame = t =>
                {
                    eye = Vector3.Lerp(P(-4.6f, -0.3f, 2.0f), P(-5.3f, 0.3f, 1.85f), Smoother(U(t, 0, 8)));
                    look = P(-7.4f, 2.9f, 1.05f);
                    fov = 44;
                },
            };
            yield return new Take
            {
                Name = "lab_delta", Seconds = 8,
                Frame = t =>
                {
                    eye = Vector3.Lerp(P(0.4f, 4.3f, 2.1f), P(0.0f, 4.6f, 1.95f), Smoother(U(t, 0, 8)));
                    look = P(-1.4f, 6.65f, 0.95f);
                    fov = 42;
                },
            };
            yield return new Take
            {
                Name = "lab_cobots", Seconds = 8,
                Frame = t =>
                {
                    eye = Vector3.Lerp(P(8.0f, 1.2f, 1.85f), P(8.4f, 1.8f, 1.7f), Smoother(U(t, 0, 8)));
                    look = P(10.7f, 2.4f, 0.95f);
                    fov = 42;
                },
            };
            yield return new Take
            {
                Name = "lab_amr", Seconds = 9,
                Frame = t =>
                {
                    var amr = GameObject.Find("mobile robot");
                    var body = amr != null ? amr.transform.Find("body") : null;
                    var at = body != null ? body.position : P(2f, -4.4f, 0);
                    look = at + Up(0.35f);
                    eye = at + new Vector3(Mathf.Lerp(-2.6f, -1.9f, Smooth(U(t, 0, 9))), 1.25f, -2.2f);
                    fov = 44;
                },
            };
            yield return new Take
            {
                Name = "cnc_line", Seconds = 7,
                Frame = t =>
                {
                    float u = Smoother(U(t, 0, 7));
                    eye = Vector3.Lerp(P(-1.6f, 4.3f, 2.4f), P(1.6f, 4.6f, 2.3f), u);
                    look = Vector3.Lerp(P(5.2f, 9.4f, 1.3f), P(8.4f, 9.4f, 1.3f), u);
                    fov = 52;
                },
            };

            // A stop on the factory tour: the digital twin, with the tour's panel and arrow.
            yield return new Take
            {
                Name = "tour_stop", Seconds = 10,
                Setup = () =>
                {
                    app.Tour.Start();
                    for (int i = 0; i < 5; i++) app.Tour.Next();
                },
                Frame = t =>
                {
                    eye = CellLayout.TwinSpot + Up(1.63f);
                    look = Gaze(eye, CellLayout.TwinBase + Up(0.3f), 12f, 4f);
                    fov = 66;
                },
            };

            // The certificate, once all ten modules are done.
            yield return new Take
            {
                Name = "certificate", Seconds = 8,
                Setup = () =>
                {
                    foreach (var l in app.Lessons.Curriculum) app.Progress.MarkDone(l.Id, 1f);
                    app.Certificate.Show();
                },
                Frame = t =>
                {
                    eye = P(3.2f, -2.4f, 1.63f) + (robot - P(3.2f, -2.4f, 1.63f)).normalized * (0.12f * Smooth(U(t, 0, 8)));
                    look = Gaze(P(3.2f, -2.4f, 1.63f), robot, 0, 4f);
                    fov = 56;
                },
            };

            // The main menu up close: the title with the ADROIT logo.
            yield return new Take
            {
                Name = "home_logo", Seconds = 8,
                Setup = () => app.Menus.Open(app.Menus.Home),
                Frame = t =>
                {
                    var start = P(3.0f, -2.0f, 1.63f);
                    var ahead = Gaze(start, robot, 0, 7f);
                    eye = start + (ahead - start).normalized * (0.18f * Smoother(U(t, 0, 8)));
                    look = ahead;
                    fov = 50;
                },
            };

            // Reaching out and picking up the cube with the grip.
            yield return new Take
            {
                Name = "grab_cube", Seconds = 12,
                Setup = HandsOn,
                Frame = t =>
                {
                    var cube = app.Cube.transform;
                    eye = Vector3.Lerp(P(1.3f, 0.05f, 1.63f), P(0.78f, -0.06f, 1.56f), Smoother(U(t, 0, 3.6f)));
                    look = Vector3.Lerp(CellLayout.CubeSpot + Up(0.1f), CellLayout.CubeSpot + Up(0.32f), Smooth(U(t, 6.6f, 9.6f)));
                    fov = 72;
                    reachBlend = Mathf.Min(Smooth(U(t, 3.4f, 5.9f)), 1 - 0.55f * Smooth(U(t, 6.6f, 9.6f)));
                    reachPoint = t < 6.3f ? cube.position : CellLayout.CubeSpot + new Vector3(0.18f, 0.32f, 0.02f);
                    rightGrip = t > 6.1f;
                },
            };

            // The whole factory, pulling back and up.
            yield return new Take
            {
                Name = "hall_finale", Seconds = 16,
                Setup = () => RunPick(true),
                Frame = t =>
                {
                    float u = Smoother(U(t, 0, 16));
                    eye = Vector3.Lerp(P(1.9f, -3.3f, 2.15f), P(10.8f, -11.8f, 7.3f), u);
                    look = Vector3.Lerp(P(-0.3f, 0.1f, 0.95f), P(-1.2f, 1.6f, 0.6f), Smooth(U(t, 0, 16)));
                    fov = 48;
                },
            };
        }
    }
}
