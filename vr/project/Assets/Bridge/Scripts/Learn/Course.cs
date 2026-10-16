// SPDX-License-Identifier: LicenseRef-BRIDGE
// The Robotics Academy: ten hands-on modules, from finding your feet in VR to running a
// collaborative cell with its digital twin. Each step says what to look at and what to press;
// the app checks for itself when the step is done. The modules build on each other, and the
// trainer's guide (docs) follows the same order.

using System.Collections.Generic;
using Bridge.App;
using Bridge.Robot;
using Bridge.UI;
using Bridge.World;
using Bridge.XR;
using UnityEngine;
using Pose = Bridge.Robot.Pose;

namespace Bridge.Learn
{
    public static class Course
    {
        static BridgeApp app;
        static LessonRunner run;
        static Cell Cell => app.Cell;
        static RobotArm Arm => app.Panda;
        static Vector3 Tcp => Arm.Tcp.position;
        static Vector3 Head => app.XR.Head.position;
        static float Deg(float rad) => rad * Mathf.Rad2Deg;
        static (int, Control) L(Control c) => (0, c);
        static (int, Control) R(Control c) => (1, c);

        public static List<Lesson> Build(BridgeApp a, LessonRunner r)
        {
            app = a;
            run = r;
            return new List<Lesson> { Welcome(), MeetTheCobot(), JointJogging(), CartesianJogging(), PickAndPlace(), TeachAProgram(), HandGuiding(), Safety(), DigitalTwin(), Capstone() };
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static bool Facing(Vector3 target, float degrees = 30f)
        {
            var f = Vector3.ProjectOnPlane(app.XR.Head.forward, Vector3.up);
            var to = Vector3.ProjectOnPlane(target - Head, Vector3.up);
            return Vector3.Angle(f, to) < degrees;
        }

        static bool InZone(Graspable g) =>
            Mathf.Abs(g.transform.position.x - CellLayout.PlaceZone.x) < CellLayout.ZoneHalf &&
            Mathf.Abs(g.transform.position.z - CellLayout.PlaceZone.z) < CellLayout.ZoneHalf &&
            g.transform.position.y < CellLayout.BenchTop + 0.06f && !g.Held;

        // ================================================================== 1
        static Lesson Welcome()
        {
            var l = new Lesson { Id = "welcome", Title = "Welcome to the factory", Summary = "Point, walk, turn and grab", Icon = "glasses", Minutes = 5, Spot = CellLayout.WelcomeSpot };
            l.Facing = CellLayout.Facing(CellLayout.WelcomeSpot, CellLayout.RobotBase);
            var hello = l.Step("Welcome to the BRIDGE's Factory Digital Twin Floor",
                "You're standing in a digital factory. I'm your guide, and I'll take you through everything one step at a time.\n\nHave a look around. When you're ready, press <b>A</b> on your right controller.");
            hello.Highlight = new[] { R(Control.Primary) };

            var point = l.Step("Point and press",
                "Each controller sends out a beam. Point your right controller at the orange button below, then pull the <b>trigger</b> under your index finger.");
            bool pressed = false;
            point.Enter = () => pressed = false;
            point.ActionLabel = "Pull the trigger here";
            point.ActionIcon = "mouse-pointer-click";
            point.OnAction = () => pressed = true;
            point.Done = () => pressed;
            point.Highlight = new[] { L(Control.Trigger), R(Control.Trigger) };
            point.Hint = "Aim the beam at the orange button until it lights up, then squeeze the trigger.";

            var walk = l.Step("Walk",
                "Push the <b>right thumbstick</b> forward to walk where you're looking. Pull it back to step back, or push it to the side to step sideways.\n\nWalk over to the glowing circle on the floor.");
            FloorMarker ring = null;
            walk.Enter = () => ring = run.Scene.Marker(Web.P(1.8f, -1.2f, 0), "Walk here");
            walk.Done = () => ring != null && ring.Contains(Head);
            walk.Highlight = new[] { R(Control.Stick) };
            walk.Hint = "Push the right thumbstick gently forward and look where you want to go. To jump further, push the left thumbstick forward, aim the arc at the floor and let go.";

            var turn = l.Step("Turn",
                "Push the <b>left thumbstick</b> left or right to turn. You can also simply turn your body.\n\nTurn until you face the glowing blue robot. That's the digital twin, and you'll meet it later.");
            turn.Done = () => Facing(CellLayout.TwinBase, 25f);
            turn.Highlight = new[] { L(Control.Stick) };
            turn.Hint = "Flick the left thumbstick to the right, and keep going until the blue robot is in front of you.";

            var grab = l.Step("Pick something up",
                "Now walk up to the robot's bench. Put your hand on the orange cube and squeeze the <b>grip</b> button under your middle finger to pick it up. Let go of the grip to drop it.");
            grab.Enter = () => run.Scene.Arrow(app.Cube.transform, new Vector3(0, 0.06f, 0));
            grab.Done = () => run.Saw("button:grip:0") && app.Interaction.Holding(0) || run.Saw("button:grip:1") && app.Interaction.Holding(1);
            grab.Highlight = new[] { L(Control.Grip), R(Control.Grip) };
            grab.Hint = "Put your hand right inside the cube before you squeeze the grip.";

            var labels = l.Step("Every button has a label",
                "Look down at your controllers. The labels beside the buttons tell you what each one does, and they change with what you're doing.\n\nThe <b>Y</b> button switches between walking and driving the robot. You'll try it in module 3.");
            labels.Highlight = new[] { L(Control.Secondary), R(Control.Primary) };

            l.Step("The main menu",
                "The small <b>Menu</b> button on your left controller, the one with three lines, opens the main menu at any time. From there you can start any module, take the factory tour, or change settings.\n\nThat's all you need to get around. Press <b>A</b> to finish this module.");
            l.End = () => app.ResetParts();
            return l;
        }

        // ================================================================== 2
        static Lesson MeetTheCobot()
        {
            var l = new Lesson { Id = "meet", Title = "Meet the cobot", Summary = "Parts, joints and the TCP", Icon = "bot", Minutes = 6, Spot = CellLayout.OperatorSpot, Facing = CellLayout.OperatorFacing };
            l.Step("A collaborative robot",
                "This is a <b>Franka Emika Panda</b>, a collaborative robot. Collaborative means it is designed to work near people: it is light, rounded, senses the force in every joint, and limits its speed and force.\n\nIt stands on a bolted pedestal beside its work bench, and the gripper on its wrist has two fingers.");

            var parts = l.Step("Name the parts",
                "Labels name each part of the arm, from the base to the gripper. Point your ray at the arm's parts one by one: each lights up as you find it. Find five parts.");
            var found = new HashSet<string>();
            parts.Enter = () =>
            {
                found.Clear();
                Label("link0", "Base", "Bolted to the pedestal", new Vector3(0.25f, 0.05f, -0.2f));
                Label("link1", "J1 base joint", "Swings the whole arm round", new Vector3(-0.28f, 0.1f, -0.15f));
                Label("link2", "J2 shoulder", "Tilts the arm forward and back", new Vector3(-0.3f, 0.16f, 0.12f));
                Label("link3", "J3 upper arm", "Twists the upper arm", new Vector3(-0.15f, 0.25f, 0.3f));
                Label("link4", "J4 elbow", "Bends the arm", new Vector3(0.1f, 0.25f, 0.32f));
                Label("link5", "J5 forearm", "Twists the forearm", new Vector3(0.3f, 0.18f, 0.25f));
                Label("link6", "J6 wrist", "Tilts the hand", new Vector3(0.32f, 0.15f, -0.2f));
                Label("link7", "J7 flange", "Turns the tool", new Vector3(0.3f, 0.05f, -0.32f));
                Label("right_gripper", "Gripper", "Two fingers, up to 80 mm apart", new Vector3(0.25f, -0.08f, -0.35f));
            };
            parts.Tick = dt =>
            {
                var p = app.Interaction.Pointed;
                if (string.IsNullOrEmpty(p)) return;
                if (p == "leftfinger" || p == "rightfinger" || p == "right_hand" || p == "eef") p = "right_gripper";
                if (found.Add(p)) { Sfx.Play("beep", 0.6f); Arm.Tint(RobotArm.BodyIndex(p), Pal.Brand * 0.5f); }
            };
            parts.Exit = () => Arm.ClearTint();
            parts.Done = () => found.Count >= 5;
            parts.Status = () => $"Found {Mathf.Min(found.Count, 5)} of 5";
            parts.Highlight = new[] { R(Control.Trigger) };

            var seven = l.Step("Seven joints, one hand",
                "Each joint is a motor that turns the next link. Six joints are enough to put a hand at any position, pointing any way. The Panda has a <b>seventh</b>, so it can move its elbow while its hand stays perfectly still.\n\nWatch the elbow swing while the gripper holds its place.");
            float clock = 0;
            Pose hold = default;
            var qOut = new float[RobotArm.Dof];
            seven.Enter = () =>
            {
                clock = 0;
                Cell.SetQ(Arm.Home);
                hold = Arm.TcpPose;
            };
            seven.Tick = dt =>
            {
                clock += dt;
                var preferred = (float[])Arm.Home.Clone();
                preferred[0] = Mathf.Sin(clock * 0.9f) * 0.75f;
                preferred[2] = -Mathf.Sin(clock * 0.9f) * 0.9f;
                Arm.Solve(hold, Arm.Q, qOut, iterations: 30, preferred: preferred, nullGain: 0.35f);
                Arm.SetQ(qOut);
            };
            seven.Exit = () => Cell.SetQ(Arm.Home);
            seven.Done = () => clock > 7f;
            seven.Auto = false;
            seven.Status = () => $"Hand moved {Vector3.Distance(Tcp, hold.position) * 1000:0} mm while the elbow swung";

            var limits = l.Step("Every joint has limits",
                "Arcs now show each joint's range: the white band is where it can go, the red ends are its limits, and the orange needle is where it is now. A joint cannot turn past its limits, which is one reason a robot cannot reach every pose.");
            limits.Enter = () => app.Joints.ShowAll = true;
            limits.Exit = () => app.Joints.ShowAll = false;

            var tcp = l.Step("The tool centre point",
                "The <b>TCP</b>, the tool centre point, is the point the robot steers: here, between the gripper's fingertips. Its red, green and blue arrows are its x, y and z axes. When you move the robot in a straight line, it is the TCP that travels straight.");
            tcp.Enter = () => { app.Axes.Show(true, true); run.Scene.Label("TCP", "Tool centre point", Arm.Tcp, new Vector3(0.18f, 0.12f, -0.12f)); };
            tcp.Exit = () => app.Axes.Show(false, false);

            l.Ask("Why does the Panda have seven joints instead of six?",
                "The seventh joint is redundancy: the arm can change its posture (its elbow) without moving its hand, which helps it avoid obstacles and its own joint limits.",
                1, "To lift heavier parts", "So it can move its elbow while its hand stays still", "To move faster", "Six joints cannot reach the bench");
            l.Ask("What makes a robot collaborative?",
                "Collaborative robots are designed to share a workspace with people: limited force and speed, force sensing, rounded shapes, and safety functions that stop them on contact.",
                1, "It works alongside other robots", "It is designed to work safely near people", "It is controlled over the internet");
            return l;
        }

        static void Label(string body, string title, string sub, Vector3 offset)
        {
            run.Scene.Label(title, sub, Arm.Bodies[RobotArm.BodyIndex(body)], offset);
        }

        // ================================================================== 3
        static Lesson JointJogging()
        {
            var l = new Lesson { Id = "joint", Title = "Joint jogging", Summary = "Turn one joint at a time", Icon = "rotate-3d", Minutes = 6, Spot = CellLayout.OperatorSpot, Facing = CellLayout.OperatorFacing };
            var take = l.Step("Take control",
                "Press <b>Y</b> to switch to Drive mode. Your teach pendant, the robot's control tablet, appears in your left hand, and the sticks now move the robot instead of you.");
            take.Done = () => app.Interaction.Mode == ControlMode.Drive;
            take.Highlight = new[] { L(Control.Secondary) };
            take.Exit = () => { Cell.SetMode(JogMode.Joint); Cell.SelectedJoint = 0; };

            var pick = l.Step("Pick a joint",
                "You are in <b>joint mode</b>. Push the <b>left stick</b> left or right to choose a joint: the chosen joint's range lights up as an arc, and the pendant highlights it.");
            int picks = 0, last = 0;
            pick.Enter = () => { Cell.SetMode(JogMode.Joint); picks = 0; last = Cell.SelectedJoint; };
            pick.Tick = dt => { if (Cell.SelectedJoint != last) { picks++; last = Cell.SelectedJoint; } };
            pick.Done = () => picks >= 3;
            pick.Status = () => $"Chosen: J{Cell.SelectedJoint + 1} ({RobotArm.JointLabel(Cell.SelectedJoint)})";
            pick.Highlight = new[] { L(Control.Stick) };

            var turn = l.Step("Turn the base",
                "Choose <b>J1</b>, then push the <b>right stick</b> up or down to turn it. The whole arm swings round its base.");
            float start = 0;
            turn.Enter = () => { Cell.SetMode(JogMode.Joint); start = Arm.Q[0]; };
            turn.Done = () => Mathf.Abs(Arm.Q[0] - start) > 20 * Mathf.Deg2Rad;
            turn.Status = () => $"J1 has turned {Mathf.Abs(Deg(Arm.Q[0] - start)):0}° of 20°";
            turn.Highlight = new[] { L(Control.Stick), R(Control.Stick) };
            turn.Hint = "The pendant shows which joint is chosen; push the left stick left until it says J1.";

            var limit = l.Step("Find a limit",
                "Choose <b>J4</b>, the elbow, and keep turning it in one direction until it stops. The needle reaches the red end of its arc, the controller buzzes and the pendant says <b>Joint limit</b>.");
            limit.Enter = () => Cell.SetMode(JogMode.Joint);
            limit.Done = () => run.Saw("joint_limit");
            limit.Highlight = new[] { R(Control.Stick) };

            var match = l.Step("Match the ghost",
                "A blue ghost shows a target pose. Turn the joints until the robot matches it: each joint within 5°. This is <b>forward kinematics</b>: the joint angles decide where the hand ends up.");
            float[] goal = null;
            match.Enter = () =>
            {
                Cell.SetMode(JogMode.Joint);
                goal = SafeTarget(new[] { (0, 0.55f), (1, -0.2f), (3, 0.35f) }, new[] { (0, -0.55f), (1, -0.25f), (3, 0.3f) }, new[] { (0, 0.45f), (3, -0.25f) });
                app.ShowGhost(goal);
            };
            match.Exit = () => app.ShowGhost(null);
            match.Done = () => goal != null && MaxError(goal) < 5f;
            match.Status = () =>
            {
                if (goal == null) return "";
                var parts = new List<string>();
                for (int j = 0; j < 4; j++) parts.Add($"J{j + 1} {Deg(goal[j] - Arm.Q[j]):+0;-0}°");
                return "Still to go: " + string.Join("   ", parts);
            };
            match.Hint = "Work joint by joint, J1 first. Only J1, J2 and J4 differ from where the robot started.";

            var speed = l.Step("Speed",
                "Press the <b>right stick</b> in to change the speed: 10, 25, 50 or 100 percent. People teach robots at low speed and run the finished program faster.");
            float s0 = 0;
            speed.Enter = () => s0 = Cell.SpeedOverride;
            speed.Done = () => Mathf.Abs(Cell.SpeedOverride - s0) > 0.01f;
            speed.Status = () => $"Speed {Cell.SpeedOverride * 100:0}%";
            speed.Highlight = new[] { R(Control.Stick) };

            l.Ask("You turn only J1, the base joint. What does the hand do?",
                "J1 turns everything above it about the base's vertical axis, so the hand swings round in a circle at the same height.",
                1, "It moves straight up", "It swings round the base", "Nothing, only the base turns");
            l.End = () => { app.ShowGhost(null); app.Interaction.SetMode(ControlMode.Explore); };
            return l;
        }

        /// <summary>The first joint offset set whose pose stays clear of the bench and above it.</summary>
        static float[] SafeTarget(params (int joint, float delta)[][] candidates)
        {
            foreach (var set in candidates)
            {
                var q = (float[])Arm.Home.Clone();
                foreach (var (j, d) in set) q[j] = Mathf.Clamp(q[j] + d, Arm.QMin[j] + 0.1f, Arm.QMax[j] - 0.1f);
                var tcp = Arm.Forward(q).position;
                if (tcp.y > CellLayout.BenchTop + 0.15f && !Arm.Collides(q, RobotArm.MaxOpening, Layers.EnvironmentMask, out _)) return q;
            }
            var fallback = (float[])Arm.Home.Clone();
            fallback[0] += 0.5f;
            return fallback;
        }

        static float MaxError(float[] goal)
        {
            float e = 0;
            for (int j = 0; j < RobotArm.Dof; j++) e = Mathf.Max(e, Mathf.Abs(Deg(goal[j] - Arm.Q[j])));
            return e;
        }

        // ================================================================== 4
        static Lesson CartesianJogging()
        {
            var l = new Lesson { Id = "cartesian", Title = "Moving the hand", Summary = "Cartesian jogging and frames", Icon = "move-3d", Minutes = 7, Spot = CellLayout.OperatorSpot, Facing = CellLayout.OperatorFacing };
            var mode = l.Step("Move the hand, not the joints",
                "Press <b>Y</b> to drive, then press <b>B</b> until the pendant shows <b>Cartesian</b>. Now the <b>left stick</b> moves the hand across the floor (forward pushes it away from you) and the <b>right stick</b> moves it up and down.\n\nYou move the hand; the controller works out every joint for you. That is <b>inverse kinematics</b>.");
            Vector3 p0 = default;
            mode.Enter = () => { p0 = Tcp; };
            mode.Done = () => app.Interaction.Mode == ControlMode.Drive && Cell.Mode == JogMode.Cartesian && Vector3.Distance(Tcp, p0) > 0.06f;
            mode.Highlight = new[] { L(Control.Secondary), R(Control.Secondary), L(Control.Stick), R(Control.Stick) };

            var targets = l.Step("Touch three targets",
                "Move the gripper's fingertips into each glowing sphere. Each one turns green when you reach it.");
            var spheres = new List<HandTarget>();
            targets.Enter = () =>
            {
                if (app.Interaction.Mode != ControlMode.Drive) app.Interaction.SetMode(ControlMode.Drive);
                Cell.SetMode(JogMode.Cartesian);
                spheres.Clear();
                spheres.Add(run.Scene.Target(CellLayout.FromRobotWorld(0.42f, -0.24f, 1.05f), "1"));
                spheres.Add(run.Scene.Target(CellLayout.FromRobotWorld(0.52f, 0.12f, 0.93f), "2"));
                spheres.Add(run.Scene.Target(CellLayout.FromRobotWorld(0.32f, 0.26f, 1.18f), "3"));
            };
            targets.Tick = dt => { foreach (var t in spheres) if (!t.Reached && Vector3.Distance(Tcp, t.transform.position) < 0.03f) { t.MarkReached(); Sfx.Play("waypoint"); } };
            targets.Done = () => spheres.Count == 3 && spheres.TrueForAll(t => t.Reached);
            targets.Status = () => $"{spheres.FindAll(t => t.Reached).Count} of 3 reached";
            targets.Hint = "Line up from the side first, then adjust the height with the right stick.";

            var frames = l.Step("Frames",
                "Press the <b>left stick</b> in to change the <b>jog frame</b>, the directions the sticks move along. <b>Your view</b>: forward is away from you. <b>Robot base</b>: the base's own x (red), y (green) and z (blue) axes. <b>Tool</b>: the gripper's own axes, handy when the tool is tilted. Try all three.");
            var seenFrames = new HashSet<JogFrame>();
            frames.Enter = () => { seenFrames.Clear(); app.Axes.Show(true, true); };
            frames.Tick = dt => seenFrames.Add(Cell.Frame);
            frames.Exit = () => app.Axes.Show(false, false);
            frames.Done = () => seenFrames.Count >= 3;
            frames.Status = () => $"Frame: {Interaction.FrameName(Cell.Frame)}  ({seenFrames.Count} of 3 tried)";
            frames.Highlight = new[] { L(Control.Stick) };

            var wrist = l.Step("Turn the wrist",
                "Push the <b>right stick</b> left or right to turn the gripper about its own axis. Turn it a quarter turn, as you would to line the fingers up with a part's faces.");
            Quaternion r0 = Quaternion.identity;
            wrist.Enter = () => r0 = Arm.Tcp.rotation;
            wrist.Done = () => Quaternion.Angle(r0, Arm.Tcp.rotation) > 45f;
            wrist.Status = () => $"Turned {Quaternion.Angle(r0, Arm.Tcp.rotation):0}° of 45°";
            wrist.Highlight = new[] { R(Control.Stick) };

            var edge = l.Step("The edge of the workspace",
                "Push the hand as far from the robot as it will go. When the arm is stretched out it cannot follow: the pendant says <b>Out of reach</b>. Near such stretched poses (singularities) a robot loses the ability to move its hand in some directions.");
            edge.Done = () => run.Saw("out_of_reach") || Cell.NearSingularity;
            edge.Highlight = new[] { L(Control.Stick) };

            var bump = l.Step("The robot won't hit the bench",
                "Lower the hand onto the bench and keep pushing down. The controller refuses to drive into the bench: the arm stops at contact and the controller buzzes.");
            bump.Done = () => run.Saw("blocked");
            bump.Highlight = new[] { R(Control.Stick) };

            l.Ask("What does inverse kinematics work out?",
                "Inverse kinematics finds the joint angles that put the hand at a position and orientation you ask for. Forward kinematics goes the other way, from joint angles to the hand's pose.",
                0, "The joint angles that put the hand where you want it", "Where the hand is, from the joint angles", "How fast the robot can move");
            l.End = () => app.Interaction.SetMode(ControlMode.Explore);
            return l;
        }

        // ================================================================== 5
        static Lesson PickAndPlace()
        {
            var l = new Lesson { Id = "pick", Title = "Pick and place", Summary = "Grip a part and set it down", Icon = "hand-grab", Minutes = 8, Spot = CellLayout.OperatorSpot, Facing = CellLayout.OperatorFacing };
            var task = l.Step("The task",
                "Pick up the orange cube and put it in the green square: the same pick and place the browser version of this simulation runs. Work like a robot programmer: approach from above, lower, grip, lift, move, lower, release, retreat.");
            task.Enter = () =>
            {
                run.Scene.Label("Part", "50 mm cube", app.Cube.transform, new Vector3(0.1f, 0.2f, -0.12f));
                run.Scene.Label("Place zone", "80 mm square", app.Factory.PlaceZoneMarker.transform, new Vector3(0.12f, 0.22f, 0.1f));
            };

            var open = l.Step("Open the gripper",
                "Press <b>Y</b> to drive. The <b>A</b> button opens and closes the gripper. Make sure it is open before you approach.");
            open.Done = () => app.Interaction.Mode == ControlMode.Drive && !Cell.GripperClosed && Arm.Opening > 0.07f;
            open.Highlight = new[] { L(Control.Secondary), R(Control.Primary) };

            var approach = l.Step("Approach from above",
                "Move the hand above the cube, about 10 cm up, in <b>Cartesian</b> mode. Turn the wrist so the fingers will close across two opposite faces.");
            approach.Enter = () => { Cell.SetMode(JogMode.Cartesian); run.Scene.Arrow(app.Cube.transform, new Vector3(0, 0.15f, 0)); };
            approach.Done = () => Flat(Tcp, app.Cube.transform.position) < 0.03f && Tcp.y - app.Cube.transform.position.y > 0.05f && Tcp.y - app.Cube.transform.position.y < 0.2f;
            approach.Status = () => $"{Flat(Tcp, app.Cube.transform.position) * 100:0.0} cm to the side, {(Tcp.y - app.Cube.transform.position.y) * 100:0} cm above";

            var lower = l.Step("Lower onto the part",
                "Lower the hand straight down until the cube sits between the fingers. Slow down near the part: press the right stick in for 10% speed.");
            lower.Done = () => Vector3.Distance(Tcp, app.Cube.transform.position) < 0.018f;
            lower.Status = () => $"{Vector3.Distance(Tcp, app.Cube.transform.position) * 100:0.0} cm from the centre of the cube";
            lower.Highlight = new[] { R(Control.Stick) };

            var grip = l.Step("Grip",
                "Press <b>A</b> to close. The fingers stop when they touch the cube: the gripper is holding it.");
            grip.Done = () => Cell.Held == app.Cube;
            grip.Highlight = new[] { R(Control.Primary) };
            grip.Hint = "If the fingers closed on nothing, press A to open, line up again and retry.";

            var move = l.Step("Lift and carry",
                "Lift the cube clear of the bench, then carry it over the green square.");
            move.Enter = () => run.Scene.Arrow(app.Factory.PlaceZoneMarker.transform, new Vector3(0, 0.12f, 0));
            move.Done = () => Cell.Held == app.Cube && Flat(app.Cube.transform.position, CellLayout.PlaceZone) < 0.03f && app.Cube.transform.position.y > CellLayout.BenchTop + 0.05f;
            move.Status = () => $"{Flat(app.Cube.transform.position, CellLayout.PlaceZone) * 100:0.0} cm from the zone";

            var place = l.Step("Place and retreat",
                "Lower the cube onto the square, press <b>A</b> to let go, then lift the hand away.");
            place.Done = () => InZone(app.Cube) && Tcp.y - app.Cube.transform.position.y > 0.05f && app.Cube.Body.linearVelocity.magnitude < 0.02f;
            place.Highlight = new[] { R(Control.Primary) };
            place.ActionLabel = "Reset the cube";
            place.OnAction = () => app.ResetParts();

            l.Ask("Why does a robot approach a part from above before gripping it?",
                "A straight approach along the gripper's axis lines the fingers up with the part and keeps the hand from knocking it sideways.",
                0, "To line up the grip without knocking the part", "Because robots cannot move sideways", "To save energy");
            l.End = () => app.Interaction.SetMode(ControlMode.Explore);
            return l;
        }

        // ================================================================== 6
        static Lesson TeachAProgram()
        {
            var l = new Lesson { Id = "teach", Title = "Teach a program", Summary = "Waypoints, moves, run and loop", Icon = "list-ordered", Minutes = 9, Spot = CellLayout.OperatorSpot, Facing = CellLayout.OperatorFacing };
            l.Begin = () => Cell.ClearProgram();
            l.Step("Programs are waypoints",
                "Robots repeat programs: lists of places to go (waypoints) and things to do there. You will teach the pick and place you just did, then let the robot repeat it.\n\nOpen the pendant's <b>Program</b> tab to watch your program grow.");

            var w1 = l.Step("Waypoint 1: above the part",
                "Drive (<b>Y</b>), move the hand above the cube, and press <b>X</b> to save this spot.");
            w1.Enter = () => { app.Pendant.Show(true); app.Pendant.SetTab("program"); };
            w1.Done = () => Cell.Program.Points.Count >= 1;
            w1.Highlight = new[] { L(Control.Secondary), L(Control.Primary) };

            var w2 = l.Step("Waypoint 2: at the part, then grip",
                "Lower the hand to the cube and press <b>X</b>. Then tap <b>+ Close</b> on the pendant to add a gripper step.");
            w2.Done = () => Cell.Program.Points.Count >= 2 && Cell.Program.Steps.Exists(s => s.Kind == StepKind.Close);
            w2.Highlight = new[] { L(Control.Primary) };

            var w3 = l.Step("Carry and release",
                "Save three more spots with <b>X</b>: lifted above the cube, above the green square, down at the square. Then tap <b>+ Open</b>, and save one last spot lifted away.");
            w3.Done = () => Cell.Program.Points.Count >= 5 && Cell.Program.Steps.Exists(s => s.Kind == StepKind.Open);
            w3.Status = () => $"{Cell.Program.Points.Count} waypoints, {(Cell.Program.Steps.Exists(s => s.Kind == StepKind.Close) ? "close" : "no close")}, {(Cell.Program.Steps.Exists(s => s.Kind == StepKind.Open) ? "open" : "no open yet")}";

            var runIt = l.Step("Run it",
                "Tap <b>Reset parts</b> on the pendant to put the cube back, then tap <b>Run</b>. The robot plays back your program step by step: the pendant marks the step it is on.");
            runIt.Done = () => run.Saw("cycle");
            runIt.ActionLabel = "Reset parts";
            runIt.OnAction = () => app.ResetParts();
            runIt.Hint = "If the robot misses the cube, adjust the grip waypoint: clear the program and teach it again, a little lower.";

            var moves = l.Step("Joint moves and linear moves",
                "Every spot you saved is a <b>joint move</b> (MoveJ): quick for the robot, but the hand travels on a curve. Near parts, programs use <b>linear moves</b> (MoveL), where the hand travels in a straight line.\n\nTap <b>Quick start</b> on the pendant to load a program that uses both, then <b>Run</b> it and watch the straight approaches.");
            moves.Done = () => run.SawDetail("cycle:Pick and place");
            moves.ActionLabel = "Reset parts";
            moves.OnAction = () => app.ResetParts();

            var fast = l.Step("Cycle time",
                "The pendant shows the <b>cycle time</b>. Set the speed slider to 100% and tap <b>Loop</b>: faster cycles mean more parts an hour. Let it complete a cycle, then tap <b>Stop</b>.");
            fast.Enter = () => app.ResetParts();
            fast.Done = () => run.Saw("cycle") && Cell.SpeedOverride > 0.74f;
            fast.Status = () => $"Speed {Cell.SpeedOverride * 100:0}%, last cycle {(Cell.LastCycleTime > 0 ? Cell.LastCycleTime.ToString("0.0") + " s" : "-")}";

            l.Ask("Which kind of move keeps the hand on a straight line?",
                "A linear move (MoveL) interpolates the tool's position in a straight line; a joint move (MoveJ) interpolates the joint angles, so the hand's path curves.",
                1, "MoveJ, a joint move", "MoveL, a linear move", "Both");
            l.End = () => { Cell.StopProgram(); app.Interaction.SetMode(ControlMode.Explore); };
            return l;
        }

        // ================================================================== 7
        static Lesson HandGuiding()
        {
            var l = new Lesson { Id = "guide", Title = "Hand guiding", Summary = "Teach the arm by hand", Icon = "hand", Minutes = 5, Spot = CellLayout.GuideSpot };
            l.Facing = CellLayout.Facing(CellLayout.GuideSpot, CellLayout.CubeSpot);
            l.Begin = () => Cell.ClearProgram();
            var free = l.Step("Free drive",
                "Cobots can be taught by hand. Press <b>Y</b> to drive, then press <b>B</b> until the pendant shows <b>Hand guide</b>. In this mode the arm floats: it holds itself up and lets your hand move it.");
            free.Done = () => Cell.Mode == JogMode.FreeDrive;
            free.Highlight = new[] { L(Control.Secondary), R(Control.Secondary) };

            var take = l.Step("Take the wrist",
                "Reach for the robot's wrist, just above the gripper, squeeze the <b>grip</b> and move your hand. The arm follows; let go and it stays where you left it.");
            float moved = 0;
            Vector3 last = default;
            take.Enter = () => { moved = 0; last = Tcp; run.Scene.Arrow(Arm.Hand, new Vector3(0, 0.1f, 0)); };
            take.Tick = dt => { if (app.Interaction.Guiding) moved += Vector3.Distance(Tcp, last); last = Tcp; };
            take.Done = () => moved > 0.25f;
            take.Status = () => $"Guided {moved * 100:0} cm of 25 cm";
            take.Highlight = new[] { L(Control.Grip), R(Control.Grip) };
            take.Hint = "Put your controller right on the wrist (the white joint above the fingers) before you squeeze.";

            var teach = l.Step("Teach a spot by hand",
                "Guide the hand above the green square and press <b>X</b> to save it as a waypoint, just as you did with the sticks.");
            teach.Done = () => Cell.Program.Points.Count >= 1;
            teach.Highlight = new[] { L(Control.Primary) };

            l.Step("Why it matters",
                "Hand guiding is one of the collaborative modes ISO 10218 describes. It lets people who have never written code teach a cobot in minutes, and it keeps the expert, the person who knows the job, in charge. That is a core idea of <b>Industry 5.0</b>: technology that adapts to people.");
            l.Ask("In hand-guiding mode, what moves the robot?",
                "In hand guiding the operator's hand moves the arm directly; the controller supports its weight and follows the force the hand applies.",
                2, "The thumbsticks", "A taught program", "The operator's hand on the arm");
            l.End = () => { Cell.SetMode(JogMode.Idle); app.Interaction.SetMode(ControlMode.Explore); };
            return l;
        }

        // ================================================================== 8
        static Lesson Safety()
        {
            var l = new Lesson { Id = "safety", Title = "Safety and collaboration", Summary = "Fields, force limits, e-stop", Icon = "shield-check", Minutes = 10, Spot = CellLayout.SafetySpot };
            l.Facing = CellLayout.Facing(CellLayout.SafetySpot, CellLayout.RobotBase);
            void Production()
            {
                app.ResetParts();
                app.QuickStartProgram();
                Cell.SpeedOverride = 0.6f;
                if (!Cell.Running) Cell.StartProgram(loop: true);
            }
            l.Step("Four ways to share a workspace",
                "The safety standard for industrial robots, ISO 10218 (since its 2025 edition it also holds what ISO/TS 15066 said about working together), describes four ways people and robots can share space: <b>safety-rated monitored stop</b>, <b>hand guiding</b>, <b>speed and separation monitoring</b> and <b>power and force limiting</b>. You already used hand guiding; now you will try the others.");

            var running = l.Step("The cell is running",
                "The robot is now running its program in a loop. Look at the cabinet's stack light: <b>green</b> means running. The floor scanner's two fields are drawn round the robot: amber for warning, red for protective.");
            running.Enter = () => { Production(); Cell.Safety.SeparationMonitoring = true; app.SafetyZones.Show(true); };
            running.Done = () => Cell.Running;
            running.Auto = false;

            var warn = l.Step("Speed and separation monitoring",
                "Walk slowly towards the robot until you are inside the <b>amber</b> field. Watch the robot and the stack light.");
            warn.Enter = () => { if (!Cell.Running) Production(); Cell.Safety.SeparationMonitoring = true; app.SafetyZones.Show(true); };
            warn.Done = () => Cell.Safety.Zone == "warning";
            warn.Status = () => $"You are {Cell.Safety.HumanDistance:0.0} m from the robot (amber field: {Cell.Safety.WarningRadius:0.0} m)";
            warn.Hint = "Walk a little closer with the right thumbstick, or step forward in your play space.";

            var stop = l.Step("Protective stop",
                "The robot slowed to 30% and the light turned <b>amber</b>. Now step into the <b>red</b> field.");
            stop.Done = () => Cell.Safety.Stop == StopKind.Protective;
            stop.Status = () => $"You are {Cell.Safety.HumanDistance:0.0} m from the robot (red field: {Cell.Safety.ProtectiveRadius:0.0} m)";

            var resume = l.Step("Step back",
                "The robot stopped and the light turned <b>red</b>. Step back out of the red field: the robot carries on by itself, because nobody is in danger any more.");
            resume.Done = () => Cell.Safety.Stop == StopKind.None && Cell.Safety.Zone != "protective" && Cell.Running && !Cell.Paused;

            var pfl = l.Step("Power and force limiting",
                "The scanner is off for this step. Hold your hand in the robot's path and let it touch you. A cobot senses the contact through its joints and stops at once: its force and speed are limited so a touch cannot hurt.\n\nThen tap <b>Reset</b> on the pendant's <b>Safety</b> tab to carry on.");
            pfl.Enter = () => { Cell.Safety.SeparationMonitoring = false; app.SafetyZones.Show(false); if (!Cell.Running) Production(); app.Pendant.Show(true); app.Pendant.SetTab("safety"); };
            pfl.Done = () => run.Saw("contact") && Cell.Safety.Stop == StopKind.None;
            pfl.Status = () => run.Saw("contact") ? (Cell.Safety.Stop == StopKind.None ? "Reset: running again" : "Stopped by contact. Tap Reset on the pendant.") : "Waiting for a touch";

            var estop = l.Step("Emergency stop",
                "Find the red mushroom button on the <b>operator panel</b> beside the cell and press it: poke it with your controller, or point at it and pull the trigger.");
            estop.Enter = () => { if (!Cell.Running) Production(); run.Scene.Arrow(app.Factory.Hmi.StopButton, new Vector3(0, 0.12f, 0)); };
            estop.Done = () => run.Saw("estop");

            var reset = l.Step("Release and reset",
                "Everything stopped and the light flashes red. An emergency stop <b>latches</b>: the cell stays stopped until a person decides it is safe. Press the mushroom again to twist it free, then tap <b>Reset</b> on the pendant.");
            reset.Done = () => !Cell.Safety.EStopPressed && Cell.Safety.Stop == StopKind.None;
            reset.Enter = () => { app.Pendant.Show(true); app.Pendant.SetTab("safety"); };

            var enable = l.Step("The enabling switch",
                "When you teach a robot, a three-position <b>enabling switch</b> on the pendant has to be held for it to move. Here it is the <b>left grip</b>: squeeze it halfway and hold while you jog. Let go, or squeeze it hard in a panic, and the robot stops.\n\nPress <b>Y</b> to drive and jog the arm with the enabling switch held.");
            float jogged = 0;
            Vector3 lastTcp = default;
            enable.Enter = () => { Cell.StopProgram(); Cell.RequireEnabling = true; jogged = 0; lastTcp = Tcp; app.Pendant.SetTab("jog"); };
            enable.Tick = dt => { if (Cell.EnablingHeld) jogged += Vector3.Distance(Tcp, lastTcp); lastTcp = Tcp; };
            enable.Done = () => jogged > 0.08f;
            enable.Status = () => Cell.EnablingHeld ? $"Enabled: moved {jogged * 100:0} cm of 8 cm" : "Hold the left grip halfway";
            enable.Highlight = new[] { L(Control.Grip), L(Control.Secondary) };
            enable.Exit = () => Cell.RequireEnabling = app.Settings.RequireEnabling;

            l.Ask("A person walks into the protective field. What should the cell do?",
                "In speed and separation monitoring the robot keeps a safe distance: it slows when people come close and stops when they are too close, resuming once they leave.",
                1, "Keep working at full speed", "Stop until the person leaves the field", "Only sound an alarm");
            l.Ask("After an emergency stop, how does the robot start again?",
                "An emergency stop latches. Someone has to release the button deliberately and then reset the cell: it never restarts on its own.",
                1, "By itself, after a few seconds", "Only after a person releases the button and resets the cell", "When the program is reloaded");
            l.End = () =>
            {
                Cell.StopProgram();
                Cell.Safety.SeparationMonitoring = false;
                app.SafetyZones.Show(false);
                Cell.RequireEnabling = app.Settings.RequireEnabling;
                app.Interaction.SetMode(ControlMode.Explore);
            };
            return l;
        }

        // ================================================================== 9
        static Lesson DigitalTwin()
        {
            var l = new Lesson { Id = "twin", Title = "The digital twin", Summary = "Mirror, monitor, test first", Icon = "layers", Minutes = 10, Spot = CellLayout.TwinSpot };
            l.Facing = CellLayout.Facing(CellLayout.TwinSpot, CellLayout.TwinBase);
            RobotProgram program = null;
            float clearance = 0.06f;
            l.Begin = () => { app.ResetParts(); app.QuickStartProgram(); Cell.StartProgram(loop: true); };

            l.Step("Meet the twin",
                "This translucent robot is the cell's <b>digital twin</b>: a live virtual copy of the real cell, fed by the data its controller sends out (the pulses along the line from the cabinet). Look back at the real cell and at its twin: they move together.");

            var mirror = l.Step("A live mirror",
                "Press <b>Y</b> to drive the real robot and move its hand. The twin follows a moment later: that delay is the time the data takes to arrive. The dashboard shows it.");
            Vector3 start = default;
            mirror.Enter = () => { Cell.StopProgram(); start = Tcp; };
            mirror.Done = () => app.Interaction.Mode == ControlMode.Drive && Vector3.Distance(Tcp, start) > 0.1f;
            mirror.Highlight = new[] { L(Control.Secondary), L(Control.Stick) };

            var board = l.Step("What the twin knows",
                "The dashboard shows what the cell is doing: <b>cycles</b> and <b>cycle time</b>, <b>energy per cycle</b>, <b>OEE</b> (availability x performance x quality, the standard measure of how well a machine is used), and each motor's temperature and the cell's power over time. People use these to spot problems early.");
            board.Enter = () => { app.Interaction.SetMode(ControlMode.Explore); app.ResetParts(); app.QuickStartProgram(); Cell.StartProgram(loop: true); };

            var test = l.Step("Test changes on the twin first",
                "A new fixture has been bolted to the bench (the grey block), and a new program has arrived: it moves the cube into the tray. Never try a new program on the real robot first. Tap <b>Simulate on twin</b> on the dashboard and watch the twin run it.");
            test.Enter = () =>
            {
                Cell.StopProgram();
                Cell.SetQ(Arm.Home);
                app.ResetParts();
                app.SetFixture(true);
                clearance = 0.06f;
                program = Cell.PickAndPlace(CubePick(), TrayDrop(), clearance, "New program");
                app.Twin.PreviewRuns = 0;
                app.Twin.PreviewDone = false;
                app.Twin.Board.ShowCommissioning(true);
                app.Twin.Board.OnSimulate = () => { if (program != null) app.Twin.Preview(program); };
                app.Twin.Board.OnFix = () =>
                {
                    clearance = 0.2f;
                    program = Cell.PickAndPlace(CubePick(), TrayDrop(), clearance, "New program (raised)");
                    app.Twin.PreviewDone = false;
                    app.Twin.PreviewResult = "Approach raised to 20 cm. Simulate again.";
                    app.Toasts.Show("Approach height raised to 20 cm", "arrow-up", Pal.Info);
                };
                app.Twin.Board.OnDeploy = () =>
                {
                    if (!app.Twin.PreviewOk) { app.Toasts.Show("Test it on the twin first", "triangle-alert", Pal.Warn); return; }
                    app.ResetParts();
                    Cell.Program = program;
                    Cell.StartProgram(false);
                };
            };
            test.Done = () => app.Twin.PreviewDone && !app.Twin.PreviewOk;
            test.Status = () => app.Twin.PreviewResult;

            var fix = l.Step("Fix it",
                "The twin found a collision: the path runs into the new fixture. On the real cell that would have been a crash, and a damaged robot and fixture. Tap <b>Raise clearance</b> to lift the path over the fixture, then <b>Simulate on twin</b> again.");
            fix.Done = () => app.Twin.PreviewDone && app.Twin.PreviewOk;
            fix.Status = () => app.Twin.PreviewResult;

            var deploy = l.Step("Deploy",
                "The twin ran the new program with no collisions. Now tap <b>Deploy to cell</b> and watch the real robot run it, clear of the fixture.");
            deploy.Done = () => run.SawDetail("cycle:New program (raised)");

            var predict = l.Step("Predictive maintenance",
                "Look at the motor temperature chart: <b>J4</b>, the elbow, is running hotter every cycle. The twin compares the trend with its limit (the red line) and predicts how many cycles are left before it would fault.");
            predict.Enter = () =>
            {
                app.Twin.Board.ShowCommissioning(false);
                app.SetFixture(false);
                Cell.Telemetry.Wear = 9f;
                Cell.Telemetry.Temperature[3] = Mathf.Max(Cell.Telemetry.Temperature[3], 52f);
                app.ResetParts();
                app.QuickStartProgram();
                Cell.SpeedOverride = 1f;
                Cell.StartProgram(loop: true);
            };
            predict.Done = () => run.StepClock > 8f;
            predict.Auto = false;

            l.Ask("The twin predicts J4 will reach its temperature limit in about 40 cycles. What should the team do?",
                "Predictive maintenance acts before the failure: plan the repair for a convenient time instead of waiting for the robot to stop the line.",
                1, "Nothing until the robot faults", "Schedule maintenance for J4 before it reaches the limit", "Run the robot faster to finish sooner");
            l.Ask("What is a digital twin?",
                "A digital twin is a live virtual copy of a real system, kept in step with it by data, used to monitor it, predict its behaviour and try changes safely.",
                1, "A 3D model of a machine", "A live virtual copy of a real system, kept in step with its data", "A backup robot in the store room");
            l.End = () =>
            {
                Cell.StopProgram();
                Cell.Telemetry.Wear = 1f;
                for (int j = 0; j < RobotArm.Dof; j++) Cell.Telemetry.Temperature[j] = Mathf.Min(Cell.Telemetry.Temperature[j], 34f);
                app.SetFixture(false);
                app.Twin.Board.ShowCommissioning(false);
            };
            return l;
        }

        /// <summary>Where the cube's centre sits when it rests in the tray's first pocket.</summary>
        static Vector3 TrayDrop() => Factory.TrayPockets()[0] + Vector3.up * (0.025f + 0.012f);

        /// <summary>The cube's centre where it starts: the new program is written for that spot, and Deploy puts it back there.</summary>
        public static Vector3 CubePick() => CellLayout.CubeSpot + Vector3.up * 0.026f;

        // ================================================================== 10
        static Lesson Capstone()
        {
            var l = new Lesson { Id = "capstone", Title = "Industry 5.0 challenge", Summary = "Run a safe, efficient batch", Icon = "trophy", Minutes = 10, Spot = CellLayout.SafetySpot };
            l.Facing = CellLayout.Facing(CellLayout.SafetySpot, CellLayout.RobotBase);
            RobotProgram batch = null;
            int dispensed = 0;
            bool tested = false, jam = false, jammed = false;
            int stops = 0;
            float energy0 = 0, t0 = 0, runTime = 0;
            Graspable onFeeder = null;

            void Dispense(bool tipped = false)
            {
                if (dispensed >= app.Batch.Count) return;
                var g = app.Batch[dispensed++];
                g.gameObject.SetActive(true);
                g.Held = false;
                g.transform.SetParent(g.SpawnParent, true);
                g.Body.isKinematic = false;
                g.Body.linearVelocity = Vector3.zero;
                g.Body.angularVelocity = Vector3.zero;
                var p = CellLayout.FeederSpot + Vector3.up * (0.011f + 0.0225f + 0.002f);
                g.transform.SetPositionAndRotation(p, tipped ? Quaternion.Euler(0, 20, 40) : Quaternion.identity);
                g.SpawnPosition = p;
                g.SpawnRotation = Quaternion.identity;
                onFeeder = g;
                Sfx.Play("beep", 0.5f);
            }

            bool FeederEmpty() => onFeeder == null || onFeeder.Held || Flat(onFeeder.transform.position, CellLayout.FeederSpot) > 0.04f;

            int InTray()
            {
                int n = 0;
                foreach (var g in app.Batch)
                {
                    if (!g.gameObject.activeSelf || g.Held) continue;
                    foreach (var p in Factory.TrayPockets())
                        if (Flat(g.transform.position, p) < 0.025f && g.transform.position.y < CellLayout.BenchTop + 0.07f) { n++; break; }
                }
                return n;
            }

            void Feeder(float dt)
            {
                if (jammed) return;
                if (FeederEmpty() && dispensed < app.Batch.Count && Cell.Held != onFeeder) Dispense(jam && dispensed == 2);
            }

            l.Begin = () =>
            {
                app.ResetParts();
                app.Cube.gameObject.SetActive(false);
                dispensed = 0;
                onFeeder = null;
                tested = false;
                jam = false;
                jammed = false;
                stops = 0;
                Dispense();
                batch = app.TrayProgram();
                app.Twin.PreviewRuns = 0;
                app.Twin.PreviewDone = false;
                Cell.Safety.SeparationMonitoring = true;
                app.SafetyZones.Show(true);
                app.Twin.Board.OnSimulate = () => { if (batch != null) { app.Twin.Preview(batch, 3f); tested = true; } };
                app.Twin.Board.OnFix = () => app.Toasts.Show("Nothing to fix: the path is clear", "check", Pal.Ok);
                app.Twin.Board.OnDeploy = () =>
                {
                    if (batch == null) return;
                    if (Cell.Safety.Zone != "clear") { app.Toasts.Show("Step outside the scanner's fields first", "triangle-alert", Pal.Warn); return; }
                    Cell.Program = batch;
                    Cell.SpeedOverride = Mathf.Max(Cell.SpeedOverride, 0.5f);
                    energy0 = Cell.Telemetry.Energy;
                    t0 = Time.time;
                    Cell.StartProgram(false);
                };
                app.Cell.Event += Count;
            };

            void Count(string n, string d) { if (run.Current != null && run.Current.Id == "capstone" && (n == "protective_stop" || n == "estop" || n == "blocked")) stops++; }

            l.Step("Industry 5.0",
                "Industry 4.0 connected machines and data. <b>Industry 5.0</b> keeps all of that and adds three aims: <b>human-centric</b> (technology that works for people), <b>sustainable</b> (less energy and waste) and <b>resilient</b> (cells that cope with change and disruption).\n\nThis challenge puts everything you have learned together.");

            var brief = l.Step("The challenge",
                "Fill the four-pocket tray with the parts the feeder hands out. The batch program is ready. Your job: test it on the twin, run it from outside the scanner's fields, deal with a problem when it comes up, and finish the batch with no unplanned stops.");
            brief.Enter = () => app.GoTo(CellLayout.TwinSpot, CellLayout.Facing(CellLayout.TwinSpot, CellLayout.TwinBase));
            brief.Tick = Feeder;

            var twin = l.Step("Test the batch on the twin",
                "At the twin's dashboard, tap <b>Simulate on twin</b>. The twin runs all four pick and place cycles at three times speed and checks the path.");
            twin.Enter = () => app.Twin.Board.ShowCommissioning(true);
            twin.Tick = Feeder;
            twin.Done = () => app.Twin.PreviewDone && app.Twin.PreviewOk;
            twin.Status = () => app.Twin.PreviewResult;

            var go = l.Step("Run the batch",
                "Stay outside the amber field and tap <b>Deploy to cell</b>. The robot starts filling the tray; the feeder hands out a new part each time one is taken.");
            go.Tick = dt => { Feeder(dt); if (Cell.Running) runTime += dt; };
            go.Done = () => InTray() >= 2;
            go.Status = () => $"{InTray()} of 4 parts in the tray";

            var fixJam = l.Step("A part needs a hand",
                "The next part landed tipped over, and the robot cannot grip it like that: the cell has paused. Walk in (the robot stays stopped while you are inside its fields), stand the part upright on the feeder by hand, then step back out. The cell picks up where it left off.");
            fixJam.Enter = () =>
            {
                jam = true;
                if (onFeeder != null && !onFeeder.Held && Flat(onFeeder.transform.position, CellLayout.FeederSpot) < 0.05f)
                    onFeeder.transform.rotation = Quaternion.Euler(0, 20, 40);
                jammed = true;
                Cell.PauseProgram(true);
                run.Scene.Arrow(onFeeder != null ? onFeeder.transform : app.Factory.Root, new Vector3(0, 0.1f, 0));
                app.Toasts.Show("Feeder: part misaligned", "triangle-alert", Pal.Warn, 4f);
            };
            fixJam.Tick = dt =>
            {
                if (jammed && onFeeder != null && !onFeeder.Held && Flat(onFeeder.transform.position, CellLayout.FeederSpot) < 0.04f && Vector3.Angle(onFeeder.transform.up, Vector3.up) % 90f < 10f && onFeeder.Body.linearVelocity.magnitude < 0.02f)
                {
                    onFeeder.transform.rotation = Quaternion.Euler(0, Mathf.Round(onFeeder.transform.eulerAngles.y / 90f) * 90f, 0);
                    jammed = false;
                }
                if (!jammed && Cell.Safety.Zone == "clear" && Cell.Paused && Cell.Safety.Stop == StopKind.None) Cell.PauseProgram(false);
                if (Cell.Running) runTime += dt;
                Feeder(dt);
            };
            fixJam.Done = () => !jammed && Cell.Running && !Cell.Paused;
            fixJam.Status = () => jammed ? "Stand the part upright on the feeder plate" : Cell.Safety.Zone != "clear" ? "Upright. Now step back out of the fields." : "Resuming";
            fixJam.Highlight = new[] { L(Control.Grip), R(Control.Grip) };

            var finish = l.Step("Finish the batch",
                "Let the robot finish. Watch the dashboard's cycle time and energy as it works.");
            finish.Tick = dt => { Feeder(dt); if (Cell.Running) runTime += dt; };
            finish.Done = () => InTray() >= 4 && !Cell.Running;
            finish.Status = () => $"{InTray()} of 4 parts in the tray";

            var results = l.Step("Results",
                "");
            results.Enter = () =>
            {
                float energyWh = (Cell.Telemetry.Energy - energy0) / 3600f;
                int stars = 1 + (tested ? 1 : 0) + (stops <= 2 ? 1 : 0) + (InTray() >= 4 ? 1 : 0) + (energyWh / 4f < 1.2f ? 1 : 0);
                stars = Mathf.Clamp(stars, 1, 5);
                results.Body = $"<size=140%>{new string('★', stars)}{new string('☆', 5 - stars)}</size>\n\n" +
                               $"Parts in the tray: <b>{InTray()} of 4</b>\n" +
                               $"Tested on the twin first: <b>{(tested ? "yes" : "no")}</b>\n" +
                               $"Stops along the way: <b>{stops}</b> (one is expected: you walked in to fix the part)\n" +
                               $"Running time: <b>{runTime:0} s</b>, energy: <b>{energyWh:0.0} Wh</b> ({energyWh / 4f:0.00} Wh a part)\n\n" +
                               "Human-centric, sustainable, resilient: you kept people safe, used the twin to avoid a costly mistake, and recovered from a fault without stopping the line for long.";
            };
            l.End = () =>
            {
                app.Cell.Event -= Count;
                Cell.StopProgram();
                Cell.Safety.SeparationMonitoring = false;
                app.SafetyZones.Show(false);
                app.Twin.Board.ShowCommissioning(false);
                app.ResetParts();
            };
            return l;
        }
    }
}
