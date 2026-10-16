// SPDX-License-Identifier: LicenseRef-BRIDGE
// The robot cell's controller: what a cobot's own controller does, for a simulated Panda. It jogs the
// arm joint by joint or along a frame, lets a hand guide it, opens and closes the gripper and holds
// what it closes on, runs taught programs (joint moves, linear moves, grip, wait) at the pendant's
// speed, refuses moves that would drive the arm into the bench, and obeys the safety system: a
// protective stop, an emergency stop, or the reduced speed a person nearby calls for.
//
// Everything here is simulated and kinematic: there is no dynamics solver, and the torques,
// temperatures and energy the telemetry reports are models (labelled as simulated in the UI).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bridge.Robot
{
    public enum JogMode { Idle, Joint, Cartesian, FreeDrive }
    public enum JogFrame { View, Base, Tool }
    public enum StopKind { None, Protective, Emergency }
    public enum StepKind { MoveJ, MoveL, Close, Open, Wait, Home }

    /// <summary>Something the gripper can pick up: a part, a cube, a carton.</summary>
    public class Graspable : MonoBehaviour
    {
        public string Id;
        public Rigidbody Body;
        public bool Held;
        public Vector3 SpawnPosition;
        public Quaternion SpawnRotation;
        public Transform SpawnParent;
        public Vector3 Size;

        public void Respawn()
        {
            Held = false;
            transform.SetParent(SpawnParent, true);
            transform.SetPositionAndRotation(SpawnPosition, SpawnRotation);
            if (Body != null)
            {
                // The body too, so an interpolated rigidbody does not carry the old pose into the next step.
                Body.position = SpawnPosition;
                Body.rotation = SpawnRotation;
                Body.isKinematic = false;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>The part's extent along a world direction (its box seen edge on).</summary>
        public float WidthAlong(Vector3 dir)
        {
            dir = dir.normalized;
            Vector3 h = Size * 0.5f;
            return 2f * (Mathf.Abs(Vector3.Dot(transform.right, dir)) * h.x + Mathf.Abs(Vector3.Dot(transform.up, dir)) * h.y + Mathf.Abs(Vector3.Dot(transform.forward, dir)) * h.z);
        }
    }

    public class Waypoint
    {
        public string Name;
        public float[] Q = new float[RobotArm.Dof];
        /// <summary>The TCP pose in the robot's base frame, so a program means the same on any copy of the arm.</summary>
        public Pose Pose;
        public Waypoint(string name, float[] q, Pose basePose) { Name = name; Array.Copy(q, Q, RobotArm.Dof); Pose = basePose; }
    }

    public class Step
    {
        public StepKind Kind;
        public Waypoint Target;
        public float Seconds;
        public float Speed = 1f;
        public Step(StepKind kind, Waypoint target = null, float seconds = 0, float speed = 1f) { Kind = kind; Target = target; Seconds = seconds; Speed = speed; }

        public string Label => Kind switch
        {
            StepKind.MoveJ => "Joint move to " + Target?.Name,
            StepKind.MoveL => "Linear move to " + Target?.Name,
            StepKind.Close => "Close gripper",
            StepKind.Open => "Open gripper",
            StepKind.Wait => $"Wait {Seconds:0.#} s",
            _ => "Go home",
        };

        public string Icon => Kind switch
        {
            StepKind.MoveJ => "rotate-3d",
            StepKind.MoveL => "move-3d",
            StepKind.Close => "hand-grab",
            StepKind.Open => "hand",
            StepKind.Wait => "timer",
            _ => "house",
        };
    }

    public class RobotProgram
    {
        public string Name = "My program";
        public readonly List<Step> Steps = new();
        public readonly List<Waypoint> Points = new();
        public bool Loop;
    }

    public class Cell
    {
        public readonly RobotArm Arm;
        public readonly Safety Safety;
        public readonly Telemetry Telemetry;
        public readonly List<Graspable> Parts = new();

        // Pendant state.
        public JogMode Mode = JogMode.Idle;
        public JogFrame Frame = JogFrame.View;
        public int SelectedJoint = 3;
        public float SpeedOverride = 0.5f;
        public bool RequireEnabling;
        public bool EnablingHeld;
        public float LinearSpeed = 0.25f;   // m/s at 100 %
        public float AngularSpeed = 0.9f;   // rad/s at 100 %

        // What the controller reports.
        public string Message = "Ready";
        public bool Blocked;
        public string BlockedBy;
        public bool AtJointLimit;
        public bool OutOfReach;
        public float Manipulability = 1f;
        public bool NearSingularity;
        /// <summary>Below this (the hand's travel manipulability) the pendant warns of a singular pose.</summary>
        public float SingularBelow = 0.015f;
        public float GripperTarget = RobotArm.MaxOpening;
        public Graspable Held;
        public bool GripperContact;

        // Program state.
        public RobotProgram Program = new();
        public bool Running;
        public bool Paused;
        public int StepIndex = -1;
        public int Cycles;
        public float CycleTime;
        public float LastCycleTime;
        public bool StepMode;

        // Inputs, set each frame by whoever drives the cell (controllers, pendant, lessons, tests).
        public Vector3 JogLinear;        // -1..1 along the jog frame's axes (x right, y up, z forward)
        public Vector3 JogAngular;       // -1..1 about the jog frame's axes
        public float JogJointRate;       // -1..1 for the selected joint
        public Pose? GuideTarget;        // where a hand guiding the arm wants the TCP
        public Vector3 ViewForward = Vector3.forward;
        public Vector3 HumanHead;
        public readonly Vector3[] HumanHands = new Vector3[2];
        public bool HumanPresent;

        /// <summary>(name, detail): jog, joint_limit, blocked, grip_close, grip_open, grasp, release, estop, reset, protective_stop, resume, program_start, step, cycle, program_end, waypoint, zone, guide_start, guide_end, out_of_reach.</summary>
        public event Action<string, string> Event;

        public Pose Target;
        readonly float[] qCandidate = new float[RobotArm.Dof];
        readonly float[] qPrev = new float[RobotArm.Dof];
        readonly float[] qd = new float[RobotArm.Dof];
        public float[] JointVelocity => qd;
        float blockedSince;
        bool guiding;
        Vector3 holdOffset;
        Quaternion holdRotation;
        Vector3 lastTcp;
        Vector3 tcpVelocity;
        public Vector3 TcpVelocity => tcpVelocity;
        public int EnvironmentMask = Layers.EnvironmentMask;
        /// <summary>The arm whose place in the room collision checks use (the real cell's, for the twin).</summary>
        public RobotArm CollisionArm;

        public Pose ToLocal(Pose world)
        {
            var t = Arm.Root.transform;
            return new Pose(t.InverseTransformPoint(world.position), Quaternion.Inverse(t.rotation) * world.rotation);
        }

        public Pose ToWorld(Pose local)
        {
            var t = Arm.Root.transform;
            return new Pose(t.TransformPoint(local.position), t.rotation * local.rotation);
        }

        // Running segment.
        MotionSegment segment;
        float stepClock;
        bool homeMove;

        public Cell(RobotArm arm)
        {
            Arm = arm;
            Safety = new Safety(this);
            Telemetry = new Telemetry(this);
            Target = arm.TcpPose;
            Array.Copy(arm.Q, qPrev, RobotArm.Dof);
            lastTcp = arm.Tcp.position;
        }

        public void Emit(string name, string detail = "") => Event?.Invoke(name, detail);

        public bool Stopped => Safety.Stop != StopKind.None;
        /// <summary>The speed the arm may move at now: the pendant's override times the safety scale.</summary>
        public float EffectiveSpeed => SpeedOverride * Safety.Scale;

        // ------------------------------------------------------------------ the tick
        public void Tick(float dt)
        {
            if (dt <= 0) return;
            Array.Copy(Arm.Q, qPrev, RobotArm.Dof);
            Safety.Tick(dt);
            Blocked = Blocked && Time.time - blockedSince < 0.6f;
            AtJointLimit = false;
            if (Running || Mode != JogMode.Cartesian) jogging = false;

            if (Running) TickProgram(dt);
            else if (Mode == JogMode.FreeDrive) TickGuide(dt);
            else if (Mode == JogMode.Joint) TickJointJog(dt);
            else if (Mode == JogMode.Cartesian) TickCartesianJog(dt);
            else Target = Arm.TcpPose;

            TickGripper(dt);
            Arm.Apply();
            Manipulability = Arm.ManipulabilityAt(Arm.Q, positionOnly: true);
            NearSingularity = Manipulability < SingularBelow;
            for (int j = 0; j < RobotArm.Dof; j++) qd[j] = (Arm.Q[j] - qPrev[j]) / dt;
            Vector3 tcp = Arm.Tcp.position;
            tcpVelocity = Vector3.Lerp(tcpVelocity, (tcp - lastTcp) / dt, 0.5f);
            lastTcp = tcp;
            Telemetry.Tick(dt);
            if (Running) CycleTime += dt;
        }

        bool CanJog()
        {
            if (Stopped) return false;
            if (RequireEnabling && !EnablingHeld) return false;
            return true;
        }

        /// <summary>The jog frame's axes in world space: right, up, forward.</summary>
        public void FrameAxes(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            switch (Frame)
            {
                case JogFrame.Tool:
                    var r = Arm.Tcp.rotation;
                    right = r * Vector3.right;
                    up = r * Vector3.up;
                    forward = r * Vector3.forward;
                    break;
                case JogFrame.Base:
                    // The base frame as MuJoCo has it: x forward from the base, y to its left, z up;
                    // in Unity's axes that is x, z, y of the base transform.
                    var b = Arm.Root.transform;
                    right = -(b.rotation * Vector3.forward);   // -y_base
                    up = b.up;
                    forward = b.right;                         // +x_base
                    break;
                default:
                    forward = Vector3.ProjectOnPlane(ViewForward, Vector3.up);
                    if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
                    forward.Normalize();
                    up = Vector3.up;
                    right = Vector3.Cross(up, forward);
                    break;
            }
        }

        void TickJointJog(float dt)
        {
            if (!CanJog() || Mathf.Abs(JogJointRate) < 0.02f)
            {
                Target = Arm.TcpPose;
                return;
            }
            int j = Mathf.Clamp(SelectedJoint, 0, RobotArm.Dof - 1);
            Array.Copy(Arm.Q, qCandidate, RobotArm.Dof);
            float v = JogJointRate * Arm.VMax[j] * 0.35f * EffectiveSpeed;
            float next = Arm.Q[j] + v * dt;
            if (next <= Arm.QMin[j] || next >= Arm.QMax[j])
            {
                next = Mathf.Clamp(next, Arm.QMin[j], Arm.QMax[j]);
                if (!AtJointLimit) Emit("joint_limit", RobotArm.JointLabel(j));
                AtJointLimit = true;
            }
            qCandidate[j] = next;
            if (Commit(qCandidate)) Emit("jog", "joint");
            Target = Arm.TcpPose;
        }

        // The Cartesian jog's own goal: it moves at the commanded speed and the hand follows it, so the
        // solver's small misses each tick do not add up into a drift.
        Pose jogGoal;
        bool jogging;

        void TickCartesianJog(float dt)
        {
            if (!CanJog() || (JogLinear.sqrMagnitude < 0.0004f && JogAngular.sqrMagnitude < 0.0004f))
            {
                Target = Arm.TcpPose;
                OutOfReach = false;
                jogging = false;
                return;
            }
            FrameAxes(out var right, out var up, out var forward);
            float speed = EffectiveSpeed;
            Vector3 move = (right * JogLinear.x + up * JogLinear.y + forward * JogLinear.z) * (LinearSpeed * speed * dt);
            Quaternion turn = Quaternion.AngleAxis(JogAngular.y * AngularSpeed * speed * dt * Mathf.Rad2Deg, up)
                              * Quaternion.AngleAxis(JogAngular.x * AngularSpeed * speed * dt * Mathf.Rad2Deg, right)
                              * Quaternion.AngleAxis(JogAngular.z * AngularSpeed * speed * dt * Mathf.Rad2Deg, forward);
            var current = Arm.TcpPose;
            // Start from the hand when a jog starts, or when the hand fell behind (held back, out of reach).
            if (!jogging || Vector3.Distance(jogGoal.position, current.position) > 0.01f || Quaternion.Angle(jogGoal.rotation, current.rotation) > 3f)
                jogGoal = current;
            jogging = true;
            jogGoal = new Pose(jogGoal.position + move, turn * jogGoal.rotation);
            if (MoveTowards(jogGoal, dt, 1f)) Emit("jog", "cartesian");
            Target = Arm.TcpPose;
        }

        void TickGuide(float dt)
        {
            if (GuideTarget == null || Stopped)
            {
                if (guiding)
                {
                    guiding = false;
                    Emit("guide_end");
                }
                Target = Arm.TcpPose;
                return;
            }
            if (!guiding)
            {
                guiding = true;
                // Hold the arm where the hand took it: keep the offset from hand to TCP.
                var tcp = Arm.TcpPose;
                holdRotation = Quaternion.Inverse(GuideTarget.Value.rotation) * tcp.rotation;
                holdOffset = Quaternion.Inverse(GuideTarget.Value.rotation) * (tcp.position - GuideTarget.Value.position);
                Emit("guide_start");
            }
            var hand = GuideTarget.Value;
            var want = new Pose(hand.position + hand.rotation * holdOffset, hand.rotation * holdRotation);
            // Hand guiding is compliant but not instant: the arm follows at up to 0.5 m/s.
            var current = Arm.TcpPose;
            Vector3 delta = want.position - current.position;
            float maxStep = 0.5f * Mathf.Max(0.2f, SpeedOverride) * dt;
            if (delta.magnitude > maxStep) delta = delta.normalized * maxStep;
            var goal = new Pose(current.position + delta, Quaternion.RotateTowards(current.rotation, want.rotation, 120f * dt));
            MoveTowards(goal, dt, 1f);
            Target = Arm.TcpPose;
        }

        /// <summary>
        /// Solves for a TCP pose near the current one and commits it if the joints can get there
        /// this tick and nothing is in the way. Returns whether the arm moved.
        /// </summary>
        public bool MoveTowards(Pose goal, float dt, float speedScale)
        {
            var result = Arm.Solve(goal, Arm.Q, qCandidate, iterations: 16);
            bool reach = result.positionError < 0.004f && result.rotationError < 0.05f;
            if (!reach && !OutOfReach) Emit("out_of_reach");
            OutOfReach = !reach;
            // Respect each joint's speed limit: scale the whole step down if one would exceed it.
            float worst = 0;
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                float limit = Arm.VMax[j] * Mathf.Max(0.05f, SpeedOverride * speedScale) * dt;
                float d = Mathf.Abs(qCandidate[j] - Arm.Q[j]);
                worst = Mathf.Max(worst, d / Mathf.Max(limit, 1e-6f));
            }
            if (worst > 1f)
                for (int j = 0; j < RobotArm.Dof; j++) qCandidate[j] = Arm.Q[j] + (qCandidate[j] - Arm.Q[j]) / worst;
            return Commit(qCandidate);
        }

        /// <summary>Takes joint angles if the arm would not hit anything there. Returns whether the arm moved.</summary>
        bool Commit(float[] q) => Commit(q, out _);

        /// <summary>As <see cref="Commit(float[])"/>; <paramref name="blocked"/> says something was in the way.</summary>
        bool Commit(float[] q, out bool blocked)
        {
            blocked = false;
            if ((CollisionArm ?? Arm).Collides(q, Arm.Opening, EnvironmentMask, out string what))
            {
                if (!Blocked) Emit("blocked", what);
                Blocked = true;
                BlockedBy = what;
                blockedSince = Time.time;
                blocked = true;
                return false;
            }
            bool moved = false;
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                float v = Mathf.Clamp(q[j], Arm.QMin[j], Arm.QMax[j]);
                if (Mathf.Abs(v - Arm.Q[j]) > 1e-6f) moved = true;
                Arm.Q[j] = v;
            }
            return moved;
        }

        // ------------------------------------------------------------------ gripper
        public void CloseGripper() { GripperTarget = 0f; Emit("grip_close"); }
        public void OpenGripper() { GripperTarget = RobotArm.MaxOpening; Emit("grip_open"); }
        public void ToggleGripper() { if (GripperTarget > 0.04f) CloseGripper(); else OpenGripper(); }
        public bool GripperClosed => GripperTarget < 0.04f;

        void TickGripper(float dt)
        {
            if (Safety.Stop == StopKind.Emergency) return;
            // The Franka hand moves its fingers at up to 0.1 m/s together.
            float speed = 0.1f * dt;
            float target = GripperTarget;
            if (Held != null && GripperTarget < Arm.Opening)
            {
                // Holding: the fingers rest on the part.
                GripperContact = true;
                return;
            }
            if (GripperTarget < Arm.Opening)
            {
                var part = PartBetweenFingers(out float width);
                if (part != null && Arm.Opening - speed <= width)
                {
                    Arm.Opening = Mathf.Max(width, 0);
                    Grasp(part);
                    return;
                }
            }
            if (Held != null && GripperTarget > Arm.Opening + 0.001f) Release();
            GripperContact = false;
            Arm.Opening = Mathf.MoveTowards(Arm.Opening, target, speed);
        }

        static readonly Collider[] between = new Collider[8];

        /// <summary>The part whose body lies between the finger pads now, and its width across them.</summary>
        Graspable PartBetweenFingers(out float width)
        {
            width = 0;
            var tcp = Arm.Tcp;
            // The pads close along the finger joint axis; the box between them reaches 2 cm along the
            // fingers from the TCP towards the hand and 1 cm past it.
            Vector3 closing = (Arm.Bodies[RobotArm.BodyIndex("leftfinger")].position - Arm.Bodies[RobotArm.BodyIndex("rightfinger")].position);
            if (closing.sqrMagnitude < 1e-8f) closing = tcp.right;
            closing.Normalize();
            Vector3 along = tcp.up;   // the TCP's +y in Unity is MuJoCo's +z: out of the hand
            Vector3 centre = tcp.position - along * 0.004f;
            var rot = Quaternion.LookRotation(along, closing);
            int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(0.006f, Arm.Opening * 0.5f + 0.002f, 0.016f), between, rot, Layers.GrabbableMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var g = between[i].GetComponentInParent<Graspable>();
                if (g == null || g.Held) continue;
                width = g.WidthAlong(closing);
                if (width > RobotArm.MaxOpening) continue;
                return g;
            }
            return null;
        }

        void Grasp(Graspable part)
        {
            Held = part;
            part.Held = true;
            if (part.Body != null)
            {
                part.Body.isKinematic = true;
                part.Body.interpolation = RigidbodyInterpolation.None;
            }
            part.transform.SetParent(Arm.Hand, true);
            GripperContact = true;
            Emit("grasp", part.Id);
        }

        void Release()
        {
            var part = Held;
            Held = null;
            if (part == null) return;
            part.Held = false;
            part.transform.SetParent(part.SpawnParent, true);
            if (part.Body != null)
            {
                part.Body.isKinematic = false;
                part.Body.interpolation = RigidbodyInterpolation.Interpolate;
                part.Body.linearVelocity = tcpVelocity;
                part.Body.angularVelocity = Vector3.zero;
            }
            Emit("release", part.Id);
        }

        /// <summary>Drops whatever is held and puts every part back where it started.</summary>
        public void ResetParts()
        {
            if (Held != null) Release();
            foreach (var p in Parts) p.Respawn();
        }

        // ------------------------------------------------------------------ motions
        public abstract class MotionSegment
        {
            public float Duration;
            public float Clock;
            public bool Done => Clock >= Duration;
        }

        class JointSegment : MotionSegment
        {
            public float[] From = new float[RobotArm.Dof];
            public float[] To = new float[RobotArm.Dof];
        }

        class LinearSegment : MotionSegment
        {
            public Pose From;
            public Pose To;
        }

        static float Smoother(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }

        JointSegment PlanJoint(float[] to, float speed)
        {
            var s = new JointSegment();
            Array.Copy(Arm.Q, s.From, RobotArm.Dof);
            Array.Copy(to, s.To, RobotArm.Dof);
            float t = 0.25f;
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                float d = Mathf.Abs(to[j] - Arm.Q[j]);
                float v = Arm.VMax[j] * 0.6f * speed;
                float a = v * 3f;
                // A quintic blend peaks at 1.875 d / T in speed and 5.77 d / T^2 in acceleration.
                t = Mathf.Max(t, Mathf.Max(1.875f * d / Mathf.Max(v, 1e-3f), Mathf.Sqrt(5.77f * d / Mathf.Max(a, 1e-3f))));
            }
            s.Duration = t;
            return s;
        }

        LinearSegment PlanLinear(Pose to, float speed)
        {
            var s = new LinearSegment { From = ToLocal(Arm.TcpPose), To = to };
            float l = Vector3.Distance(s.From.position, to.position);
            float ang = Quaternion.Angle(s.From.rotation, to.rotation) * Mathf.Deg2Rad;
            float v = LinearSpeed * speed;
            float w = AngularSpeed * speed;
            s.Duration = Mathf.Max(0.25f, Mathf.Max(1.875f * l / Mathf.Max(v, 1e-3f), Mathf.Max(1.875f * ang / Mathf.Max(w, 1e-3f), Mathf.Sqrt(5.77f * l / 1.0f))));
            return s;
        }

        public void StartProgram(bool loop = false)
        {
            if (Program.Steps.Count == 0) { Message = "The program has no steps"; return; }
            if (Stopped) { Message = "Clear the stop first"; return; }
            Program.Loop = loop;
            Running = true;
            Paused = false;
            StepIndex = -1;
            CycleTime = 0;
            segment = null;
            Mode = JogMode.Idle;
            NextStep();
            Emit("program_start", Program.Name);
        }

        public void StopProgram(string why = "stopped")
        {
            if (!Running) return;
            Running = false;
            Paused = false;
            segment = null;
            StepIndex = -1;
            Emit("program_end", why);
        }

        public void PauseProgram(bool pause)
        {
            if (!Running) return;
            Paused = pause;
            Emit(pause ? "program_pause" : "program_resume");
        }

        /// <summary>Moves to a joint configuration as a one-step program (Home, a lesson's preset).</summary>
        public void MoveToJoints(float[] q, string name = "home")
        {
            Program = new RobotProgram { Name = name };
            var wp = new Waypoint(name, q, ToLocal(Arm.Forward(q)));
            Program.Points.Add(wp);
            Program.Steps.Add(new Step(StepKind.MoveJ, wp));
            homeMove = true;
            StartProgram();
        }

        void NextStep()
        {
            StepIndex++;
            if (StepIndex >= Program.Steps.Count)
            {
                Cycles++;
                LastCycleTime = CycleTime;
                Emit("cycle", Program.Name);
                if (Program.Loop && !homeMove)
                {
                    CycleTime = 0;
                    StepIndex = 0;
                }
                else
                {
                    Running = false;
                    StepIndex = -1;
                    segment = null;
                    bool wasHome = homeMove;
                    homeMove = false;
                    Emit("program_end", wasHome ? "home" : "done");
                    return;
                }
            }
            var step = Program.Steps[StepIndex];
            stepClock = 0;
            segment = step.Kind switch
            {
                StepKind.MoveJ => PlanJoint(step.Target.Q, step.Speed),
                StepKind.Home => PlanJoint(Arm.Home, step.Speed),
                StepKind.MoveL => (MotionSegment)PlanLinear(step.Target.Pose, step.Speed),
                _ => null,
            };
            if (step.Kind == StepKind.Close) CloseGripper();
            if (step.Kind == StepKind.Open) OpenGripper();
            Emit("step", step.Label);
        }

        void TickProgram(float dt)
        {
            if (Paused || Stopped) return;
            if (StepMode && StepIndex >= 0 && segment == null && stepClock > 0.5f) return;
            var step = Program.Steps[StepIndex];
            stepClock += dt;
            switch (step.Kind)
            {
                case StepKind.MoveJ:
                case StepKind.Home:
                {
                    var s = (JointSegment)segment;
                    s.Clock += dt * EffectiveSpeed;
                    float u = Smoother(s.Clock / s.Duration);
                    for (int j = 0; j < RobotArm.Dof; j++) qCandidate[j] = Mathf.Lerp(s.From[j], s.To[j], u);
                    Commit(qCandidate, out bool blocked);
                    if (blocked)
                    {
                        Safety.ProtectiveStop("Contact: the path runs into " + BlockedBy);
                        return;
                    }
                    if (s.Done) NextStep();
                    break;
                }
                case StepKind.MoveL:
                {
                    var s = (LinearSegment)segment;
                    s.Clock += dt * EffectiveSpeed;
                    float u = Smoother(s.Clock / s.Duration);
                    var pose = ToWorld(Pose.Lerp(s.From, s.To, u));
                    var result = Arm.Solve(pose, Arm.Q, qCandidate, iterations: 20);
                    if (result.positionError > 0.006f)
                    {
                        Safety.Fault("Linear move unreachable: the straight line leaves the arm's workspace");
                        StopProgram("unreachable");
                        return;
                    }
                    Commit(qCandidate, out bool blocked);
                    if (blocked)
                    {
                        Safety.ProtectiveStop("Contact: the path runs into " + BlockedBy);
                        return;
                    }
                    if (s.Done) NextStep();
                    break;
                }
                case StepKind.Close:
                    if (Held != null || Arm.Opening <= 0.0005f) NextStep();
                    break;
                case StepKind.Open:
                    if (Arm.Opening >= RobotArm.MaxOpening - 0.0005f) NextStep();
                    break;
                case StepKind.Wait:
                    if (stepClock >= step.Seconds) NextStep();
                    break;
            }
        }

        /// <summary>Progress through the current step, 0..1.</summary>
        public float StepProgress => segment == null ? 0 : Mathf.Clamp01(segment.Clock / Mathf.Max(segment.Duration, 1e-3f));

        // ------------------------------------------------------------------ teaching
        public Waypoint SaveWaypoint(string name = null)
        {
            name ??= "P" + (Program.Points.Count + 1);
            var wp = new Waypoint(name, Arm.Q, ToLocal(Arm.TcpPose));
            Program.Points.Add(wp);
            Program.Steps.Add(new Step(StepKind.MoveJ, wp));
            Emit("waypoint", name);
            return wp;
        }

        public void AddStep(StepKind kind, float seconds = 1f)
        {
            Program.Steps.Add(new Step(kind, null, seconds));
            Emit("step_added", kind.ToString());
        }

        public void ClearProgram()
        {
            StopProgram("cleared");
            Program = new RobotProgram();
            Emit("program_cleared");
        }

        /// <summary>
        /// The pick-and-place the web app's Quick start writes: above the part, down, close, lift,
        /// across, down, open, lift, home. Linear moves near the parts, a joint move across.
        /// </summary>
        public RobotProgram PickAndPlace(Vector3 pick, Vector3 place, float clearance = 0.12f, string name = "Pick and place")
        {
            var p = new RobotProgram { Name = name };
            var down = PickRotation();
            var pickAbove = new Pose(pick + Vector3.up * clearance, down);
            var pickAt = new Pose(pick, down);
            var placeAbove = new Pose(place + Vector3.up * clearance, down);
            var placeAt = new Pose(place + Vector3.up * 0.004f, down);
            var w1 = Waypoint(p, "Above part", pickAbove);
            var w2 = Waypoint(p, "Grasp", pickAt);
            var w3 = Waypoint(p, "Above place", placeAbove);
            var w4 = Waypoint(p, "Place", placeAt);
            if (w1 == null || w2 == null || w3 == null || w4 == null) return null;
            p.Steps.Add(new Step(StepKind.Open));
            p.Steps.Add(new Step(StepKind.MoveJ, w1));
            p.Steps.Add(new Step(StepKind.MoveL, w2, speed: 0.5f));
            p.Steps.Add(new Step(StepKind.Close));
            p.Steps.Add(new Step(StepKind.MoveL, w1, speed: 0.6f));
            p.Steps.Add(new Step(StepKind.MoveJ, w3));
            p.Steps.Add(new Step(StepKind.MoveL, w4, speed: 0.5f));
            p.Steps.Add(new Step(StepKind.Open));
            p.Steps.Add(new Step(StepKind.MoveL, w3, speed: 0.6f));
            p.Steps.Add(new Step(StepKind.Home));
            return p;
        }

        /// <summary>The hand pointing straight down, jaws turned as at home.</summary>
        public Quaternion PickRotation()
        {
            var home = Arm.Forward(Arm.Home);
            return home.rotation;
        }

        Waypoint Waypoint(RobotProgram p, string name, Pose pose)
        {
            var q = new float[RobotArm.Dof];
            var seed = p.Points.Count > 0 ? p.Points[p.Points.Count - 1].Q : Arm.Home;
            var r = Arm.Solve(pose, seed, q, iterations: 120);
            if (r.positionError > 0.003f)
            {
                r = Arm.Solve(pose, Arm.Home, q, iterations: 160);
                if (r.positionError > 0.003f)
                {
                    Debug.LogWarning($"[bridge] {name} is out of reach ({r.positionError * 1000:0} mm)");
                    return null;
                }
            }
            var wp = new Waypoint(name, q, ToLocal(pose));
            p.Points.Add(wp);
            return wp;
        }

        public void SetMode(JogMode mode)
        {
            if (Running && mode != JogMode.Idle) StopProgram("jog");
            Mode = mode;
            Target = Arm.TcpPose;
            Emit("mode", mode.ToString());
        }

        // ------------------------------------------------------------------ readings
        /// <summary>The TCP position in the base frame as the Panda reports it (x forward, y left, z up) [m].</summary>
        public Vector3 TcpInBase
        {
            get
            {
                var u = Arm.Root.transform.InverseTransformPoint(Arm.Tcp.position);
                return new Vector3(u.x, u.z, u.y);
            }
        }

        /// <summary>The TCP's roll, pitch and yaw in the base frame [deg].</summary>
        public Vector3 TcpRpyInBase
        {
            get
            {
                var u = Quaternion.Inverse(Arm.Root.transform.rotation) * Arm.Tcp.rotation;
                // Back to MuJoCo's frame: (w, x, y, z) = (w, -x', -z', -y').
                float w = u.w, x = -u.x, y = -u.z, z = -u.y;
                float roll = Mathf.Atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
                float pitch = Mathf.Asin(Mathf.Clamp(2 * (w * y - z * x), -1, 1));
                float yaw = Mathf.Atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
                return new Vector3(roll, pitch, yaw) * Mathf.Rad2Deg;
            }
        }

        public void Home() => MoveToJoints(Arm.Home, "home");

        /// <summary>Puts the arm at joint angles at once (a reset, a lesson's starting pose).</summary>
        public void SetQ(float[] q)
        {
            Arm.SetQ(q);
            Target = Arm.TcpPose;
        }
    }

    // ====================================================================== safety
    /// <summary>
    /// The cell's safety functions, as ISO 10218 (2025, which took in ISO/TS 15066) describes them for a
    /// collaborative application: emergency stop (latched, reset by hand), protective stop, speed and
    /// separation monitoring from a floor scanner's fields, and power and force limiting when a person
    /// touches the arm.
    /// </summary>
    public class Safety
    {
        readonly Cell cell;
        public StopKind Stop = StopKind.None;
        public string Reason = "";
        public bool EStopPressed;          // the mushroom is down (latched)
        public bool SeparationMonitoring;  // scanner fields active
        public bool ForceLimiting = true;  // contact with the arm stops it
        public float WarningRadius = 1.6f;
        public float ProtectiveRadius = 1.0f;
        public float ReducedSpeed = 0.3f;
        public string Zone = "clear";      // clear, warning, protective
        public float Scale = 1f;           // what the controller may use of the planned speed
        public bool AutoResume = true;     // after a separation stop
        public float HumanDistance = 99f;
        public string FaultText = "";
        float target = 1f;
        float contactCooldown;

        public Safety(Cell cell) { this.cell = cell; }

        public void Tick(float dt)
        {
            contactCooldown -= dt;
            HumanDistance = 99f;
            if (cell.HumanPresent)
            {
                var b = cell.Arm.Root.transform.position;
                var head = cell.HumanHead;
                HumanDistance = Vector2.Distance(new Vector2(head.x, head.z), new Vector2(b.x, b.z));
            }
            string zone = "clear";
            if (SeparationMonitoring && cell.HumanPresent)
            {
                if (HumanDistance < ProtectiveRadius) zone = "protective";
                else if (HumanDistance < WarningRadius) zone = "warning";
            }
            if (zone != Zone)
            {
                Zone = zone;
                cell.Emit("zone", zone);
                if (zone == "protective" && Stop == StopKind.None) ProtectiveStop("Person inside the protective field", auto: true);
            }
            if (Stop == StopKind.Protective && Reason.StartsWith("Person") && zone != "protective" && AutoResume) Clear("auto");

            // Power and force limiting: a hand touching a link while it moves stops the arm.
            if (ForceLimiting && cell.HumanPresent && Stop == StopKind.None && contactCooldown <= 0)
            {
                float moving = 0;
                foreach (var v in cell.JointVelocity) moving = Mathf.Max(moving, Mathf.Abs(v));
                if (moving > 0.02f)
                {
                    foreach (var hand in cell.HumanHands)
                    {
                        if (cell.Arm.DistanceTo(hand, out _) < 0.025f)
                        {
                            contactCooldown = 1.5f;
                            ProtectiveStop("Contact detected: force limit exceeded (power and force limiting)");
                            cell.Emit("contact");
                            break;
                        }
                    }
                }
            }

            target = Stop != StopKind.None ? 0f : zone == "warning" ? ReducedSpeed : 1f;
            // Category 1 stop and speed changes ramp over about 0.3 s; an emergency stop is immediate.
            Scale = Stop == StopKind.Emergency ? 0f : Mathf.MoveTowards(Scale, target, dt / 0.3f);
        }

        public void PressEStop()
        {
            if (EStopPressed) return;
            EStopPressed = true;
            Stop = StopKind.Emergency;
            Reason = "Emergency stop pressed";
            Scale = 0;
            cell.PauseProgram(true);
            cell.Emit("estop");
        }

        /// <summary>Twist to release the mushroom: the stop stays latched until Reset.</summary>
        public void ReleaseEStop()
        {
            if (!EStopPressed) return;
            EStopPressed = false;
            cell.Emit("estop_release");
        }

        /// <summary>Resets a stop once its cause is gone; an emergency stop needs its button released first.</summary>
        public bool Reset()
        {
            if (Stop == StopKind.Emergency && EStopPressed)
            {
                cell.Message = "Release the emergency stop first (twist it)";
                return false;
            }
            if (Stop == StopKind.Protective && Zone == "protective")
            {
                cell.Message = "Step out of the protective field first";
                return false;
            }
            Clear("reset");
            return true;
        }

        void Clear(string how)
        {
            if (Stop == StopKind.None) return;
            Stop = StopKind.None;
            Reason = "";
            FaultText = "";
            cell.PauseProgram(false);
            cell.Emit(how == "auto" ? "resume" : "reset");
        }

        public void ProtectiveStop(string reason, bool auto = false)
        {
            if (Stop == StopKind.Emergency) return;
            Stop = StopKind.Protective;
            Reason = reason;
            cell.PauseProgram(true);
            cell.Emit("protective_stop", reason);
        }

        public void Fault(string reason)
        {
            FaultText = reason;
            ProtectiveStop(reason);
        }

        /// <summary>The stack light's state (IEC 60204-1 colours).</summary>
        public string Lamp
        {
            get
            {
                if (Stop != StopKind.None) return "red";
                if (Zone == "warning") return "amber";
                if (cell.Running) return "green";
                if (cell.Mode != JogMode.Idle) return "blue";
                return "green";
            }
        }
    }

    // ====================================================================== telemetry
    /// <summary>
    /// What the cell's digital twin streams: joint positions and speeds, a gravity-torque model,
    /// motor temperatures from a first-order thermal model, electrical power and energy, cycles.
    /// All simulated, and said so wherever it is shown.
    /// </summary>
    public class Telemetry
    {
        readonly Cell cell;
        public readonly float[] Torque = new float[RobotArm.Dof];
        public readonly float[] Temperature = new float[RobotArm.Dof];
        public readonly float[] Power = new float[RobotArm.Dof];
        public float TotalPower;       // W, including the controller's idle draw
        public float Energy;           // J since start
        public float EnergyThisCycle;
        public float LastCycleEnergy;
        public float Ambient = 24f;
        public float Wear = 1f;        // > 1 makes joint 4 run hot (the predictive-maintenance lesson)
        public float Uptime;
        public float RunningTime;
        public int GoodParts, RejectedParts;
        public readonly Ring[] JointHistory = new Ring[RobotArm.Dof];
        public readonly Ring[] TempHistory = new Ring[RobotArm.Dof];
        public readonly Ring PowerHistory = new(240);
        public readonly Ring SpeedHistory = new(240);
        float sample;
        int lastCycles;
        readonly float[] gravity = new float[RobotArm.Dof];

        public Telemetry(Cell cell)
        {
            this.cell = cell;
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                Temperature[j] = Ambient + 6f;
                JointHistory[j] = new Ring(240);
                TempHistory[j] = new Ring(240);
            }
        }

        public void Tick(float dt)
        {
            Uptime += dt;
            if (cell.Running) RunningTime += dt;
            cell.Arm.GravityTorques(gravity);
            TotalPower = 85f; // the controller's own draw
            var qd = cell.JointVelocity;
            for (int j = 0; j < RobotArm.Dof; j++)
            {
                // Holding torque against gravity plus a viscous term while moving.
                Torque[j] = gravity[j] + 2.5f * qd[j];
                float mech = Mathf.Abs(Torque[j] * qd[j]);
                // Motors draw current to hold against gravity even when still (no brakes engaged).
                float hold = 0.6f * Mathf.Abs(gravity[j]);
                Power[j] = mech / 0.8f + hold;
                TotalPower += Power[j];
                float heat = (0.04f * Power[j] + 0.02f * Mathf.Abs(gravity[j])) * (j == 3 ? Wear : 1f);
                Temperature[j] += (heat - (Temperature[j] - Ambient) / 90f) * dt;
            }
            Energy += TotalPower * dt;
            EnergyThisCycle += TotalPower * dt;
            if (cell.Cycles != lastCycles)
            {
                lastCycles = cell.Cycles;
                LastCycleEnergy = EnergyThisCycle;
                EnergyThisCycle = 0;
            }
            sample += dt;
            if (sample >= 0.25f)
            {
                sample = 0;
                for (int j = 0; j < RobotArm.Dof; j++)
                {
                    JointHistory[j].Push(cell.Arm.Q[j]);
                    TempHistory[j].Push(Temperature[j]);
                }
                PowerHistory.Push(TotalPower);
                SpeedHistory.Push(cell.TcpVelocity.magnitude);
            }
        }

        /// <summary>Overall equipment effectiveness: availability x performance x quality (simulated).</summary>
        public void Oee(out float availability, out float performance, out float quality)
        {
            availability = Uptime > 1 ? Mathf.Clamp01(RunningTime / Uptime + 0.55f) : 1f;
            performance = cell.LastCycleTime > 0 ? Mathf.Clamp01(9f / cell.LastCycleTime) : 0.8f;
            int total = GoodParts + RejectedParts;
            quality = total > 0 ? GoodParts / (float)total : 1f;
        }
    }

    /// <summary>A fixed-size history for charts.</summary>
    public class Ring
    {
        readonly float[] data;
        int head;
        public int Count { get; private set; }
        public Ring(int size) { data = new float[size]; }
        public int Capacity => data.Length;
        public void Push(float v) { data[head] = v; head = (head + 1) % data.Length; Count = Mathf.Min(Count + 1, data.Length); }
        /// <summary>The i-th oldest sample still kept.</summary>
        public float this[int i] => data[(head - Count + i + data.Length * 2) % data.Length];
        public float Last => Count == 0 ? 0 : this[Count - 1];
        public void Clear() { Count = 0; head = 0; }
    }
}
