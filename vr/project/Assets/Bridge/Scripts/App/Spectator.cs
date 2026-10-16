// SPDX-License-Identifier: LicenseRef-BRIDGE
// The laptop's screen during a session: a steadied view of what the trainee sees (or an overview of
// the cell, or the robot up close) for the room to watch, with a trainer's overlay: the module and
// step, the trainee's progress, the cell's state, and keyboard shortcuts to help a trainee along.
//
//   F1 trainee's view   F2 overview   F3 robot close-up   F4 hide this overlay
//   N next step   B back   R repeat step   H home menu   E emergency stop   C reset the cell
//   Desktop without a headset: right mouse to look, WASD to walk, left click to select,
//   1 A, 2 B, 3 X, 4 Y, Tab menu, G and H grips, arrows and PgUp/PgDn/,/. the sticks, Q/E stick clicks.

using Bridge.Robot;
using Bridge.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Bridge.App
{
    public class Spectator : MonoBehaviour
    {
        BridgeApp app;
        Camera cam;
        public int View = 1;   // 1 trainee, 2 overview, 3 robot
        Vector3 pos;
        Quaternion rot;
        Canvas canvas;
        TextMeshProUGUI title, status, lesson, keys;
        Image band;
        bool overlay = true;
        public bool CertificateShot;

        public static Spectator Create(BridgeApp app)
        {
            var go = new GameObject("Spectator");
            go.transform.SetParent(app.transform, false);
            var s = go.AddComponent<Spectator>();
            s.app = app;
            s.cam = go.AddComponent<Camera>();
            s.cam.stereoTargetEye = StereoTargetEyeMask.None;
            s.cam.depth = 10;
            s.cam.fieldOfView = 72;
            s.cam.nearClipPlane = 0.03f;
            s.cam.farClipPlane = 120f;
            s.cam.enabled = false;
            var data = s.cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            s.BuildOverlay();
            return s;
        }

        void BuildOverlay()
        {
            var go = new GameObject("Trainer overlay");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            band = Panel(go.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), new Vector2(0, 0), new Color(0.05f, 0.06f, 0.08f, 0.72f));
            title = Label(go.transform, "", 22, new Vector2(0, 1), new Vector2(24, -14), new Vector2(1100, 40), TextAlignmentOptions.TopLeft, "Figtree-Bold");
            LogoBeforeText(title, "'s Next-Gen Factory Digital Twin Simulation for Robotics  <size=70%><color=#9AA3AD>by Awaiz Ahmed</color></size>");
            status = Label(go.transform, "", 17, new Vector2(1, 1), new Vector2(-24, -18), new Vector2(560, 40), TextAlignmentOptions.TopRight, "Figtree-SemiBold");
            Panel(go.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 96), new Color(0.05f, 0.06f, 0.08f, 0.72f));
            lesson = Label(go.transform, "", 18, new Vector2(0, 0), new Vector2(24, 82), new Vector2(1100, 80), TextAlignmentOptions.TopLeft, "Figtree-Medium");
            keys = Label(go.transform, "", 14, new Vector2(1, 0), new Vector2(-24, 82), new Vector2(560, 80), TextAlignmentOptions.TopRight, "IBMPlexMono-Regular");
        }

        Image Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color c)
        {
            var go = new GameObject("band");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            var rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return img;
        }

        // The white logo's own proportions (wordmark_white.png, 1154 x 272 px): its letters fill rows 82 to 271
        // and stand on the bottom edge, ending at column 1065; the orange bar rides above the E.
        const float LogoAspect = 1154f / 272f, LogoLetters = 190f / 272f, LogoInk = 1065f / 1154f;

        /// <summary>
        /// Puts the white BRIDGE logo where a line's first word would be: its letters as tall as the
        /// text's capitals and on the same baseline, and <paramref name="rest"/> after it.
        /// </summary>
        static void LogoBeforeText(TextMeshProUGUI text, string rest)
        {
            text.text = rest;
            text.ForceMeshUpdate(true);
            var line = text.textInfo.lineInfo[0];
            var face = text.font.faceInfo;
            float capOfAscent = face.capLine > 0 && face.ascentLine > 0 ? face.capLine / face.ascentLine : 0.72f;
            float h = (line.ascender - line.baseline) * capOfAscent / LogoLetters, w = h * LogoAspect;
            var go = new GameObject("logo", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(text.transform, false);
            var img = go.GetComponent<RawImage>();
            img.texture = Gen.Tex("wordmark_white");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
            rt.localPosition = new Vector3(text.rectTransform.rect.xMin, line.baseline, 0);
            // <space> counts in ems of the font size for screen text; a little air before the apostrophe.
            string gap = (w * LogoInk / text.fontSize + 0.06f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            text.text = $"<space={gap}em>{rest}";
        }

        TextMeshProUGUI Label(Transform parent, string text, float size, Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align, string font)
        {
            var go = new GameObject("text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            var f = Gen.Font(font);
            if (f != null) t.font = f;
            t.text = text;
            t.fontSize = size;
            t.color = new Color(0.92f, 0.94f, 0.96f);
            t.alignment = align;
            t.richText = true;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }

        void Update()
        {
            if (app == null || !app.Ready) return;
            Keys();
            var xr = app.XR;
            cam.enabled = xr.XRActive || CertificateShot;
            canvas.enabled = overlay;
            if (cam.enabled) Follow(Time.unscaledDeltaTime);
            Texts();
        }

        void Follow(float dt)
        {
            var head = app.XR.Head;
            Vector3 wantPos;
            Quaternion wantRot;
            if (CertificateShot && app.Certificate.Window != null)
            {
                var w = app.Certificate.Window.transform;
                wantPos = w.position - w.forward * 0.95f;
                wantRot = Quaternion.LookRotation(w.forward, Vector3.up);
                pos = wantPos;
                rot = wantRot;
            }
            else if (View == 2)
            {
                wantPos = CellLayout.RobotBase + new Vector3(3.6f, 1.6f, -3.2f);
                wantRot = Quaternion.LookRotation(CellLayout.BenchCentre + Vector3.up * 0.2f - wantPos, Vector3.up);
            }
            else if (View == 3)
            {
                var tcp = app.Panda.Tcp.position;
                wantPos = tcp + new Vector3(0.9f, 0.35f, -0.75f);
                wantRot = Quaternion.LookRotation(tcp - wantPos, Vector3.up);
            }
            else
            {
                // The trainee's view, steadied so it is comfortable to watch on a projector.
                wantPos = head.position;
                var e = head.rotation.eulerAngles;
                wantRot = Quaternion.Euler(e.x, e.y, 0);
            }
            pos = Vector3.Lerp(pos, wantPos, 1 - Mathf.Exp(-(View == 1 ? 9 : 3) * dt));
            rot = Quaternion.Slerp(rot, wantRot, 1 - Mathf.Exp(-(View == 1 ? 6 : 3) * dt));
            transform.SetPositionAndRotation(pos, rot);
        }

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) View = 1;
            if (kb.f2Key.wasPressedThisFrame) View = 2;
            if (kb.f3Key.wasPressedThisFrame) View = 3;
            if (kb.f4Key.wasPressedThisFrame) overlay = !overlay;
            bool desktop = !app.XR.XRActive;
            // In the headset the trainer's keys act directly; on the desktop the number row is the
            // controllers, so the trainer keys there need Ctrl.
            bool trainer = !desktop || kb.ctrlKey.isPressed;
            if (!trainer) return;
            if (kb.nKey.wasPressedThisFrame) { if (app.Tour.Active) app.Tour.Next(); else app.Lessons.Skip(); }
            if (kb.bKey.wasPressedThisFrame) app.Lessons.Back();
            if (kb.rKey.wasPressedThisFrame) app.Lessons.Repeat();
            if (kb.hKey.wasPressedThisFrame) app.Menus.ToggleHome();
            if (kb.eKey.wasPressedThisFrame) { if (app.Cell.Safety.EStopPressed) app.Cell.Safety.ReleaseEStop(); else app.Cell.Safety.PressEStop(); }
            if (kb.cKey.wasPressedThisFrame) app.ResetCell();
        }

        float slow;

        void Texts()
        {
            slow += Time.unscaledDeltaTime;
            if (slow < 0.2f) return;
            slow = 0;
            var xr = app.XR;
            var cell = app.Cell;
            string headset = xr.XRActive ? "<color=#3DDC84>●</color> Headset on" : $"<color=#E8A200>●</color> {xr.XRStatus}";
            string state = cell.Safety.Stop != StopKind.None ? "<color=#FF4D4F>STOPPED</color>" : cell.Safety.Zone == "warning" ? "<color=#E8A200>REDUCED</color>" : cell.Running ? "<color=#3DDC84>RUNNING</color>" : "READY";
            status.text = $"{headset}   ·   Cell 01 {state}   ·   {app.Progress.Count}/10 modules";
            var r = app.Lessons;
            if (r.Active && r.Step != null)
            {
                var s = r.Step;
                string done = r.StepComplete ? "  <color=#3DDC84>✓ done</color>" : "";
                lesson.text = $"<color=#FF8A5C>MODULE {r.LessonIndex + 1} · {r.Current.Title.ToUpperInvariant()}</color>   step {r.StepIndex + 1}/{r.Current.Steps.Count}{done}\n<b>{s.Title}</b>\n<size=85%><color=#B8C0C8>{(s.Quiz != null ? s.Quiz.Question : Short(s.Body))}</color></size>";
            }
            else if (app.Tour.Active) lesson.text = $"<color=#FF8A5C>FACTORY TOUR</color>   stop {app.Tour.Index + 1}/{app.Tour.Count}\n<b>{app.Tour.CurrentTitle}</b>";
            else if (app.FreePlay) lesson.text = "<color=#FF8A5C>FREE PLAY</color>\nThe trainee is driving the cobot freely.";
            else lesson.text = app.Menus.IsOpen ? "<color=#FF8A5C>HOME</color>\nThe trainee is at the main menu." : "<color=#FF8A5C>EXPLORING</color>\nThe trainee is looking around the factory.";
            keys.text = (xr.XRActive ? "F1 trainee view  F2 overview  F3 robot  F4 overlay\nN next  B back  R repeat  H menu  E e-stop  C reset cell"
                : "Right mouse: look  WASD: walk  Click: select  Tab: menu\n1 A  2 B  3 X  4 Y  G/H grips  arrows, PgUp/PgDn , .: sticks\nCtrl+N next  Ctrl+B back  Ctrl+E e-stop  F4 overlay");
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
            return s.Length > 170 ? s.Substring(0, 167) + "..." : s;
        }
    }
}
