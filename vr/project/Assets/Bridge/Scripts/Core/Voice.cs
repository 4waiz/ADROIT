// SPDX-License-Identifier: LicenseRef-BRIDGE
// The voice guide: a narrator (Kokoro's af_heart voice) reads each lesson step, hint and tour stop.
// Every line is an OGG in StreamingAssets/Voice, named by a hash of the words spoken. vr/build.ps1
// asks the app for every line it can say (-autotest narration), speaks the new ones with
// vr/tools/voice.py, and puts the files beside the player. A line with no file is not spoken.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace Bridge
{
    public class Voice : MonoBehaviour
    {
        public static Voice I { get; private set; }

        /// <summary>Speak at all (Settings: Voice guide).</summary>
        public bool On = true;
        AudioSource source;
        int ticket;
        bool loading;
        string queued;
        readonly Dictionary<string, AudioClip> clips = new();
        readonly Queue<string> recent = new();

        /// <summary>A line is playing or about to.</summary>
        public bool Speaking => loading || source != null && source.isPlaying;
        /// <summary>The words of the line playing now, or null.</summary>
        public string Current { get; private set; }

        public static string Dir => Path.Combine(Application.streamingAssetsPath, "Voice");

        public static Voice Create(Transform parent)
        {
            var go = new GameObject("Voice guide");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<Voice>();
            v.source = go.AddComponent<AudioSource>();
            v.source.playOnAwake = false;
            v.source.spatialBlend = 0;   // a narrator, not a sound in the room
            v.source.priority = 0;
            I = v;
            return v;
        }

        /// <summary>
        /// Speaks a line (the app's rich text is fine), cutting off whatever was being said.
        /// With <paramref name="after"/>, waits for the line playing now to end instead.
        /// </summary>
        public void Say(string line, float delay = 0.25f, bool after = false)
        {
            string text = Speech(line);
            if (string.IsNullOrEmpty(text) || !On) return;
            if (after && Speaking) { queued = text; return; }
            Stop();
            StartCoroutine(Play(text, delay, ticket));
        }

        public void Stop()
        {
            ticket++;
            queued = null;
            loading = false;
            Current = null;
            if (source != null) source.Stop();
        }

        void Update()
        {
            if (queued != null && !Speaking)
            {
                string next = queued;
                queued = null;
                StartCoroutine(Play(next, 0.15f, ticket));
            }
            if (source != null) source.volume = Sfx.Volume;
        }

        IEnumerator Play(string text, float delay, int mine)
        {
            loading = true;
            string key = Key(text);
            if (!clips.TryGetValue(key, out var clip))
            {
                string path = Path.Combine(Dir, key + ".ogg");
                if (!File.Exists(path))
                {
                    if (mine == ticket) loading = false;
                    yield break;
                }
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS))
                {
                    ((DownloadHandlerAudioClip)req.downloadHandler).compressed = true;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[bridge] voice {key}: {req.error}");
                        if (mine == ticket) loading = false;
                        yield break;
                    }
                    clip = DownloadHandlerAudioClip.GetContent(req);
                }
                Remember(key, clip);
            }
            float until = Time.unscaledTime + delay;
            while (Time.unscaledTime < until && mine == ticket) yield return null;
            if (mine != ticket) yield break;
            loading = false;
            Current = text;
            source.clip = clip;
            source.volume = Sfx.Volume;
            source.Play();
        }

        void Remember(string key, AudioClip clip)
        {
            clips[key] = clip;
            recent.Enqueue(key);
            while (recent.Count > 24)
            {
                var old = recent.Dequeue();
                if (clips.TryGetValue(old, out var c) && c != source.clip) { Destroy(c); clips.Remove(old); }
            }
        }

        // ------------------------------------------------------------------ the words
        static readonly Regex buttonWithWord = new(@"\bthe <b>([ABXY])</b> button\b", RegexOptions.IgnoreCase);
        static readonly Regex button = new(@"<b>([ABXY])</b>");
        static readonly Regex tag = new(@"<[^>]+>");
        static readonly Regex space = new(@"\s+");

        /// <summary>
        /// What the narrator says for a line of the app's text: the tags taken out, the A, B, X and Y
        /// buttons named so the voice reads them as letters, and the lines run together.
        /// </summary>
        public static string Speech(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return "";
            string s = buttonWithWord.Replace(rich, "the $1-button");
            s = button.Replace(s, "the $1-button");
            s = tag.Replace(s, "");
            s = s.Replace("·", ",").Replace(" ", " ");
            return space.Replace(s, " ").Trim();
        }

        /// <summary>The file name for a spoken line: FNV-1a over its UTF-8 bytes, as 16 hex digits.</summary>
        public static string Key(string speech)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(speech))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }
    }
}
