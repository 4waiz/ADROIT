// SPDX-License-Identifier: LicenseRef-BRIDGE
// BRIDGE VR: the BRIDGE's Next-Gen Factory Digital Twin Simulation for Robotics, by Awaiz Ahmed.
// This component builds everything when the app starts (the factory, the lab, the Panda and its
// controller, the digital twin, the rig, the interface, the Academy) and runs one ordered tick each
// frame: input, lessons, the robot, the twin, the room, the interface, the sound. Lessons reach the
// rest of the app through the helpers at the end of this file.
//
// Command line: -desktop (never start XR), -autotest <scenario> -shots <folder> (scripted checks
// that save screenshots; see AutoTest), -lesson <n> (open straight into a module).

using System;
using System.Collections;
using System.Collections.Generic;
using Bridge.Learn;
using Bridge.Robot;
using Bridge.Twin;
using Bridge.UI;
using Bridge.World;
using Bridge.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bridge.App
{
    public class BridgeApp : MonoBehaviour
    {
        public static BridgeApp I;
        public Transform WorldRoot, UIRoot;
        public XRSystem XR;
        public Locomotion Locomotion;
        public Interaction Interaction;
        public Factory Factory;
        public Lab Lab;
        public RobotArm Panda;
        public Cell Cell;
        public DigitalTwin Twin;
        public Menus Menus;
        public Pendant Pendant;
        public Toasts Toasts;
        public LessonRunner Lessons;
        public Tour Tour;
        public Certificate Certificate;
        public SafetyZones SafetyZones;
        public JointViz Joints;
        public AxesViz Axes;
        public RobotArm Ghost;
        public Settings Settings;
        public Progress Progress;
        public Spectator Spectator;
        public Voice Voice;
        public readonly List<Graspable> Parts = new();
        public Graspable Cube;
        public readonly List<Graspable> Batch = new();
        public Transform Fixture;
        public bool Ready { get; private set; }
        public bool FreePlay { get; private set; }
        public string[] Args;
        AudioSource servo, gripper, alarm, hall;
        float lastOpening, duck = 1f;

        public static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        public static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            Time.fixedDeltaTime = 1f / 90f;
            Physics.defaultContactOffset = 0.004f;
            Settings.Ephemeral = HasArg("-autotest");
            Settings = Settings.Load();
            Progress = Progress.Load();
        }

        IEnumerator Start()
        {
            WorldRoot = new GameObject("World").transform;
            WorldRoot.SetParent(transform, false);
            UIRoot = new GameObject("Interface").transform;
            UIRoot.SetParent(transform, false);
            Sfx.Init(transform);
            Voice = Voice.Create(transform);

            Factory = new Factory(WorldRoot);
            Lab = new Lab(WorldRoot);
            yield return null;

            Panda = new RobotArm(WorldRoot, "Franka Panda", CellLayout.RobotBase, Quaternion.identity);
            Cell = new Cell(Panda);
            Ghost = new RobotArm(WorldRoot, "target ghost", CellLayout.RobotBase, Quaternion.identity, Mats.Hologram(new Color(0.35f, 0.7f, 1.5f), 1.2f, 0.08f), colliders: false);
            Ghost.Root.SetActive(false);
            MakeParts();
            Twin = new DigitalTwin(this);
            foreach (var p in Parts) Twin.TrackPart(p);
            SafetyZones = new SafetyZones(WorldRoot, CellLayout.RobotBase, Cell.Safety);
            Joints = new JointViz(WorldRoot, Panda);
            Axes = new AxesViz(WorldRoot);
            yield return null;

            XR = XRSystem.Create(transform, HasArg("-desktop") || HasArg("-autotest"));
            Locomotion = new Locomotion(XR, transform);
            Toasts = new Toasts(UIRoot);
            Lessons = new LessonRunner(this);
            Interaction = new Interaction(this);
            Tour = new Tour(this);
            Lessons.Build();
            Menus = new Menus(this);
            Pendant = new Pendant(this);
            Certificate = new Certificate(this);
            Spectator = Spectator.Create(this);
            ApplySettings();
            WireEvents();

            servo = Sfx.Loop("servo", Panda.Bodies[RobotArm.BodyIndex("link4")], 0f, 0.6f, 9f);
            gripper = Sfx.Loop("gripper", Panda.Hand, 0f, 0.4f, 6f);
            alarm = Sfx.Loop("alarm", Factory.Stack.Root, 0f, 1.5f, 25f);
            Sfx.Loop("conveyor", Factory.Root.Find("cell surround"), 0.25f, 1f, 12f);
            hall = Sfx.Loop("hall", null, 0.12f);

            var cam = XR.Cam.GetUniversalAdditionalCameraData();
            cam.renderPostProcessing = true;
            cam.antialiasing = AntialiasingMode.None;
            yield return null;
            Factory.RenderReflections();
            yield return null;

            XR.Place(CellLayout.WelcomeSpot, CellLayout.Facing(CellLayout.WelcomeSpot, CellLayout.RobotBase));
            if (!XR.XRActive) XR.SetDesktopView(CellLayout.WelcomeSpot + Vector3.up * 1.62f, Quaternion.LookRotation(CellLayout.Facing(CellLayout.WelcomeSpot, CellLayout.RobotBase)).eulerAngles.y, 6f);
            Ready = true;
            StartCoroutine(XR.StartXR());
            XR.XRChanged += on =>
            {
                ApplySettings();
                Toasts.Show(on ? "Headset connected" : "Headset disconnected", on ? "glasses" : "monitor", on ? Pal.Ok : Pal.Warn);
                if (on) StartCoroutine(PlaceAfterXR());
            };

            string auto = Arg("-autotest");
            string lesson = Arg("-lesson");
            if (auto != null) gameObject.AddComponent<AutoTest>().Run(auto, Arg("-shots"));
            else if (lesson != null && int.TryParse(lesson, out int li)) Lessons.Start(Mathf.Clamp(li - 1, 0, Lessons.Curriculum.Count - 1));
            else StartCoroutine(Welcome());
        }

        /// <summary>
        /// The first launches open on the welcome module, which shows how to point, walk, turn and grab,
        /// once the headset is on (or after a few seconds on the desktop). Later ones open on the main
        /// menu, with a word on how to use it.
        /// </summary>
        IEnumerator Welcome()
        {
            bool first = !Progress.Done("welcome") && Settings.WelcomeOffered < 2;
            if (!first)
            {
                Menus.Open(Menus.Home);
                Voice.Say(Menus.Greeting, 1.0f);
                yield break;
            }
            float waited = 0;
            while (!XR.XRActive && waited < 6f) { waited += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.8f);
            Settings.WelcomeOffered++;
            Settings.Save();
            if (!Lessons.Active && !Tour.Active && !Menus.AnyOpen) Lessons.Start(0);
        }

        IEnumerator PlaceAfterXR()
        {
            yield return null;
            yield return null;
            var spot = Lessons.Active && Lessons.Current.Spot.HasValue ? Lessons.Current.Spot.Value : CellLayout.WelcomeSpot;
            XR.Place(spot, CellLayout.Facing(spot, CellLayout.RobotBase));
            if (Menus.AnyOpen) Menus.Open(Menus.Home);
        }

        void MakeParts()
        {
            Cube = MakePart("cube", CellLayout.CubeSpot, 0.05f, new Color(0.95f, 0.34f, 0.13f));
            var batchColours = new[] { Pal.Hex(0x2f7cf6), Pal.Hex(0x1fa463), Pal.Hex(0x8b5cf6), Pal.Hex(0xe8a200) };
            for (int i = 0; i < 4; i++)
            {
                var g = MakePart("part " + (i + 1), CellLayout.FeederSpot + Vector3.up * (0.6f + i * 0.1f) + Vector3.right * 9f, 0.045f, batchColours[i]);
                g.gameObject.SetActive(false);
                Batch.Add(g);
            }
        }

        Graspable MakePart(string id, Vector3 benchPoint, float size, Color colour)
        {
            var go = new GameObject(id);
            go.layer = Layers.Grabbable;
            go.transform.SetParent(WorldRoot, false);
            go.transform.position = benchPoint + Vector3.up * (size / 2 + 0.001f);
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.RoundedBox(new Vector3(size, size, size), 0.004f);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mats.Paint(colour, 0.38f, 0.1f);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size, size, size);
            col.material = new PhysicsMaterial("part") { dynamicFriction = 0.7f, staticFriction = 0.8f, bounciness = 0.05f };
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var g = go.AddComponent<Graspable>();
            g.Id = id;
            g.Body = rb;
            g.Size = new Vector3(size, size, size);
            g.SpawnParent = WorldRoot;
            g.SpawnPosition = go.transform.position;
            g.SpawnRotation = Quaternion.identity;
            Parts.Add(g);
            Cell.Parts.Add(g);
            return g;
        }

        void WireEvents()
        {
            Cell.Event += (name, detail) =>
            {
                switch (name)
                {
                    case "protective_stop": Toasts.Show("Protective stop: " + detail, "octagon-alert", Pal.Err, 3.5f); Sfx.Play("warn"); break;
                    case "estop": Toasts.Show("Emergency stop", "octagon-x", Pal.Err, 3.5f); break;
                    case "reset": Toasts.Show("Stop reset: ready", "rotate-ccw", Pal.Ok); break;
                    case "resume": Toasts.Show("Clear: resuming", "play", Pal.Ok); break;
                    case "grasp": Sfx.Play("grasp"); break;
                    case "zone":
                        if (detail == "warning") { Toasts.Show("Warning field: reduced speed", "triangle-alert", Pal.Warn); Sfx.Play("beep", 0.7f); }
                        break;
                    case "out_of_reach": Toasts.Show("Out of reach", "crosshair", Pal.Warn, 1.6f); break;
                    case "joint_limit": Toasts.Show(detail + " at its limit", "rotate-3d", Pal.Warn, 1.6f); break;
                    case "mode": Joints.Mode = Cell.Mode; break;
                }
            };
            // The operator panel's red mushroom is the cell's emergency stop.
            var hmi = Factory.Hmi;
            var push = PushButton.Make(hmi.StopButton.parent, hmi.StopButton.Find("mushroom"), 0.035f, true, down =>
            {
                if (down) Cell.Safety.PressEStop();
                else Cell.Safety.ReleaseEStop();
            }, hmi.StopButton.localPosition);
            estop = push;
        }

        PushButton estop;

        // ------------------------------------------------------------------ the frame
        void Update()
        {
            if (!Ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Interaction.Tick(dt);
            Lessons.Tick(dt);
            Tour.Tick(dt);
            Cell.Tick(dt);
            Twin.Tick(dt);
            Factory.Tick(dt, Cell);
            Lab.Person = XR.Head.position;
            Lab.Tick(dt);
            SafetyZones.Tick(dt, XR.Head.position);
            Joints.Tick(dt, Cell, Interaction.Mode == ControlMode.Drive);
            Axes.Tick(Panda, Cell);
            Pendant.Tick(XR.Left.Grip, XR.XRActive, XR.Head);
            Toasts.Tick(XR.Head);
            Menus.Tick();
            Certificate.Tick(dt);
            estop.Set(Cell.Safety.EStopPressed);
            Sound(dt);
            if (Factory.Vignette != null) Factory.Vignette.intensity.value = Settings.Vignette ? Locomotion.Comfort * 0.45f : 0f;
            KeepPartsOnTheFloor();
        }

        void Sound(float dt)
        {
            float speed = 0;
            foreach (var v in Cell.JointVelocity) speed = Mathf.Max(speed, Mathf.Abs(v));
            if (servo != null)
            {
                servo.volume = Mathf.Lerp(servo.volume, Mathf.Clamp01(speed * 0.6f) * 0.55f * Sfx.Volume, 1 - Mathf.Exp(-10 * dt));
                servo.pitch = 0.8f + Mathf.Clamp01(speed) * 0.6f;
            }
            if (gripper != null)
            {
                float moving = Mathf.Abs(Panda.Opening - lastOpening) / Mathf.Max(dt, 1e-4f);
                gripper.volume = Mathf.Lerp(gripper.volume, moving > 0.005f ? 0.4f * Sfx.Volume : 0, 1 - Mathf.Exp(-20 * dt));
                lastOpening = Panda.Opening;
            }
            if (alarm != null) alarm.volume = Cell.Safety.Stop == StopKind.Emergency ? 0.35f * Sfx.Volume : 0;
            // The hall's hum dips while the narrator speaks.
            duck = Mathf.MoveTowards(duck, Voice != null && Voice.Speaking ? 0.45f : 1f, dt * 1.5f);
            if (hall != null) hall.volume = 0.12f * Sfx.Volume * duck;
        }

        /// <summary>A part dropped off the edge of the world goes back to its spot.</summary>
        void KeepPartsOnTheFloor()
        {
            foreach (var p in Parts)
                if (p.gameObject.activeSelf && p.transform.position.y < -1f) p.Respawn();
        }

        // ------------------------------------------------------------------ settings
        public void ApplySettings()
        {
            var s = Settings;
            Locomotion.StickWalk = s.StickWalk;
            // Scripted checks stay quiet.
            if (Voice != null) Voice.On = s.VoiceGuide && !Settings.Ephemeral;
            Locomotion.SnapAngle = s.SnapAngle;
            XR.SetSeated(s.Seated);
            Cell.RequireEnabling = s.RequireEnabling;
            Sfx.Volume = s.Volume;
            foreach (var m in Interaction.Models) if (!Lessons.Active) m.HintsForced = s.AlwaysHints;
            s.Save();
        }

        // ------------------------------------------------------------------ helpers for lessons
        /// <summary>Moves you to a floor spot facing a direction (with a fade in the headset).</summary>
        public void GoTo(Vector3 spot, Vector3 facing)
        {
            Locomotion.FadeThrough(() =>
            {
                XR.Place(spot, facing);
                if (!XR.XRActive) XR.SetDesktopView(new Vector3(spot.x, 1.62f, spot.z), Quaternion.LookRotation(facing).eulerAngles.y, 12f);
            }, 0.18f);
        }

        /// <summary>Puts the cell back as it starts: home pose, no stops, parts in place, no program.</summary>
        public void ResetCell(bool keepProgram = false)
        {
            Cell.StopProgram("reset");
            Cell.Safety.ReleaseEStop();
            Cell.Safety.Reset();
            Cell.Safety.Stop = StopKind.None;
            Cell.Safety.SeparationMonitoring = false;
            SafetyZones.Show(false);
            Cell.SetMode(JogMode.Idle);
            Cell.SetQ(Panda.Home);
            Cell.SpeedOverride = Settings.DefaultSpeed;
            Cell.OpenGripper();
            Panda.Opening = RobotArm.MaxOpening;
            Panda.Apply();
            Cell.Telemetry.Wear = 1f;
            if (!keepProgram) Cell.ClearProgram();
            ResetParts();
        }

        public void ResetParts()
        {
            Interaction.DropAll();
            Cell.ResetParts();
            foreach (var b in Batch) b.gameObject.SetActive(false);
            Cube.gameObject.SetActive(true);
        }

        public void QuickStartProgram()
        {
            var p = Cell.PickAndPlace(Cube.transform.position, CellLayout.PlaceZone + Vector3.up * 0.025f, 0.12f, "Pick and place");
            if (p == null) { Toasts.Show("The part is out of the arm's reach", "triangle-alert", Pal.Warn); return; }
            Cell.Program = p;
            Toasts.Show("Loaded: pick and place (10 steps)", "sparkles", Pal.Info);
        }

        /// <summary>A program that takes parts from the feeder to each tray pocket in turn.</summary>
        public RobotProgram TrayProgram(float clearance = 0.12f)
        {
            var pick = CellLayout.FeederSpot + Vector3.up * (0.0225f + 0.011f);
            var program = new RobotProgram { Name = "Feeder to tray" };
            var pockets = Factory.TrayPockets();
            for (int i = 0; i < pockets.Length; i++)
            {
                var one = Cell.PickAndPlace(pick, pockets[i] + Vector3.up * (0.0225f + 0.012f), clearance, "pocket " + (i + 1));
                if (one == null) return null;
                foreach (var s in one.Steps) if (s.Kind != StepKind.Home || i == pockets.Length - 1) program.Steps.Add(s);
                program.Points.AddRange(one.Points);
            }
            return program;
        }

        public void ShowGhost(float[] q)
        {
            Ghost.Root.SetActive(q != null);
            if (q != null) Ghost.SetQ(q);
        }

        public void StartFreePlay()
        {
            Lessons.Exit(silent: true);
            Tour.Stop();
            ResetCell();
            FreePlay = true;
            Interaction.DriveAllowed = true;
            Interaction.ModeSwitchAllowed = true;
            Interaction.MenuLocked = false;
            GoTo(CellLayout.OperatorSpot, CellLayout.OperatorFacing);
            Toasts.Show("Free play: press Y to drive the robot", "gamepad-2", Pal.Info, 4f);
        }

        public void PrepareForLesson()
        {
            FreePlay = false;
            ResetCell();
            Interaction.SetMode(ControlMode.Explore);
            Interaction.DriveAllowed = true;
            Interaction.ModeSwitchAllowed = true;
            Interaction.MenuLocked = false;
            Twin.Board.ShowCommissioning(false);
            ShowGhost(null);
            Joints.ShowAll = false;
            Axes.Show(false, false);
            Pendant.Show(false);
            SetFixture(false);
        }

        public void AfterLesson()
        {
            ShowGhost(null);
            Joints.ShowAll = false;
            Axes.Show(false, false);
            SetFixture(false);
            Twin.Board.ShowCommissioning(false);
            Cell.Safety.SeparationMonitoring = false;
            SafetyZones.Show(false);
            Cell.RequireEnabling = Settings.RequireEnabling;
            Cell.Telemetry.Wear = 1f;
        }

        /// <summary>The new fixture the digital-twin lesson bolts to the bench, between the part and the tray.</summary>
        public void SetFixture(bool on)
        {
            if (on && Fixture == null)
            {
                // Between the part and the tray, low enough that a high approach clears it.
                var at = CellLayout.FromRobotWorld(0.445f, 0.065f, CellLayout.BenchTop + 0.05f);
                Fixture = Go.Mesh("new fixture", WorldRoot, MeshKit.Box(), Mats.Panel(0x5d6670), at, null, new Vector3(0.04f, 0.10f, 0.03f)).transform;
                Fixture.gameObject.layer = Layers.Environment;
                Fixture.gameObject.AddComponent<BoxCollider>();
                Twin.SetFixture(Fixture);
            }
            else if (!on && Fixture != null)
            {
                Destroy(Fixture.gameObject);
                Fixture = null;
                Twin.SetFixture(null);
            }
        }

        public Vector3 HeadFloor => new(XR.Head.position.x, 0, XR.Head.position.z);
    }

    // ====================================================================== persistence
    [Serializable]
    public class Settings
    {
        // The right stick walks, unless someone prefers to jump (teleport) and turn with it.
        public bool StickWalk = true;
        // The narrator reads the lessons, hints and the tour.
        public bool VoiceGuide = true;
        // How many launches have opened on the welcome module, before it was finished.
        public int WelcomeOffered;
        public bool Seated;
        public bool AlwaysHints;
        public bool Vignette = true;
        public bool RequireEnabling;
        public float SnapAngle = 30f;
        public float DefaultSpeed = 0.5f;
        public float Volume = 0.9f;

        /// <summary>Scripted checks keep settings and progress in memory, so they never touch a trainee's own.</summary>
        public static bool Ephemeral;

        public static Settings Load()
        {
            if (Ephemeral) return new Settings();
            try
            {
                var s = PlayerPrefs.GetString("bridge.settings", "");
                if (!string.IsNullOrEmpty(s)) return JsonUtility.FromJson<Settings>(s);
            }
            catch (Exception) { }
            return new Settings();
        }

        public void Save()
        {
            if (!Ephemeral) PlayerPrefs.SetString("bridge.settings", JsonUtility.ToJson(this));
        }
    }

    [Serializable]
    public class Progress
    {
        public List<string> Completed = new();
        public List<float> Scores = new();
        public string FirstCompleted = "";

        public static Progress Load()
        {
            if (Settings.Ephemeral) return new Progress();
            try
            {
                var s = PlayerPrefs.GetString("bridge.progress", "");
                if (!string.IsNullOrEmpty(s)) return JsonUtility.FromJson<Progress>(s);
            }
            catch (Exception) { }
            return new Progress();
        }

        public bool Done(string id) => Completed.Contains(id);
        public int Count => Completed.Count;

        public void MarkDone(string id, float score)
        {
            int i = Completed.IndexOf(id);
            if (i < 0) { Completed.Add(id); Scores.Add(score); }
            else Scores[i] = Mathf.Max(Scores[i], score);
            if (string.IsNullOrEmpty(FirstCompleted)) FirstCompleted = DateTime.Now.ToString("d MMMM yyyy");
            Save();
        }

        public void ResetAll()
        {
            Completed.Clear();
            Scores.Clear();
            FirstCompleted = "";
            Save();
        }

        public void Save()
        {
            if (Settings.Ephemeral) return;
            PlayerPrefs.SetString("bridge.progress", JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
