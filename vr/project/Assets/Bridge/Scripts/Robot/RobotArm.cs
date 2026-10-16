// SPDX-License-Identifier: LicenseRef-BRIDGE
// The Franka Emika Panda, built from the MJCF the web app's simulation uses
// (assets/embodiments/panda/panda_standalone.xml, exported by vr/tools/export_assets.py): its bodies
// at their rest poses, its seven revolute joints and two finger slides with their limits, its meshes
// and colours, and capsules fitted to its links for contact checks.
//
// Joint angles are kept in MuJoCo's convention, so a reading here matches the web app's and a real
// Panda's. Kinematics run on the model's own numbers (not on the scene's transforms), so the
// inverse-kinematics solver can try poses without moving anything.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bridge.Robot
{
    [Serializable] public class JointDef { public string name; public string type; public float[] axis; public int sign; public float[] range; }
    [Serializable] public class VisualDef { public string mesh; public float[] rgba; public string material; }
    [Serializable] public class ColliderDef { public string type; public float[] center; public float[] size; public float[] a; public float[] b; public float r; }
    [Serializable] public class BodyDef { public string name; public int parent; public float[] pos; public float[] rot; public JointDef joint; public VisualDef[] visuals; public ColliderDef[] colliders; public float mass; public float[] com; }
    [Serializable] public class TcpDef { public int body; public float[] pos; }
    [Serializable] public class RobotDef { public string name; public string source; public BodyDef[] bodies; public TcpDef tcp; public float[] home; public float[] velocity; }

    public struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Pose(Vector3 p, Quaternion r) { position = p; rotation = r; }
        public Vector3 Forward => rotation * Vector3.forward;
        public Vector3 Up => rotation * Vector3.up;
        public Vector3 Right => rotation * Vector3.right;
        public static Pose Lerp(Pose a, Pose b, float t) => new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));
    }

    public struct IKResult
    {
        public bool converged;
        public float positionError;
        public float rotationError;
        public float manipulability;
        public int iterations;
    }

    public class RobotArm
    {
        public const int Dof = 7;
        public static RobotDef Def => def ??= Gen.Json<RobotDef>("Robots/panda");
        static RobotDef def;

        public readonly GameObject Root;
        public readonly Transform[] Bodies;
        public readonly float[] Q = new float[Dof];
        public readonly float[] QMin = new float[Dof];
        public readonly float[] QMax = new float[Dof];
        public readonly float[] VMax = new float[Dof];
        public readonly float[] Home = new float[Dof];
        public float Opening = 0.08f;              // between the fingers [m], 0..0.08
        public const float MaxOpening = 0.08f;
        public readonly string[] JointNames = new string[Dof];
        /// <summary>For each arm joint, the index of the body it moves.</summary>
        public readonly int[] JointBody = new int[Dof];
        public readonly List<Renderer>[] LinkRenderers;
        public readonly List<Collider> Colliders = new();
        public readonly List<(Collider collider, int body)> ContactColliders = new();
        public Transform Tcp => Bodies[Def.tcp.body];
        public Transform Hand => Bodies[BodyIndex("right_hand")];
        public Transform Flange => Bodies[BodyIndex("link7")];

        readonly int leftFinger, rightFinger;
        readonly Vector3[] restPos;
        readonly Quaternion[] restRot;
        readonly Vector3[] axes;
        readonly int[] jointOf;     // body -> arm joint index, or -1
        readonly int[] chain;       // bodies from the base to the TCP, in order
        readonly MaterialPropertyBlock block = new();

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public static int BodyIndex(string name)
        {
            for (int i = 0; i < Def.bodies.Length; i++) if (Def.bodies[i].name == name) return i;
            throw new Exception("no body " + name);
        }

        /// <summary>
        /// Builds a Panda under parent. With a ghost material every part is drawn in it (the digital
        /// twin, target poses) and no colliders are made.
        /// </summary>
        public RobotArm(Transform parent, string name, Vector3 position, Quaternion rotation, Material ghost = null, bool colliders = true)
        {
            var d = Def;
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = position;
            Root.transform.localRotation = rotation;
            int n = d.bodies.Length;
            Bodies = new Transform[n];
            restPos = new Vector3[n];
            restRot = new Quaternion[n];
            axes = new Vector3[n];
            jointOf = new int[n];
            LinkRenderers = new List<Renderer>[n];
            int arm = 0;
            var paint = new Dictionary<string, Material>();
            for (int i = 0; i < n; i++)
            {
                var b = d.bodies[i];
                var go = new GameObject(b.name);
                go.layer = Layers.Robot;
                go.transform.SetParent(b.parent >= 0 ? Bodies[b.parent] : Root.transform, false);
                restPos[i] = V(b.pos);
                restRot[i] = new Quaternion(b.rot[0], b.rot[1], b.rot[2], b.rot[3]);
                go.transform.localPosition = restPos[i];
                go.transform.localRotation = restRot[i];
                Bodies[i] = go.transform;
                jointOf[i] = -1;
                LinkRenderers[i] = new List<Renderer>();
                if (b.joint != null && !string.IsNullOrEmpty(b.joint.name))
                {
                    axes[i] = V(b.joint.axis);
                    if (b.joint.type == "hinge" && arm < Dof)
                    {
                        jointOf[i] = arm;
                        JointBody[arm] = i;
                        JointNames[arm] = b.joint.name;
                        QMin[arm] = b.joint.range[0];
                        QMax[arm] = b.joint.range[1];
                        arm++;
                    }
                }
                foreach (var v in b.visuals ?? Array.Empty<VisualDef>())
                {
                    var mat = ghost ?? PartMaterial(v.rgba, paint);
                    var part = Go.Mesh(v.mesh, go.transform, Gen.Mesh(v.mesh), mat, cast: ghost == null, layer: Layers.Robot);
                    var r = part.GetComponent<MeshRenderer>();
                    if (ghost != null) r.receiveShadows = false;
                    LinkRenderers[i].Add(r);
                }
                if (colliders && ghost == null)
                {
                    foreach (var c in b.colliders ?? Array.Empty<ColliderDef>()) MakeCollider(go.transform, c, i);
                }
            }
            leftFinger = BodyIndex("leftfinger");
            rightFinger = BodyIndex("rightfinger");
            chain = BuildChain(d.tcp.body);
            for (int j = 0; j < Dof; j++)
            {
                Home[j] = d.home[j];
                Q[j] = d.home[j];
                VMax[j] = d.velocity != null && j < d.velocity.Length ? d.velocity[j] : 2.0f;
            }
            if (colliders && ghost == null)
            {
                // Kinematic bodies on the moving links, so they push what they touch.
                for (int i = 0; i < n; i++)
                {
                    if (Bodies[i].GetComponentInChildren<Collider>() == null) continue;
                    var rb = Bodies[i].gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.interpolation = RigidbodyInterpolation.None;
                }
            }
            Apply();
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        int[] BuildChain(int tip)
        {
            var list = new List<int>();
            for (int b = tip; b >= 0; b = Def.bodies[b].parent) list.Add(b);
            list.Reverse();
            return list.ToArray();
        }

        Material PartMaterial(float[] rgba, Dictionary<string, Material> paint)
        {
            var c = new Color(rgba[0], rgba[1], rgba[2], 1);
            string key = ColorUtility.ToHtmlStringRGB(c);
            if (paint.TryGetValue(key, out var m)) return m;
            m = Mats.Clone("LitEmissive");
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            bool grey = Mathf.Abs(c.r - c.g) < 0.05f && Mathf.Abs(c.g - c.b) < 0.05f;
            // Franka's satin white shells, graphite joints, the anodised fingers; the coloured faces
            // on the wrist are its status lights.
            if (grey && lum > 0.85f) { m.SetColor("_BaseColor", new Color(0.93f, 0.94f, 0.95f)); m.SetFloat("_Smoothness", 0.62f); }
            else if (grey && lum > 0.4f) { m.SetColor("_BaseColor", new Color(0.62f, 0.64f, 0.66f)); m.SetFloat("_Smoothness", 0.58f); m.SetFloat("_Metallic", 0.55f); }
            else if (grey) { m.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.19f)); m.SetFloat("_Smoothness", 0.46f); }
            else { m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.7f); }
            m.SetColor("_EmissionColor", grey ? Color.black : c * 0.6f);
            m.name = "panda:" + key;
            paint[key] = m;
            return m;
        }

        void MakeCollider(Transform body, ColliderDef c, int bodyIndex)
        {
            Collider col;
            if (c.type == "capsule")
            {
                Vector3 a = V(c.a), b = V(c.b);
                var holder = new GameObject("collider");
                holder.layer = Layers.Robot;
                holder.transform.SetParent(body, false);
                holder.transform.localPosition = (a + b) / 2;
                Vector3 axis = b - a;
                holder.transform.localRotation = axis.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                var cap = holder.AddComponent<CapsuleCollider>();
                cap.direction = 1;
                cap.radius = c.r;
                cap.height = axis.magnitude + 2 * c.r;
                col = cap;
            }
            else
            {
                var box = body.gameObject.AddComponent<BoxCollider>();
                box.center = V(c.center);
                // The fingers' boxes are trimmed so closing on a part does not jitter against it.
                Vector3 size = V(c.size);
                if (Def.bodies[bodyIndex].name.Contains("finger")) size = Vector3.Scale(size, new Vector3(0.8f, 0.92f, 0.8f));
                box.size = size;
                col = box;
            }
            Colliders.Add(col);
            ContactColliders.Add((col, bodyIndex));
        }

        // ------------------------------------------------------------------ pose
        /// <summary>Sets the transforms from Q and Opening.</summary>
        public void Apply()
        {
            for (int j = 0; j < Dof; j++)
            {
                int b = JointBody[j];
                Bodies[b].localRotation = restRot[b] * Quaternion.AngleAxis(Def.bodies[b].joint.sign * Q[j] * Mathf.Rad2Deg, axes[b]);
            }
            float half = Mathf.Clamp(Opening, 0, MaxOpening) * 0.5f;
            Bodies[leftFinger].localPosition = restPos[leftFinger] + restRot[leftFinger] * (axes[leftFinger] * half);
            Bodies[rightFinger].localPosition = restPos[rightFinger] + restRot[rightFinger] * (axes[rightFinger] * -half);
        }

        public void CopyFrom(RobotArm other)
        {
            Array.Copy(other.Q, Q, Dof);
            Opening = other.Opening;
            Apply();
        }

        public void SetQ(float[] q)
        {
            for (int j = 0; j < Dof; j++) Q[j] = Mathf.Clamp(q[j], QMin[j], QMax[j]);
            Apply();
        }

        public Pose TcpPose => new Pose(Tcp.position, Tcp.rotation);

        public Vector3 JointPosition(int j) => Bodies[JointBody[j]].position;
        public Vector3 JointAxisWorld(int j) => Bodies[JointBody[j]].rotation * axes[JointBody[j]];
        public Transform JointTransform(int j) => Bodies[JointBody[j]];
        public float JointSign(int j) => Def.bodies[JointBody[j]].joint.sign;

        // ------------------------------------------------------------------ kinematics
        readonly Vector3[] fkPos = new Vector3[32];
        readonly Quaternion[] fkRot = new Quaternion[32];

        /// <summary>The TCP pose in world space for joint angles q (does not move the model).</summary>
        public Pose Forward(float[] q)
        {
            ForwardChain(q);
            int tip = chain[chain.Length - 1];
            return ToWorld(fkPos[tip], fkRot[tip]);
        }

        Pose ToWorld(Vector3 p, Quaternion r)
        {
            var t = Root.transform;
            return new Pose(t.TransformPoint(p), t.rotation * r);
        }

        void ForwardChain(float[] q)
        {
            Vector3 p = Vector3.zero;
            Quaternion r = Quaternion.identity;
            foreach (int b in chain)
            {
                p += r * restPos[b];
                r *= restRot[b];
                int j = jointOf[b];
                if (j >= 0) r *= Quaternion.AngleAxis(Def.bodies[b].joint.sign * q[j] * Mathf.Rad2Deg, axes[b]);
                fkPos[b] = p;
                fkRot[b] = r;
            }
        }

        readonly float[,] jac = new float[6, Dof];
        readonly float[] err = new float[6];
        readonly float[,] a6 = new float[6, 6];
        readonly float[] y6 = new float[6];
        readonly float[] dq = new float[Dof];
        readonly float[] z7 = new float[Dof];

        /// <summary>
        /// Damped least squares on position and orientation, with joint limits and a pull towards a
        /// preferred posture in the null space (the seventh joint's freedom keeps the elbow tidy).
        /// The answer goes into qOut; the result says how close it came.
        /// </summary>
        public IKResult Solve(Pose target, float[] seed, float[] qOut, int iterations = 24, float rotationWeight = 0.35f, float[] preferred = null, float nullGain = 0.05f, bool orientation = true)
        {
            Array.Copy(seed, qOut, Dof);
            var root = Root.transform;
            Vector3 tp = root.InverseTransformPoint(target.position);
            Quaternion tr = Quaternion.Inverse(root.rotation) * target.rotation;
            preferred ??= Home;
            var result = new IKResult();
            int tip = chain[chain.Length - 1];
            for (int it = 0; it < iterations; it++)
            {
                ForwardChain(qOut);
                Vector3 p = fkPos[tip];
                Quaternion r = fkRot[tip];
                Vector3 ep = tp - p;
                Vector3 er = Vector3.zero;
                if (orientation)
                {
                    var qe = tr * Quaternion.Inverse(r);
                    if (qe.w < 0) { qe.x = -qe.x; qe.y = -qe.y; qe.z = -qe.z; qe.w = -qe.w; }
                    qe.ToAngleAxis(out float angle, out Vector3 axis);
                    if (float.IsNaN(axis.x) || angle < 1e-4f) er = Vector3.zero;
                    else er = axis * (angle * Mathf.Deg2Rad);
                }
                result.positionError = ep.magnitude;
                result.rotationError = er.magnitude;
                result.iterations = it;
                if (result.positionError < 0.0004f && result.rotationError < 0.004f)
                {
                    result.converged = true;
                    break;
                }
                // Jacobian columns: d(tip)/dq = s * axis x (tip - joint); d(rotation)/dq = s * axis.
                for (int j = 0; j < Dof; j++)
                {
                    int b = JointBody[j];
                    float s = Def.bodies[b].joint.sign;
                    Vector3 z = fkRot[b] * axes[b];
                    Vector3 lin = Vector3.Cross(z, p - fkPos[b]) * s;
                    Vector3 ang = orientation ? z * (s * rotationWeight) : Vector3.zero;
                    jac[0, j] = lin.x; jac[1, j] = lin.y; jac[2, j] = lin.z;
                    jac[3, j] = ang.x; jac[4, j] = ang.y; jac[5, j] = ang.z;
                }
                // Clamp the task step so a far target is approached in stages.
                float maxStep = 0.08f;
                if (ep.magnitude > maxStep) ep = ep.normalized * maxStep;
                if (er.magnitude > 0.35f) er = er.normalized * 0.35f;
                err[0] = ep.x; err[1] = ep.y; err[2] = ep.z;
                err[3] = er.x * rotationWeight; err[4] = er.y * rotationWeight; err[5] = er.z * rotationWeight;
                // A = J J^T + lambda^2 I; damping grows near singular poses.
                float lambda = 0.02f;
                for (int r1 = 0; r1 < 6; r1++)
                for (int c1 = 0; c1 < 6; c1++)
                {
                    float sum = 0;
                    for (int k = 0; k < Dof; k++) sum += jac[r1, k] * jac[c1, k];
                    a6[r1, c1] = sum;
                }
                result.manipulability = Manipulability(a6);
                float w0 = 0.004f;
                if (result.manipulability < w0) lambda += 0.1f * (1 - result.manipulability / w0);
                for (int r1 = 0; r1 < 6; r1++) a6[r1, r1] += lambda * lambda;
                Array.Copy(err, y6, 6);
                if (!Cholesky6(a6, y6)) break;
                for (int k = 0; k < Dof; k++)
                {
                    float sum = 0;
                    for (int r1 = 0; r1 < 6; r1++) sum += jac[r1, k] * y6[r1];
                    dq[k] = sum;
                }
                // Null space: towards the preferred posture and away from the limits.
                for (int k = 0; k < Dof; k++)
                {
                    float range = QMax[k] - QMin[k];
                    float mid = (QMax[k] + QMin[k]) * 0.5f;
                    float pull = nullGain * (preferred[k] - qOut[k]);
                    float edge = (qOut[k] - mid) / (range * 0.5f);
                    if (Mathf.Abs(edge) > 0.85f) pull -= 0.15f * Mathf.Sign(edge) * (Mathf.Abs(edge) - 0.85f);
                    z7[k] = pull;
                }
                // n = z - J^T (A^-1 (J z))
                for (int r1 = 0; r1 < 6; r1++)
                {
                    float sum = 0;
                    for (int k = 0; k < Dof; k++) sum += jac[r1, k] * z7[k];
                    y6[r1] = sum;
                }
                // a6 now holds the Cholesky factor; solve again with it.
                CholeskySolve(a6, y6);
                for (int k = 0; k < Dof; k++)
                {
                    float sum = 0;
                    for (int r1 = 0; r1 < 6; r1++) sum += jac[r1, k] * y6[r1];
                    dq[k] += z7[k] - sum;
                }
                float big = 0;
                for (int k = 0; k < Dof; k++) big = Mathf.Max(big, Mathf.Abs(dq[k]));
                float scale = big > 0.25f ? 0.25f / big : 1f;
                for (int k = 0; k < Dof; k++) qOut[k] = Mathf.Clamp(qOut[k] + dq[k] * scale, QMin[k], QMax[k]);
            }
            return result;
        }

        /// <summary>
        /// How far the pose at <paramref name="q"/> is from a singular one: sqrt(det(J Jᵀ)), with the
        /// rotation rows weighted as the solver weights them. Near zero the hand cannot move in some direction.
        /// With <paramref name="positionOnly"/>, only the hand's travel counts (a stretched arm drives it to zero).
        /// </summary>
        public float ManipulabilityAt(float[] q, float rotationWeight = 0.35f, bool positionOnly = false)
        {
            ForwardChain(q);
            int tip = chain[chain.Length - 1];
            Vector3 p = fkPos[tip];
            for (int j = 0; j < Dof; j++)
            {
                int b = JointBody[j];
                float s = Def.bodies[b].joint.sign;
                Vector3 z = fkRot[b] * axes[b];
                Vector3 lin = Vector3.Cross(z, p - fkPos[b]) * s;
                Vector3 ang = z * (s * rotationWeight);
                jac[0, j] = lin.x; jac[1, j] = lin.y; jac[2, j] = lin.z;
                jac[3, j] = ang.x; jac[4, j] = ang.y; jac[5, j] = ang.z;
            }
            for (int r1 = 0; r1 < 6; r1++)
            for (int c1 = 0; c1 < 6; c1++)
            {
                float sum = 0;
                for (int k = 0; k < Dof; k++) sum += jac[r1, k] * jac[c1, k];
                a6[r1, c1] = sum;
            }
            if (positionOnly)
            {
                double m00 = a6[0, 0], m01 = a6[0, 1], m02 = a6[0, 2], m11 = a6[1, 1], m12 = a6[1, 2], m22 = a6[2, 2];
                double det = m00 * (m11 * m22 - m12 * m12) - m01 * (m01 * m22 - m12 * m02) + m02 * (m01 * m12 - m11 * m02);
                return (float)Math.Sqrt(Math.Max(det, 0));
            }
            return Manipulability(a6);
        }

        static float Manipulability(float[,] jjt)
        {
            // sqrt(det(J J^T)) through a Cholesky factorisation of a copy.
            var m = new float[6, 6];
            Array.Copy(jjt, m, 36);
            double det = 1;
            for (int i = 0; i < 6; i++)
            {
                double s = m[i, i];
                for (int k = 0; k < i; k++) s -= m[i, k] * m[i, k];
                if (s <= 1e-14) return 0;
                double l = Math.Sqrt(s);
                m[i, i] = (float)l;
                det *= s;
                for (int r = i + 1; r < 6; r++)
                {
                    double t = m[r, i];
                    for (int k = 0; k < i; k++) t -= m[r, k] * m[i, k];
                    m[r, i] = (float)(t / l);
                }
            }
            return (float)Math.Sqrt(det);
        }

        /// <summary>Factors a (in place, lower triangle) and solves a x = b (b becomes x).</summary>
        static bool Cholesky6(float[,] a, float[] b)
        {
            for (int i = 0; i < 6; i++)
            {
                float s = a[i, i];
                for (int k = 0; k < i; k++) s -= a[i, k] * a[i, k];
                if (s <= 1e-12f) return false;
                float l = Mathf.Sqrt(s);
                a[i, i] = l;
                for (int r = i + 1; r < 6; r++)
                {
                    float t = a[r, i];
                    for (int k = 0; k < i; k++) t -= a[r, k] * a[i, k];
                    a[r, i] = t / l;
                }
            }
            CholeskySolve(a, b);
            return true;
        }

        static void CholeskySolve(float[,] l, float[] b)
        {
            for (int i = 0; i < 6; i++)
            {
                float s = b[i];
                for (int k = 0; k < i; k++) s -= l[i, k] * b[k];
                b[i] = s / l[i, i];
            }
            for (int i = 5; i >= 0; i--)
            {
                float s = b[i];
                for (int k = i + 1; k < 6; k++) s -= l[k, i] * b[k];
                b[i] = s / l[i, i];
            }
        }

        // ------------------------------------------------------------------ contact checks
        struct Shape { public int body; public bool capsule; public Vector3 a, b, center, size; public float r; }
        List<Shape> shapes;
        Vector3[] allPos;
        Quaternion[] allRot;
        static readonly Collider[] overlap = new Collider[16];

        /// <summary>World poses of every body for joint angles q and a finger opening.</summary>
        void ForwardAll(float[] q, float opening)
        {
            var d = Def;
            int n = d.bodies.Length;
            allPos ??= new Vector3[n];
            allRot ??= new Quaternion[n];
            var root = Root.transform;
            for (int i = 0; i < n; i++)
            {
                int parent = d.bodies[i].parent;
                Vector3 pp = parent >= 0 ? allPos[parent] : root.position;
                Quaternion pr = parent >= 0 ? allRot[parent] : root.rotation;
                Vector3 local = restPos[i];
                if (i == leftFinger) local += restRot[i] * (axes[i] * (opening * 0.5f));
                if (i == rightFinger) local += restRot[i] * (axes[i] * (-opening * 0.5f));
                Quaternion r = pr * restRot[i];
                int j = jointOf[i];
                if (j >= 0) r *= Quaternion.AngleAxis(d.bodies[i].joint.sign * q[j] * Mathf.Rad2Deg, axes[i]);
                allPos[i] = pp + pr * local;
                allRot[i] = r;
            }
        }

        void CollectShapes()
        {
            shapes = new List<Shape>();
            var d = Def;
            for (int i = 0; i < d.bodies.Length; i++)
            {
                foreach (var c in d.bodies[i].colliders ?? Array.Empty<ColliderDef>())
                {
                    if (c.type == "capsule") shapes.Add(new Shape { body = i, capsule = true, a = V(c.a), b = V(c.b), r = c.r });
                    else shapes.Add(new Shape { body = i, capsule = false, center = V(c.center), size = V(c.size) });
                }
            }
        }

        /// <summary>
        /// Whether the arm at q would overlap anything on the given layers; the first body it would
        /// hit is named. Shapes shrink by margin so a light touch is not a crash. The base links are
        /// left out (they stand on their pedestal), and so are bodies listed in ignore.
        /// </summary>
        /// <summary>The arm's own body that touched something in the last <see cref="Collides"/> that found a hit.</summary>
        public string LastHitLink = "";

        public bool Collides(float[] q, float opening, int mask, out string what, float margin = 0.004f, Collider[] ignore = null)
        {
            what = null;
            if (shapes == null) CollectShapes();
            ForwardAll(q, opening);
            foreach (var s in shapes)
            {
                string name = Def.bodies[s.body].name;
                if (name == "link0" || name == "link1" || name == "base") continue;
                int hits;
                if (s.capsule)
                {
                    Vector3 a = allPos[s.body] + allRot[s.body] * s.a;
                    Vector3 b = allPos[s.body] + allRot[s.body] * s.b;
                    hits = Physics.OverlapCapsuleNonAlloc(a, b, Mathf.Max(0.002f, s.r - margin), overlap, mask, QueryTriggerInteraction.Ignore);
                }
                else
                {
                    Vector3 c = allPos[s.body] + allRot[s.body] * s.center;
                    Vector3 half = Vector3.Max(s.size * 0.5f - Vector3.one * margin, Vector3.one * 0.002f);
                    hits = Physics.OverlapBoxNonAlloc(c, half, overlap, allRot[s.body], mask, QueryTriggerInteraction.Ignore);
                }
                for (int h = 0; h < hits; h++)
                {
                    var col = overlap[h];
                    if (col == null || Colliders.Contains(col)) continue;
                    if (ignore != null && Array.IndexOf(ignore, col) >= 0) continue;
                    what = col.name;
                    LastHitLink = name;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The closest distance from a point to the arm's links (for separation monitoring).</summary>
        public float DistanceTo(Vector3 point, out Vector3 closest)
        {
            if (shapes == null) CollectShapes();
            ForwardAll(Q, Opening);
            float best = float.MaxValue;
            closest = Root.transform.position;
            foreach (var s in shapes)
            {
                Vector3 c;
                float d;
                if (s.capsule)
                {
                    Vector3 a = allPos[s.body] + allRot[s.body] * s.a;
                    Vector3 b = allPos[s.body] + allRot[s.body] * s.b;
                    Vector3 ab = b - a;
                    float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
                    c = a + ab * t;
                    d = Vector3.Distance(point, c) - s.r;
                }
                else
                {
                    c = allPos[s.body] + allRot[s.body] * s.center;
                    d = Vector3.Distance(point, c) - s.size.magnitude * 0.35f;
                }
                if (d < best)
                {
                    best = d;
                    closest = c;
                }
            }
            return Mathf.Max(0, best);
        }

        /// <summary>Gravity torque on each joint [N m], from the model's link masses (simulated).</summary>
        public void GravityTorques(float[] tau)
        {
            var d = Def;
            for (int j = 0; j < Dof; j++)
            {
                int b = JointBody[j];
                Vector3 o = Bodies[b].position;
                Vector3 axis = JointAxisWorld(j);
                Vector3 moment = Vector3.zero;
                for (int i = b; i < d.bodies.Length; i++)
                {
                    if (!IsDescendant(i, b)) continue;
                    var body = d.bodies[i];
                    if (body.mass <= 0) continue;
                    Vector3 com = Bodies[i].TransformPoint(body.com != null && body.com.Length == 3 ? V(body.com) : Vector3.zero);
                    moment += Vector3.Cross(com - o, Vector3.down * (9.81f * body.mass));
                }
                tau[j] = -Vector3.Dot(moment, axis) * JointSign(j);
            }
        }

        bool IsDescendant(int body, int ancestor)
        {
            for (int b = body; b >= 0; b = Def.bodies[b].parent) if (b == ancestor) return true;
            return false;
        }

        // ------------------------------------------------------------------ look
        /// <summary>Lights a link (and optionally everything below it) with an emissive tint.</summary>
        public void Tint(int body, Color emission, bool descendants = false)
        {
            for (int i = 0; i < Bodies.Length; i++)
            {
                if (i != body && !(descendants && IsDescendant(i, body))) continue;
                foreach (var r in LinkRenderers[i])
                {
                    r.GetPropertyBlock(block);
                    block.SetColor(EmissionColor, emission);
                    r.SetPropertyBlock(block);
                }
            }
        }

        public void ClearTint()
        {
            for (int i = 0; i < Bodies.Length; i++)
                foreach (var r in LinkRenderers[i]) r.SetPropertyBlock(null);
        }

        public void SetVisible(bool visible)
        {
            foreach (var list in LinkRenderers) foreach (var r in list) r.enabled = visible;
        }

        public IEnumerable<Renderer> AllRenderers()
        {
            foreach (var list in LinkRenderers) foreach (var r in list) yield return r;
        }

        public static string JointLabel(int j) => j switch
        {
            0 => "J1 base",
            1 => "J2 shoulder",
            2 => "J3 upper arm",
            3 => "J4 elbow",
            4 => "J5 forearm",
            5 => "J6 wrist",
            _ => "J7 flange",
        };
    }
}
