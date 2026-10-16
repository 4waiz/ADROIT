// SPDX-License-Identifier: LicenseRef-BRIDGE
// The app's sounds, synthesised when it starts (there are no audio files to ship or license): the
// interface's ticks and chimes, warnings, the emergency-stop alarm, and the room: the arm's servo hum
// that rises with its speed, the gripper, the conveyor, the hall's air.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bridge
{
    public static class Sfx
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> clips = new();
        static readonly List<AudioSource> pool = new();
        static Transform root;
        static int next;
        static readonly System.Random rng = new(7);
        public static float Volume = 0.9f;

        public static void Init(Transform parent)
        {
            root = new GameObject("Sfx").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < 14; i++)
            {
                var s = root.gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0;
                pool.Add(s);
            }
            Make("hover", 0.035f, t => Tone(t, 2600, 0.0f, 40) * 0.18f);
            Make("click", 0.06f, t => (Tone(t, 1700, 0, 55) * 0.5f + Noise() * Env(t, 0.004f) * 0.35f) * 0.6f);
            Make("toggle", 0.09f, t => Tone(t, 1250, 0, 35) * 0.4f + Tone(t - 0.04f, 1650, 0, 40) * 0.35f);
            Make("open", 0.22f, t => Sweep(t, 600, 1400, 0.2f) * Env2(t, 0.01f, 0.2f) * 0.25f);
            Make("close", 0.22f, t => Sweep(t, 1300, 520, 0.2f) * Env2(t, 0.01f, 0.2f) * 0.25f);
            Make("success", 0.9f, t => (Bell(t, 1046.5f) + Bell(t - 0.11f, 1318.5f) + Bell(t - 0.22f, 1568f)) * 0.22f);
            Make("step", 0.5f, t => (Bell(t, 880) + Bell(t - 0.08f, 1318.5f)) * 0.2f);
            Make("complete", 1.6f, t => (Bell(t, 784) + Bell(t - 0.12f, 988) + Bell(t - 0.24f, 1175) + Bell(t - 0.42f, 1568) * 1.2f) * 0.2f);
            Make("warn", 0.5f, t => (Square(t, 880) * Env2(t, 0.005f, 0.16f) + Square(t - 0.22f, 660) * Env2(t - 0.22f, 0.005f, 0.16f)) * 0.12f);
            Make("error", 0.45f, t => Square(t, 220) * Env2(t, 0.005f, 0.4f) * 0.14f);
            Make("blocked", 0.12f, t => (Noise() * 0.5f + Square(t, 140)) * Env2(t, 0.002f, 0.1f) * 0.18f);
            Make("grasp", 0.12f, t => (Noise() * Env(t, 0.01f) * 0.6f + Tone(t, 320, 0, 30)) * 0.35f);
            Make("whoosh", 0.35f, t => Noise() * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI) * 0.12f);
            Make("waypoint", 0.35f, t => (Bell(t, 1567.98f) * 0.7f + Bell(t - 0.06f, 2093f) * 0.5f) * 0.25f);
            Make("beep", 0.18f, t => Tone(t, 2000, 0, 12) * Env2(t, 0.005f, 0.15f) * 0.25f);
            MakeLoop("alarm", 1.0f, t => Square(t, t % 1 < 0.5f ? 960 : 720) * 0.11f);
            MakeLoop("servo", 2.0f, t => (Saw(t, 196) * 0.3f + Saw(t, 293.7f) * 0.15f + Brown() * 0.6f) * 0.35f);
            MakeLoop("gripper", 1.0f, t => (Saw(t, 410) * 0.25f + Noise() * 0.15f) * 0.3f);
            MakeLoop("conveyor", 2.0f, t => (Brown() * 0.8f + Clack(t)) * 0.3f);
            MakeLoop("hall", 6.0f, t => (Brown() * 0.9f + Mathf.Sin(t * 2 * Mathf.PI * 50) * 0.08f + Mathf.Sin(t * 2 * Mathf.PI * 100) * 0.05f) * 0.25f);
        }

        // ------------------------------------------------------------------ synthesis
        static float Tone(float t, float f, float a, float decay) => t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * decay);
        static float Bell(float t, float f) => t < 0 ? 0 : (Mathf.Sin(2 * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2 * Mathf.PI * f * 2.01f * t) * Mathf.Exp(-t * 9)) * Mathf.Exp(-t * 4.5f) * Mathf.Min(1, t * 400);
        static float Square(float t, float f) => t < 0 ? 0 : Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)) * 0.6f;
        static float Saw(float t, float f) => 2 * (t * f - Mathf.Floor(t * f + 0.5f));
        static float Sweep(float t, float f0, float f1, float d) => Mathf.Sin(2 * Mathf.PI * (f0 * t + (f1 - f0) * t * t / (2 * d)));
        static float Env(float t, float d) => t < 0 ? 0 : Mathf.Exp(-t / d);
        static float Env2(float t, float attack, float length) => t < 0 || t > length ? 0 : Mathf.Min(1, t / attack) * (1 - t / length);
        static float Noise() => (float)(rng.NextDouble() * 2 - 1);
        static float brown;
        static float Brown() { brown = Mathf.Clamp(brown + Noise() * 0.04f, -1, 1) * 0.995f; return brown * 3f; }
        static float Clack(float t) { float p = t * 3.1f % 1; return p < 0.01f ? Noise() * 1.2f * (1 - p / 0.01f) : 0; }

        static void Make(string name, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1, 1);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }

        /// <summary>A loop with its ends cross-faded so it repeats without a click.</summary>
        static void MakeLoop(string name, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            int fade = Rate / 10;
            var data = new float[n + fade];
            for (int i = 0; i < data.Length; i++) data[i] = f(i / (float)Rate);
            var loop = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = data[i];
                if (i < fade)
                {
                    float k = i / (float)fade;
                    v = data[i] * k + data[n + i] * (1 - k);
                }
                loop[i] = Mathf.Clamp(v, -1, 1);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(loop, 0);
            clips[name] = clip;
        }

        // ------------------------------------------------------------------ playback
        public static void Play(string name, float volume = 1f, float pitch = 1f)
        {
            if (!clips.TryGetValue(name, out var clip) || pool.Count == 0) return;
            var s = pool[next];
            next = (next + 1) % pool.Count;
            s.pitch = pitch;
            s.PlayOneShot(clip, volume * Volume);
        }

        public static void PlayAt(string name, Vector3 at, float volume = 1f)
        {
            if (!clips.TryGetValue(name, out var clip)) return;
            AudioSource.PlayClipAtPoint(clip, at, volume * Volume);
        }

        /// <summary>A looping source at a place in the room (or on the listener if at is null).</summary>
        public static AudioSource Loop(string name, Transform at, float volume, float minDistance = 1f, float maxDistance = 18f)
        {
            if (!clips.TryGetValue(name, out var clip)) return null;
            var go = new GameObject("loop " + name);
            go.transform.SetParent(at != null ? at : root, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.volume = volume * Volume;
            s.spatialBlend = at != null ? 1 : 0;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.dopplerLevel = 0;
            s.Play();
            return s;
        }
    }
}
