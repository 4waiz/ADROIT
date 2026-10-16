// SPDX-License-Identifier: LicenseRef-BRIDGE
// What each button does. Two modes, switched with Y:
//
//   EXPLORE  right stick: teleport (push forward, release) and snap turn; left stick: walk (if on);
//            grips: pick up parts, or take the robot's wrist when it is in hand-guiding mode;
//            A: next lesson step; B: repeat the step; X: show or hide the pendant.
//   DRIVE    the sticks jog the robot (left: across the floor, right: up and down, turn the wrist,
//            or, in joint mode, left picks a joint and right turns it); A: gripper; B: jog mode
//            (Cartesian, joint, hand guiding); X: save this spot as a waypoint; stick clicks: the jog
//            frame (left) and the speed (right); left grip: the enabling switch, when one is needed.
//
//   Both     triggers: point and select; Menu: the main menu; Y: switch mode.
//
// On the desktop the mouse is the right hand, and keys stand in for the rest (see TrainerOverlay).

using System.Collections.Generic;
using Bridge.Robot;
using Bridge.UI;
using Bridge.XR;
using UnityEngine;
using Pose = Bridge.Robot.Pose;

namespace Bridge.App
{
    public enum ControlMode { Explore, Drive }

    public class Interaction
    {
        readonly BridgeApp app;
        public ControlMode Mode = ControlMode.Explore;
        public bool MenuLocked;           // a lesson can keep the menu from opening
        public bool DriveAllowed = true;
        public bool ModeSwitchAllowed = true;
        public readonly ControllerModel[] Models = new ControllerModel[2];
        readonly Graspable[] held = new Graspable[2];
        readonly Vector3[] heldPos = new Vector3[2];
        readonly Quaternion[] heldRot = new Quaternion[2];
        int jointLatch;
        bool wasBlocked, wasLimit;
        float hoverHaptic;
        public System.Action<ControlMode> ModeChanged;
        public System.Action<string> ButtonPressed;   // "A", "B", "X", "Y", "menu", "grip:0", "trigger:1"... for lessons
        public bool Guiding { get; private set; }
        /// <summary>The robot body a ray rests on this frame ("" if none): lessons ask about it.</summary>
        public string Pointed = "";
        public int GuidingHand = -1;
        static readonly Collider[] near = new Collider[12];

        public Interaction(BridgeApp app)
        {
            this.app = app;
            var xr = app.XR;
            for (int i = 0; i < 2; i++) Models[i] = new ControllerModel(xr.Hands[i], app.transform);
            UIInput.HoverEnter += (p, w) =>
            {
                Sfx.Play("hover", 0.6f);
                if (p.Id < 2) app.XR.Buzz(p.Id, 0.08f, 0.012f);
            };
            UIInput.Clicked += (p, w) =>
            {
                Sfx.Play("click");
                if (p.Id < 2) app.XR.Buzz(p.Id, 0.35f, 0.025f);
            };
        }

        public void SetMode(ControlMode mode)
        {
            if (Mode == mode) return;
            if (mode == ControlMode.Drive && !DriveAllowed) return;
            Mode = mode;
            var cell = app.Cell;
            if (mode == ControlMode.Drive)
            {
                if (cell.Mode == JogMode.Idle) cell.SetMode(JogMode.Cartesian);
                app.Pendant.Show(true);
            }
            else
            {
                if (cell.Mode != JogMode.FreeDrive) cell.SetMode(JogMode.Idle);
            }
            Sfx.Play("toggle");
            ModeChanged?.Invoke(mode);
        }

        public void Tick(float dt)
        {
            var xr = app.XR;
            var cell = app.Cell;
            var left = xr.Left;
            var right = xr.Right;

            // Interface first: rays and fingertips.
            UIInput.Process(xr.Pointers, dt);
            bool rightOnUI = right.Pointer.HitUI || (!xr.XRActive && xr.Mouse.HitUI);
            bool leftOnUI = left.Pointer.HitUI;

            // Buttons that work everywhere.
            if (left.MenuDown) { Press("menu"); if (!MenuLocked) app.Menus.ToggleHome(); }
            if (left.SecondaryDown) { Press("Y"); if (ModeSwitchAllowed) SetMode(Mode == ControlMode.Drive ? ControlMode.Explore : ControlMode.Drive); }
            if (left.StickClickDown && Mode == ControlMode.Explore) { Press("L3"); Models[0].HintsForced = Models[1].HintsForced = !Models[1].HintsForced; }
            if (right.StickClickDown && Mode == ControlMode.Explore) { Press("R3"); app.Lessons.Panel.Recentre(); }
            for (int i = 0; i < 2; i++)
            {
                var h = xr.Hands[i];
                if (h.TriggerDown) Press("trigger:" + i);
                if (h.GripDown) Press("grip:" + i);
            }

            // Who is in the cell, for the safety functions.
            cell.HumanPresent = true;
            cell.HumanHead = xr.Head.position;
            cell.HumanHands[0] = xr.XRActive && left.Tracked ? left.Grip.position : xr.Head.position + Vector3.down * 0.5f;
            cell.HumanHands[1] = xr.XRActive && right.Tracked ? right.Grip.position : xr.Head.position + Vector3.down * 0.5f;
            cell.ViewForward = xr.Head.forward;

            if (Mode == ControlMode.Explore) Explore(dt, rightOnUI);
            else Drive(dt);
            Grabbing(dt);
            Guide(dt);
            Feedback(dt);
            Hints();

            Pointed = "";
            if (!xr.XRActive && !xr.Mouse.HitUI && Physics.Raycast(xr.Mouse.Ray, out var mh, 6f, Layers.RobotMask, QueryTriggerInteraction.Ignore)) Pointed = BodyOf(mh.collider.transform);
            // Rays: hide a hand's ray while it holds something or guides the arm.
            for (int i = 0; i < 2; i++)
            {
                var h = xr.Hands[i];
                Models[i].RayVisible = held[i] == null && !(Guiding && GuidingHand == i);
                Vector3? hit = null;
                if (!h.Pointer.HitUI && Physics.Raycast(h.Aim.position, h.Aim.forward, out var rh, 4f, Layers.RobotMask | Layers.GrabbableMask, QueryTriggerInteraction.Ignore))
                {
                    hit = rh.point;
                    if (xr.XRActive && rh.collider.gameObject.layer == Layers.Robot) Pointed = BodyOf(rh.collider.transform);
                }
                Models[i].Tick(dt, xr.Head, h.Pointer.HitUI, h.Pointer.UIDistance, hit);
            }
        }

        void Press(string what) => ButtonPressed?.Invoke(what);

        /// <summary>The Panda body a collider belongs to.</summary>
        string BodyOf(Transform t)
        {
            foreach (var b in app.Panda.Bodies)
                for (var x = t; x != null; x = x.parent)
                    if (x == b) return b.name;
            return "";
        }

        void Explore(float dt, bool rightOnUI)
        {
            var xr = app.XR;
            var cell = app.Cell;
            cell.JogLinear = Vector3.zero;
            cell.JogAngular = Vector3.zero;
            cell.JogJointRate = 0;
            app.Locomotion.Enabled = true;
            app.Locomotion.Tick(dt, xr.Left, xr.Right, true, rightOnUI);
            if (xr.Right.PrimaryDown) { Press("A"); if (app.Tour.Active) app.Tour.Next(); else app.Lessons.Next(fromButton: true); }
            if (xr.Right.SecondaryDown) { Press("B"); if (app.Tour.Active) app.Tour.Back(); else app.Lessons.Repeat(); }
            if (xr.Left.PrimaryDown) { Press("X"); app.Pendant.Show(!app.Pendant.Visible); }
        }

        void Drive(float dt)
        {
            var xr = app.XR;
            var cell = app.Cell;
            var left = xr.Left;
            var right = xr.Right;
            app.Locomotion.Enabled = false;
            app.Locomotion.Tick(dt, left, right, false, false);
            cell.EnablingHeld = left.Squeeze > 0.35f && left.Squeeze < 0.97f || (!xr.XRActive && left.GripHeld);
            Vector2 ls = Dead(left.Stick), rs = Dead(right.Stick);
            if (right.PrimaryDown) { Press("A"); cell.ToggleGripper(); Sfx.Play(cell.GripperClosed ? "close" : "open"); }
            if (right.SecondaryDown)
            {
                Press("B");
                var next = cell.Mode switch { JogMode.Cartesian => JogMode.Joint, JogMode.Joint => JogMode.FreeDrive, _ => JogMode.Cartesian };
                cell.SetMode(next);
                Sfx.Play("toggle");
            }
            if (left.PrimaryDown)
            {
                Press("X");
                var wp = cell.SaveWaypoint();
                Sfx.Play("waypoint");
                app.Toasts.Show("Waypoint " + wp.Name + " saved", "save", Pal.Ok);
            }
            if (left.StickClickDown)
            {
                Press("L3");
                cell.Frame = cell.Frame switch { JogFrame.View => JogFrame.Base, JogFrame.Base => JogFrame.Tool, _ => JogFrame.View };
                app.Toasts.Show("Jog frame: " + FrameName(cell.Frame), "axis-3d", Pal.Info);
                Sfx.Play("toggle");
            }
            if (right.StickClickDown)
            {
                Press("R3");
                float[] steps = { 0.1f, 0.25f, 0.5f, 1f };
                int i = 0;
                while (i < steps.Length && steps[i] <= cell.SpeedOverride + 0.01f) i++;
                cell.SpeedOverride = steps[i % steps.Length];
                app.Toasts.Show($"Speed {cell.SpeedOverride * 100:0}%", "gauge", Pal.Info);
                Sfx.Play("toggle");
            }
            switch (cell.Mode)
            {
                case JogMode.Cartesian:
                    cell.JogLinear = new Vector3(ls.x, rs.y, ls.y);
                    cell.JogAngular = new Vector3(0, rs.x, 0);
                    cell.JogJointRate = 0;
                    break;
                case JogMode.Joint:
                    cell.JogLinear = Vector3.zero;
                    cell.JogAngular = Vector3.zero;
                    if (Mathf.Abs(ls.x) > 0.7f && jointLatch == 0)
                    {
                        jointLatch = (int)Mathf.Sign(ls.x);
                        cell.SelectedJoint = (cell.SelectedJoint + jointLatch + RobotArm.Dof) % RobotArm.Dof;
                        Sfx.Play("toggle", 0.6f);
                    }
                    else if (Mathf.Abs(ls.x) < 0.3f) jointLatch = 0;
                    cell.JogJointRate = rs.y;
                    break;
                default:
                    cell.JogLinear = Vector3.zero;
                    cell.JogAngular = Vector3.zero;
                    cell.JogJointRate = 0;
                    break;
            }
        }

        public static string FrameName(JogFrame f) => f switch { JogFrame.Base => "robot base", JogFrame.Tool => "tool", _ => "your view" };

        static Vector2 Dead(Vector2 v)
        {
            float m = v.magnitude;
            if (m < 0.15f) return Vector2.zero;
            // A gentle curve: small pushes give fine control.
            return v.normalized * Mathf.Pow(Mathf.Clamp01((m - 0.15f) / 0.85f), 1.6f);
        }

        // ------------------------------------------------------------------ hands
        void Grabbing(float dt)
        {
            var xr = app.XR;
            for (int i = 0; i < 2; i++)
            {
                var h = xr.Hands[i];
                if (held[i] != null)
                {
                    var g = held[i];
                    if (!h.GripHeld)
                    {
                        // Let go: it falls (or flies) from the hand.
                        g.transform.SetParent(g.SpawnParent, true);
                        g.Held = false;
                        if (g.Body != null)
                        {
                            g.Body.isKinematic = false;
                            g.Body.linearVelocity = h.Velocity;
                        }
                        held[i] = null;
                        Sfx.Play("grasp", 0.5f, 0.8f);
                    }
                    continue;
                }
                if (!h.GripDown || (Guiding && GuidingHand == i)) continue;
                if (app.Cell.Mode == JogMode.FreeDrive && NearWrist(h)) continue;
                // The nearest part within reach of the palm.
                Vector3 palm = h.Grip.position + h.Grip.forward * 0.02f;
                int n = Physics.OverlapSphereNonAlloc(palm, 0.08f, near, Layers.GrabbableMask, QueryTriggerInteraction.Ignore);
                Graspable best = null;
                float bestD = 1e9f;
                for (int k = 0; k < n; k++)
                {
                    var g = near[k].GetComponentInParent<Graspable>();
                    if (g == null || g.Held) continue;
                    float d = Vector3.Distance(g.transform.position, palm);
                    if (d < bestD) { bestD = d; best = g; }
                }
                if (best == null) continue;
                held[i] = best;
                best.Held = true;
                if (best.Body != null) best.Body.isKinematic = true;
                best.transform.SetParent(h.Grip, true);
                xr.Buzz(i, 0.4f, 0.04f);
                Sfx.Play("grasp", 0.6f, 1.2f);
            }
        }

        /// <summary>Drops anything the hands hold (a lesson resetting its parts).</summary>
        public void DropAll()
        {
            for (int i = 0; i < 2; i++)
            {
                if (held[i] == null) continue;
                held[i].Held = false;
                held[i].transform.SetParent(held[i].SpawnParent, true);
                if (held[i].Body != null) held[i].Body.isKinematic = false;
                held[i] = null;
            }
        }

        public bool Holding(int hand) => held[hand] != null;

        bool NearWrist(Hand h) => Vector3.Distance(h.Grip.position, app.Panda.Hand.position) < 0.16f || Vector3.Distance(h.Grip.position, app.Panda.Flange.position) < 0.16f;

        /// <summary>Hand guiding: in free-drive mode, squeeze a grip at the robot's wrist and move it.</summary>
        void Guide(float dt)
        {
            var xr = app.XR;
            var cell = app.Cell;
            if (cell.Mode != JogMode.FreeDrive)
            {
                if (Guiding) EndGuide();
                cell.GuideTarget = null;
                return;
            }
            if (!Guiding)
            {
                for (int i = 0; i < 2; i++)
                {
                    var h = xr.Hands[i];
                    if (h.GripDown && held[i] == null && NearWrist(h))
                    {
                        Guiding = true;
                        GuidingHand = i;
                        xr.Buzz(i, 0.5f, 0.06f);
                        Sfx.Play("grasp", 0.7f, 0.9f);
                        break;
                    }
                }
                // On the desktop, G at the wrist (or anywhere in hand-guide mode) guides with the mouse hand.
                if (!xr.XRActive && xr.Right.GripDown)
                {
                    Guiding = true;
                    GuidingHand = 1;
                }
            }
            if (Guiding)
            {
                var h = xr.Hands[GuidingHand];
                if (!h.GripHeld)
                {
                    EndGuide();
                    return;
                }
                cell.GuideTarget = new Pose(h.Grip.position, h.Grip.rotation);
                // A soft resistance: buzz in proportion to how far the arm lags the hand.
                float lag = Vector3.Distance(app.Panda.Tcp.position, cell.Target.position);
                if (lag > 0.01f) xr.Buzz(GuidingHand, Mathf.Clamp01(lag * 8f), dt * 1.2f);
            }
            else cell.GuideTarget = null;
        }

        void EndGuide()
        {
            Guiding = false;
            GuidingHand = -1;
            app.Cell.GuideTarget = null;
        }

        // ------------------------------------------------------------------ feedback
        void Feedback(float dt)
        {
            var cell = app.Cell;
            var xr = app.XR;
            int hand = Mode == ControlMode.Drive ? 1 : (Guiding ? GuidingHand : -1);
            if (cell.Blocked && !wasBlocked)
            {
                Sfx.Play("blocked");
                if (hand >= 0) xr.Buzz(hand, 0.8f, 0.08f);
                xr.Buzz(0, 0.5f, 0.05f);
            }
            wasBlocked = cell.Blocked;
            if (cell.AtJointLimit && !wasLimit)
            {
                Sfx.Play("blocked", 0.6f, 1.4f);
                xr.Buzz(1, 0.6f, 0.06f);
            }
            wasLimit = cell.AtJointLimit;
        }

        /// <summary>Labels the controllers' buttons for the mode you are in.</summary>
        void Hints()
        {
            var l = Models[0];
            var r = Models[1];
            var cell = app.Cell;
            bool drive = Mode == ControlMode.Drive;
            l.SetHint(Control.Menu, MenuLocked ? "" : "Menu");
            l.SetHint(Control.Secondary, ModeSwitchAllowed ? (drive ? "Explore" : "Drive robot") : "");
            l.SetHint(Control.Trigger, "Select");
            r.SetHint(Control.Trigger, "Select");
            if (!drive)
            {
                l.SetHint(Control.Stick, app.Locomotion.SmoothMove ? "Walk" : "");
                l.SetHint(Control.Primary, "Pendant");
                l.SetHint(Control.Grip, cell.Mode == JogMode.FreeDrive ? "Guide arm" : "Grab");
                r.SetHint(Control.Stick, "Teleport / turn");
                r.SetHint(Control.Primary, app.Lessons.Active ? "Next" : "");
                r.SetHint(Control.Secondary, app.Lessons.Active ? "Repeat" : "");
                r.SetHint(Control.Grip, cell.Mode == JogMode.FreeDrive ? "Guide arm" : "Grab");
            }
            else
            {
                l.SetHint(Control.Primary, "Save spot");
                l.SetHint(Control.Grip, cell.RequireEnabling ? "Enable (hold)" : "");
                r.SetHint(Control.Primary, cell.GripperClosed ? "Open gripper" : "Close gripper");
                r.SetHint(Control.Secondary, cell.Mode switch { JogMode.Cartesian => "Joint mode", JogMode.Joint => "Hand guide", _ => "Cartesian" });
                r.SetHint(Control.Grip, cell.Mode == JogMode.FreeDrive ? "Guide arm" : "");
                switch (cell.Mode)
                {
                    case JogMode.Cartesian:
                        l.SetHint(Control.Stick, "Move hand (press: frame)");
                        r.SetHint(Control.Stick, "Up, down, turn (press: speed)");
                        break;
                    case JogMode.Joint:
                        l.SetHint(Control.Stick, "Pick joint");
                        r.SetHint(Control.Stick, "Turn joint (press: speed)");
                        break;
                    default:
                        l.SetHint(Control.Stick, "");
                        r.SetHint(Control.Stick, "");
                        break;
                }
            }
        }
    }
}
