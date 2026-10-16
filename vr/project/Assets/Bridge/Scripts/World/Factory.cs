// SPDX-License-Identifier: LicenseRef-BRIDGE
// The digital factory around the robot cell, ported from the web app's
// frontend/src/viewport/factory (hall.ts, cell.ts, mounts.ts) in the numbers it was written in: a
// 36 x 26 m portal-frame hall with window bands, skylights and LED high-bays, pallet racking, a CNC
// machine line, a bench wall, walkways; around the cell its painted boundary, laser scanner,
// control cabinet with its stack light, operator panel and conveyor. Then what the lessons use: the
// Panda on its pedestal beside the work bench, the parts, the place zone.
//
// Frames: the factory's own frame (the web app's, z up) has its origin on the floor under the
// cell's centre; Web.P maps it to Unity. The robot's world frame (MuJoCo's) is the factory frame
// moved by CellCentre: the web app centres the factory on the cell's bounding box.

using System;
using System.Collections.Generic;
using Bridge.Robot;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bridge.World
{
    /// <summary>Where things are, in Unity world coordinates (metres).</summary>
    public static class CellLayout
    {
        // The pick-and-place cell's bounding box centre in the robot's world frame, and the half
        // extents the web app fits the factory to (engine.ts fitFactory, margin 0.15 m).
        public const float CellCentreX = 0.374f;
        public const float Hx = 0.674f, Hy = 0.8f;
        public const float BenchTop = 0.75f;

        /// <summary>A point in the robot's world frame (MuJoCo: x forward, y left, z up) in Unity.</summary>
        public static Vector3 FromRobotWorld(float x, float y, float z) => Web.P(x - CellCentreX, y, z);

        // The bench (bench_075): centred at (0.6, 0) in the robot's world, 0.595 x 1.3 m, top at 0.75.
        public static readonly Vector3 BenchCentre = FromRobotWorld(0.6f, 0f, BenchTop);
        public static readonly Vector3 BenchSize = new(0.595f, BenchTop, 1.3f);   // Unity x, y, z
        // The Panda's base: 0.6 m behind the bench's centre, 115 mm to one side, 132 mm above its top.
        public static readonly Vector3 RobotBase = FromRobotWorld(0f, -0.115f, 0.882f);
        // The pick-and-place scenario's cube and place zone (scenarios.py).
        public static readonly Vector3 CubeSpot = FromRobotWorld(0.457f, -0.115f, BenchTop);
        public static readonly Vector3 PlaceZone = FromRobotWorld(0.457f, 0.115f, BenchTop);
        public const float ZoneHalf = 0.04f;
        // More stations on the bench for the lessons, all inside the arm's comfortable reach.
        public static readonly Vector3 FeederSpot = FromRobotWorld(0.40f, -0.32f, BenchTop);
        public static readonly Vector3 TrayCentre = FromRobotWorld(0.46f, 0.28f, BenchTop);

        // Where a person stands: the operator's side of the bench, looking at the arm.
        public static readonly Vector3 OperatorSpot = Web.P(1.3f, 0.05f, 0);
        public static readonly Vector3 OperatorFacing = new(-1, 0, 0.04f);
        public static readonly Vector3 GuideSpot = Web.P(0.75f, -0.62f, 0);
        public static readonly Vector3 SafetySpot = Web.P(3.0f, -1.2f, 0);
        public static readonly Vector3 WelcomeSpot = Web.P(3.4f, -2.6f, 0);
        // The twin's stage stands in the clear floor between the operator panel and the conveyor.
        public static readonly Vector3 TwinBase = Web.P(3.6f, 1.0f, 0.882f);
        public static readonly Vector3 TwinSpot = Web.P(3.6f, -0.85f, 0);
        public static Vector3 Facing(Vector3 from, Vector3 to) { var d = to - from; d.y = 0; return d.normalized; }

        // The cell's painted boundary (cell.ts): 0.9 m outside the footprint.
        public const float Bx0 = -Hx - 0.9f, Bx1 = Hx + 0.9f, By0 = -Hy - 0.9f, By1 = Hy + 0.9f;
    }

    /// <summary>A Poly Haven prop exported from the web app's GLB (see export_prop), placed in web numbers.</summary>
    public static class Props
    {
        static readonly Dictionary<string, PropDef> defs = new();

        public static PropDef Def(string name)
        {
            if (!defs.TryGetValue(name, out var d))
            {
                d = Gen.Json<PropDef>("Props/" + name);
                defs[name] = d;
            }
            return d;
        }

        /// <summary>A copy at web (x, y, z) turned by yaw, scaled to size (web x, y, z) or its own size.</summary>
        public static GameObject Place(string name, Transform parent, float x, float y, float z = 0, float yaw = 0, float[] size = null, bool cast = true, int layer = Layers.Environment)
        {
            var d = Def(name);
            var root = new GameObject(name);
            root.layer = layer;
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Web.P(x, y, z);
            root.transform.localRotation = Web.Yaw(yaw);
            if (size != null)
                root.transform.localScale = new Vector3(size[0] / d.size[0], size[2] / d.size[1], size[1] / d.size[2]);
            foreach (var part in d.parts)
                Go.Mesh(part.mesh, root.transform, Gen.Mesh(part.mesh), Mats.Prop(part.material), cast: cast, layer: layer);
            return root;
        }
    }

    public class Factory
    {
        public const float X0 = -16, X1 = 20, Y0 = -13, Y1 = 13, Eave = 8.5f, Ridge = 10.2f;
        public readonly Transform Root;
        public StackLight Stack;
        public OperatorPanel Hmi;
        public Transform Pedestal;
        public readonly List<Transform> ConveyorCartons = new();
        public Collider BenchCollider;
        public GameObject PlaceZoneMarker;
        public readonly List<ReflectionProbe> Probes = new();
        public Light Sun;
        public Volume Post;
        public Vignette Vignette;
        public ColorAdjustments Grade;
        public ParticleSystem Dust;
        public Transform Scanner;
        float conveyorClock;

        uint seed = 3;
        float Rand()
        {
            seed = (uint)((seed * 16807UL) % 2147483647UL);
            return seed / 2147483647f;
        }

        static float[] V(params float[] v) => v;

        public Factory(Transform parent)
        {
            Root = new GameObject("Factory").transform;
            Root.SetParent(parent, false);
            BuildHall();
            BuildSurround();
            BuildCellWork();
            BuildLighting();
            BuildDust();
        }

        // ================================================================== the hall (hall.ts)
        void BuildHall()
        {
            var hall = new GameObject("hall").transform;
            hall.SetParent(Root, false);
            float W = X1 - X0, D = Y1 - Y0, cx = (X0 + X1) / 2;

            var floor = Go.Mesh("floor", hall, MeshKit.FloorQuad(), Mats.Floor(W, D, 0x939a9f), Web.P(cx, 0, 0), null, new Vector3(W, 1, D), cast: false, layer: Layers.Teleport);
            floor.GetComponent<MeshRenderer>().receiveShadows = true;
            var floorCol = floor.AddComponent<BoxCollider>();
            floorCol.center = new Vector3(0, -0.05f, 0);
            floorCol.size = new Vector3(1, 0.1f / 1f, 1);

            var b = new Batcher();
            var steel = Mats.Paint(0x9aa1a8, 0.5f, 0.55f);
            var darkSteel = Mats.Paint(0x3c4248, 0.55f, 0.45f);
            var frame = Mats.Paint(0x5d6670, 0.45f, 0.5f);
            var sky = Mats.Glow(0xe8f0f8, 3.2f);
            var windowGlow = Mats.Glow(0xdfe8f2, 2.4f);
            var led = Mats.Glow(0xfff6ea, 7.5f);

            // ------------------------------------------------------------- walls
            var walls = new (float ax, float ay, float bx, float by, float ix, float iy)[]
            {
                (X0, Y0, X1, Y0, 0, 1), (X0, Y1, X1, Y1, 0, -1), (X0, Y0, X0, Y1, 1, 0), (X1, Y0, X1, Y1, -1, 0),
            };
            const float dadoH = 1.2f, windowZ0 = 6.1f, windowZ1 = 7.3f;
            foreach (var w in walls)
            {
                float len = Mathf.Sqrt((w.bx - w.ax) * (w.bx - w.ax) + (w.by - w.ay) * (w.by - w.ay));
                float mx = (w.ax + w.bx) / 2, my = (w.ay + w.by) / 2;
                float yaw = Mathf.Atan2(w.by - w.ay, w.bx - w.ax);
                var dado = Go.Mesh("dado", hall, MeshKit.Box(), Mats.Concrete(len, dadoH, 0x8b9095), Web.P(mx - w.ix * 0.05f, my - w.iy * 0.05f, dadoH / 2), Web.Yaw(yaw), Web.S(len, 0.25f, dadoH), cast: false);
                dado.AddComponent<BoxCollider>();
                foreach (var (z0, z1) in new[] { (dadoH, windowZ0), (windowZ1, Eave + 0.6f) })
                {
                    float h = z1 - z0;
                    var inward = new Vector3(w.ix, 0, w.iy);
                    Go.Mesh("cladding", hall, MeshKit.Quad(), Mats.Cladding(len, h), Web.P(mx - w.ix * 0.12f, my - w.iy * 0.12f, (z0 + z1) / 2), Quaternion.LookRotation(-inward, Vector3.up), new Vector3(len, h, 1), cast: false);
                }
                // A collider for the whole wall height, so nothing (a teleport, a part) leaves the hall.
                var wallCol = new GameObject("wall").AddComponent<BoxCollider>();
                wallCol.gameObject.layer = Layers.Environment;
                wallCol.transform.SetParent(hall, false);
                wallCol.transform.localPosition = Web.P(mx - w.ix * 0.2f, my - w.iy * 0.2f, Eave / 2);
                wallCol.transform.localRotation = Web.Yaw(yaw);
                wallCol.size = Web.S(len, 0.3f, Eave);
                b.Box(windowGlow, V(len, 0.04f, windowZ1 - windowZ0), V(mx - w.ix * 0.14f, my - w.iy * 0.14f, (windowZ0 + windowZ1) / 2), V(0, 0, yaw));
                int n = Mathf.FloorToInt(len / 1.2f);
                for (int k = 0; k <= n; k++)
                {
                    float t = k / (float)n;
                    float x = w.ax + (w.bx - w.ax) * t, y = w.ay + (w.by - w.ay) * t;
                    b.Box(darkSteel, V(0.08f, 0.1f, windowZ1 - windowZ0 + 0.1f), V(x, y, (windowZ0 + windowZ1) / 2), V(0, 0, yaw));
                }
                b.Box(darkSteel, V(len, 0.14f, 0.1f), V(mx - w.ix * 0.12f, my - w.iy * 0.12f, windowZ0), V(0, 0, yaw));
                b.Box(darkSteel, V(len, 0.14f, 0.1f), V(mx - w.ix * 0.12f, my - w.iy * 0.12f, windowZ1), V(0, 0, yaw));
                b.Box(Mats.Steel(0xb8bdc2, 0.4f), V(len, 0.3f, 0.08f), V(mx + w.ix * 0.25f, my + w.iy * 0.25f, 4.2f), V(0, 0, yaw));
            }

            // Pilasters every 6 m on the long walls, hazard-striped guards at their feet.
            var guard = Mats.Textured("hazard", new Vector2(1, 3));
            var frames = new List<float>();
            for (float x = X0; x <= X1 + 1e-4f; x += 6) frames.Add(x);
            foreach (float x in frames)
            {
                foreach (var (y, dir) in new[] { (Y0 + 0.2f, 1f), (Y1 - 0.2f, -1f) })
                {
                    b.Box(frame, V(0.32f, 0.32f, Eave), V(x, y, Eave / 2));
                    b.Box(guard, V(0.46f, 0.46f, 1.2f), V(x, y + dir * 0.02f, 0.6f));
                }
            }

            // ------------------------------------------------------------- roof
            float slope = Mathf.Atan2(Ridge - Eave, Y1);
            float rafterLen = Mathf.Sqrt(Y1 * Y1 + (Ridge - Eave) * (Ridge - Eave));
            var roof = new Batcher();
            foreach (float x in frames)
                foreach (float side in new[] { -1f, 1f })
                    roof.Box(frame, V(0.18f, rafterLen, 0.5f), V(x, side * Y1 / 2, (Eave + Ridge) / 2 - 0.3f), V(-side * slope, 0, 0));
            var roofSheet = Mats.Paint(0x4a5058, 0.7f, 0.4f);
            foreach (float side in new[] { -1f, 1f })
            {
                for (float s = 0.6f; s < rafterLen; s += 1.8f)
                {
                    float y = side * (Y1 - s * Y1 / rafterLen);
                    float z = Eave + s / rafterLen * (Ridge - Eave) - 0.02f;
                    roof.Box(darkSteel, V(W, 0.06f, 0.16f), V(cx, y, z), V(-side * slope, 0, 0));
                }
                roof.Box(roofSheet, V(W, rafterLen + 0.4f, 0.05f), V(cx, side * Y1 / 2, (Eave + Ridge) / 2 + 0.12f), V(-side * slope, 0, 0));
                foreach (float s in new[] { 0.36f, 0.62f })
                {
                    float y = side * (Y1 * (1 - s));
                    float z = Eave + s * (Ridge - Eave) + 0.08f;
                    for (int i = 0; i < frames.Count - 1; i++)
                    {
                        float x0 = frames[i] + 0.5f, x1 = frames[i + 1] - 0.5f;
                        roof.Box(sky, V(x1 - x0, 1.1f, 0.04f), V((x0 + x1) / 2, y, z), V(-side * slope, 0, 0));
                    }
                }
            }
            roof.Build("roof", hall, false);

            // LED high-bays on rods at 7.6 m.
            for (float x = X0 + 3; x < X1 - 1; x += 4)
            {
                foreach (float y in new[] { -8.5f, -3f, 3f, 8.5f })
                {
                    b.Box(darkSteel, V(1.3f, 0.3f, 0.09f), V(x, y, 7.64f));
                    b.Box(led, V(1.2f, 0.22f, 0.02f), V(x, y, 7.59f));
                    b.Box(steel, V(0.012f, 0.012f, 1.6f), V(x - 0.5f, y, 8.45f));
                    b.Box(steel, V(0.012f, 0.012f, 1.6f), V(x + 0.5f, y, 8.45f));
                }
            }

            // ------------------------------------------------------------- doors
            var doorFrame = Mats.Textured("hazard", new Vector2(1, 6));
            var slat = Mats.Paint(0xb4b9bd, 0.45f, 0.5f);
            foreach (var d in new[] { (x: 9f, y: Y1 - 0.15f, yaw: 0f, w: 5f, h: 5f), (x: X0 + 0.15f, y: 5f, yaw: Mathf.PI / 2, w: 4.5f, h: 4.5f) })
            {
                float c = Mathf.Cos(d.yaw), s = Mathf.Sin(d.yaw);
                for (float z = 0.1f; z < d.h; z += 0.12f) b.Box(slat, V(d.w, 0.08f, 0.1f), V(d.x, d.y, z + 0.05f), V(0, 0, d.yaw));
                b.Box(doorFrame, V(0.3f, 0.3f, d.h + 0.3f), V(d.x - c * (d.w / 2 + 0.15f), d.y - s * (d.w / 2 + 0.15f), (d.h + 0.3f) / 2), V(0, 0, d.yaw));
                b.Box(doorFrame, V(0.3f, 0.3f, d.h + 0.3f), V(d.x + c * (d.w / 2 + 0.15f), d.y + s * (d.w / 2 + 0.15f), (d.h + 0.3f) / 2), V(0, 0, d.yaw));
                b.Box(darkSteel, V(d.w + 0.6f, 0.5f, 0.5f), V(d.x, d.y, d.h + 0.3f), V(0, 0, d.yaw));
            }
            WallSign(hall, Mats.UnlitTex("sign_exit", new Color(1.25f, 1.25f, 1.25f)), 0.6f, 0.3f, -9, Y1 - 0.2f, 2.6f, 0, -1);
            b.Box(Mats.Paint(0x6c747c, 0.5f, 0.3f), V(1.0f, 0.06f, 2.1f), V(-9, Y1 - 0.18f, 1.05f));

            // ------------------------------------------------------------- racking (west wall)
            var upright = Mats.Paint(Pal.SignalBlue, 0.4f, 0.4f);
            var beam = Mats.Paint(Pal.RackOrange, 0.4f, 0.35f);
            var pallet = Mats.Paint(0xb08a5a, 0.85f, 0);
            var wrap = Mats.Glass(Pal.Hex(0xdfe4e8), 0.85f);
            float rackX = X0 + 1.1f;
            float[] levels = { 0.15f, 1.75f, 3.35f, 4.95f };
            var carton = Mats.Textured("carton", Vector2.one, 0.85f, 0);
            var crates = new List<(float x, float y, float z)>();
            for (float y = Y0 + 2.5f; y < 1.5f; y += 2.8f)
            {
                foreach (float dy in new[] { 0f, 2.8f })
                {
                    if (y + dy > 1.6f) continue;
                    b.Box(upright, V(0.09f, 0.07f, 6.0f), V(rackX - 0.5f, y + dy, 3.0f));
                    b.Box(upright, V(0.09f, 0.07f, 6.0f), V(rackX + 0.5f, y + dy, 3.0f));
                    for (float z = 0.4f; z < 6; z += 0.9f) b.Box(upright, V(1.0f, 0.03f, 0.03f), V(rackX, y + dy, z));
                }
                for (int li = 1; li < levels.Length; li++)
                {
                    b.Box(beam, V(0.05f, 2.75f, 0.12f), V(rackX - 0.5f, y + 1.4f, levels[li]));
                    b.Box(beam, V(0.05f, 2.75f, 0.12f), V(rackX + 0.5f, y + 1.4f, levels[li]));
                }
                foreach (float z in levels)
                {
                    foreach (float py in new[] { y + 0.75f, y + 2.05f })
                    {
                        float zz = z + (z > 0.2f ? 0.06f : 0);
                        b.Box(pallet, V(1.15f, 0.78f, 0.14f), V(rackX, py, zz + 0.07f));
                        float r = Rand();
                        if (r < 0.45f)
                        {
                            foreach (var (ox, oy) in new[] { (-0.29f, -0.2f), (0.29f, -0.2f), (-0.29f, 0.2f), (0.29f, 0.2f) })
                            {
                                b.Box(carton, V(0.56f, 0.38f, 0.4f), V(rackX + ox, py + oy, zz + 0.14f + 0.2f), V(0, 0, Rand() * 0.06f - 0.03f));
                                if (z < 4.9f && Rand() < 0.7f) b.Box(carton, V(0.56f, 0.38f, 0.4f), V(rackX + ox, py + oy, zz + 0.54f + 0.2f), V(0, 0, Rand() * 0.06f - 0.03f));
                            }
                        }
                        else if (r < 0.75f)
                        {
                            b.Box(wrap, V(1.1f, 0.76f, 0.9f + Rand() * 0.3f), V(rackX, py, zz + 0.14f + 0.5f));
                        }
                        else
                        {
                            for (int k = 0; k < 4; k++) crates.Add((rackX + (k % 2 == 1 ? 0.28f : -0.28f), py + (k < 2 ? -0.18f : 0.18f), zz + 0.14f));
                        }
                    }
                }
            }
            foreach (var c in crates) Props.Place("plastic_crate", hall, c.x, c.y, c.z, 0, V(0.5f, 0.34f, 0.3f), cast: false);
            var rackCol = new GameObject("racking").AddComponent<BoxCollider>();
            rackCol.gameObject.layer = Layers.Environment;
            rackCol.transform.SetParent(hall, false);
            rackCol.transform.localPosition = Web.P(rackX, (Y0 + 2.5f + 1.6f) / 2, 3);
            rackCol.size = Web.S(1.3f, 1.6f - (Y0 + 2.5f) + 0.2f, 6);

            // ------------------------------------------------------------- CNC machine line (north)
            var enclosure = Mats.Panel(Pal.LightGrey);
            var mbase = Mats.Panel(Pal.Anthracite);
            var accent = Mats.Paint(0x1f4f8f, 0.35f, 0.3f);
            var glass = Mats.Glass(Pal.Hex(0x9fb4c4), 0.22f);
            var screen = Mats.Glow(0x4fa3ff, 1.6f);
            var green = Mats.Glow(0x3ddc84, 5f);
            foreach (float x in new[] { -2f, 2.6f, 7.2f, 11.8f })
            {
                float y = Y1 - 3.4f;
                b.Shape(MeshKit.RoundedBox(Web.S(3.2f, 2.2f, 0.55f), 0.05f), mbase, V(x, y, 0.275f));
                b.Shape(MeshKit.RoundedBox(Web.S(3.2f, 2.2f, 2.05f), 0.08f), enclosure, V(x, y, 0.55f + 1.025f), cast: true);
                b.Box(accent, V(3.22f, 2.22f, 0.12f), V(x, y, 2.66f));
                b.Box(Mats.Paint(0xd2d4d1, 0.38f, 0.3f), V(1.6f, 0.04f, 1.6f), V(x - 0.35f, y - 1.12f, 1.55f));
                b.Box(glass, V(1.1f, 0.02f, 0.9f), V(x - 0.35f, y - 1.145f, 1.75f));
                b.Box(Mats.Steel(0xcfd3d6, 0.25f), V(0.04f, 0.05f, 0.5f), V(x + 0.35f, y - 1.17f, 1.5f));
                b.Box(darkSteel, V(0.06f, 0.06f, 0.5f), V(x + 1.25f, y - 1.15f, 2.25f));
                b.Shape(MeshKit.RoundedBox(Web.S(0.5f, 0.12f, 0.62f), 0.02f), Mats.Paint(0x2b3036, 0.4f, 0.3f), V(x + 1.25f, y - 1.22f, 1.75f));
                b.Box(screen, V(0.36f, 0.01f, 0.26f), V(x + 1.25f, y - 1.285f, 1.86f));
                b.Shape(MeshKit.Cylinder(0.03f, 0.4f), steel, V(x + 1.45f, y + 0.8f, 2.92f));
                b.Shape(MeshKit.Cylinder(0.04f, 0.08f), green, V(x + 1.45f, y + 0.8f, 3.16f));
                b.Shape(MeshKit.Cylinder(0.04f, 0.16f), Mats.Paint(0x5a2020, 0.3f, 0), V(x + 1.45f, y + 0.8f, 3.28f));
                b.Shape(MeshKit.RoundedBox(Web.S(0.7f, 1.6f, 0.9f), 0.03f), mbase, V(x + 1.95f, y, 0.45f));
                Collider(hall, "cnc", x + 0.3f, y, 1.4f, 3.9f, 2.4f, 2.8f);
            }
            var yellowPaint = Mats.Paint(Pal.SafetyYellow, 0.5f, 0);
            const float lineZ = 0.0025f;
            float lineY = Y1 - 3.4f - 1.6f;
            b.Box(yellowPaint, V(16.8f, 0.1f, 0.004f), V(4.9f, lineY, lineZ));
            b.Box(yellowPaint, V(16.8f, 0.1f, 0.004f), V(4.9f, Y1 - 1.6f, lineZ));
            float keepClear = Y1 - 1.6f - lineY;
            foreach (float x in new[] { -3.7f, 13.5f }) b.Box(yellowPaint, V(0.1f, keepClear, 0.004f), V(x, (lineY + Y1 - 1.6f) / 2, lineZ));

            // Walkways: green lanes edged in white, with arrows.
            var walk = Mats.Paint(0x2f7d4b, 0.55f, 0);
            var white = Mats.Paint(0xeeeeea, 0.5f, 0);
            foreach (var l in new[] { (x: 2f, y: -7.8f, w: 34f, h: 1.6f), (x: -10.5f, y: 0f, w: 1.6f, h: 24f) })
            {
                b.Box(walk, V(l.w, l.h, 0.003f), V(l.x, l.y, 0.0015f));
                if (l.w > l.h)
                {
                    b.Box(white, V(l.w, 0.08f, 0.004f), V(l.x, l.y - l.h / 2, lineZ));
                    b.Box(white, V(l.w, 0.08f, 0.004f), V(l.x, l.y + l.h / 2, lineZ));
                }
                else
                {
                    b.Box(white, V(0.08f, l.h, 0.004f), V(l.x - l.w / 2, l.y, lineZ));
                    b.Box(white, V(0.08f, l.h, 0.004f), V(l.x + l.w / 2, l.y, lineZ));
                }
            }
            var arrow = Mats.Decal("floor_arrow", 0.85f);
            foreach (float x in new[] { -12f, -4f, 6f, 14f }) Go.FloorDecal("arrow", hall, arrow, 0.6f, 1.2f, x, -7.8f, -Mathf.PI / 2, 0.004f);
            foreach (float y in new[] { -6f, 6f }) Go.FloorDecal("arrow", hall, arrow, 0.6f, 1.2f, -10.5f, y, 0, 0.004f);

            // ------------------------------------------------------------- bench wall (south)
            var benchTop = Mats.Paint(0x8b8f93, 0.35f, 0.65f);
            var benchLeg = Mats.Paint(Pal.Anthracite, 0.5f, 0.3f);
            foreach (float x in new[] { -8f, -5.2f, 6.5f, 9.3f })
            {
                float y = Y0 + 1.0f;
                b.Box(benchTop, V(2.4f, 0.8f, 0.05f), V(x, y, 0.9f));
                foreach (var (ox, oy) in new[] { (-1.12f, -0.34f), (1.12f, -0.34f), (-1.12f, 0.34f), (1.12f, 0.34f) })
                    b.Box(benchLeg, V(0.06f, 0.06f, 0.88f), V(x + ox, y + oy, 0.44f));
                b.Box(benchLeg, V(2.3f, 0.7f, 0.03f), V(x, y, 0.18f));
                b.Box(Mats.Paint(0x9aa0a6, 0.6f, 0.3f), V(2.4f, 0.03f, 1.0f), V(x, Y0 + 0.4f, 1.7f));
                Collider(hall, "bench", x, y, 0.45f, 2.4f, 0.8f, 0.95f);
            }
            Props.Place("tool_chest", hall, -3.2f, Y0 + 0.9f, 0, Mathf.PI / 2, cast: false);
            Props.Place("tool_chest", hall, 11.6f, Y0 + 0.9f, 0, Mathf.PI / 2, cast: false);
            Props.Place("storage_cart", hall, 4.2f, Y0 + 1.6f, 0, 0.3f, cast: false);
            Props.Place("floor_sign", hall, -9.2f, -6.6f, 0, 0.5f, cast: false);

            b.Box(Mats.Paint(Pal.SafetyYellow, 0.55f, 0.1f), V(1.3f, 1.3f, 0.3f), V(X0 + 2.4f, Y0 + 1.6f, 0.15f));
            Props.Place("drum", hall, X0 + 2.1f, Y0 + 1.3f, 0.3f, 0, V(0.58f, 0.58f, 0.92f), cast: false);
            Props.Place("drum", hall, X0 + 2.7f, Y0 + 1.3f, 0.3f, 1, V(0.58f, 0.58f, 0.92f), cast: false);
            Props.Place("drum", hall, X0 + 2.1f, Y0 + 1.9f, 0.3f, 2, V(0.58f, 0.58f, 0.92f), cast: false);
            Props.Place("drum", hall, X0 + 2.7f, Y0 + 1.9f, 0.3f, 3, V(0.58f, 0.58f, 0.92f), cast: false);
            for (int k = 0; k < 6; k++) b.Box(carton, V(0.56f, 0.38f, 0.4f), V(7 + (k % 3) * 0.62f, Y1 - 1.0f - (k / 3) * 0.42f, 0.2f), V(0, 0, Rand() * 0.2f));
            for (int k = 0; k < 2; k++) b.Box(carton, V(0.56f, 0.38f, 0.4f), V(7.3f + k * 0.62f, Y1 - 1.2f, 0.6f), V(0, 0, Rand() * 0.2f));

            // ------------------------------------------------------------- AMR charger (east)
            float amrX = X1 - 2.2f, amrY = -2;
            b.Shape(MeshKit.RoundedBox(Web.S(1.0f, 0.7f, 0.32f), 0.06f), Mats.Paint(0xe9eaea, 0.35f, 0.1f), V(amrX, amrY, 0.19f));
            b.Shape(MeshKit.RoundedBox(Web.S(1.02f, 0.72f, 0.06f), 0.03f), Mats.Rubber(), V(amrX, amrY, 0.06f));
            b.Box(Mats.Glow(0x4b9dff, 4), V(0.02f, 0.5f, 0.02f), V(amrX - 0.51f, amrY, 0.26f));
            b.Shape(MeshKit.Cylinder(0.06f, 0.08f), Mats.Rubber(), V(amrX - 0.4f, amrY, 0.39f));
            b.Box(darkSteel, V(0.3f, 0.6f, 1.2f), V(amrX + 0.75f, amrY, 0.6f));
            b.Box(Mats.Glow(0x3ddc84, 3), V(0.01f, 0.06f, 0.06f), V(amrX + 0.59f, amrY, 1.0f));

            // ------------------------------------------------------------- safety furniture
            Props.Place("fire_extinguisher", hall, -10, Y1 - 0.55f, 0, Mathf.PI, cast: false);
            Props.Place("fire_extinguisher", hall, 8, Y0 + 0.55f, 0, 0, cast: false);
            var fire = Mats.Textured("sign_fire", Vector2.one, 0.6f, 0);
            fire.SetColor("_BaseColor", Pal.Hex(0xff4444));
            WallSign(hall, fire, 0.4f, 0.4f, -10, Y1 - 0.42f, 1.6f, 0, -1);
            WallSign(hall, fire, 0.4f, 0.4f, 8, Y0 + 0.42f, 1.6f, 0, 1);
            WallSign(hall, Mats.Textured("sign_glasses", Vector2.one, 0.6f, 0), 0.52f, 0.39f, -12.5f, Y1 - 0.22f, 1.7f, 0, -1);
            Go.FloorDecal("machining", hall, Mats.Decal("floor_machining", 0.8f), 4, 1, 4.9f, Y1 - 5.6f, 0, 0.004f);

            b.Build("structure", hall);

            // Contact shadows where the key light's shadow map does not reach.
            var blob = Mats.Decal("blob", 0.9f, lit: false);
            var shadows = new List<(float x, float y, float w, float d)>();
            foreach (float x in new[] { -2f, 2.6f, 7.2f, 11.8f }) shadows.Add((x, Y1 - 3.4f, 4.0f, 3.0f));
            for (float y = Y0 + 2.5f; y < 1.5f; y += 2.8f) shadows.Add((rackX, y + 1.4f, 1.8f, 3.4f));
            foreach (float x in new[] { -8f, -5.2f, 6.5f, 9.3f }) shadows.Add((x, Y0 + 1.0f, 3.0f, 1.4f));
            shadows.Add((amrX + 0.3f, amrY, 2.0f, 1.4f));
            foreach (var s in shadows) Go.FloorDecal("contact", hall, blob, s.w, s.d, s.x, s.y, 0, 0.0035f);

            // The wordmark on a dark plate on the north wall, above the machine line (white on graphite).
            var logo = Gen.Tex("wall_logo");
            float lh = 1.3f, lw = logo != null ? lh * logo.width / (float)logo.height : 4.5f;
            Go.Mesh("logo plate", hall, MeshKit.Box(), Mats.Paint(0x1b1f24, 0.55f, 0.3f), Web.P(-6.5f, Y1 - 0.22f, 5.0f), null, Web.S(lw + 0.9f, 0.06f, lh + 0.6f), cast: false);
            Go.Mesh("logo stripe", hall, MeshKit.Box(), Mats.Glow(0xff4713, 2.2f), Web.P(-6.5f, Y1 - 0.22f, 5.0f - (lh + 0.6f) / 2 + 0.04f), null, Web.S(lw + 0.9f, 0.065f, 0.08f), cast: false);
            WallSign(hall, Mats.Decal("wall_logo", 1f), lw, lh, -6.5f, Y1 - 0.26f, 5.05f, 0, -1);
        }

        /// <summary>A plane standing upright at web (x, y, z), its face towards web (nx, ny).</summary>
        static GameObject WallSign(Transform parent, Material m, float w, float h, float x, float y, float z, float nx, float ny)
        {
            var facing = new Vector3(nx, 0, ny);
            return Go.Mesh("sign", parent, MeshKit.Quad(), m, Web.P(x, y, z), Quaternion.LookRotation(-facing, Vector3.up), new Vector3(w, h, 1), cast: false);
        }

        static void Collider(Transform parent, string name, float x, float y, float z, float sx, float sy, float sz)
        {
            var c = new GameObject(name).AddComponent<BoxCollider>();
            c.gameObject.layer = Layers.Environment;
            c.transform.SetParent(parent, false);
            c.transform.localPosition = Web.P(x, y, z);
            c.size = Web.S(sx, sy, sz);
        }

        // ================================================================== the cell's surround (cell.ts)
        void BuildSurround()
        {
            var cell = new GameObject("cell surround").transform;
            cell.SetParent(Root, false);
            var b = new Batcher();
            var yellow = Mats.Paint(Pal.SafetyYellow, 0.45f, 0.25f);
            float fx0 = -CellLayout.Hx, fx1 = CellLayout.Hx, fy0 = -CellLayout.Hy, fy1 = CellLayout.Hy;
            float bx0 = CellLayout.Bx0, bx1 = CellLayout.Bx1, by0 = CellLayout.By0, by1 = CellLayout.By1;
            const float lw = 0.1f, z = 0.0025f;
            b.Box(yellow, V(bx1 - bx0 + lw, lw, 0.004f), V((bx0 + bx1) / 2, by0, z));
            b.Box(yellow, V(bx1 - bx0 + lw, lw, 0.004f), V((bx0 + bx1) / 2, by1, z));
            b.Box(yellow, V(lw, by1 - by0, 0.004f), V(bx0, (by0 + by1) / 2, z));
            b.Box(yellow, V(lw, by1 - by0, 0.004f), V(bx1, (by0 + by1) / 2, z));
            var band = Mats.Textured("hazard", new Vector2(0.5f, (by1 - by0) / 0.5f), 0.5f, 0);
            Go.FloorDecal("keep clear", cell, band, 0.4f, by1 - by0, bx1 + 0.25f, (by0 + by1) / 2, 0, 0.003f);
            Go.FloorDecal("CELL 01", cell, Mats.Decal("floor_cell", 0.92f), 1.6f, 0.4f, bx1 - 1.0f, by0 - 0.42f, 0, 0.004f);

            // A safety laser scanner at the bench's operator corner, watching the boundary.
            b.Shape(MeshKit.RoundedBox(Web.S(0.14f, 0.14f, 0.15f), 0.015f), yellow, V(fx1 - 0.02f, fy0 - 0.02f, 0.075f), cast: true);
            b.Shape(MeshKit.Cylinder(0.055f, 0.06f), Mats.Paint(0x111214, 0.15f, 0.2f), V(fx1 - 0.02f, fy0 - 0.02f, 0.18f));
            Scanner = Go.Empty("scanner", cell, Web.P(fx1 - 0.02f, fy0 - 0.02f, 0.2f)).transform;

            // ------------------------------------------------------------- control cabinet
            float cabX = bx0 - 0.75f, cabY = by0 + 0.35f;
            b.Shape(MeshKit.RoundedBox(Web.S(0.62f, 0.82f, 2.0f), 0.02f), Mats.Panel(Pal.LightGrey), V(cabX, cabY, 1.1f), cast: true);
            b.Box(Mats.Paint(0x2a2e33, 0.6f, 0.2f), V(0.64f, 0.84f, 0.1f), V(cabX, cabY, 0.05f));
            b.Box(Mats.Paint(0xb9bdc0, 0.4f, 0.3f), V(0.012f, 0.76f, 1.9f), V(cabX + 0.316f, cabY, 1.1f));
            b.Box(Mats.Steel(0x9ea4aa, 0.3f), V(0.03f, 0.03f, 0.22f), V(cabX + 0.33f, cabY + 0.28f, 1.15f));
            b.Box(yellow, V(0.012f, 0.16f, 0.16f), V(cabX + 0.322f, cabY - 0.22f, 1.45f));
            b.Shape(MeshKit.Cylinder(0.035f, 0.05f), Mats.Paint(0xc8102e, 0.35f, 0.1f), V(cabX + 0.345f, cabY - 0.22f, 1.45f), V(0, Mathf.PI / 2, 0));
            WallSign(cell, Mats.Textured("sign_robot_cell", Vector2.one, 0.55f, 0), 0.26f, 0.195f, cabX + 0.324f, cabY + 0.05f, 1.68f, 1, 0);
            Collider(cell, "cabinet", cabX, cabY, 1.05f, 0.64f, 0.84f, 2.1f);
            Stack = new StackLight(cell, Web.P(cabX + 0.18f, cabY + 0.28f, 2.1f));

            // ------------------------------------------------------------- operator panel
            float hmiX = bx1 + 0.85f, hmiY = fy1 + 0.1f;
            b.Shape(MeshKit.Cylinder(0.035f, 1.05f), Mats.Steel(0xaab0b6, 0.35f), V(hmiX, hmiY, 0.525f), cast: true);
            b.Shape(MeshKit.RoundedBox(Web.S(0.36f, 0.36f, 0.02f), 0.005f), Mats.Paint(0x2a2e33, 0.5f, 0.3f), V(hmiX, hmiY, 0.01f));
            Hmi = new OperatorPanel(cell, Web.P(hmiX, hmiY, 1.22f), Web.Yaw(Mathf.Atan2(1.0f, 0.95f)), yellow);
            Collider(cell, "panel", hmiX, hmiY, 0.6f, 0.2f, 0.2f, 1.2f);

            // ------------------------------------------------------------- conveyor
            float cy = by1 + 1.3f, c0 = bx0 - 1.2f, c1 = bx1 + 4.5f, cLen = c1 - c0, cz = 0.82f;
            var rail = Mats.Paint(0x8e969d, 0.4f, 0.6f);
            foreach (float dy in new[] { -0.32f, 0.32f }) b.Box(rail, V(cLen, 0.05f, 0.12f), V((c0 + c1) / 2, cy + dy, cz), cast: true);
            var roller = Mats.Steel(0xc9ced2, 0.25f);
            for (float x = c0 + 0.08f; x < c1 - 0.05f; x += 0.12f) b.Shape(MeshKit.Cylinder(0.024f, 0.6f, 12), roller, V(x, cy, cz + 0.02f), V(Mathf.PI / 2, 0, 0));
            for (float x = c0 + 0.3f; x < c1; x += 1.6f)
            {
                foreach (float dy in new[] { -0.3f, 0.3f }) b.Box(Mats.Paint(0x5c6570, 0.5f, 0.4f), V(0.05f, 0.05f, cz - 0.05f), V(x, cy + dy, (cz - 0.05f) / 2), cast: true);
                b.Box(Mats.Rubber(), V(0.12f, 0.12f, 0.02f), V(x, cy - 0.3f, 0.01f));
                b.Box(Mats.Rubber(), V(0.12f, 0.12f, 0.02f), V(x, cy + 0.3f, 0.01f));
            }
            Collider(cell, "conveyor", (c0 + c1) / 2, cy, cz / 2, cLen, 0.7f, cz);
            int ci = 0;
            foreach (float x in new[] { c0 + 0.9f, c0 + 2.4f, c1 - 1.5f, c0 + 4.1f, c0 + 6.0f })
            {
                var carton = Props.Place("cardboard_box", cell, x, cy, cz + 0.045f, ci * 0.04f - 0.03f, V(0.5f, 0.36f, 0.34f));
                ConveyorCartons.Add(carton.transform);
                ci++;
            }
            conveyorStart = c0 + 0.3f;
            conveyorEnd = c1 - 0.3f;

            Props.Place("plastic_crate", cell, bx1 + 5.2f, by1 + 0.4f, 0, 0.1f, V(0.5f, 0.34f, 0.3f));
            Props.Place("plastic_crate", cell, bx1 + 5.2f, by1 + 0.4f, 0.3f, 0.12f, V(0.5f, 0.34f, 0.3f));
            Props.Place("plastic_crate", cell, bx1 + 5.75f, by1 + 0.4f, 0, -0.05f, V(0.5f, 0.34f, 0.3f));
            WallSign(cell, Mats.Textured("sign_collab", Vector2.one, 0.6f, 0), 0.4f, 0.3f, hmiX + 0.02f, hmiY - 0.0f, 1.62f, 0.72f, -0.69f).SetActive(false);

            b.Build("cell parts", cell);
        }

        float conveyorStart, conveyorEnd;

        // ================================================================== the work cell
        void BuildCellWork()
        {
            var work = new GameObject("work cell").transform;
            work.SetParent(Root, false);
            // The bench: a cabinet-base workbench with a dark composite top (the simulation's slab).
            var top = CellLayout.BenchCentre;
            var size = CellLayout.BenchSize;
            var benchRoot = Go.Empty("bench", work, new Vector3(top.x, 0, top.z)).transform;
            var topMat = Mats.Paint(new Color(0.28f, 0.28f, 0.29f), 0.5f, 0.05f);
            Go.Mesh("top", benchRoot, MeshKit.RoundedBox(new Vector3(size.x + 0.02f, 0.03f, size.z + 0.02f), 0.006f), topMat, new Vector3(0, CellLayout.BenchTop - 0.015f, 0));
            var body = Mats.Panel(Pal.LightGrey);
            Go.Mesh("cabinet", benchRoot, MeshKit.RoundedBox(new Vector3(size.x - 0.04f, CellLayout.BenchTop - 0.11f, size.z - 0.04f), 0.01f), body, new Vector3(0, (CellLayout.BenchTop - 0.11f) / 2 + 0.08f, 0));
            Go.Mesh("plinth", benchRoot, MeshKit.Box(), Mats.Paint(0x2a2e33, 0.6f, 0.2f), new Vector3(0, 0.04f, 0), null, new Vector3(size.x - 0.06f, 0.08f, size.z - 0.06f));
            var handle = Mats.Steel(0xb8bdc2, 0.3f);
            var seam = Mats.Paint(0x8f959b, 0.5f, 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.2f + i * 0.18f;
                Go.Mesh("drawer seam", benchRoot, MeshKit.Box(), seam, new Vector3(size.x / 2 - 0.018f, y + 0.08f, 0), null, new Vector3(0.004f, 0.004f, size.z - 0.1f), cast: false);
                Go.Mesh("handle", benchRoot, MeshKit.RoundedBox(new Vector3(0.016f, 0.018f, 0.22f), 0.006f), handle, new Vector3(size.x / 2 - 0.012f, y + 0.02f, 0));
            }
            var benchCol = benchRoot.gameObject.AddComponent<BoxCollider>();
            benchCol.center = new Vector3(0, CellLayout.BenchTop / 2, 0);
            benchCol.size = new Vector3(size.x, CellLayout.BenchTop, size.z);
            benchRoot.gameObject.layer = Layers.Environment;
            BenchCollider = benchCol;

            // The pedestal under the Panda (mounts.ts): measured from the arm's own base.
            var basePos = CellLayout.RobotBase;
            Pedestal = Go.Empty("pedestal", work, new Vector3(basePos.x, 0, basePos.z)).transform;
            BuildPedestal(Pedestal, basePos.y, 0.2f, 0.2f);

            // The place zone: a square painted on the bench top, and its outline.
            var zone = Go.Empty("place zone", work, CellLayout.PlaceZone + Vector3.up * 0.0012f).transform;
            float h = CellLayout.ZoneHalf * 2;
            Go.Mesh("fill", zone, MeshKit.FloorQuad(), Mats.Transparent(new Color(0.24f, 0.86f, 0.52f, 0.28f)), Vector3.zero, null, new Vector3(h, 1, h), cast: false);
            var edge = Mats.Unlit(new Color(0.24f, 0.86f, 0.52f) * 1.2f);
            foreach (var (dx, dz, sx, sz) in new[] { (0f, -1f, 1f, 0f), (0f, 1f, 1f, 0f), (-1f, 0f, 0f, 1f), (1f, 0f, 0f, 1f) })
                Go.Mesh("edge", zone, MeshKit.Box(), edge, new Vector3(dx * h / 2, 0.0005f, dz * h / 2), null, new Vector3(sx * h + 0.004f, 0.001f, sz * h + 0.004f), cast: false);
            PlaceZoneMarker = zone.gameObject;

            // A small tray of four pockets, and the parts feeder's plate.
            var tray = Go.Empty("tray", work, CellLayout.TrayCentre).transform;
            var trayMat = Mats.Paint(0x30353b, 0.6f, 0.1f);
            var trayBody = Go.Mesh("tray", tray, MeshKit.RoundedBox(new Vector3(0.15f, 0.012f, 0.15f), 0.004f), trayMat, new Vector3(0, 0.006f, 0));
            trayBody.AddComponent<BoxCollider>();
            var pocket = Mats.Paint(0x1c1f23, 0.7f, 0.05f);
            foreach (var p in TrayPockets()) Go.Mesh("pocket", tray, MeshKit.RoundedBox(new Vector3(0.058f, 0.002f, 0.058f), 0.004f), pocket, tray.InverseTransformPoint(p) + Vector3.up * 0.0115f, cast: false);
            var feeder = Go.Empty("feeder", work, CellLayout.FeederSpot).transform;
            var plate = Go.Mesh("plate", feeder, MeshKit.RoundedBox(new Vector3(0.1f, 0.011f, 0.1f), 0.004f), Mats.Steel(0xaab0b6, 0.3f), new Vector3(0, 0.0055f, 0));
            plate.AddComponent<BoxCollider>();
            Go.Mesh("ring", feeder, MeshKit.Cylinder(0.042f, 0.002f, 32), Mats.Glow(0xff4713, 1.4f), new Vector3(0, 0.011f, 0), cast: false);
        }

        /// <summary>The tray's four pocket centres on the bench top (world).</summary>
        public static Vector3[] TrayPockets()
        {
            var c = CellLayout.TrayCentre;
            return new[] { c + new Vector3(-0.035f, 0, -0.035f), c + new Vector3(0.035f, 0, -0.035f), c + new Vector3(-0.035f, 0, 0.035f), c + new Vector3(0.035f, 0, 0.035f) };
        }

        static void BuildPedestal(Transform g, float height, float sx, float sy)
        {
            const uint Steel = 0x8d9399, Coat = 0x2b3036, Plate = 0xb8bec4, Accent = 0xff4713;
            float column = Mathf.Clamp(Mathf.Min(sx, sy) * 0.82f, 0.14f, 0.3f);
            float top = Mathf.Clamp(Mathf.Max(sx, sy) + 0.03f, 0.22f, 0.5f);
            float foot = top + 0.2f;
            const float footH = 0.025f, topH = 0.03f;
            var coat = Mats.Paint(Coat, 0.55f, 0.25f);
            var steel = Mats.Paint(Steel, 0.42f, 0.75f);
            var plate = Mats.Paint(Plate, 0.3f, 0.85f);
            var accent = Mats.Paint(Accent, 0.45f, 0.1f);
            Go.Mesh("foot", g, MeshKit.Box(), steel, new Vector3(0, footH / 2, 0), null, new Vector3(foot, footH, foot));
            foreach (var (bx, by) in new[] { (1f, 1f), (1f, -1f), (-1f, 1f), (-1f, -1f) })
                Go.Mesh("bolt", g, MeshKit.Cylinder(0.014f, 0.022f, 12), plate, new Vector3(bx * (foot - 0.07f) / 2, footH + 0.011f, by * (foot - 0.07f) / 2));
            float columnH = Mathf.Max(0.02f, height - footH - topH);
            Go.Mesh("column", g, MeshKit.RoundedBox(new Vector3(column, columnH, column), 0.008f), coat, new Vector3(0, footH + columnH / 2, 0));
            float gw = (foot - column) / 2 - 0.02f;
            float gh = Mathf.Min(0.16f, columnH * 0.5f);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f;
                var dir = Quaternion.Euler(0, a, 0);
                // A gusset: a thin wedge from the column down to the foot plate.
                var gusset = Go.Mesh("gusset", g, Wedge(gw, gh, 0.012f), coat, dir * new Vector3(column / 2, footH, 0), dir);
            }
            Go.Mesh("band", g, MeshKit.Box(), accent, new Vector3(0, height - topH - 0.05f, 0), null, new Vector3(column + 0.004f, 0.022f, column + 0.004f));
            Go.Mesh("plate", g, MeshKit.Box(), plate, new Vector3(0, height - topH / 2, 0), null, new Vector3(top, topH, top));
            var col = g.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, height / 2, 0);
            col.size = new Vector3(top, height - 0.02f, top);
            g.gameObject.layer = Layers.Environment;
        }

        /// <summary>A right-triangle plate along +x (length w) rising along +y (height h), t thick.</summary>
        static Mesh Wedge(float w, float h, float t)
        {
            var v = new List<Vector3>
            {
                new(0, 0, -t / 2), new(w, 0, -t / 2), new(0, h, -t / 2),
                new(0, 0, t / 2), new(w, 0, t / 2), new(0, h, t / 2),
            };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b2, Vector3 c)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b2); verts.Add(c);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            void Quad(Vector3 a, Vector3 b2, Vector3 c, Vector3 d) { Tri(a, b2, c); Tri(a, c, d); }
            Tri(v[0], v[2], v[1]);
            Tri(v[3], v[4], v[5]);
            Quad(v[0], v[1], v[4], v[3]);
            Quad(v[1], v[2], v[5], v[4]);
            Quad(v[2], v[0], v[3], v[5]);
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            var uvs = new Vector2[verts.Count];
            m.uv = uvs;
            var colors = new Color32[verts.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
            m.colors32 = colors;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ================================================================== light
        void BuildLighting()
        {
            var lights = new GameObject("lighting").transform;
            lights.SetParent(Root, false);
            // The key light falls in through the skylights from high above (engine.ts's key position).
            var sunGo = new GameObject("key light");
            sunGo.transform.SetParent(lights, false);
            Vector3 from = Web.P(-3.2f, -4.6f, 10.5f);
            sunGo.transform.rotation = Quaternion.LookRotation(-from.normalized, Vector3.up);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = new Color(1.0f, 0.97f, 0.92f);
            Sun.intensity = 1.35f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.82f;
            Sun.shadowBias = 0.03f;
            Sun.shadowNormalBias = 0.25f;
            var sunData = sunGo.GetComponent<UniversalAdditionalLightData>();
            if (sunData == null) sunData = sunGo.AddComponent<UniversalAdditionalLightData>();
            sunData.usePipelineSettings = true;

            // Warm high-bay pools over the cell and the lab.
            foreach (var (x, y) in new[] { (-1f, -3f), (3f, -3f), (-1f, 3f), (3f, 3f), (-9f, 3f), (11f, 3f), (-1f, 8.5f), (7f, -3f) })
            {
                var l = new GameObject("high-bay").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.localPosition = Web.P(x, y, 7.4f);
                l.transform.rotation = Quaternion.LookRotation(Vector3.down);
                l.type = LightType.Spot;
                l.spotAngle = 120f;
                l.innerSpotAngle = 60f;
                l.range = 14f;
                l.intensity = 9f;
                l.color = new Color(1f, 0.95f, 0.88f);
                l.shadows = LightShadows.None;
            }

            ApplyEnvironmentLight();

            // Reflection probes: the hall as a whole, and a sharper one around the cell.
            Probes.Add(Probe(lights, "hall", Web.P((X0 + X1) / 2, 0, 3.6f), new Vector3(X1 - X0, 9.5f, Y1 - Y0), 128));
            Probes.Add(Probe(lights, "cell", Web.P(0.2f, 0, 1.3f), new Vector3(7f, 3.2f, 7f), 256));

            // Post: ACES tone mapping, a little bloom for lamps and LEDs, and a vignette locomotion can use.
            var postGo = new GameObject("post");
            postGo.transform.SetParent(lights, false);
            Post = postGo.AddComponent<Volume>();
            Post.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            Grade = profile.Add<ColorAdjustments>(true);
            Grade.postExposure.Override(0.35f);
            Grade.contrast.Override(8f);
            Grade.saturation.Override(6f);
            Vignette = profile.Add<Vignette>(true);
            Vignette.intensity.Override(0f);
            Vignette.smoothness.Override(0.6f);
            Vignette.color.Override(Color.black);
            Post.sharedProfile = profile;
        }

        [Serializable] class HdriSamples { public int w, h; public float[] rgb; }

        /// <summary>
        /// Ambient light from the factory HDRI (Poly Haven's industrial workshop): its low-resolution
        /// copy, summed as directional lights into the ambient probe.
        /// </summary>
        public static void ApplyEnvironmentLight(float scale = 1.0f)
        {
            var sh = new SphericalHarmonicsL2();
            HdriSamples s = null;
            try { s = Gen.Json<HdriSamples>("Textures/hdri_samples"); } catch (Exception) { }
            if (s == null || s.rgb == null || s.rgb.Length < s.w * s.h * 3)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.52f, 0.55f, 0.58f);
                RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.42f);
                RenderSettings.ambientGroundColor = new Color(0.22f, 0.22f, 0.22f);
                return;
            }
            for (int j = 0; j < s.h; j++)
            {
                float theta = (j + 0.5f) / s.h * Mathf.PI;
                float dTheta = Mathf.PI / s.h;
                for (int i = 0; i < s.w; i++)
                {
                    float phi = (i + 0.5f) / s.w * Mathf.PI * 2;
                    var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    int k = (j * s.w + i) * 3;
                    var c = new Color(s.rgb[k], s.rgb[k + 1], s.rgb[k + 2]);
                    float omega = Mathf.Sin(theta) * dTheta * (Mathf.PI * 2 / s.w);
                    sh.AddDirectionalLight(dir, c, omega / Mathf.PI * scale);
                }
            }
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
        }

        ReflectionProbe Probe(Transform parent, string name, Vector3 centre, Vector3 size, int resolution)
        {
            var go = new GameObject("probe " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            p.size = size;
            p.boxProjection = true;
            p.resolution = resolution;
            p.hdr = true;
            p.importance = name == "cell" ? 2 : 1;
            p.blendDistance = 1f;
            p.cullingMask = ~(Layers.UIMask | (1 << Layers.Hands));
            p.farClipPlane = 60f;
            return p;
        }

        /// <summary>Renders the reflection probes once everything is built.</summary>
        public void RenderReflections()
        {
            foreach (var p in Probes) p.RenderProbe();
        }

        void BuildDust()
        {
            var go = new GameObject("dust");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = Web.P(0.2f, 0, 1.9f);
            Dust = go.AddComponent<ParticleSystem>();
            var main = Dust.main;
            main.loop = true;
            main.startLifetime = 26f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.012f);
            main.startColor = new Color(1f, 0.96f, 0.88f, 0.35f);
            main.maxParticles = 320;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            var emission = Dust.emission;
            emission.rateOverTime = 12f;
            var shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(4.5f, 3.4f, 4.5f);
            var vel = Dust.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
            vel.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            var noise = Dust.noise;
            noise.enabled = true;
            noise.strength = 0.03f;
            noise.frequency = 0.2f;
            var col = Dust.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = Mats.Clone("UnlitAdditive");
            mat.SetTexture("_BaseMap", Gen.Tex("softdot"));
            mat.SetColor("_BaseColor", new Color(1, 1, 1, 0.5f));
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.minParticleSize = 0.0005f;
        }

        // ================================================================== motion
        public void Tick(float dt, Cell cell)
        {
            Stack.Set(cell.Safety.Lamp, cell.Safety.Stop == StopKind.Emergency);
            Stack.Tick(dt);
            Hmi.Show(cell);
            // Cartons ride the conveyor east at 0.18 m/s and come round again.
            conveyorClock += dt;
            float span = conveyorEnd - conveyorStart;
            for (int i = 0; i < ConveyorCartons.Count; i++)
            {
                var t = ConveyorCartons[i];
                var p = t.localPosition;
                float x = p.x + 0.18f * dt;
                if (x > conveyorEnd) x -= span;
                t.localPosition = new Vector3(x, p.y, p.z);
            }
        }
    }

    /// <summary>The cabinet's four-segment stack light (IEC 60204-1 colours), driven by the cell.</summary>
    public class StackLight
    {
        readonly Dictionary<string, MeshRenderer> segments = new();
        readonly Dictionary<string, (Material on, Material off)> mats = new();
        string state = "";
        bool blink;
        float clock;
        static readonly string[] Lamps = { "red", "amber", "green", "blue" };
        static readonly Dictionary<string, uint> Colors = new() { { "red", 0xff3b30 }, { "amber", 0xffa000 }, { "green", 0x34d058 }, { "blue", 0x3d8bff } };
        public readonly Transform Root;

        public StackLight(Transform parent, Vector3 at)
        {
            Root = Go.Empty("stack light", parent, at).transform;
            Go.Mesh("pole", Root, MeshKit.Cylinder(0.018f, 0.22f), Mats.Steel(0xb9bec3, 0.3f), new Vector3(0, 0.11f, 0));
            for (int k = 0; k < Lamps.Length; k++)
            {
                string lamp = Lamps[k];
                float y = 0.22f + (Lamps.Length - 1 - k) * 0.075f + 0.0375f;
                var on = Mats.Glow(Colors[lamp], 9f);
                var off = Mats.Glass(Pal.Hex(Colors[lamp]) * 0.3f, 0.92f);
                mats[lamp] = (on, off);
                var seg = Go.Mesh(lamp, Root, MeshKit.Cylinder(0.035f, 0.07f), off, new Vector3(0, y, 0), cast: false);
                segments[lamp] = seg.GetComponent<MeshRenderer>();
            }
            Go.Mesh("cap", Root, MeshKit.Cylinder(0.036f, 0.02f), Mats.Paint(0x202326, 0.4f, 0.2f), new Vector3(0, 0.22f + Lamps.Length * 0.075f + 0.01f, 0));
        }

        public void Set(string lamp, bool blinking)
        {
            state = lamp;
            blink = blinking || lamp == "red";
        }

        public void Tick(float dt)
        {
            clock = (clock + dt) % 1f;
            foreach (var lamp in Lamps)
            {
                bool lit = lamp == state && (!blink || clock < 0.5f);
                segments[lamp].sharedMaterial = lit ? mats[lamp].on : mats[lamp].off;
            }
        }
    }

    /// <summary>
    /// The operator panel beside the cell: its screen says what the cell is doing in words (as the web
    /// app's does), and its red mushroom is the cell's emergency stop.
    /// </summary>
    public class OperatorPanel
    {
        public readonly Transform Head;
        public readonly Transform Face;
        public readonly Transform StopButton;
        readonly TextMeshPro title, status, detail;
        readonly MeshRenderer dot;
        readonly Material[] tones;
        string last = "";

        public OperatorPanel(Transform parent, Vector3 at, Quaternion yaw, Material yellow)
        {
            Head = Go.Empty("operator panel", parent, at, yaw).transform;
            var tilt = Go.Empty("tilt", Head, Vector3.zero, Web.About('x', -0.32f)).transform;
            Go.Mesh("housing", tilt, MeshKit.RoundedBox(Web.S(0.58f, 0.08f, 0.5f), 0.02f), Mats.Paint(0x30353b, 0.45f, 0.3f));
            Face = Go.Empty("screen", tilt, new Vector3(0, 0.06f, -0.0405f)).transform;
            Go.Mesh("glass", Face, MeshKit.Quad(), Mats.Unlit(Pal.Hex(0x0d1014)), Vector3.zero, null, new Vector3(0.5f, 0.31f, 1), cast: false);
            Go.Mesh("bar", Face, MeshKit.Quad(), Mats.Unlit(Pal.Hex(0x151a20)), new Vector3(0, 0.155f - 0.025f, -0.0005f), null, new Vector3(0.5f, 0.05f, 1), cast: false);
            Go.Mesh("rule", Face, MeshKit.Quad(), Mats.Unlit(Pal.Brand * 1.3f), new Vector3(0, 0.155f - 0.05f, -0.0006f), null, new Vector3(0.5f, 0.004f, 1), cast: false);
            var logo = Go.Mesh("logo", Face, MeshKit.Quad(), Mats.UnlitTex("wordmark_white", Color.white), new Vector3(-0.17f, 0.13f, -0.0008f), null, new Vector3(0.12f, 0.028f, 1), cast: false);
            logo.GetComponent<MeshRenderer>().sharedMaterial = TransparentLogo();
            title = Bridge.UI.Text.Make(Face, "CELL 01", 0.16f, new Color(0.91f, 0.93f, 0.95f), TextAlignmentOptions.Right, 0.2f, 0.04f, "Figtree-Bold", pos: new Vector3(0.13f, 0.13f, -0.001f));
            tones = new[] { Mats.Unlit(Pal.OkGlow * 1.6f), Mats.Unlit(Pal.SafetyYellow * 1.6f), Mats.Unlit(Pal.Hex(0xff4d4f) * 1.6f), Mats.Unlit(Pal.Hex(0x4b9dff) * 1.6f), Mats.Unlit(Pal.Hex(0x9aa3ad)) };
            dot = Go.Mesh("dot", Face, MeshKit.Cylinder(0.012f, 0.001f, 24), tones[0], new Vector3(-0.205f, 0.045f, -0.001f), Quaternion.Euler(90, 0, 0), cast: false).GetComponent<MeshRenderer>();
            status = Bridge.UI.Text.Make(Face, "READY", 0.36f, Color.white, TextAlignmentOptions.Left, 0.4f, 0.06f, "Figtree-ExtraBold", pos: new Vector3(0.02f, 0.045f, -0.001f));
            detail = Bridge.UI.Text.Make(Face, "", 0.15f, new Color(0.6f, 0.64f, 0.68f), TextAlignmentOptions.TopLeft, 0.46f, 0.1f, "Figtree-Medium", pos: new Vector3(0, -0.06f, -0.001f));
            foreach (var t in new[] { title, status, detail }) t.gameObject.layer = Layers.Environment;
            // The stop button under the screen, in its yellow collar.
            StopButton = Go.Empty("emergency stop", tilt, new Vector3(0, -0.17f, -0.06f)).transform;
            Go.Mesh("collar", StopButton, MeshKit.Cylinder(0.048f, 0.012f), yellow, new Vector3(0, 0, 0.014f), Quaternion.Euler(-90, 0, 0));
            Go.Mesh("mushroom", StopButton, MeshKit.Cylinder(0.032f, 0.03f, 32, 0.028f), Mats.Paint(0xd0021b, 0.3f, 0.1f), Vector3.zero, Quaternion.Euler(-90, 0, 0));
        }

        static Material transparentLogo;
        static Material TransparentLogo()
        {
            if (transparentLogo != null) return transparentLogo;
            transparentLogo = Mats.Clone("UnlitTransparent");
            transparentLogo.SetTexture("_BaseMap", Gen.Tex("wordmark_white"));
            return transparentLogo;
        }

        public void Show(Cell cell)
        {
            string s, d;
            int tone;
            var safety = cell.Safety;
            if (safety.Stop == StopKind.Emergency) { s = "E-STOP"; d = safety.EStopPressed ? "Emergency stop pressed. Twist to release, then reset." : "Released. Press Reset on the pendant."; tone = 2; }
            else if (safety.Stop == StopKind.Protective) { s = "STOPPED"; d = safety.Reason; tone = 2; }
            else if (safety.Zone == "warning") { s = "SLOW"; d = "Person nearby: reduced speed"; tone = 1; }
            else if (cell.Running) { s = "RUNNING"; d = cell.Program.Name + (cell.StepIndex >= 0 && cell.StepIndex < cell.Program.Steps.Count ? " - " + cell.Program.Steps[cell.StepIndex].Label : ""); tone = 0; }
            else if (cell.Mode == JogMode.FreeDrive) { s = "HAND GUIDE"; d = "Free drive: guide the arm by hand"; tone = 3; }
            else if (cell.Mode != JogMode.Idle) { s = "MANUAL"; d = $"Jogging, {cell.SpeedOverride * 100:0}% speed"; tone = 3; }
            else { s = "READY"; d = "Collaborative cell, idle"; tone = 0; }
            string key = s + d + tone;
            if (key == last) return;
            last = key;
            status.text = s;
            detail.text = d;
            dot.sharedMaterial = tones[tone];
        }
    }
}
