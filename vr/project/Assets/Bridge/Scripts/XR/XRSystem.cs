// SPDX-License-Identifier: LicenseRef-BRIDGE
// The headset and the controllers. BRIDGE VR is a PC VR app: it runs on the laptop and draws into a
// Meta Quest 2 or 3 over Quest Link (cable or Air Link), through OpenXR. The app starts XR itself: with no
// headset connected it opens on the desktop, says how to connect, and keeps trying; when Link comes
// up it switches into the headset. On the desktop a mouse and keyboard stand in for the controllers,
// so trainers can run and check a lesson without a headset.
//
// Controllers: Touch (Quest 2), Touch Plus (Quest 3) and Touch Pro all read through OpenXR's
// profiles; every input is bound by usage as well as by name, so any of them works.

using System;
using System.Collections;
using System.Collections.Generic;
using Bridge.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using InputDevice = UnityEngine.XR.InputDevice;
using Pointer = Bridge.UI.Pointer;

namespace Bridge.XR
{
    /// <summary>One hand's controller, read each frame.</summary>
    public class Hand
    {
        public int Index;
        public string Name;
        public Transform Grip;      // the controller's body (device pose)
        public Transform Aim;       // where it points (aim pose)
        public bool Tracked;
        public Vector2 Stick;
        public float Trigger, Squeeze;
        public bool TriggerHeld, TriggerDown, TriggerUp;
        public bool GripHeld, GripDown, GripUp;
        public bool Primary, PrimaryDown;     // A or X
        public bool Secondary, SecondaryDown; // B or Y
        public bool Menu, MenuDown;
        public bool StickClick, StickClickDown;
        public bool ThumbTouch;
        public Vector3 Velocity;
        public Pointer Pointer;
        Vector3 lastPos;

        public string PrimaryName => Index == 0 ? "X" : "A";
        public string SecondaryName => Index == 0 ? "Y" : "B";

        public Vector3 Tip => Aim.position + Aim.forward * 0.035f;

        internal void Track(float dt)
        {
            if (dt > 0) Velocity = Vector3.Lerp(Velocity, (Grip.position - lastPos) / dt, 0.4f);
            lastPos = Grip.position;
        }
    }

    public class XRSystem : MonoBehaviour
    {
        public static XRSystem I;
        public bool XRActive { get; private set; }
        public bool Starting { get; private set; }
        public string XRStatus = "Starting";
        public Transform Origin;        // the play area's floor, moved by locomotion
        public Transform TrackingSpace;
        public Camera Cam;
        public Transform Head;
        public Hand Left, Right;
        public Hand[] Hands;
        public readonly List<Pointer> Pointers = new();
        public Pointer Mouse;
        public float SeatedLift;        // added to the head's height when seated
        public bool DesktopForced;
        public event Action<bool> XRChanged;

        InputActionMap map;
        readonly Dictionary<string, InputAction> actions = new();
        float desktopYaw, desktopPitch = 8f;
        Vector3 desktopEye = new(0, 1.65f, 0);
        float retryAt;
        TrackedPoseDriver headDriver, leftDriver, rightDriver;

        public static XRSystem Create(Transform parent, bool desktopOnly)
        {
            var go = new GameObject("XR");
            go.transform.SetParent(parent, false);
            var x = go.AddComponent<XRSystem>();
            I = x;
            x.DesktopForced = desktopOnly;
            x.Build();
            return x;
        }

        void Build()
        {
            Origin = new GameObject("XR Origin").transform;
            Origin.SetParent(transform, false);
            TrackingSpace = new GameObject("Tracking Space").transform;
            TrackingSpace.SetParent(Origin, false);

            var camGo = new GameObject("Head");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(TrackingSpace, false);
            camGo.transform.localPosition = desktopEye;
            Cam = camGo.AddComponent<Camera>();
            Cam.nearClipPlane = 0.02f;
            Cam.farClipPlane = 120f;
            Cam.fieldOfView = 70f;
            Cam.allowMSAA = true;
            Cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            Head = camGo.transform;
            Bridge.UI.Rig.Head = Head;

            map = new InputActionMap("bridge");
            // The head.
            var hp = Add("head/position", InputActionType.Value, "<XRHMD>/centerEyePosition", "<XRHMD>/devicePosition");
            var hr = Add("head/rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", "<XRHMD>/deviceRotation");
            Left = MakeHand(0, "LeftHand");
            Right = MakeHand(1, "RightHand");
            Hands = new[] { Left, Right };
            map.Enable();

            headDriver = camGo.AddComponent<TrackedPoseDriver>();
            headDriver.positionInput = new InputActionProperty(hp);
            headDriver.rotationInput = new InputActionProperty(hr);
            headDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            headDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            headDriver.enabled = false;
            leftDriver = Driver(Left, "LeftHand");
            rightDriver = Driver(Right, "RightHand");

            foreach (var h in Hands)
            {
                h.Pointer = new Pointer { Id = h.Index };
                Pointers.Add(h.Pointer);
            }
            Mouse = new Pointer { Id = 2 };
            Pointers.Add(Mouse);
        }

        InputAction Add(string name, InputActionType type, params string[] bindings)
        {
            var a = map.AddAction(name, type);
            foreach (var b in bindings) a.AddBinding(b);
            actions[name] = a;
            return a;
        }

        Hand MakeHand(int index, string usage)
        {
            string c = "<XRController>{" + usage + "}";
            var h = new Hand { Index = index, Name = index == 0 ? "left" : "right" };
            string n = h.Name;
            Add(n + "/position", InputActionType.Value, c + "/devicePosition", c + "/{DevicePosition}");
            Add(n + "/rotation", InputActionType.Value, c + "/deviceRotation", c + "/{DeviceRotation}");
            Add(n + "/aimPosition", InputActionType.Value, c + "/pointerPosition", c + "/{PointerPosition}");
            Add(n + "/aimRotation", InputActionType.Value, c + "/pointerRotation", c + "/{PointerRotation}");
            Add(n + "/tracked", InputActionType.Button, c + "/isTracked", c + "/{IsTracked}");
            Add(n + "/stick", InputActionType.Value, c + "/thumbstick", c + "/{Primary2DAxis}");
            Add(n + "/trigger", InputActionType.Value, c + "/trigger", c + "/{Trigger}");
            Add(n + "/grip", InputActionType.Value, c + "/grip", c + "/{Grip}");
            Add(n + "/primary", InputActionType.Button, c + "/primaryButton", c + "/{PrimaryButton}");
            Add(n + "/secondary", InputActionType.Button, c + "/secondaryButton", c + "/{SecondaryButton}");
            Add(n + "/menu", InputActionType.Button, c + "/menu", c + "/{MenuButton}", c + "/menuButton");
            Add(n + "/stickClick", InputActionType.Button, c + "/thumbstickClicked", c + "/{Primary2DAxisClick}");
            Add(n + "/stickTouch", InputActionType.Button, c + "/thumbstickTouched", c + "/{Primary2DAxisTouch}");
            Add(n + "/haptic", InputActionType.PassThrough, c + "/{Haptic}", c + "/haptic");

            var grip = new GameObject(index == 0 ? "Left Controller" : "Right Controller").transform;
            grip.SetParent(TrackingSpace, false);
            grip.localPosition = new Vector3(index == 0 ? -0.2f : 0.2f, 1.1f, 0.35f);
            var aim = new GameObject("aim").transform;
            aim.SetParent(TrackingSpace, false);
            h.Grip = grip;
            h.Aim = aim;
            return h;
        }

        TrackedPoseDriver Driver(Hand h, string usage)
        {
            var d = h.Grip.gameObject.AddComponent<TrackedPoseDriver>();
            d.positionInput = new InputActionProperty(actions[h.Name + "/position"]);
            d.rotationInput = new InputActionProperty(actions[h.Name + "/rotation"]);
            d.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            d.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            d.enabled = false;
            return d;
        }

        // ------------------------------------------------------------------ starting XR
        /// <summary>Tries OpenXR; on failure stays on the desktop and tries again every few seconds.</summary>
        public IEnumerator StartXR()
        {
            if (DesktopForced)
            {
                XRStatus = "Desktop mode (started with -desktop)";
                yield break;
            }
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                XRStatus = "XR is not configured in this build";
                yield break;
            }
            Starting = true;
            XRStatus = "Looking for the headset (Meta Quest Link)...";
            yield return settings.Manager.InitializeLoader();
            Starting = false;
            if (settings.Manager.activeLoader == null)
            {
                XRStatus = "No headset found. Put on your Meta Quest, turn on Quest Link, and the simulation will switch to it.";
                retryAt = Time.unscaledTime + 6f;
                yield break;
            }
            settings.Manager.StartSubsystems();
            yield return null;
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            XRActive = true;
            XRStatus = "Headset connected";
            headDriver.enabled = true;
            leftDriver.enabled = true;
            rightDriver.enabled = true;
            Head.localPosition = Vector3.zero;
            Head.localRotation = Quaternion.identity;
            // The desktop window shows the spectator view, not an eye's.
            XRSettings.gameViewRenderMode = GameViewRenderMode.None;
            Debug.Log("[bridge] XR started: " + settings.Manager.activeLoader.name);
            XRChanged?.Invoke(true);
        }

        public void StopXR()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null || settings.Manager.activeLoader == null) return;
            settings.Manager.StopSubsystems();
            settings.Manager.DeinitializeLoader();
            XRActive = false;
            headDriver.enabled = leftDriver.enabled = rightDriver.enabled = false;
            XRChanged?.Invoke(false);
        }

        void OnApplicationQuit() => StopXR();

        // ------------------------------------------------------------------ reading input
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!XRActive && !Starting && !DesktopForced && retryAt > 0 && Time.unscaledTime > retryAt)
            {
                retryAt = 0;
                StartCoroutine(StartXR());
            }
            if (XRActive && !AnyDisplayRunning())
            {
                // Link was closed: back to the desktop until it returns.
                StopXR();
                XRStatus = "The headset disconnected. Reconnect Quest Link to continue in VR.";
                retryAt = Time.unscaledTime + 5f;
            }
            if (XRActive) ReadControllers();
            else Desktop(dt);
            foreach (var h in Hands) h.Track(dt);
        }

        static readonly List<XRDisplaySubsystem> displays = new();

        static bool AnyDisplayRunning()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return true;
            return false;
        }

        void ReadControllers()
        {
            foreach (var h in Hands)
            {
                string n = h.Name;
                h.Tracked = actions[n + "/tracked"].IsPressed() || actions[n + "/position"].ReadValue<Vector3>() != Vector3.zero;
                var ap = actions[n + "/aimPosition"].ReadValue<Vector3>();
                var ar = actions[n + "/aimRotation"].ReadValue<Quaternion>();
                if (ar.x == 0 && ar.y == 0 && ar.z == 0 && ar.w == 0)
                {
                    // No aim pose from this runtime: point along the grip, tilted down as Touch aims.
                    h.Aim.localPosition = h.Grip.localPosition;
                    h.Aim.localRotation = h.Grip.localRotation * Quaternion.Euler(-30, 0, 0);
                }
                else
                {
                    h.Aim.localPosition = ap;
                    h.Aim.localRotation = ar;
                }
                h.Stick = actions[n + "/stick"].ReadValue<Vector2>();
                h.Trigger = actions[n + "/trigger"].ReadValue<float>();
                h.Squeeze = actions[n + "/grip"].ReadValue<float>();
                Edge(ref h.TriggerHeld, ref h.TriggerDown, ref h.TriggerUp, h.Trigger, 0.55f, 0.35f);
                Edge(ref h.GripHeld, ref h.GripDown, ref h.GripUp, h.Squeeze, 0.55f, 0.35f);
                Button(actions[n + "/primary"], ref h.Primary, ref h.PrimaryDown);
                Button(actions[n + "/secondary"], ref h.Secondary, ref h.SecondaryDown);
                Button(actions[n + "/menu"], ref h.Menu, ref h.MenuDown);
                Button(actions[n + "/stickClick"], ref h.StickClick, ref h.StickClickDown);
                h.ThumbTouch = actions[n + "/stickTouch"].IsPressed();
                var p = h.Pointer;
                p.Active = h.Tracked;
                p.Ray = new Ray(h.Aim.position, h.Aim.forward);
                p.Select = h.TriggerHeld;
                p.SelectDown = h.TriggerDown;
                p.SelectUp = h.TriggerUp;
                p.Tip = h.Tracked ? h.Tip : (Vector3?)null;
            }
            Mouse.Active = false;
        }

        static void Edge(ref bool held, ref bool down, ref bool up, float value, float on, float off)
        {
            bool was = held;
            held = was ? value > off : value > on;
            down = held && !was;
            up = !held && was;
        }

        static void Button(InputAction a, ref bool held, ref bool down)
        {
            bool was = held;
            held = a.IsPressed();
            down = held && !was;
        }

        // ------------------------------------------------------------------ desktop stand-in
        /// <summary>When true, the mouse and keys drive the desktop camera; lessons and tests can turn it off.</summary>
        public bool DesktopLook = true;

        void Desktop(float dt)
        {
            var kb = Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            Head.localPosition = desktopEye + Vector3.up * SeatedLift;
            if (mouse != null && DesktopLook && mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue();
                desktopYaw += d.x * 0.15f;
                desktopPitch = Mathf.Clamp(desktopPitch - d.y * 0.15f, -80, 80);
            }
            Head.localRotation = Quaternion.Euler(desktopPitch, desktopYaw, 0);
            if (kb != null && DesktopLook && !kb.ctrlKey.isPressed)
            {
                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move += Vector3.forward;
                if (kb.sKey.isPressed) move += Vector3.back;
                if (kb.aKey.isPressed) move += Vector3.left;
                if (kb.dKey.isPressed) move += Vector3.right;
                if (move != Vector3.zero)
                {
                    var yaw = Quaternion.Euler(0, Head.eulerAngles.y, 0);
                    Origin.position += yaw * move.normalized * (kb.shiftKey.isPressed ? 3.5f : 1.6f) * dt;
                }
            }
            // The mouse is the right controller's ray; the left hand rests beside the view.
            var r = Right;
            if (mouse != null && Cam != null)
            {
                var ray = Cam.ScreenPointToRay(mouse.position.ReadValue());
                r.Aim.position = ray.origin;
                r.Aim.rotation = Quaternion.LookRotation(ray.direction, Head.up);
                r.Grip.position = Head.position + Head.rotation * new Vector3(0.22f, -0.3f, 0.45f);
                r.Grip.rotation = Quaternion.LookRotation(ray.direction, Head.up);
                bool click = mouse.leftButton.isPressed;
                r.TriggerDown = mouse.leftButton.wasPressedThisFrame;
                r.TriggerUp = mouse.leftButton.wasReleasedThisFrame;
                r.TriggerHeld = click;
                r.Trigger = click ? 1 : 0;
                Mouse.Active = true;
                Mouse.Ray = ray;
                Mouse.Select = click;
                Mouse.SelectDown = mouse.leftButton.wasPressedThisFrame;
                Mouse.SelectUp = mouse.leftButton.wasReleasedThisFrame;
                Mouse.Tip = null;
            }
            var l = Left;
            l.Grip.position = Head.position + Head.rotation * new Vector3(-0.22f, -0.3f, 0.45f);
            l.Grip.rotation = Head.rotation * Quaternion.Euler(20, 0, 0);
            l.Aim.position = l.Grip.position;
            l.Aim.rotation = l.Grip.rotation;
            foreach (var h in Hands)
            {
                h.Tracked = h == Right;
                h.Pointer.Active = false;
                h.Pointer.Tip = null;
            }
            // Keys for the controllers' buttons (shown in the trainer overlay).
            if (kb == null) return;
            DesktopKeys(kb);
        }

        void DesktopKeys(Keyboard kb)
        {
            var r = Right;
            var l = Left;
            Key(kb.digit1Key, ref r.Primary, ref r.PrimaryDown);
            Key(kb.digit2Key, ref r.Secondary, ref r.SecondaryDown);
            Key(kb.digit3Key, ref l.Primary, ref l.PrimaryDown);
            Key(kb.digit4Key, ref l.Secondary, ref l.SecondaryDown);
            Key(kb.tabKey, ref l.Menu, ref l.MenuDown);
            bool g = kb.gKey.isPressed;
            r.GripDown = g && !r.GripHeld;
            r.GripUp = !g && r.GripHeld;
            r.GripHeld = g;
            r.Squeeze = g ? 1 : 0;
            bool eh = kb.hKey.isPressed;
            l.GripDown = eh && !l.GripHeld;
            l.GripUp = !eh && l.GripHeld;
            l.GripHeld = eh;
            l.Squeeze = eh ? 1 : 0;
            // Sticks: the arrow keys and page keys stand in for the thumbsticks.
            Vector2 ls = Vector2.zero, rs = Vector2.zero;
            if (kb.upArrowKey.isPressed) ls.y += 1;
            if (kb.downArrowKey.isPressed) ls.y -= 1;
            if (kb.leftArrowKey.isPressed) ls.x -= 1;
            if (kb.rightArrowKey.isPressed) ls.x += 1;
            if (kb.pageUpKey.isPressed) rs.y += 1;
            if (kb.pageDownKey.isPressed) rs.y -= 1;
            if (kb.commaKey.isPressed) rs.x -= 1;
            if (kb.periodKey.isPressed) rs.x += 1;
            if (Override != null)
            {
                ls = Override.LeftStick;
                rs = Override.RightStick;
            }
            l.Stick = ls;
            r.Stick = rs;
            Key(kb.qKey, ref l.StickClick, ref l.StickClickDown);
            Key(kb.eKey, ref r.StickClick, ref r.StickClickDown);
        }

        static void Key(UnityEngine.InputSystem.Controls.KeyControl k, ref bool held, ref bool down)
        {
            bool was = held;
            held = k.isPressed;
            down = held && !was;
        }

        /// <summary>Lets the automated tests hold the sticks.</summary>
        public class StickOverride { public Vector2 LeftStick, RightStick; }
        public StickOverride Override;

        public void SetDesktopView(Vector3 eyeWorld, float yaw, float pitch)
        {
            Origin.position = new Vector3(eyeWorld.x, 0, eyeWorld.z);
            Origin.rotation = Quaternion.identity;
            desktopEye = new Vector3(0, eyeWorld.y, 0);
            desktopYaw = yaw;
            desktopPitch = pitch;
            Head.localPosition = desktopEye;
            Head.localRotation = Quaternion.Euler(pitch, yaw, 0);
        }

        // ------------------------------------------------------------------ haptics
        public void Buzz(int hand, float amplitude, float seconds)
        {
            if (!XRActive || hand < 0 || hand > 1) return;
            var node = hand == 0 ? XRNode.LeftHand : XRNode.RightHand;
            InputDevice dev = InputDevices.GetDeviceAtXRNode(node);
            bool sent = false;
            try
            {
                if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    sent = dev.SendHapticImpulse(0, Mathf.Clamp01(amplitude), seconds);
            }
            catch (Exception) { }
            if (sent) return;
            try
            {
                UnityEngine.XR.OpenXR.Input.OpenXRInput.SendHapticImpulse(actions[(hand == 0 ? "left" : "right") + "/haptic"], Mathf.Clamp01(amplitude), seconds);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ placing the rig
        /// <summary>Moves the play area so the head stands at a floor point, looking along a direction.</summary>
        public void Place(Vector3 floorPoint, Vector3 facing)
        {
            facing = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            float headYaw = XRActive ? Head.localEulerAngles.y : 0;
            float want = Quaternion.LookRotation(facing).eulerAngles.y;
            Origin.rotation = Quaternion.Euler(0, want - headYaw, 0);
            if (!XRActive) desktopYaw = 0;
            Vector3 headOffset = Origin.TransformVector(new Vector3(Head.localPosition.x, 0, Head.localPosition.z));
            if (!XRActive) headOffset = Vector3.zero;
            Origin.position = new Vector3(floorPoint.x, 0, floorPoint.z) - headOffset;
        }

        /// <summary>Turns the play area about the head.</summary>
        public void Turn(float degrees)
        {
            Vector3 pivot = Head.position;
            Origin.RotateAround(new Vector3(pivot.x, 0, pivot.z), Vector3.up, degrees);
            if (!XRActive) desktopYaw += 0;
        }

        public void SetSeated(bool seated)
        {
            SeatedLift = seated ? 0.45f : 0f;
            TrackingSpace.localPosition = XRActive ? new Vector3(0, SeatedLift, 0) : Vector3.zero;
        }
    }
}
