// SPDX-License-Identifier: LicenseRef-BRIDGE
// The robotics lab around the cell, ported from the web app's frontend/src/viewport/factory/lab.ts:
// a palletizing arm stacking cartons off an infeed, a delta picker taking parts off a moving belt,
// two cobots passing parts between their trays, and a mobile robot driving its loop. They are
// scenery that runs its own loops, as on the web page; the arms follow their tools through the
// same closed-form inverse kinematics. One addition for a headset: the mobile robot stops when a
// person stands in its path, as a real one's scanner makes it do.
//
// Everything below is written in the web app's frame (z up) and mapped with Web.P / Web.About.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bridge.World
{
    public class Lab
    {
        public readonly Transform Root;
        readonly List<Station> stations = new();
        float time;
        public bool Moving = true;
        public Vector3 Person;          // the user's head, for the mobile robot's scanner
        public bool AmrBlocked { get; private set; }

        static float[] V(params float[] v) => v;
        static float Ease(float t) => t <= 0 ? 0 : t >= 1 ? 1 : 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
        static readonly float[] YAxis = { Mathf.PI / 2, 0, 0 };   // a +z cylinder lying along y
        static readonly float[] XAxis = { 0, Mathf.PI / 2, 0 };   // a +z cylinder lying along x

        public Lab(Transform parent)
        {
            Root = new GameObject("Robotics lab").transform;
            Root.SetParent(parent, false);
            var m = new LabMaterials();
            stations.Add(Palletizer(m, -7.6f, 2.6f));
            stations.Add(DeltaPicker(m, -1.4f, 6.65f));
            stations.Add(CobotTable(m, 10.7f, 2.4f, Mathf.PI / 2));
            stations.Add(Amr(m));
            Go.FloorDecal("ROBOTICS LAB", Root, m.Lettering, 4, 1, -4.9f, 7.05f, 0, 0.004f);
            Tick(0);
        }

        public void Tick(float dt)
        {
            if (Moving) time += Mathf.Min(dt, 0.1f);
            foreach (var s in stations) s.Update(time, dt);
        }

        abstract class Station
        {
            public Transform Group;
            public abstract void Update(float time, float dt);
        }

        class LabMaterials
        {
            public Material RobotOrange = Mats.Paint(0xf0661a, 0.3f, 0.05f);
            public Material Graphite = Mats.Paint(0x2a2e33, 0.5f, 0.35f);
            public Material Steel = Mats.Paint(Pal.SteelGrey, 0.38f, 0.8f);
            public Material Aluminium = Mats.Paint(0xc3c8cd, 0.3f, 0.85f);
            public Material CobotWhite = Mats.Paint(0xe6e9ec, 0.28f, 0.02f);
            public Material Accent = Mats.Paint(0xff4713, 0.4f, 0.1f);
            public Material Rubber = Mats.Paint(0x16171a, 0.85f, 0);
            public Material Belt = Mats.Paint(0x1c1f23, 0.8f, 0.05f);
            public Material Tabletop = Mats.Paint(0xdfe2e4, 0.55f, 0.05f);
            public Material Tray = Mats.Paint(0x30353b, 0.6f, 0.1f);
            public Material Part = Mats.Paint(0xff5a1f, 0.38f, 0.15f);
            public Material Pallet = Mats.Paint(0xb08a5a, 0.85f, 0);
            public Material AmrWhite = Mats.Paint(0xe9eaea, 0.32f, 0.05f);
            public Material Yellow = Mats.Paint(Pal.SafetyYellow, 0.5f, 0);
            public Material LedBlue = Mats.Glow(0x4b9dff, 4);
            public Material LedGreen = Mats.Glow(0x3ddc84, 4);
            public Material LedOrange = Mats.Glow(0xff4713, 3.5f);
            public Material LedAmber = Mats.Glow(0xffa000, 5);
            public Material Carton = Mats.Textured("carton", Vector2.one, 0.85f, 0);
            public Material Hazard = Mats.Textured("hazard", new Vector2(6, 1), 0.5f, 0);
            public Material Blob = Mats.Decal("blob", 0.85f, lit: false);
            public Material Lettering = Mats.Decal("floor_lab", 0.8f);
        }

        /// <summary>A rigid link: a pivot at web position `at` under its parent, its parts batched beneath.</summary>
        static Transform Link(Transform parent, float[] at, Action<Batcher> parts, string name)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = Web.P(at[0], at[1], at[2]);
            var b = new Batcher();
            parts(b);
            b.Build(name + " parts", pivot, true, Layers.Environment);
            foreach (var r in pivot.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return pivot;
        }

        static Transform Part(Mesh mesh, Material mat, Transform parent, float[] at = null, float[] rot = null)
        {
            at ??= V(0, 0, 0);
            var go = Go.Mesh(mat.name, parent, mesh, mat, Web.P(at[0], at[1], at[2]), rot == null ? Quaternion.identity : Web.Q(rot[0], rot[1], rot[2]));
            return go.transform;
        }

        static void Decal(Transform parent, Material m, float x, float y, float w, float d, float z = 0.0035f) => Go.FloorDecal("decal", parent, m, w, d, x, y, 0, z);

        static Mesh Rounded(float x, float y, float z, float r) => MeshKit.RoundedBox(Web.S(x, y, z), r);
        static Mesh Cyl(float r, float h, int seg = 24, float top = -1) => MeshKit.Cylinder(r, h, seg, top);

        static Vector3 W(float x, float y, float z) => new(x, y, z);   // a web-frame vector

        // ================================================================== the industrial arm
        /// <summary>A 1.6 m-reach six-axis arm (KR 16 proportions); its tool points along +x of the flange.</summary>
        class IndustrialArm
        {
            public const float Foot = 0.1f, ShX = 0.16f, ShZ = 0.675f, L1 = 0.68f, A3 = 0.035f, L2A = 0.3f, L2B = 0.37f, Wr = 0.115f, Tool = 0.19f;
            public readonly Transform Root, ToolT;
            readonly Transform[] j;

            public IndustrialArm(LabMaterials m, Transform parent)
            {
                Root = new GameObject("industrial arm").transform;
                Root.SetParent(parent, false);
                Link(Root, V(0, 0, 0), b =>
                {
                    b.Box(m.Steel, V(0.64f, 0.64f, 0.025f), V(0, 0, 0.0125f));
                    b.Shape(Cyl(0.27f, Foot - 0.025f, 40), m.Graphite, V(0, 0, 0.025f + (Foot - 0.025f) / 2));
                    foreach (var (x, y) in new[] { (0.27f, 0.27f), (0.27f, -0.27f), (-0.27f, 0.27f), (-0.27f, -0.27f) })
                        b.Shape(Cyl(0.018f, 0.024f, 12), m.Aluminium, V(x, y, 0.037f));
                }, "arm-foot");
                var j1 = Link(Root, V(0, 0, Foot), b =>
                {
                    b.Shape(Rounded(0.52f, 0.46f, 0.22f, 0.05f), m.RobotOrange, V(0.02f, 0, 0.11f));
                    b.Shape(Rounded(0.32f, 0.36f, 0.46f, 0.07f), m.RobotOrange, V(ShX - 0.04f, 0, 0.36f));
                    b.Shape(Cyl(0.075f, 0.2f, 20), m.Graphite, V(-0.19f, 0.13f, 0.2f));
                    b.Shape(Cyl(0.11f, 0.15f, 24), m.Graphite, V(ShX, 0.255f, ShZ - Foot), YAxis);
                    b.Shape(Cyl(0.06f, 0.02f, 20), m.Aluminium, V(ShX, 0.335f, ShZ - Foot), YAxis);
                }, "arm-a1");
                var j2 = Link(j1, V(ShX, 0, ShZ - Foot), b =>
                {
                    b.Shape(Cyl(0.155f, 0.34f, 32), m.RobotOrange, V(0, 0, 0), YAxis);
                    b.Shape(Rounded(0.18f, 0.21f, L1 + 0.1f, 0.06f), m.RobotOrange, V(0, -0.02f, L1 / 2));
                }, "arm-a2");
                var j3 = Link(j2, V(0, 0, L1), b =>
                {
                    b.Shape(Cyl(0.125f, 0.3f, 32), m.RobotOrange, V(0, 0, 0), YAxis);
                    b.Shape(Rounded(0.26f, 0.24f, 0.2f, 0.05f), m.Graphite, V(-0.22f, 0, A3));
                    b.Shape(Rounded(L2A + 0.08f, 0.2f, 0.19f, 0.06f), m.RobotOrange, V(L2A / 2 - 0.02f, 0, A3));
                    b.Shape(Cyl(0.05f, 0.02f, 16), m.Accent, V(0, 0.155f, 0), YAxis);
                }, "arm-a3");
                var j4 = Link(j3, V(L2A, 0, A3), b => b.Shape(Cyl(0.078f, L2B - 0.05f, 28), m.RobotOrange, V((L2B - 0.05f) / 2, 0, 0), XAxis), "arm-a4");
                var j5 = Link(j4, V(L2B, 0, 0), b =>
                {
                    b.Shape(Rounded(0.14f, 0.13f, 0.15f, 0.05f), m.RobotOrange, V(0.015f, 0, 0));
                    b.Shape(Cyl(0.056f, 0.17f, 24), m.Graphite, V(0, 0, 0), YAxis);
                }, "arm-a5");
                var j6 = Link(j5, V(Wr, 0, 0), b =>
                {
                    b.Shape(Cyl(0.05f, 0.03f, 24), m.Aluminium, V(0.015f, 0, 0), XAxis);
                    b.Shape(Cyl(0.04f, 0.06f, 20), m.Graphite, V(0.06f, 0, 0), XAxis);
                    b.Box(m.Graphite, V(0.035f, 0.38f, 0.3f), V(0.105f, 0, 0));
                    b.Box(m.Accent, V(0.036f, 0.38f, 0.03f), V(0.105f, 0, 0.12f));
                    foreach (var (y, z) in new[] { (0.13f, 0.095f), (0.13f, -0.095f), (-0.13f, 0.095f), (-0.13f, -0.095f) })
                        b.Shape(Cyl(0.026f, 0.07f, 16, 0.034f), m.Rubber, V(0.155f, y, z), XAxis);
                }, "arm-a6");
                ToolT = new GameObject("tool").transform;
                ToolT.SetParent(j6, false);
                ToolT.localPosition = Web.P(Tool, 0, 0);
                j = new[] { j1, j2, j3, j4, j5, j6 };
            }

            public void Set(float[] q)
            {
                j[0].localRotation = Web.About('z', q[0]);
                j[1].localRotation = Web.About('y', q[1]);
                j[2].localRotation = Web.About('y', q[2]);
                j[3].localRotation = Web.About('x', q[3]);
                j[4].localRotation = Web.About('y', q[4]);
                j[5].localRotation = Web.About('x', q[5]);
            }

            /// <summary>Joints that put the tool face at p (arm frame, web) pointing straight down, its long side along yaw.</summary>
            public static float[] Solve(Vector3 p, float yaw)
            {
                float q1 = Mathf.Atan2(p.y, p.x);
                float dr = Mathf.Sqrt(p.x * p.x + p.y * p.y) - ShX;
                float dz = p.z + Wr + Tool - ShZ;
                float lf = Mathf.Sqrt((L2A + L2B) * (L2A + L2B) + A3 * A3);
                float delta = Mathf.Atan2(A3, L2A + L2B);
                float d = Mathf.Min(Mathf.Sqrt(dr * dr + dz * dz), L1 + lf - 1e-4f);
                float beta = Mathf.Acos(Mathf.Clamp((L1 * L1 + d * d - lf * lf) / (2 * L1 * d), -1, 1));
                float a1 = Mathf.Atan2(dz, dr) + beta;
                float a3 = Mathf.Atan2(dz - L1 * Mathf.Sin(a1), dr - L1 * Mathf.Cos(a1)) - delta;
                float q2 = Mathf.PI / 2 - a1;
                return new[] { q1, q2, -q2 - a3, 0, a3 + Mathf.PI / 2, q1 - yaw };
            }
        }

        /// <summary>Interpolates in cylindrical coordinates about the base, so a long move swings round like a real arm.</summary>
        static Vector3 Swing(Vector3 a, Vector3 b, float t, float lift)
        {
            float ra = Mathf.Sqrt(a.x * a.x + a.y * a.y), rb = Mathf.Sqrt(b.x * b.x + b.y * b.y);
            float pa = Mathf.Atan2(a.y, a.x), pb = Mathf.Atan2(b.y, b.x);
            if (pb - pa > Mathf.PI) pb -= 2 * Mathf.PI;
            if (pa - pb > Mathf.PI) pb += 2 * Mathf.PI;
            float k = Ease(t);
            float r = ra + (rb - ra) * k;
            float phi = pa + (pb - pa) * k;
            return W(r * Mathf.Cos(phi), r * Mathf.Sin(phi), a.z + (b.z - a.z) * k + Mathf.Sin(Mathf.PI * k) * lift);
        }

        // ================================================================== the palletizer
        class PalletizerStation : Station
        {
            public IndustrialArm Arm;
            public Vector3 Pick, PickUp;
            public List<Vector3> Slots = new();
            public Transform Waiting, Carried;
            public List<Transform> Stacked = new();
            public float RollZ, C1;
            const float Cycle = 6.4f;

            public override void Update(float time, float dt)
            {
                int n = Mathf.FloorToInt(time / Cycle);
                float t = time - n * Cycle;
                int k = n % Slots.Count;
                var slot = Slots[k];
                var slotUp = slot + W(0, 0, 0.3f);
                Vector3 target;
                float yaw = Mathf.PI / 2;
                if (t < 0.55f) target = Vector3.Lerp(PickUp, Pick, Ease(t / 0.55f));
                else if (t < 0.8f) target = Pick;
                else if (t < 1.4f) target = Vector3.Lerp(Pick, PickUp, Ease((t - 0.8f) / 0.6f));
                else if (t < 3.0f)
                {
                    target = Swing(PickUp, slotUp, (t - 1.4f) / 1.6f, 0.08f);
                    yaw = Mathf.PI / 2 * (1 - Ease((t - 1.4f) / 1.6f));
                }
                else if (t < 4.4f)
                {
                    yaw = 0;
                    if (t < 3.6f) target = Vector3.Lerp(slotUp, slot, Ease((t - 3.0f) / 0.6f));
                    else if (t < 3.85f) target = slot;
                    else target = Vector3.Lerp(slot, slotUp, Ease((t - 3.85f) / 0.55f));
                }
                else if (t < 6.0f)
                {
                    target = Swing(slotUp, PickUp, (t - 4.4f) / 1.6f, 0.06f);
                    yaw = Mathf.PI / 2 * Ease((t - 4.4f) / 1.6f);
                }
                else target = PickUp;
                Arm.Set(IndustrialArm.Solve(target, yaw));
                Carried.gameObject.SetActive(t >= 0.65f && t < 3.7f);
                for (int i = 0; i < Stacked.Count; i++) Stacked[i].gameObject.SetActive(i < k || (i == k && t >= 3.7f));
                Waiting.gameObject.SetActive(t < 0.65f || t >= 1.6f);
                float travel = t >= 1.6f ? 1 - Ease(Mathf.Clamp01((t - 1.6f) / 2.2f)) : 0;
                var wp = Waiting.localPosition;
                Waiting.localPosition = Web.P(Pick.x, Pick.y + travel * (C1 - 0.3f - Pick.y), RollZ + 0.13f);
            }
        }

        Station Palletizer(LabMaterials m, float x, float y)
        {
            var group = new GameObject("palletizer").transform;
            group.SetParent(Root, false);
            group.localPosition = Web.P(x, y, 0);
            var st = new PalletizerStation { Group = group };
            st.Arm = new IndustrialArm(m, group);
            var box = W(0.4f, 0.3f, 0.26f);
            const float rollZ = 0.7f, c0 = 0.82f, c1 = 3.1f;
            st.RollZ = rollZ;
            st.C1 = c1;
            st.Pick = W(0, 1.08f, rollZ + box.z);
            st.PickUp = st.Pick + W(0, 0, 0.3f);
            var pallet = W(1.18f, 0, 0.144f);
            for (int layer = 0; layer < 2; layer++)
                foreach (var (sx, sy) in new[] { (-0.21f, -0.155f), (0.21f, -0.155f), (-0.21f, 0.155f), (0.21f, 0.155f) })
                    st.Slots.Add(W(pallet.x + sx, pallet.y + sy, pallet.z + box.z * (layer + 1)));

            var s = new Batcher();
            foreach (float dx in new[] { -0.26f, 0.26f }) s.Box(m.Steel, V(0.05f, c1 - c0, 0.1f), V(dx, (c0 + c1) / 2, rollZ - 0.03f), cast: true);
            for (float yy = c0 + 0.06f; yy < c1 - 0.04f; yy += 0.11f) s.Shape(Cyl(0.022f, 0.5f, 12), m.Aluminium, V(0, yy, rollZ - 0.022f), XAxis);
            foreach (float yy in new[] { c0 + 0.2f, c1 - 0.2f }) foreach (float dx in new[] { -0.26f, 0.26f }) s.Box(m.Graphite, V(0.05f, 0.05f, rollZ - 0.08f), V(dx, yy, (rollZ - 0.08f) / 2), cast: true);
            s.Box(m.Pallet, V(1.2f, 0.8f, 0.022f), V(pallet.x, pallet.y, 0.133f), cast: true);
            foreach (float py in new[] { -0.36f, 0f, 0.36f }) s.Box(m.Pallet, V(1.2f, 0.1f, 0.1f), V(pallet.x, py, 0.072f), cast: true);
            foreach (float px in new[] { -0.55f, 0f, 0.55f }) foreach (float py in new[] { -0.36f, 0f, 0.36f }) s.Box(m.Pallet, V(0.14f, 0.1f, 0.022f), V(pallet.x + px, py, 0.011f));
            const float lz = 0.0025f;
            s.Box(m.Yellow, V(3.0f, 0.08f, 0.004f), V(0.35f, -1.25f, lz));
            s.Box(m.Yellow, V(3.0f, 0.08f, 0.004f), V(0.35f, 1.45f, lz));
            s.Box(m.Yellow, V(0.08f, 2.7f, 0.004f), V(-1.15f, 0.1f, lz));
            s.Box(m.Yellow, V(0.08f, 2.7f, 0.004f), V(1.85f, 0.1f, lz));
            s.Build("palletizer static", group);
            Decal(group, m.Hazard, 0.35f, -1.45f, 3.0f, 0.32f, 0.003f);
            Decal(group, m.Blob, 0.35f, 0.4f, 3.4f, 3.0f);
            var boxMesh = MeshKit.RoundedBox(Web.S(box.x, box.y, box.z), 0.004f);
            st.Waiting = Part(boxMesh, m.Carton, group, V(st.Pick.x, st.Pick.y, rollZ + box.z / 2), V(0, 0, Mathf.PI / 2));
            st.Carried = Part(boxMesh, m.Carton, st.Arm.ToolT, V(box.z / 2, 0, 0), V(0, -Mathf.PI / 2, 0));
            foreach (var p in st.Slots)
            {
                var t = Part(boxMesh, m.Carton, group, V(p.x, p.y, p.z - box.z / 2));
                t.gameObject.SetActive(false);
                st.Stacked.Add(t);
            }
            Collider(group, 0.35f, 0.75f, 1.0f, 3.1f, 4.4f, 2.0f);
            return st;
        }

        static void Collider(Transform parent, float x, float y, float z, float sx, float sy, float sz)
        {
            var c = new GameObject("collider").AddComponent<BoxCollider>();
            c.gameObject.layer = Layers.Environment;
            c.transform.SetParent(parent, false);
            c.transform.localPosition = Web.P(x, y, z);
            c.size = Web.S(sx, sy, sz);
        }

        // ================================================================== the delta picker
        class DeltaStation : Station
        {
            public const float BaseZ = 1.72f, RB = 0.16f, RP = 0.05f, LA = 0.3f, LB = 0.7f, BeltZ = 0.82f, BeltY = -0.05f, Cup = 0.0925f;
            public const float PartH = 0.03f, Speed = 0.25f, Period = 1.2f, X0 = -0.82f, PickX = -0.05f;
            public Transform[] Upper = new Transform[3];
            public Vector3[] Hinges = new Vector3[3];
            public Transform[] Rods = new Transform[6];
            public Transform Platform, Carried;
            public Transform[] BeltParts = new Transform[4];
            public List<Vector3> TrayPos = new();
            public List<Transform> Placed = new();

            void Reach(Vector3 target)
            {
                for (int i = 0; i < 3; i++)
                {
                    float phi = i * 2 * Mathf.PI / 3;
                    var radial = W(Mathf.Cos(phi), Mathf.Sin(phi), 0);
                    var tangent = W(-Mathf.Sin(phi), Mathf.Cos(phi), 0);
                    var joint = target + radial * RP;
                    var d = joint - Hinges[i];
                    float dr = Vector3.Dot(d, radial), dtn = Vector3.Dot(d, tangent);
                    float A = -2 * LA * dr, B = 2 * LA * d.z;
                    float C = LB * LB - LA * LA - (dr * dr + dtn * dtn + d.z * d.z);
                    float off = Mathf.Acos(Mathf.Clamp(C / Mathf.Sqrt(A * A + B * B), -1, 1));
                    float mid = Mathf.Atan2(B, A);
                    float Wrap(float th) => Mathf.Atan2(Mathf.Sin(th), Mathf.Cos(th));
                    float t1 = Wrap(mid + off), t2 = Wrap(mid - off);
                    float theta = Mathf.Abs(t1 - 0.55f) < Mathf.Abs(t2 - 0.55f) ? t1 : t2;
                    Upper[i].localRotation = Web.About('y', theta);
                    var elbow = Hinges[i] + radial * (LA * Mathf.Cos(theta));
                    elbow.z -= LA * Mathf.Sin(theta);
                    for (int k = 0; k < 2; k++)
                    {
                        float side = k == 0 ? 1 : -1;
                        var a = elbow + tangent * (0.035f * side);
                        var b = joint + tangent * (0.035f * side);
                        var rod = Rods[i * 2 + k];
                        rod.localPosition = Web.P((a + b) * 0.5f);
                        var dir = Web.P(b - a);
                        rod.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                    }
                }
                Platform.localPosition = Web.P(target);
            }

            public override void Update(float time, float dt)
            {
                int n = Mathf.FloorToInt(time / Period);
                float t = (time - n * Period) / Period;
                float crossing = (n + 0.25f) * Period;
                float atBelt = BeltZ + PartH + Cup;
                var part = W(PickX + Speed * (time - crossing), BeltY, atBelt);
                var slot = TrayPos[n % TrayPos.Count];
                var drop = W(slot.x, slot.y, slot.z + PartH / 2 + Cup);
                var hover = W(PickX - 0.04f, BeltY, atBelt + 0.11f);
                Vector3 target;
                if (t < 0.25f) target = Vector3.Lerp(hover, W(part.x, part.y, atBelt), Ease(t / 0.25f));
                else if (t < 0.33f) target = part;
                else if (t < 0.62f)
                {
                    var at33 = W(PickX + Speed * 0.08f * Period, BeltY, atBelt);
                    float k = Ease((t - 0.33f) / 0.29f);
                    target = Vector3.Lerp(at33, drop, k);
                    target.z += Mathf.Sin(Mathf.PI * k) * 0.1f;
                }
                else if (t < 0.7f) target = drop;
                else
                {
                    float k = Ease((t - 0.7f) / 0.3f);
                    target = Vector3.Lerp(drop, hover, k);
                    target.z += Mathf.Sin(Mathf.PI * k) * 0.05f;
                }
                Reach(target);
                Carried.gameObject.SetActive(t >= 0.29f && t < 0.66f);
                for (int i = 0; i < BeltParts.Length; i++)
                {
                    int j = n + i;
                    float xj = PickX + Speed * (time - (j + 0.25f) * Period);
                    BeltParts[i].localPosition = Web.P(xj, BeltY, BeltZ + PartH / 2);
                    BeltParts[i].gameObject.SetActive(xj >= X0 && time < (j + 0.29f) * Period);
                }
                int filled = n % TrayPos.Count;
                for (int k = 0; k < Placed.Count; k++) Placed[k].gameObject.SetActive(k < filled || (k == filled && t >= 0.66f));
            }
        }

        Station DeltaPicker(LabMaterials m, float x, float y)
        {
            var group = new GameObject("delta picker").transform;
            group.SetParent(Root, false);
            group.localPosition = Web.P(x, y, 0);
            var st = new DeltaStation { Group = group };
            const float baseZ = DeltaStation.BaseZ, rb = DeltaStation.RB, la = DeltaStation.LA, lb = DeltaStation.LB, beltZ = DeltaStation.BeltZ, beltY = DeltaStation.BeltY;
            var s = new Batcher();
            foreach (var (px, py) in new[] { (-0.8f, -0.48f), (0.8f, -0.48f), (-0.8f, 0.48f), (0.8f, 0.48f) })
            {
                s.Box(m.Aluminium, V(0.06f, 0.06f, 1.98f), V(px, py, 0.99f), cast: true);
                s.Box(m.Graphite, V(0.16f, 0.16f, 0.012f), V(px, py, 0.006f));
            }
            foreach (float py in new[] { -0.48f, 0.48f }) s.Box(m.Aluminium, V(1.66f, 0.06f, 0.06f), V(0, py, 1.95f), cast: true);
            foreach (float px in new[] { -0.8f, 0f, 0.8f }) s.Box(m.Aluminium, V(0.06f, 1.02f, 0.06f), V(px, 0, 1.95f), cast: true);
            s.Box(m.Graphite, V(0.5f, 0.5f, 0.04f), V(0, 0, 1.9f), cast: true);
            s.Box(m.Graphite, V(0.06f, 0.06f, 0.12f), V(0, 0, baseZ + 0.12f), cast: true);
            s.Shape(Cyl(0.25f, 0.04f, 6), m.Aluminium, V(0, 0, baseZ + 0.06f), cast: true);
            for (int i = 0; i < 3; i++)
            {
                float phi = i * 2 * Mathf.PI / 3;
                s.Shape(Cyl(0.055f, 0.14f, 20), m.Graphite, V(Mathf.Cos(phi) * (rb - 0.03f), Mathf.Sin(phi) * (rb - 0.03f), baseZ + 0.02f), V(Mathf.PI / 2, 0, phi), cast: true);
            }
            s.Box(m.Belt, V(1.7f, 0.4f, 0.04f), V(0, beltY, beltZ - 0.02f), cast: true);
            foreach (float py in new[] { beltY - 0.22f, beltY + 0.22f }) s.Box(m.Steel, V(1.7f, 0.04f, 0.08f), V(0, py, beltZ - 0.005f), cast: true);
            foreach (float px in new[] { -0.7f, 0.7f }) foreach (float py in new[] { beltY - 0.22f, beltY + 0.22f }) s.Box(m.Graphite, V(0.05f, 0.05f, beltZ - 0.05f), V(px, py, (beltZ - 0.05f) / 2), cast: true);
            s.Box(m.Tray, V(0.4f, 0.17f, 0.025f), V(0, 0.3f, beltZ + 0.0125f), cast: true);
            s.Box(m.Graphite, V(0.05f, 0.05f, beltZ), V(0, 0.3f, beltZ / 2), cast: true);
            s.Build("delta static", group);
            Decal(group, m.Blob, 0, 0, 2.2f, 1.5f);
            for (int i = 0; i < 3; i++)
            {
                float phi = i * 2 * Mathf.PI / 3;
                var hinge = new GameObject("hinge " + i).transform;
                hinge.SetParent(group, false);
                st.Hinges[i] = W(Mathf.Cos(phi) * rb, Mathf.Sin(phi) * rb, baseZ);
                hinge.localPosition = Web.P(st.Hinges[i]);
                hinge.localRotation = Web.About('z', phi);
                st.Upper[i] = Link(hinge, V(0, 0, 0), b =>
                {
                    b.Shape(Rounded(la + 0.04f, 0.04f, 0.035f, 0.012f), m.RobotOrange, V(la / 2, 0, 0));
                    b.Shape(Cyl(0.022f, 0.09f, 16), m.Aluminium, V(la, 0, 0), YAxis);
                }, "delta arm " + i);
            }
            var rod = Cyl(0.008f, lb, 10);
            for (int i = 0; i < 6; i++) st.Rods[i] = Go.Mesh("rod", group, rod, m.Graphite).transform;
            st.Platform = new GameObject("platform").transform;
            st.Platform.SetParent(group, false);
            Part(Cyl(0.075f, 0.025f, 6), m.Aluminium, st.Platform);
            Part(Cyl(0.012f, 0.07f, 10), m.Graphite, st.Platform, V(0, 0, -0.045f));
            Part(Cyl(0.018f, 0.015f, 12, 0.012f), m.Rubber, st.Platform, V(0, 0, -0.085f));
            var partMesh = Cyl(0.026f, DeltaStation.PartH, 18);
            for (int i = 0; i < 4; i++) st.BeltParts[i] = Part(partMesh, m.Part, group);
            st.Carried = Part(partMesh, m.Part, st.Platform, V(0, 0, -DeltaStation.Cup - DeltaStation.PartH / 2));
            foreach (float ty in new[] { 0.265f, 0.335f })
                foreach (float tx in new[] { -0.135f, -0.045f, 0.045f, 0.135f })
                    st.TrayPos.Add(W(tx, ty, beltZ + 0.025f + DeltaStation.PartH / 2));
            foreach (var p in st.TrayPos)
            {
                var t = Part(partMesh, m.Part, group, V(p.x, p.y, p.z));
                t.gameObject.SetActive(false);
                st.Placed.Add(t);
            }
            Collider(group, 0, 0, 1.0f, 1.8f, 1.1f, 2.0f);
            return st;
        }

        // ================================================================== the cobots
        /// <summary>A collaborative arm in the style of a UR5: coplanar links and a two-finger gripper.</summary>
        class Cobot
        {
            public const float D1 = 0.16f, A2 = 0.42f, A3 = 0.39f, W1 = 0.1f, W2 = 0.09f, Tool = 0.135f;
            public readonly Transform Root, Tip;
            readonly Transform[] j;
            readonly Transform[] fingers = new Transform[2];

            public Cobot(LabMaterials m, Transform parent)
            {
                Root = new GameObject("cobot").transform;
                Root.SetParent(parent, false);
                void Ring(Batcher b, float[] at, float[] rot, float r) => b.Shape(Cyl(r + 0.002f, 0.012f, 32), m.Accent, at, rot);
                void Cap(Batcher b, float[] at, float[] rot, float r) => b.Shape(Cyl(r * 0.82f, 0.006f, 32), m.Graphite, at, rot);
                Link(Root, V(0, 0, 0), b =>
                {
                    b.Shape(Cyl(0.078f, 0.05f, 32), m.Graphite, V(0, 0, 0.025f));
                    Ring(b, V(0, 0, 0.05f), V(0, 0, 0), 0.076f);
                }, "cobot base");
                var j1 = Link(Root, V(0, 0, 0.05f), b =>
                {
                    b.Shape(Cyl(0.062f, 0.1f, 32), m.CobotWhite, V(0, 0, 0.05f));
                    b.Shape(Cyl(0.064f, 0.13f, 32), m.CobotWhite, V(0, 0, D1 - 0.05f), YAxis);
                    Cap(b, V(0, 0.066f, D1 - 0.05f), YAxis, 0.064f);
                    Ring(b, V(0, -0.06f, D1 - 0.05f), YAxis, 0.064f);
                }, "cobot j1");
                var j2 = Link(j1, V(0, 0, D1 - 0.05f), b => b.Shape(Cyl(0.044f, A2, 28), m.CobotWhite, V(0, 0, A2 / 2)), "cobot j2");
                var j3 = Link(j2, V(0, 0, A2), b =>
                {
                    b.Shape(Cyl(0.054f, 0.115f, 32), m.CobotWhite, V(0, 0, 0), YAxis);
                    Cap(b, V(0, 0.058f, 0), YAxis, 0.054f);
                    Ring(b, V(0, -0.052f, 0), YAxis, 0.054f);
                    b.Shape(Cyl(0.037f, A3, 28), m.CobotWhite, V(0, 0, A3 / 2));
                }, "cobot j3");
                var j4 = Link(j3, V(0, 0, A3), b =>
                {
                    b.Shape(Cyl(0.043f, 0.1f, 28), m.CobotWhite, V(0, 0, 0), YAxis);
                    Cap(b, V(0, 0.051f, 0), YAxis, 0.043f);
                    b.Shape(Cyl(0.036f, W1, 24), m.CobotWhite, V(0, 0, W1 / 2));
                }, "cobot j4");
                var j5 = Link(j4, V(0, 0, W1), b =>
                {
                    b.Shape(Cyl(0.043f, 0.09f, 28), m.CobotWhite, V(0, 0, 0.045f));
                    Ring(b, V(0, 0, 0.004f), V(0, 0, 0), 0.043f);
                }, "cobot j5");
                var j6 = Link(j5, V(0, 0, W2), b =>
                {
                    b.Shape(Cyl(0.034f, 0.012f, 24), m.Aluminium, V(0, 0, 0.006f));
                    b.Shape(Rounded(0.075f, 0.085f, 0.06f, 0.012f), m.Graphite, V(0, 0, 0.042f));
                }, "cobot j6");
                var finger = Rounded(0.014f, 0.024f, 0.056f, 0.004f);
                for (int k = 0; k < 2; k++) fingers[k] = Part(finger, m.Aluminium, j6, V(0.02f * (k == 0 ? 1 : -1), 0, 0.098f));
                Tip = new GameObject("tip").transform;
                Tip.SetParent(j6, false);
                Tip.localPosition = Web.P(0, 0, Tool);
                j = new[] { j1, j2, j3, j4, j5, j6 };
            }

            public void Set(float[] q, float open)
            {
                j[0].localRotation = Web.About('z', q[0]);
                j[1].localRotation = Web.About('y', q[1]);
                j[2].localRotation = Web.About('y', q[2]);
                j[3].localRotation = Web.About('y', q[3]);
                for (int k = 0; k < 2; k++)
                {
                    var p = fingers[k].localPosition;
                    fingers[k].localPosition = new Vector3((0.013f + 0.017f * open) * (k == 0 ? 1 : -1), p.y, p.z);
                }
            }

            /// <summary>Joints that put the fingertips at p (the arm's base frame, web), pointing straight down.</summary>
            public static float[] Solve(Vector3 p)
            {
                float q1 = Mathf.Atan2(p.y, p.x);
                float r = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                float dz = p.z + W1 + W2 + Tool - D1;
                float d = Mathf.Min(Mathf.Sqrt(r * r + dz * dz), A2 + A3 - 1e-4f);
                float beta = Mathf.Acos(Mathf.Clamp((A2 * A2 + d * d - A3 * A3) / (2 * A2 * d), -1, 1));
                float a1 = Mathf.Atan2(dz, r) + beta;
                float a2 = Mathf.Atan2(dz - A2 * Mathf.Sin(a1), r - A2 * Mathf.Cos(a1));
                return new[] { q1, Mathf.PI / 2 - a1, a1 - a2, a2 + Mathf.PI / 2 };
            }
        }

        class CobotStation : Station
        {
            public const float Top = 0.86f, PartH = 0.05f, P = 4.9f;
            public Cobot[] Arms = new Cobot[2];
            public Vector3[] ArmPos = new Vector3[2];
            public float[] ArmYaw = new float[2];
            public Vector3[] Homes = new Vector3[2];
            public Transform[] Parts = new Transform[6];
            public Vector3 Fixture;
            static readonly float Round = 6 * P + 3.5f;

            static Vector3 Slot(float side, int k) => W(0.62f * side + ((k % 3) - 1) * 0.09f, -0.24f + ((k / 3) - 0.5f) * 0.09f, Top + 0.025f + PartH / 2);
            static Vector3 Lift(Vector3 p) => p + W(0, 0, 0.14f);
            static Vector3 Tween(Vector3 a, Vector3 b, float k) => Vector3.Lerp(a, b, Ease(k));

            (Vector3, float) Giver(float tau, Vector3 from, Vector3 home)
            {
                var upFrom = Lift(from);
                var upFix = Lift(Fixture);
                if (tau < 0.9f) return (Tween(home, upFrom, tau / 0.9f), 1);
                if (tau < 1.4f) return (Tween(upFrom, from, (tau - 0.9f) / 0.5f), 1);
                if (tau < 1.6f) return (from, 1 - Ease((tau - 1.4f) / 0.2f));
                if (tau < 2.1f) return (Tween(from, upFrom, (tau - 1.6f) / 0.5f), 0);
                if (tau < 3.0f) return (Tween(upFrom, upFix, (tau - 2.1f) / 0.9f), 0);
                if (tau < 3.4f) return (Tween(upFix, Fixture, (tau - 3.0f) / 0.4f), 0);
                if (tau < 3.6f) return (Fixture, Ease((tau - 3.4f) / 0.2f));
                if (tau < 4.0f) return (Tween(Fixture, upFix, (tau - 3.6f) / 0.4f), 1);
                return (Tween(upFix, home, (tau - 4.0f) / 0.9f), 1);
            }

            (Vector3, float) Taker(float tau, Vector3 to, Vector3 home)
            {
                var upTo = Lift(to);
                var upFix = Lift(Fixture);
                if (tau < 4.3f) return (Tween(home, upFix, (tau - 3.4f) / 0.9f), 1);
                if (tau < 4.7f) return (Tween(upFix, Fixture, (tau - 4.3f) / 0.4f), 1);
                if (tau < 4.9f) return (Fixture, 1 - Ease((tau - 4.7f) / 0.2f));
                if (tau < 5.4f) return (Tween(Fixture, upFix, (tau - 4.9f) / 0.5f), 0);
                if (tau < 6.3f) return (Tween(upFix, upTo, (tau - 5.4f) / 0.9f), 0);
                if (tau < 6.7f) return (Tween(upTo, to, (tau - 6.3f) / 0.4f), 0);
                if (tau < 6.9f) return (to, Ease((tau - 6.7f) / 0.2f));
                if (tau < 7.3f) return (Tween(to, upTo, (tau - 6.9f) / 0.4f), 1);
                return (Tween(upTo, home, (tau - 7.3f) / 0.9f), 1);
            }

            public override void Update(float time, float dt)
            {
                var tasks = new (Vector3, float)?[2];
                var holder = new int[6];
                for (int k = 0; k < 6; k++) holder[k] = -1;
                int round = Mathf.FloorToInt(time / Round);
                float inRound = time - round * Round;
                int g = round % 2, tk = 1 - g;
                float[] sides = { -1, 1 };
                for (int k = 0; k < 6; k++)
                {
                    float tau = inRound - k * P;
                    var from = Slot(sides[g], k);
                    var to = Slot(sides[tk], k);
                    if (tau >= 0 && tau < P) tasks[g] = Giver(tau, from, Homes[g]);
                    if (tau >= 3.4f && tau < 8.2f) tasks[tk] = Taker(tau, to, Homes[tk]);
                    if (tau < 1.5f) Parts[k].localPosition = Web.P(from);
                    else if (tau < 3.5f) holder[k] = g;
                    else if (tau < 4.8f) Parts[k].localPosition = Web.P(Fixture);
                    else if (tau < 6.8f) holder[k] = tk;
                    else Parts[k].localPosition = Web.P(to);
                }
                for (int i = 0; i < 2; i++)
                {
                    var (tipAt, open) = tasks[i] ?? (Homes[i], 1f);
                    var local = Quaternion.AngleAxis(-ArmYaw[i] * Mathf.Rad2Deg, Vector3.forward) * (tipAt - ArmPos[i]);
                    Arms[i].Set(Cobot.Solve(local), open);
                }
                for (int k = 0; k < 6; k++)
                {
                    int h = holder[k];
                    if (h < 0) continue;
                    Parts[k].position = Arms[h].Tip.position;
                }
            }
        }

        Station CobotTable(LabMaterials m, float x, float y, float yaw)
        {
            var group = new GameObject("cobots").transform;
            group.SetParent(Root, false);
            group.localPosition = Web.P(x, y, 0);
            group.localRotation = Web.Yaw(yaw);
            var st = new CobotStation { Group = group };
            const float top = CobotStation.Top;
            var s = new Batcher();
            s.Box(m.Tabletop, V(1.9f, 0.95f, 0.04f), V(0, 0, top - 0.02f), cast: true);
            s.Box(m.Graphite, V(1.86f, 0.91f, 0.05f), V(0, 0, top - 0.065f), cast: true);
            foreach (var (px, py) in new[] { (-0.88f, -0.42f), (0.88f, -0.42f), (-0.88f, 0.42f), (0.88f, 0.42f) })
                s.Box(m.Aluminium, V(0.05f, 0.05f, top - 0.09f), V(px, py, (top - 0.09f) / 2), cast: true);
            foreach (float py in new[] { -0.4f, 0.4f }) s.Box(m.Aluminium, V(1.7f, 0.04f, 0.04f), V(0, py, 0.2f));
            foreach (float tx in new[] { -0.62f, 0.62f }) s.Box(m.Tray, V(0.34f, 0.24f, 0.025f), V(tx, -0.24f, top + 0.0125f));
            s.Box(m.Aluminium, V(0.12f, 0.12f, 0.04f), V(0, 0.2f, top + 0.02f));
            const float sx = 0.84f, sy = 0.36f;
            s.Box(m.Graphite, V(0.04f, 0.04f, 0.4f), V(sx, sy, top + 0.2f));
            s.Box(m.Graphite, V(0.03f, 0.56f, 0.34f), V(sx, sy, top + 0.52f), cast: true);
            foreach (float face in new[] { sx - 0.017f, sx + 0.017f })
            {
                s.Box(m.LedGreen, V(0.005f, 0.5f, 0.02f), V(face, sy, top + 0.38f));
                s.Box(m.LedBlue, V(0.005f, 0.36f, 0.012f), V(face, sy - 0.06f, top + 0.6f));
                s.Box(m.LedOrange, V(0.005f, 0.22f, 0.012f), V(face, sy - 0.13f, top + 0.56f));
                s.Box(m.LedBlue, V(0.005f, 0.3f, 0.012f), V(face, sy - 0.09f, top + 0.52f));
            }
            s.Build("cobot table", group);
            Decal(group, m.Blob, 0, 0, 2.4f, 1.4f);
            for (int i = 0; i < 2; i++)
            {
                st.Arms[i] = new Cobot(m, group);
                st.ArmPos[i] = W(i == 0 ? -0.6f : 0.6f, 0.12f, top);
                st.ArmYaw[i] = i == 0 ? 0 : Mathf.PI;
                st.Arms[i].Root.localPosition = Web.P(st.ArmPos[i]);
                st.Arms[i].Root.localRotation = Web.About('z', st.ArmYaw[i]);
                st.Homes[i] = st.ArmPos[i] + Quaternion.AngleAxis(st.ArmYaw[i] * Mathf.Rad2Deg, Vector3.forward) * W(0.32f, 0, 0.3f);
            }
            st.Fixture = W(0, 0.2f, top + 0.04f + CobotStation.PartH / 2);
            var partMesh = Cyl(0.021f, CobotStation.PartH, 18);
            for (int k = 0; k < 6; k++) st.Parts[k] = Part(partMesh, m.Part, group);
            Collider(group, 0, 0, 0.6f, 2.0f, 1.05f, 1.2f);
            return st;
        }

        // ================================================================== the mobile robot
        class AmrStation : Station
        {
            public Transform Body, Lidar;
            public List<Vector3> Path;   // web, closed, evenly spaced
            public float Length;
            public float Travelled;
            public Lab Owner;
            public MeshRenderer[] Leds;
            public Material Blue, Amber;
            const float Speed = 0.85f;

            public override void Update(float time, float dt)
            {
                Vector3 p = PointAt(Travelled / Length, out Vector3 tan);
                // Its scanner: a person within 1.4 m ahead, or 0.8 m anywhere round it, stops it.
                var person = Owner.Person;
                var here = Body.position;
                var toPerson = new Vector3(person.x - here.x, 0, person.z - here.z);
                var ahead = Web.P(tan).normalized;
                float dist = toPerson.magnitude;
                bool blocked = person != Vector3.zero && (dist < 0.8f || (dist < 1.6f && Vector3.Dot(toPerson.normalized, ahead) > 0.5f));
                Owner.AmrBlocked = blocked;
                if (!blocked && Owner.Moving) Travelled = (Travelled + Speed * Mathf.Min(dt, 0.1f)) % Length;
                p = PointAt(Travelled / Length, out tan);
                Body.localPosition = Web.P(p.x, p.y, 0);
                Body.localRotation = Web.About('z', Mathf.Atan2(tan.y, tan.x));
                Lidar.localRotation = Web.About('z', time * 9);
                foreach (var l in Leds) l.sharedMaterial = blocked ? Amber : Blue;
            }

            Vector3 PointAt(float u, out Vector3 tangent)
            {
                int n = Path.Count;
                float f = ((u % 1) + 1) % 1 * n;
                int i = Mathf.FloorToInt(f) % n;
                int j = (i + 1) % n;
                tangent = (Path[j] - Path[i]).normalized;
                return Vector3.Lerp(Path[i], Path[j], f - Mathf.Floor(f));
            }
        }

        Station Amr(LabMaterials m)
        {
            var group = new GameObject("mobile robot").transform;
            group.SetParent(Root, false);
            var st = new AmrStation { Group = group, Owner = this, Blue = m.LedBlue, Amber = m.LedAmber };
            st.Body = new GameObject("body").transform;
            st.Body.SetParent(group, false);
            var b = new Batcher();
            b.Shape(Rounded(0.95f, 0.66f, 0.26f, 0.07f), m.AmrWhite, V(0, 0, 0.19f), cast: true);
            b.Shape(Rounded(0.97f, 0.68f, 0.07f, 0.03f), m.Rubber, V(0, 0, 0.055f), cast: true);
            b.Box(m.Graphite, V(0.86f, 0.56f, 0.02f), V(0, 0, 0.33f));
            b.Box(m.Carton, V(0.56f, 0.4f, 0.36f), V(-0.05f, 0, 0.52f), cast: true);
            b.Build("amr body", st.Body);
            var leds = new List<MeshRenderer>
            {
                Part(MeshKit.Box(), m.LedBlue, st.Body, V(0.485f, 0, 0.24f)).GetComponent<MeshRenderer>(),
            };
            leds[0].transform.localScale = Web.S(0.012f, 0.44f, 0.02f);
            foreach (float py in new[] { 0.335f, -0.335f })
            {
                var l = Part(MeshKit.Box(), m.LedBlue, st.Body, V(0, py, 0.27f));
                l.localScale = Web.S(0.7f, 0.012f, 0.016f);
                leds.Add(l.GetComponent<MeshRenderer>());
            }
            var back = Part(MeshKit.Box(), m.LedOrange, st.Body, V(-0.485f, 0, 0.24f));
            back.localScale = Web.S(0.012f, 0.44f, 0.02f);
            st.Leds = leds.ToArray();
            st.Lidar = Part(Cyl(0.055f, 0.07f, 20), m.Graphite, st.Body, V(0.36f, 0, 0.375f));
            Part(Cyl(0.04f, 0.02f, 20), m.LedGreen, st.Lidar, V(0, 0, 0.04f));
            Go.FloorDecal("shadow", st.Body, m.Blob, 1.5f, 1.1f, 0, 0, 0, 0.0035f);
            var ctrl = new[]
            {
                W(-4.6f, -3.2f, 0), W(-4.6f, 3.7f, 0), W(-4.25f, 4.55f, 0), W(-3.4f, 4.9f, 0), W(7.4f, 4.9f, 0), W(8.25f, 4.55f, 0),
                W(8.6f, 3.7f, 0), W(8.6f, -3.2f, 0), W(8.25f, -4.05f, 0), W(7.4f, -4.4f, 0), W(-3.4f, -4.4f, 0), W(-4.25f, -4.05f, 0),
            };
            st.Path = EvenLoop(ctrl, 600, out st.Length);
            return st;
        }

        /// <summary>A closed centripetal Catmull-Rom curve through the points, resampled evenly by length.</summary>
        static List<Vector3> EvenLoop(Vector3[] pts, int samples, out float length)
        {
            var dense = new List<Vector3>();
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
                for (int s = 0; s < 40; s++) dense.Add(Centripetal(p0, p1, p2, p3, s / 40f));
            }
            var cum = new float[dense.Count + 1];
            for (int i = 0; i < dense.Count; i++) cum[i + 1] = cum[i] + Vector3.Distance(dense[i], dense[(i + 1) % dense.Count]);
            length = cum[dense.Count];
            var even = new List<Vector3>();
            int k = 0;
            for (int s = 0; s < samples; s++)
            {
                float d = length * s / samples;
                while (k < dense.Count - 1 && cum[k + 1] < d) k++;
                float seg = cum[k + 1] - cum[k];
                float t = seg > 1e-6f ? (d - cum[k]) / seg : 0;
                even.Add(Vector3.Lerp(dense[k], dense[(k + 1) % dense.Count], t));
            }
            return even;
        }

        static Vector3 Centripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float T(float ti, Vector3 a, Vector3 b) => ti + Mathf.Pow(Mathf.Max(Vector3.Distance(a, b), 1e-4f), 0.5f);
            float t0 = 0, t1 = T(t0, p0, p1), t2 = T(t1, p1, p2), t3 = T(t2, p2, p3);
            float u = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
            return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
        }
    }
}
