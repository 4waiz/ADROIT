// SPDX-License-Identifier: LicenseRef-BRIDGE
// Sets the Unity project up from code, so the repository carries only scripts, shaders and the
// asset exporter (vr/tools/export_assets.py): player settings, the render pipeline, OpenXR for PC VR
// over Meta Quest Link, TextMesh Pro's essentials and the Figtree font assets, the material
// templates the app clones at run time (each one keeps its shader variant in the build), and the
// one scene. vr/build.ps1 runs Setup, then Build, in batch mode.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Bridge.EditorTools
{
    public static class BridgeSetup
    {
        const string Gen = "Assets/BridgeGen";
        const string GenRes = Gen + "/Resources/Gen";
        const string ScenePath = Gen + "/Main.unity";

        /// <summary>Configures everything; exits the editor with 0 on success.</summary>
        public static void Setup() => Run("setup", () =>
        {
            ConfigureLayers();
            ConfigurePlayer();
            ConfigureRendering();
            ConfigureXR();
            ImportTextMeshProEssentials();
            CreateFontAssets();
            CreateMaterialTemplates();
            CreateScene();
            AssetDatabase.SaveAssets();
        });

        /// <summary>Builds the Windows player into the folder given by -bridgeOut.</summary>
        public static void Build() => Run("build", () =>
        {
            string outDir = Arg("-bridgeOut") ?? Path.GetFullPath("Build");
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, "BRIDGE VR.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[bridge] build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors, {report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("build failed: " + report.summary.result);
        });

        static void Run(string what, Action action)
        {
            try
            {
                action();
                Debug.Log($"[bridge] {what} done");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[bridge] {what} failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ------------------------------------------------------------------ layers
        /// <summary>Names the layers Bridge.Layers uses (6 to 11).</summary>
        static void ConfigureLayers()
        {
            var tags = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (tags == null) return;
            var so = new SerializedObject(tags);
            var layers = so.FindProperty("layers");
            string[] names = { "Environment", "Robot", "Grabbable", "UI", "Hands", "Teleport" };
            for (int i = 0; i < names.Length; i++)
            {
                var slot = layers.GetArrayElementAtIndex(6 + i);
                slot.stringValue = names[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ player
        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "BRIDGE";
            PlayerSettings.productName = "BRIDGE VR";
            PlayerSettings.bundleVersion = Arg("-bridgeVersion") ?? "1.0.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            // Direct3D 11 is the API Meta's OpenXR runtime has carried longest on PC.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(GenRes + "/UI/app_icon.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            // The Input System package only (OpenXR's controllers come through it).
            var player = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (player != null)
            {
                var so = new SerializedObject(player);
                var input = so.FindProperty("activeInputHandler");
                if (input != null) input.intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Physics.defaultSolverIterations = 10;
            Physics.defaultSolverVelocityIterations = 4;
            Time.fixedDeltaTime = 1f / 90f;
            QualitySettings.realtimeReflectionProbes = true;
        }

        // ------------------------------------------------------------------ rendering
        static void ConfigureRendering()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (asset == null) throw new Exception("Assets/Settings/PC_RPAsset.asset is missing (the URP template's settings)");
            var so = new SerializedObject(asset);
            Set(so, "m_MSAA", 4);
            Set(so, "m_SupportsHDR", true);
            Set(so, "m_RenderScale", 1f);
            Set(so, "m_RequireOpaqueTexture", true);   // the glass panels blur what is behind them
            Set(so, "m_OpaqueDownsampling", 1);       // 2x bilinear
            Set(so, "m_RequireDepthTexture", true);
            Set(so, "m_ShadowDistance", 28f);
            Set(so, "m_ShadowCascadeCount", 4);
            Set(so, "m_MainLightShadowmapResolution", 4096);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_AdditionalLightsRenderingMode", 1);
            Set(so, "m_AdditionalLightShadowsSupported", true);
            Set(so, "m_AdditionalLightsShadowmapResolution", 2048);
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_ColorGradingMode", 1);         // HDR grading
            Set(so, "m_UseSRPBatcher", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = asset;
            int levels = QualitySettings.names.Length;
            int pc = Array.IndexOf(QualitySettings.names, "PC");
            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
                QualitySettings.realtimeReflectionProbes = true;
                QualitySettings.vSyncCount = 1;
            }
            if (pc >= 0) QualitySettings.SetQualityLevel(pc, true);
            var quality = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (quality != null && pc >= 0)
            {
                var q = new SerializedObject(quality);
                var defaults = q.FindProperty("m_PerPlatformDefaultQuality");
                if (defaults != null)
                {
                    for (int i = 0; i < defaults.arraySize; i++)
                    {
                        var entry = defaults.GetArrayElementAtIndex(i);
                        var key = entry.FindPropertyRelative("first");
                        var value = entry.FindPropertyRelative("second");
                        if (key != null && value != null && key.stringValue == "Standalone") value.intValue = pc;
                    }
                }
                Set(q, "m_CurrentQuality", pc);
                q.ApplyModifiedPropertiesWithoutUndo();
            }

            // Forward+ for the hall's many lights, with screen-space ambient occlusion.
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (renderer != null)
            {
                var r = new SerializedObject(renderer);
                Set(r, "m_RenderingMode", 2);
                r.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
            }
            EditorUtility.SetDirty(asset);
        }

        static void Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null)
            {
                Debug.LogWarning($"[bridge] {so.targetObject.name} has no {name}");
                return;
            }
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i:
                    if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = i; else p.intValue = i;
                    break;
                case float f: p.floatValue = f; break;
                case string s: p.stringValue = s; break;
            }
        }

        // ------------------------------------------------------------------ OpenXR
        static void ConfigureXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            // The app starts XR itself, so it can open on the desktop when no headset is there and
            // switch to the headset once Quest Link connects.
            general.InitManagerOnStart = false;
            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!manager.activeLoaders.Any(l => l != null && l.GetType().Name == "OpenXRLoader"))
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone))
                    throw new Exception("could not assign the OpenXR loader");
            }
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (openxr == null) throw new Exception("no OpenXR settings for Standalone");
            openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            openxr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;
            foreach (var feature in openxr.GetFeatures())
            {
                bool want = feature is OculusTouchControllerProfile || feature is MetaQuestTouchPlusControllerProfile || feature is MetaQuestTouchProControllerProfile;
                if (want) feature.enabled = true;
            }
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
            Debug.Log("[bridge] OpenXR: loader assigned, Touch, Touch Plus and Touch Pro profiles on, single-pass instanced");
        }

        // ------------------------------------------------------------------ text
        /// <summary>TextMesh Pro's essentials are unpacked by vr/build.ps1 before Unity starts (an import here would be asynchronous).</summary>
        static void ImportTextMeshProEssentials()
        {
            if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
                throw new Exception("TextMesh Pro's essentials are missing: run vr/build.ps1, which unpacks them into Assets/TextMesh Pro");
            if (TMP_Settings.instance == null) throw new Exception("TMP Settings did not load");
        }

        const string Glyphs =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            " °±²³µ·×éèüöäÉ–—‘’“”•…−↑↓®©™€£";

        static void CreateFontAssets()
        {
            Directory.CreateDirectory(GenRes + "/Fonts");
            var created = new List<TMP_FontAsset>();
            foreach (var ttf in new[] { "Figtree-Regular", "Figtree-Medium", "Figtree-SemiBold", "Figtree-Bold", "Figtree-ExtraBold", "IBMPlexMono-Regular", "IBMPlexMono-SemiBold" })
            {
                string target = $"{GenRes}/Fonts/{ttf} SDF.asset";
                if (File.Exists(target)) AssetDatabase.DeleteAsset(target);
                var font = AssetDatabase.LoadAssetAtPath<Font>($"{Gen}/Fonts/{ttf}.ttf");
                if (font == null)
                {
                    Debug.LogWarning($"[bridge] font {ttf}.ttf missing; run the exporter first");
                    continue;
                }
                var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                asset.name = ttf + " SDF";
                AssetDatabase.CreateAsset(asset, target);
                asset.TryAddCharacters(Glyphs, out string missing);
                foreach (var tex in asset.atlasTextures)
                {
                    if (tex == null || AssetDatabase.Contains(tex)) continue;
                    tex.name = asset.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, asset);
                }
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
                EditorUtility.SetDirty(asset);
                created.Add(asset);
                if (!string.IsNullOrEmpty(missing)) Debug.Log($"[bridge] {ttf}: no glyphs for {missing}");
            }
            AssetDatabase.SaveAssets();
            // Glyphs Figtree lacks (arrows, ticks) fall back to TMP's Liberation Sans.
            var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            foreach (var asset in created)
            {
                if (liberation != null)
                {
                    asset.fallbackFontAssetTable = new List<TMP_FontAsset> { liberation };
                    EditorUtility.SetDirty(asset);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[bridge] {created.Count} TextMesh Pro font assets");
        }

        // ------------------------------------------------------------------ materials
        static void CreateMaterialTemplates()
        {
            string dir = GenRes + "/Materials";
            Directory.CreateDirectory(dir);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new Exception("URP Lit shader not found");

            // Each template keeps one keyword set of URP Lit in the build; the app clones them.
            SaveMaterial(LitTemplate(lit, normal: false, mg: false, transparent: false, emission: false), dir + "/Lit.mat");
            SaveMaterial(LitTemplate(lit, normal: true, mg: true, transparent: false, emission: false), dir + "/LitMapped.mat");
            SaveMaterial(LitTemplate(lit, normal: true, mg: false, transparent: false, emission: false), dir + "/LitNormal.mat");
            SaveMaterial(LitTemplate(lit, normal: false, mg: false, transparent: true, emission: false), dir + "/LitTransparent.mat");
            SaveMaterial(LitTemplate(lit, normal: true, mg: true, transparent: true, emission: false), dir + "/LitMappedTransparent.mat");
            SaveMaterial(LitTemplate(lit, normal: false, mg: false, transparent: false, emission: true), dir + "/LitEmissive.mat");

            foreach (var name in new[] { "Glass", "Unlit", "UnlitTransparent", "UnlitAdditive", "Hologram", "Zone", "Shadow" })
            {
                var shader = Shader.Find("Bridge/" + name);
                if (shader == null) throw new Exception($"shader Bridge/{name} is missing or does not compile");
                SaveMaterial(new Material(shader), $"{dir}/{name}.mat");
            }
            // The glass as a flat overlay on a panel (buttons, chips, bars): no blur, no depth write,
            // drawn after the panels and before the text.
            var overlay = new Material(Shader.Find("Bridge/Glass"));
            overlay.SetFloat("_Blur", 0f);
            overlay.SetFloat("_ZWrite", 0f);
            overlay.SetFloat("_Sheen", 0.2f);
            overlay.renderQueue = (int)RenderQueue.Transparent + 1;
            SaveMaterial(overlay, dir + "/GlassOverlay.mat");

            var panoramic = Shader.Find("Skybox/Panoramic");
            var hdri = AssetDatabase.LoadAssetAtPath<Texture>(GenRes + "/Textures/factory_hdri.hdr");
            if (panoramic != null && hdri != null)
            {
                var sky = new Material(panoramic);
                sky.SetTexture("_MainTex", hdri);
                sky.SetFloat("_Exposure", 1f);
                SaveMaterial(sky, dir + "/Sky.mat");
            }
            AssetDatabase.SaveAssets();
        }

        static Material LitTemplate(Shader lit, bool normal, bool mg, bool transparent, bool emission)
        {
            var m = new Material(lit);
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Smoothness", 0.5f);
            m.SetFloat("_Metallic", 0f);
            if (normal) m.EnableKeyword("_NORMALMAP");
            if (mg)
            {
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 1f);
            }
            if (emission)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", Color.black);
            }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)RenderQueue.Geometry;
            }
            return m;
        }

        static void SaveMaterial(Material m, string path)
        {
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        // ------------------------------------------------------------------ scene
        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var app = Type.GetType("Bridge.App.BridgeApp, Assembly-CSharp");
            if (app == null) throw new Exception("Bridge.App.BridgeApp is missing; does the project compile?");
            var go = new GameObject("BRIDGE");
            go.AddComponent(app);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(GenRes + "/Materials/Sky.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            // A light haze over the hall's 36 m; the fog keyword is kept only if a scene uses fog.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.70f);
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[bridge] scene " + ScenePath);
        }
    }

    /// <summary>Import settings for what the exporter writes under Assets/BridgeGen.</summary>
    public class BridgeImport : AssetPostprocessor
    {
        // Raised whenever the settings below change, so Unity imports the textures again.
        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/BridgeGen/")) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.wrapMode = TextureWrapMode.Repeat;
            if (name.EndsWith("_normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (name.EndsWith("_mg") || name == "macro")
            {
                ti.sRGBTexture = false;
            }
            if (assetPath.Contains("/UI/") || name.StartsWith("sign_") || name.StartsWith("floor_") && !name.StartsWith("floor_color") && !name.StartsWith("floor_mg") && !name.StartsWith("floor_normal") || name == "wall_logo" || name == "blob" || name == "softdot")
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
            }
            if (assetPath.Contains("/UI/"))
            {
                // Kept at their own size: rounding to a power of two would squash the icon atlas
                // (2048 x 1280) and the logo (778 x 184), whose quads are sized from their shapes.
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
                ti.anisoLevel = 4;
            }
        }
    }
}
