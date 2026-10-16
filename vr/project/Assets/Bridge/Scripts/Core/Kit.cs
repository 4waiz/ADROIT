// SPDX-License-Identifier: LicenseRef-BRIDGE
// BRIDGE VR's building blocks: layers, colours, easing, the web frame, the exported assets, the
// material factory and the procedural meshes the factory is made of.
//
// The web app's factory is z-up and right-handed; Unity is y-up and left-handed. Web.P maps a point
// (x, y, z) to (x, z, y); a rotation by a about an axis maps to a rotation by -a about the mapped
// axis (Web.Q). That lets the hall, the cell and the lab be ported from frontend/src/viewport/factory
// line by line, in the numbers they were written in.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bridge
{
    public static class Layers
    {
        public const int Default = 0;
        public const int Environment = 6;
        public const int Robot = 7;
        public const int Grabbable = 8;
        public const int UI = 9;
        public const int Hands = 10;
        public const int Teleport = 11;

        public const int EnvironmentMask = 1 << Environment;
        public const int RobotMask = 1 << Robot;
        public const int GrabbableMask = 1 << Grabbable;
        public const int UIMask = 1 << UI;
        public const int TeleportMask = (1 << Teleport) | (1 << Environment) | (1 << Default);
    }

    /// <summary>Colours: the plant's paint (RAL where there is one, from kit.ts) and the UI's.</summary>
    public static class Pal
    {
        public static readonly Color SafetyYellow = Hex(0xf2b705);
        public static readonly Color LightGrey = Hex(0xc5c7c4);
        public static readonly Color Anthracite = Hex(0x383e42);
        public static readonly Color SignalBlue = Hex(0x1e5aa8);
        public static readonly Color RackOrange = Hex(0xe25303);
        public static readonly Color White = Hex(0xeeeeea);
        public static readonly Color Black = Hex(0x141518);
        public static readonly Color Green = Hex(0x2f7d4b);
        public static readonly Color SteelGrey = Hex(0x8d9399);

        // The UI: BRIDGE orange on white glass, graphite ink.
        public static readonly Color Brand = Hex(0xff4713);
        public static readonly Color BrandSoft = Hex(0xff7a4d);
        public static readonly Color Ink = Hex(0x111820);
        public static readonly Color InkSoft = Hex(0x4a5562);
        public static readonly Color InkFaint = Hex(0x7c8794);
        public static readonly Color Glass = new Color(0.97f, 0.975f, 0.985f, 0.72f);
        public static readonly Color GlassDark = new Color(0.07f, 0.09f, 0.11f, 0.78f);
        public static readonly Color Ok = Hex(0x1fa463);
        public static readonly Color OkGlow = Hex(0x3ddc84);
        public static readonly Color Warn = Hex(0xe8a200);
        public static readonly Color Err = Hex(0xe5383b);
        public static readonly Color Info = Hex(0x2f7cf6);
        public static readonly Color Twin = new Color(0.3f, 0.78f, 1.0f);
        public static readonly Color AxisX = Hex(0xff4d4f);
        public static readonly Color AxisY = Hex(0x3ddc84);
        public static readonly Color AxisZ = Hex(0x4b9dff);

        public static Color Hex(uint rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        public static Color WithA(this Color c, float a) => new Color(c.r, c.g, c.b, a);
        public static string Rgb(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    public static class Ease
    {
        public static float Smooth(float t) => t <= 0 ? 0 : t >= 1 ? 1 : 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
        public static float OutCubic(float t) { t = Mathf.Clamp01(t); return 1 - Mathf.Pow(1 - t, 3); }
        public static float InOutCubic(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; }
        public static float OutBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2); }
        /// <summary>Frame-rate independent approach: moves a toward b with a time constant.</summary>
        public static float Damp(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));
        public static Vector3 Damp(Vector3 a, Vector3 b, float lambda, float dt) => Vector3.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));
        public static Quaternion Damp(Quaternion a, Quaternion b, float lambda, float dt) => Quaternion.Slerp(a, b, 1 - Mathf.Exp(-lambda * dt));
    }

    /// <summary>The web app's z-up, right-handed frame.</summary>
    public static class Web
    {
        public static Vector3 P(float x, float y, float z) => new Vector3(x, z, y);
        public static Vector3 P(Vector3 webPoint) => new Vector3(webPoint.x, webPoint.z, webPoint.y);
        /// <summary>Sizes along the web's x, y, z, as Unity scale.</summary>
        public static Vector3 S(float x, float y, float z) => new Vector3(x, z, y);

        /// <summary>three.js Euler (rx, ry, rz) in order "ZYX" (kit.ts's compose), mapped to Unity.</summary>
        public static Quaternion Q(float rx, float ry, float rz)
        {
            // q = qz * qy * qx, in the web frame, as (w, x, y, z).
            var qx = Axis(1, 0, 0, rx);
            var qy = Axis(0, 1, 0, ry);
            var qz = Axis(0, 0, 1, rz);
            var q = Mul(Mul(qz, qy), qx);
            return FromWeb(q.w, q.x, q.y, q.z);
        }

        /// <summary>Yaw about the web's +z (up), as Unity rotation about +y.</summary>
        public static Quaternion Yaw(float rz) => Quaternion.Euler(0, -rz * Mathf.Rad2Deg, 0);

        /// <summary>A web-frame quaternion (w, x, y, z) as Unity's.</summary>
        public static Quaternion FromWeb(float w, float x, float y, float z) => new Quaternion(-x, -z, -y, w);

        struct Q4 { public float w, x, y, z; }

        static Q4 Axis(float ax, float ay, float az, float angle)
        {
            float s = Mathf.Sin(angle / 2);
            return new Q4 { w = Mathf.Cos(angle / 2), x = ax * s, y = ay * s, z = az * s };
        }

        static Q4 Mul(Q4 a, Q4 b) => new Q4
        {
            w = a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
            x = a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            y = a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            z = a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
        };

        /// <summary>Rotation by angle about a web axis, applied in Unity (the web's x, y, z joint axes).</summary>
        public static Quaternion About(char webAxis, float angle)
        {
            Vector3 axis = webAxis == 'x' ? Vector3.right : webAxis == 'y' ? Vector3.forward : Vector3.up;
            return Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, axis);
        }
    }

    // ---------------------------------------------------------------------- exported assets
    [Serializable] public class IconIndex { public int cell; public string[] names; public float[] uv; }

    /// <summary>What vr/tools/export_assets.py wrote under Resources/Gen, loaded once and cached.</summary>
    public static class Gen
    {
        static readonly Dictionary<string, Mesh> meshes = new();
        static readonly Dictionary<string, Texture2D> textures = new();
        static readonly Dictionary<string, Material> templates = new();
        static readonly Dictionary<string, TMP_FontAsset> fonts = new();
        static Dictionary<string, Rect> icons;
        static Texture2D iconAtlas;

        public static Mesh Mesh(string name)
        {
            if (meshes.TryGetValue(name, out var m)) return m;
            var asset = Resources.Load<TextAsset>("Gen/Meshes/" + name);
            if (asset == null) throw new Exception("missing mesh " + name);
            m = ParseMesh(asset.bytes, name);
            Resources.UnloadAsset(asset);
            meshes[name] = m;
            return m;
        }

        public static Texture2D Tex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (textures.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("Gen/Textures/" + name) ?? Resources.Load<Texture2D>("Gen/UI/" + name);
            if (t == null) Debug.LogWarning("[bridge] missing texture " + name);
            textures[name] = t;
            return t;
        }

        public static Texture HDRI => Resources.Load<Texture>("Gen/Textures/factory_hdri");

        public static Material Template(string name)
        {
            if (templates.TryGetValue(name, out var m)) return m;
            m = Resources.Load<Material>("Gen/Materials/" + name);
            if (m == null) throw new Exception("missing material template " + name);
            templates[name] = m;
            return m;
        }

        public static TMP_FontAsset Font(string name)
        {
            if (fonts.TryGetValue(name, out var f)) return f;
            f = Resources.Load<TMP_FontAsset>("Gen/Fonts/" + name + " SDF");
            if (f == null) Debug.LogWarning("[bridge] missing font " + name);
            fonts[name] = f;
            return f;
        }

        public static T Json<T>(string path)
        {
            var asset = Resources.Load<TextAsset>("Gen/" + path);
            if (asset == null) throw new Exception("missing " + path);
            return JsonUtility.FromJson<T>(asset.text);
        }

        public static Texture2D IconAtlas
        {
            get
            {
                if (iconAtlas == null) LoadIcons();
                return iconAtlas;
            }
        }

        public static Rect Icon(string name)
        {
            if (icons == null) LoadIcons();
            if (icons.TryGetValue(name, out var r)) return r;
            Debug.LogWarning("[bridge] no icon " + name);
            return icons.TryGetValue("circle", out r) ? r : new Rect(0, 0, 0.01f, 0.01f);
        }

        static void LoadIcons()
        {
            icons = new Dictionary<string, Rect>();
            iconAtlas = Resources.Load<Texture2D>("Gen/UI/icons");
            var index = Json<IconIndex>("UI/icons");
            for (int i = 0; i < index.names.Length; i++)
                icons[index.names[i]] = new Rect(index.uv[i * 4], index.uv[i * 4 + 1], index.uv[i * 4 + 2], index.uv[i * 4 + 3]);
        }

        /// <summary>Reads BMSH (see export_assets.write_mesh): positions, normals, [uvs], indices.</summary>
        public static Mesh ParseMesh(byte[] bytes, string name)
        {
            if (bytes.Length < 20 || bytes[0] != 'B' || bytes[1] != 'M' || bytes[2] != 'S' || bytes[3] != 'H')
                throw new Exception("not a BMSH mesh: " + name);
            int vertices = BitConverter.ToInt32(bytes, 8);
            int indices = BitConverter.ToInt32(bytes, 12);
            int flags = BitConverter.ToInt32(bytes, 16);
            var span = new ReadOnlySpan<byte>(bytes);
            int at = 20;
            var positions = MemoryMarshal.Cast<byte, Vector3>(span.Slice(at, vertices * 12)).ToArray();
            at += vertices * 12;
            var normals = MemoryMarshal.Cast<byte, Vector3>(span.Slice(at, vertices * 12)).ToArray();
            at += vertices * 12;
            Vector2[] uvs = null;
            if ((flags & 2) != 0)
            {
                uvs = MemoryMarshal.Cast<byte, Vector2>(span.Slice(at, vertices * 8)).ToArray();
                at += vertices * 8;
            }
            var tris = MemoryMarshal.Cast<byte, int>(span.Slice(at, indices * 4)).ToArray();
            var mesh = new Mesh { name = name, indexFormat = vertices > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (uvs != null) mesh.RecalculateTangents();
            return mesh;
        }
    }

    // ---------------------------------------------------------------------- materials
    /// <summary>
    /// Materials cloned from the templates the editor setup saved (so their shader variants are in the
    /// build), memoised by what they are, as kit.ts's Materials class is.
    /// </summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> cache = new();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        static Material Memo(string key, Func<Material> make)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            m = make();
            m.name = key;
            cache[key] = m;
            return m;
        }

        public static Material Clone(string template) => new Material(Gen.Template(template));

        /// <summary>three.js MeshStandardMaterial(color, roughness, metalness) as URP Lit.</summary>
        public static Material Paint(Color color, float roughness = 0.42f, float metalness = 0.15f) =>
            Memo($"paint:{color}:{roughness}:{metalness}", () =>
            {
                var m = Clone("Lit");
                m.SetColor(BaseColor, color);
                m.SetFloat(Smoothness, Mathf.Clamp01(1f - roughness) * 0.92f);
                m.SetFloat(Metallic, metalness);
                return m;
            });

        public static Material Paint(uint hex, float roughness = 0.42f, float metalness = 0.15f) => Paint(Pal.Hex(hex), roughness, metalness);

        public static Material Steel(uint hex = 0x8d9399, float roughness = 0.38f) => Paint(Pal.Hex(hex), roughness, 0.85f);

        public static Material Rubber() => Paint(0x18191b, 0.85f, 0f);

        public static Material Glass(Color tint, float opacity) =>
            Memo($"glass:{tint}:{opacity}", () =>
            {
                var m = Clone("LitTransparent");
                m.SetColor(BaseColor, tint.WithA(opacity));
                m.SetFloat(Smoothness, 0.94f);
                m.SetFloat(Metallic, 0f);
                return m;
            });

        /// <summary>Emitted light, unlit; values above 1 bloom (kit.ts's glow).</summary>
        public static Material Glow(Color color, float strength) =>
            Memo($"glow:{color}:{strength}", () =>
            {
                var m = Clone("Unlit");
                m.SetColor(BaseColor, color * strength * 0.55f);
                return m;
            });

        public static Material Glow(uint hex, float strength) => Glow(Pal.Hex(hex), strength);

        /// <summary>Painted steel with the machine-panel photograph's wear (normal and roughness).</summary>
        public static Material Panel(Color color, float repeat = 1f) =>
            Memo($"panel:{color}:{repeat}", () => Mapped(color, null, "panel_normal", "panel_mg", new Vector2(repeat, repeat), 0.6f));

        public static Material Panel(uint hex, float repeat = 1f) => Panel(Pal.Hex(hex), repeat);

        /// <summary>Box-profile wall cladding, light grey, tiled every 2 m.</summary>
        public static Material Cladding(float width, float height, uint hex = 0xdfe3e6) =>
            Memo($"cladding:{width}:{height}:{hex}", () => Mapped(Pal.Hex(hex), null, "cladding_normal", "cladding_mg", new Vector2(width / 2f, height / 2f), 1.2f));

        /// <summary>Painted concrete (the floor photograph tinted), tiled every 4 m.</summary>
        public static Material Concrete(float width, float height, uint hex) =>
            Memo($"concrete:{width}:{height}:{hex}", () => Mapped(Pal.Hex(hex), "floor_color", "floor_normal", "floor_mg", new Vector2(width / 4f, height / 4f), 1f));

        /// <summary>The epoxy floor: the photograph's maps at 4 m a tile.</summary>
        public static Material Floor(float width, float depth, uint tint) =>
            Memo($"floor:{width}:{depth}:{tint}", () => Mapped(Pal.Hex(tint), "floor_color", "floor_normal", "floor_mg", new Vector2(width / 4f, depth / 4f), 0.35f));

        public static Material Mapped(Color color, string baseMap, string normal, string mg, Vector2 tiling, float normalScale)
        {
            var m = Clone(mg != null ? "LitMapped" : normal != null ? "LitNormal" : "Lit");
            m.SetColor(BaseColor, color);
            if (baseMap != null) m.SetTexture(BaseMap, Gen.Tex(baseMap));
            if (normal != null)
            {
                m.SetTexture(BumpMap, Gen.Tex(normal));
                m.SetFloat(BumpScale, normalScale);
            }
            if (mg != null)
            {
                m.SetTexture(MetallicGlossMap, Gen.Tex(mg));
                m.SetFloat(Smoothness, 1f);
            }
            m.SetTextureScale(BaseMap, tiling);
            return m;
        }

        /// <summary>A texture on a lit, transparent decal or sign.</summary>
        public static Material Decal(string texture, float opacity = 0.92f, bool lit = true) =>
            Memo($"decal:{texture}:{opacity}:{lit}", () =>
            {
                Material m;
                if (lit)
                {
                    m = Clone("LitTransparent");
                    m.SetFloat(Smoothness, 0.45f);
                }
                else m = Clone("UnlitTransparent");
                m.SetTexture(BaseMap, Gen.Tex(texture));
                m.SetColor(BaseColor, new Color(1, 1, 1, opacity));
                m.renderQueue = (int)RenderQueue.Transparent - 20;
                return m;
            });

        /// <summary>An opaque, lit texture (signs, cartons, hazard stripes) with a repeat.</summary>
        public static Material Textured(string texture, Vector2 repeat, float roughness = 0.55f, float metalness = 0.1f) =>
            Memo($"tex:{texture}:{repeat}:{roughness}:{metalness}", () =>
            {
                var m = Clone("Lit");
                m.SetTexture(BaseMap, Gen.Tex(texture));
                m.SetTextureScale(BaseMap, repeat);
                m.SetColor(BaseColor, Color.white);
                m.SetFloat(Smoothness, 1f - roughness);
                m.SetFloat(Metallic, metalness);
                return m;
            });

        public static Material Unlit(Color color) =>
            Memo($"unlit:{color}", () =>
            {
                var m = Clone("Unlit");
                m.SetColor(BaseColor, color);
                return m;
            });

        public static Material UnlitTex(string texture, Color color) =>
            Memo($"unlittex:{texture}:{color}", () =>
            {
                var m = Clone("Unlit");
                m.SetTexture(BaseMap, Gen.Tex(texture));
                m.SetColor(BaseColor, color);
                return m;
            });

        public static Material Transparent(Color color, bool onTop = false) =>
            Memo($"transparent:{color}:{onTop}", () =>
            {
                var m = Clone("UnlitTransparent");
                m.SetColor(BaseColor, color);
                if (onTop) m.SetFloat("_ZTest", (float)CompareFunction.Always);
                m.SetFloat("_Fog", 0f);
                return m;
            });

        public static Material Additive(Color color) =>
            Memo($"additive:{color}", () =>
            {
                var m = Clone("UnlitAdditive");
                m.SetColor(BaseColor, color);
                return m;
            });

        public static Material Hologram(Color color, float rim = 1.4f, float body = 0.12f) =>
            Memo($"holo:{color}:{rim}:{body}", () =>
            {
                var m = Clone("Hologram");
                m.SetColor("_Color", color);
                m.SetFloat("_Rim", rim);
                m.SetFloat("_Base", body);
                return m;
            });

        /// <summary>A material for one exported prop part (see export_prop).</summary>
        public static Material Prop(PropMaterial pm)
        {
            string key = "prop:" + pm.name + pm.baseMap;
            return Memo(key, () =>
            {
                bool mapped = !string.IsNullOrEmpty(pm.metallicGlossMap) || !string.IsNullOrEmpty(pm.normalMap);
                var m = Clone(pm.transparent ? (mapped ? "LitMappedTransparent" : "LitTransparent") : (mapped ? "LitMapped" : "Lit"));
                m.SetColor(BaseColor, pm.color != null && pm.color.Length == 4 ? new Color(pm.color[0], pm.color[1], pm.color[2], pm.color[3]) : Color.white);
                if (!string.IsNullOrEmpty(pm.baseMap)) m.SetTexture(BaseMap, Gen.Tex(pm.baseMap));
                if (!string.IsNullOrEmpty(pm.normalMap)) m.SetTexture(BumpMap, Gen.Tex(pm.normalMap));
                if (!string.IsNullOrEmpty(pm.metallicGlossMap))
                {
                    m.SetTexture(MetallicGlossMap, Gen.Tex(pm.metallicGlossMap));
                    m.SetFloat(Smoothness, 1f);
                }
                else
                {
                    m.SetFloat(Smoothness, 1f - pm.roughness);
                    m.SetFloat(Metallic, pm.metallic);
                }
                return m;
            });
        }

        public static void SetEmission(Material m, Color c)
        {
            m.SetColor(EmissionColor, c);
        }
    }

    [Serializable]
    public class PropMaterial
    {
        public string name;
        public float[] color;
        public float metallic = 1;
        public float roughness = 1;
        public bool transparent;
        public string baseMap;
        public string metallicGlossMap;
        public string normalMap;
    }

    [Serializable] public class PropPart { public string mesh; public PropMaterial material; }
    [Serializable] public class PropDef { public string name; public float[] size; public PropPart[] parts; }

    // ---------------------------------------------------------------------- meshes
    /// <summary>Procedural meshes, cached by shape: boxes, rounded boxes, cylinders, quads, rings.</summary>
    public static class MeshKit
    {
        static readonly Dictionary<string, Mesh> cache = new();

        static Mesh Memo(string key, Func<Mesh> make)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            m = make();
            m.name = key;
            cache[key] = m;
            return m;
        }

        /// <summary>A 1 m cube centred on the origin, uv 0..1 on each face.</summary>
        public static Mesh Box() => Memo("box", () => RoundedBox(new Vector3(1, 1, 1), 0f, 1));

        /// <summary>
        /// A box of the given size (Unity axes) whose edges are rounded by radius, built from a
        /// subdivided cube pushed out from an inner box, so normals are smooth round the corners.
        /// </summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int segments = 3)
        {
            string key = $"rbox:{size.x:F3}:{size.y:F3}:{size.z:F3}:{radius:F3}:{segments}";
            return Memo(key, () =>
            {
                Vector3 h = size * 0.5f;
                float r = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)));
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                float[] Axis(float half)
                {
                    var list = new List<float>();
                    if (r <= 1e-5f || segments <= 0)
                    {
                        list.Add(-half);
                        list.Add(half);
                        return list.ToArray();
                    }
                    for (int k = 0; k <= segments; k++) list.Add(-half + r * (1 - Mathf.Cos(k / (float)segments * Mathf.PI / 2)));
                    for (int k = segments; k >= 0; k--)
                    {
                        float v = half - r * (1 - Mathf.Cos(k / (float)segments * Mathf.PI / 2));
                        if (v > list[list.Count - 1] + 1e-6f) list.Add(v);
                    }
                    return list.ToArray();
                }
                float[] ax = Axis(h.x), ay = Axis(h.y), az = Axis(h.z);
                // Six faces: normal axis, u axis, v axis.
                void Face(Vector3 n, Vector3 u, Vector3 v, float[] us, float[] vs, float nOff)
                {
                    int start = verts.Count;
                    for (int j = 0; j < vs.Length; j++)
                    for (int i = 0; i < us.Length; i++)
                    {
                        Vector3 p = n * nOff + u * us[i] + v * vs[j];
                        Vector3 inner = new Vector3(Mathf.Clamp(p.x, -h.x + r, h.x - r), Mathf.Clamp(p.y, -h.y + r, h.y - r), Mathf.Clamp(p.z, -h.z + r, h.z - r));
                        Vector3 d = p - inner;
                        Vector3 nn = d.sqrMagnitude > 1e-12f ? d.normalized : n;
                        verts.Add(r > 1e-5f ? inner + nn * r : p);
                        norms.Add(r > 1e-5f ? nn : n);
                        float uu = (us[i] - us[0]) / (us[us.Length - 1] - us[0]);
                        float vv = (vs[j] - vs[0]) / (vs[vs.Length - 1] - vs[0]);
                        uvs.Add(new Vector2(uu, vv));
                    }
                    int w = us.Length;
                    for (int j = 0; j < vs.Length - 1; j++)
                    for (int i = 0; i < us.Length - 1; i++)
                    {
                        int a = start + j * w + i, b = a + 1, c = a + w, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                }
                // Unity draws a triangle (a, c, b) from the side Cross(c - a, b - a) points to, which
                // here is Cross(v, u): each face's u and v are picked so that is its outward normal,
                // and so u runs to the right as you look at the face.
                Face(Vector3.right, Vector3.forward, Vector3.up, az, ay, h.x);
                Face(Vector3.left, Vector3.back, Vector3.up, az, ay, h.x);
                Face(Vector3.up, Vector3.right, Vector3.forward, ax, az, h.y);
                Face(Vector3.down, Vector3.right, Vector3.back, ax, az, h.y);
                Face(Vector3.forward, Vector3.left, Vector3.up, ax, ay, h.z);
                Face(Vector3.back, Vector3.right, Vector3.up, ax, ay, h.z);
                return Build(verts, norms, uvs, tris);
            });
        }

        /// <summary>A cylinder along +y, centred on the origin, optionally tapered and capped.</summary>
        public static Mesh Cylinder(float radius, float height, int segments = 24, float radiusTop = -1, bool caps = true)
        {
            if (radiusTop < 0) radiusTop = radius;
            string key = $"cyl:{radius:F4}:{height:F4}:{segments}:{radiusTop:F4}:{caps}";
            return Memo(key, () =>
            {
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                float y0 = -height / 2, y1 = height / 2;
                float slope = (radius - radiusTop) / Mathf.Max(height, 1e-5f);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    Vector3 n = new Vector3(c, slope, s).normalized;
                    verts.Add(new Vector3(c * radius, y0, s * radius)); norms.Add(n); uvs.Add(new Vector2(i / (float)segments, 0));
                    verts.Add(new Vector3(c * radiusTop, y1, s * radiusTop)); norms.Add(n); uvs.Add(new Vector2(i / (float)segments, 1));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = i * 2, b = a + 1, c2 = a + 2, d = a + 3;
                    tris.Add(a); tris.Add(b); tris.Add(c2);
                    tris.Add(c2); tris.Add(b); tris.Add(d);
                }
                if (caps)
                {
                    foreach (var (y, r, up) in new[] { (y1, radiusTop, true), (y0, radius, false) })
                    {
                        if (r <= 1e-5f) continue;
                        int centre = verts.Count;
                        Vector3 n = up ? Vector3.up : Vector3.down;
                        verts.Add(new Vector3(0, y, 0)); norms.Add(n); uvs.Add(new Vector2(0.5f, 0.5f));
                        for (int i = 0; i <= segments; i++)
                        {
                            float a = i / (float)segments * Mathf.PI * 2;
                            verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); norms.Add(n);
                            uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                        }
                        for (int i = 0; i < segments; i++)
                        {
                            if (up) { tris.Add(centre); tris.Add(centre + i + 2); tris.Add(centre + i + 1); }
                            else { tris.Add(centre); tris.Add(centre + i + 1); tris.Add(centre + i + 2); }
                        }
                    }
                }
                return Build(verts, norms, uvs, tris);
            });
        }

        /// <summary>A unit quad in the xy plane facing -z (towards a viewer looking along +z).</summary>
        public static Mesh Quad() => Memo("quad", () =>
        {
            var verts = new List<Vector3> { new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(-0.5f, 0.5f, 0), new(0.5f, 0.5f, 0) };
            var norms = new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uvs = new List<Vector2> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
            var tris = new List<int> { 0, 2, 1, 1, 2, 3 };
            return Build(verts, norms, uvs, tris);
        });

        /// <summary>A unit quad lying on the floor (xz plane) facing up.</summary>
        public static Mesh FloorQuad() => Memo("floorquad", () =>
        {
            var verts = new List<Vector3> { new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(-0.5f, 0, 0.5f), new(0.5f, 0, 0.5f) };
            var norms = new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            var uvs = new List<Vector2> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
            var tris = new List<int> { 0, 2, 1, 1, 2, 3 };
            return Build(verts, norms, uvs, tris);
        });

        /// <summary>A UV sphere of radius 0.5.</summary>
        public static Mesh Sphere(int segments = 24) => Memo($"sphere:{segments}", () =>
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int rings = segments / 2;
            for (int j = 0; j <= rings; j++)
            {
                float v = j / (float)rings;
                float th = v * Mathf.PI;
                for (int i = 0; i <= segments; i++)
                {
                    float u = i / (float)segments;
                    float ph = u * Mathf.PI * 2;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    verts.Add(n * 0.5f); norms.Add(n); uvs.Add(new Vector2(u, 1 - v));
                }
            }
            int w = segments + 1;
            for (int j = 0; j < rings; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            return Build(verts, norms, uvs, tris);
        });

        /// <summary>
        /// A flat ring sector in the xz plane (an arc band), from angle a0 to a1 (radians, about +y),
        /// between radii r0 and r1; uv.x runs along the arc. Joint-angle arcs and gauges use it.
        /// </summary>
        public static Mesh Arc(float r0, float r1, float a0, float a1, int segments = 48)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float a = Mathf.Lerp(a0, a1, t);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(d * r0); norms.Add(Vector3.up); uvs.Add(new Vector2(t, 0));
                verts.Add(d * r1); norms.Add(Vector3.up); uvs.Add(new Vector2(t, 1));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
            }
            var m = Build(verts, norms, uvs, tris);
            m.name = "arc";
            return m;
        }

        /// <summary>A torus round +y (the ring of a Touch controller, a gauge's bezel).</summary>
        public static Mesh Torus(float radius, float tube, int segments = 48, int sides = 14) => Memo($"torus:{radius}:{tube}:{segments}:{sides}", () =>
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2;
                var centre = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                var outward = centre.normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2;
                    var n = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    verts.Add(centre + n * tube);
                    norms.Add(n);
                    uvs.Add(new Vector2(i / (float)segments, j / (float)sides));
                }
            }
            int w = sides + 1;
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * w + j, b = a + w, c = a + 1, d = b + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(c); tris.Add(d); tris.Add(b);
            }
            return Build(verts, norms, uvs, tris);
        });

        /// <summary>A cone along +y with its base at y = 0.</summary>
        public static Mesh Cone(float radius, float height, int segments = 20) => Memo($"cone:{radius}:{height}:{segments}", () =>
        {
            var m = Cylinder(radius, height, segments, 0.0001f, true);
            var c = UnityEngine.Object.Instantiate(m);
            var v = c.vertices;
            for (int i = 0; i < v.Length; i++) v[i].y += height / 2;
            c.vertices = v;
            c.RecalculateBounds();
            return c;
        });

        public static Mesh Build(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            var m = new Mesh { indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            var colors = new Color32[verts.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    /// <summary>
    /// Static parts collected by material and merged into one mesh each, so a hall of hundreds of
    /// parts costs a handful of draw calls (kit.ts's Batcher). Positions and sizes may be given in
    /// the web frame (Box, Shape with web=true) or Unity's (Add).
    /// </summary>
    public class Batcher
    {
        readonly Dictionary<Material, (List<CombineInstance> parts, bool cast)> parts = new();

        public void Add(Mesh mesh, Material material, Matrix4x4 matrix, bool cast = false)
        {
            if (!parts.TryGetValue(material, out var entry))
            {
                entry = (new List<CombineInstance>(), cast);
                parts[material] = entry;
            }
            entry.parts.Add(new CombineInstance { mesh = mesh, transform = matrix });
            if (cast && !entry.cast) parts[material] = (entry.parts, true);
        }

        /// <summary>kit.ts's box(material, size, at, rot): a box sized and placed in web numbers.</summary>
        public void Box(Material material, float[] size, float[] at, float[] rot = null, bool cast = false)
        {
            var r = rot == null ? Quaternion.identity : Web.Q(rot[0], rot[1], rot[2]);
            Add(MeshKit.Box(), material, Matrix4x4.TRS(Web.P(at[0], at[1], at[2]), r, Web.S(size[0], size[1], size[2])), cast);
        }

        /// <summary>kit.ts's shape(geometry, material, at, rot, scale): a mesh placed in web numbers.</summary>
        public void Shape(Mesh mesh, Material material, float[] at, float[] rot = null, float[] scale = null, bool cast = false)
        {
            var r = rot == null ? Quaternion.identity : Web.Q(rot[0], rot[1], rot[2]);
            var s = scale == null ? Vector3.one : Web.S(scale[0], scale[1], scale[2]);
            Add(mesh, material, Matrix4x4.TRS(Web.P(at[0], at[1], at[2]), r, s), cast);
        }

        public GameObject Build(string name, Transform parent, bool receiveShadows = true, int layer = Layers.Environment)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.layer = layer;
            foreach (var kv in parts)
            {
                var list = kv.Value.parts;
                // Split into chunks that keep under the 32-bit index budget comfortably.
                const int chunk = 600;
                for (int start = 0; start < list.Count; start += chunk)
                {
                    var slice = list.GetRange(start, Mathf.Min(chunk, list.Count - start));
                    var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = name + ":" + kv.Key.name };
                    mesh.CombineMeshes(slice.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var go = new GameObject(kv.Key.name);
                    go.layer = layer;
                    go.transform.SetParent(group.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = kv.Value.cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    mr.receiveShadows = receiveShadows;
                }
            }
            parts.Clear();
            return group;
        }
    }

    public static class Go
    {
        /// <summary>A child object with a mesh and a material.</summary>
        public static GameObject Mesh(string name, Transform parent, Mesh mesh, Material material, Vector3 pos = default, Quaternion? rot = null, Vector3? scale = null, bool cast = true, int layer = Layers.Environment)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 pos = default, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            return go;
        }

        /// <summary>A flat decal lying on the floor: web (x, y) centre, yaw about up, size w x h.</summary>
        public static GameObject FloorDecal(string name, Transform parent, Material material, float w, float h, float x, float y, float yaw = 0, float z = 0.002f)
        {
            var go = Mesh(name, parent, MeshKit.FloorQuad(), material, Web.P(x, y, z), Web.Yaw(yaw), new Vector3(w, 1, h), cast: false);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
            return go;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }
}
