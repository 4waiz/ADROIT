// SPDX-License-Identifier: LicenseRef-BRIDGE
// The Academy's lesson engine. A lesson is a list of steps; a step says what to look at and what to
// press, checks for itself when you have done it (or waits for Next when there is only reading),
// can light up the controller buttons it needs, and puts labels, rings, targets and ghosts into the
// room, which go away when the step does. Quizzes are steps too. The panel that carries all this
// floats to the side of your view and follows you lazily.

using System;
using System.Collections.Generic;
using Bridge.App;
using Bridge.UI;
using Bridge.XR;
using TMPro;
using UnityEngine;

namespace Bridge.Learn
{
    public class Quiz
    {
        public string Question;
        public string[] Options;
        public int Answer;
        public string Why;
    }

    public class LessonStep
    {
        public string Title;
        public string Body;
        public string Hint;                  // shown after a while if the step is not done
        public Func<bool> Done;              // null: read, then Next
        public Action Enter, Exit;
        public Action<float> Tick;
        public Func<string> Status;          // a live line under the text
        public Quiz Quiz;
        public (int hand, Control control)[] Highlight;
        public string ActionLabel;           // an extra button on the panel
        public string ActionIcon = "refresh-cw";
        public Action OnAction;
        public bool Auto = true;             // move on by itself when done
        public float HintAfter = 25f;
    }

    public class Lesson
    {
        public string Id, Title, Summary, Icon;
        public int Minutes;
        public readonly List<LessonStep> Steps = new();
        public Action Begin, End;
        public Vector3? Spot;
        public Vector3 Facing = Vector3.left;

        public LessonStep Step(string title, string body, Func<bool> done = null)
        {
            var s = new LessonStep { Title = title, Body = body, Done = done };
            Steps.Add(s);
            return s;
        }

        public LessonStep Ask(string question, string why, int answer, params string[] options)
        {
            var s = new LessonStep { Title = "Quick check", Quiz = new Quiz { Question = question, Options = options, Answer = answer, Why = why } };
            Steps.Add(s);
            return s;
        }
    }

    /// <summary>Things a step or a lesson put into the room, cleared with it.</summary>
    public class Scene
    {
        readonly Transform root;
        readonly List<GameObject> step = new(), lesson = new();
        public bool LessonScope;

        public Scene(Transform parent)
        {
            root = new GameObject("lesson objects").transform;
            root.SetParent(parent, false);
        }

        public Transform Root => root;

        public T Keep<T>(T c) where T : Component
        {
            (LessonScope ? lesson : step).Add(c.gameObject);
            return c;
        }

        public GameObject Keep(GameObject g)
        {
            (LessonScope ? lesson : step).Add(g);
            return g;
        }

        public WorldLabel Label(string title, string sub, Transform anchor, Vector3 offset, Vector3 anchorOffset = default) => Keep(WorldLabel.Make(root, title, sub, anchor, offset, anchorOffset));
        public FloorMarker Marker(Vector3 at, string caption, float radius = 0.4f) => Keep(FloorMarker.Make(root, at, radius, caption, Pal.Brand));
        public HandTarget Target(Vector3 at, string caption, float radius = 0.025f) => Keep(HandTarget.Make(root, at, caption, radius));
        public PointerArrow Arrow(Transform target, Vector3 offset) => Keep(PointerArrow.Make(root, target, offset, Pal.Brand));

        public void ClearStep()
        {
            foreach (var g in step) if (g != null) UnityEngine.Object.Destroy(g);
            step.Clear();
        }

        public void ClearAll()
        {
            ClearStep();
            foreach (var g in lesson) if (g != null) UnityEngine.Object.Destroy(g);
            lesson.Clear();
        }
    }

    public class LessonRunner
    {
        readonly BridgeApp app;
        public readonly List<Lesson> Curriculum = new();
        public Lesson Current { get; private set; }
        public int LessonIndex { get; private set; } = -1;
        public int StepIndex { get; private set; }
        public LessonStep Step => Current != null && StepIndex < Current.Steps.Count ? Current.Steps[StepIndex] : null;
        public bool Active => Current != null;
        public bool StepComplete { get; private set; }
        public bool Finished { get; private set; }
        public float StepClock { get; private set; }
        public readonly Scene Scene;
        public LessonPanel Panel;
        public int QuizRight, QuizAsked;
        /// <summary>Events seen since the step began: the cell's ("grasp", "estop", ...) and buttons ("A", "Y", ...).</summary>
        public readonly HashSet<string> Seen = new();
        public readonly List<string> SeenDetail = new();
        float doneAt;
        int quizChoice = -1;
        public event Action<Lesson> LessonFinished;

        public LessonRunner(BridgeApp app)
        {
            this.app = app;
            Scene = new Scene(app.WorldRoot);
        }

        public bool Saw(string what) => Seen.Contains(what);
        public bool SawDetail(string what) => SeenDetail.Contains(what);

        public void Build()
        {
            app.Cell.Event += (n, d) => { Seen.Add(n); SeenDetail.Add(n + ":" + d); };
            app.Interaction.ButtonPressed += b => Seen.Add("button:" + b);
            Curriculum.AddRange(Course.Build(app, this));
            Panel = new LessonPanel(app, this);
        }

        public void Start(int index)
        {
            Exit(silent: true);
            app.Menus.Close();
            app.Tour.Stop();
            LessonIndex = index;
            Current = Curriculum[index];
            Finished = false;
            QuizRight = QuizAsked = 0;
            app.PrepareForLesson();
            Scene.LessonScope = true;
            Current.Begin?.Invoke();
            Scene.LessonScope = false;
            if (Current.Spot.HasValue) app.GoTo(Current.Spot.Value, Current.Facing);
            StepIndex = 0;
            EnterStep();
            Panel.Show(true);
            Sfx.Play("open");
        }

        void EnterStep()
        {
            StepComplete = false;
            StepClock = 0;
            doneAt = -1;
            quizChoice = -1;
            Seen.Clear();
            SeenDetail.Clear();
            Scene.ClearStep();
            var s = Step;
            foreach (var m in app.Interaction.Models) m.ClearHighlights();
            if (s == null) return;
            s.Enter?.Invoke();
            if (s.Highlight != null)
                foreach (var (hand, control) in s.Highlight)
                {
                    app.Interaction.Models[hand].Highlight(control, true);
                    app.Interaction.Models[hand].HintsForced = true;
                }
            else foreach (var m in app.Interaction.Models) m.HintsForced = app.Settings.AlwaysHints;
            Panel.ShowStep();
        }

        void ExitStep()
        {
            var s = Step;
            s?.Exit?.Invoke();
            Scene.ClearStep();
        }

        public bool CanGoNext => Step != null && (Step.Done == null && Step.Quiz == null || StepComplete || Step.Quiz != null && quizChoice == Step.Quiz.Answer);

        /// <summary>Next: allowed when the step is done (or only asks you to read).</summary>
        public void Next(bool fromButton = false)
        {
            if (!Active) return;
            if (Finished)
            {
                if (fromButton) return;
                return;
            }
            if (!CanGoNext)
            {
                if (fromButton) { Sfx.Play("blocked", 0.5f); Panel.Nudge(); }
                return;
            }
            ExitStep();
            StepIndex++;
            if (StepIndex >= Current.Steps.Count)
            {
                Finish();
                return;
            }
            Sfx.Play("step", 0.7f);
            EnterStep();
        }

        /// <summary>The trainer moves the trainee on, done or not.</summary>
        public void Skip()
        {
            if (!Active || Finished) return;
            StepComplete = true;
            if (Step?.Quiz != null) quizChoice = Step.Quiz.Answer;
            Next();
        }

        public void Back()
        {
            if (!Active || Finished || StepIndex == 0) return;
            ExitStep();
            StepIndex--;
            EnterStep();
        }

        /// <summary>Starts the current step over (its objects and checks reset).</summary>
        public void Repeat()
        {
            if (!Active || Finished) return;
            ExitStep();
            EnterStep();
            Sfx.Play("whoosh", 0.4f);
        }

        public void Exit(bool silent = false)
        {
            if (!Active) return;
            ExitStep();
            Current.End?.Invoke();
            Scene.ClearAll();
            Current = null;
            LessonIndex = -1;
            Finished = false;
            foreach (var m in app.Interaction.Models) { m.ClearHighlights(); m.HintsForced = app.Settings.AlwaysHints; }
            Panel.Show(false);
            app.AfterLesson();
            if (!silent) Sfx.Play("close", 0.5f);
        }

        void Finish()
        {
            Finished = true;
            app.Progress.MarkDone(Current.Id, QuizAsked > 0 ? QuizRight / (float)QuizAsked : 1f);
            Sfx.Play("complete");
            Panel.ShowFinished();
            LessonFinished?.Invoke(Current);
        }

        public void Answer(int option)
        {
            var s = Step;
            if (s?.Quiz == null || quizChoice == s.Quiz.Answer) return;
            bool first = quizChoice < 0;
            quizChoice = option;
            if (first) QuizAsked++;
            bool right = option == s.Quiz.Answer;
            if (right && first) QuizRight++;
            if (right)
            {
                StepComplete = true;
                Sfx.Play("success", 0.8f);
            }
            else Sfx.Play("error", 0.7f);
            Panel.ShowAnswer(option, right);
        }

        public int QuizChoice => quizChoice;

        public void Tick(float dt)
        {
            if (!Active || Finished) return;
            var s = Step;
            if (s == null) return;
            StepClock += dt;
            s.Tick?.Invoke(dt);
            if (!StepComplete && s.Done != null && s.Done())
            {
                StepComplete = true;
                doneAt = StepClock;
                Sfx.Play("success", 0.8f);
                app.XR.Buzz(1, 0.3f, 0.05f);
                Panel.ShowStepDone();
            }
            if (StepComplete && s.Done != null && s.Auto && doneAt >= 0 && StepClock - doneAt > 1.6f) Next();
            Panel.Tick(dt);
        }
    }

    // ====================================================================== the panel
    public class LessonPanel
    {
        readonly BridgeApp app;
        readonly LessonRunner runner;
        public readonly Window Window;
        TextMeshPro kicker, counter, title, body, status, hint, done;
        Pane doneChip;
        Button back, repeat, next, exit, action;
        readonly List<Button> options = new();
        TextMeshPro why;
        readonly List<Pane> dots = new();
        Transform page;
        float nudge;
        const float Wd = 0.66f;

        public LessonPanel(BridgeApp app, LessonRunner runner)
        {
            this.app = app;
            this.runner = runner;
            Window = Window.Make(app.UIRoot, "lesson", new Vector2(Wd, 0.5f), null, null, null, true);
            Window.Follow = true;
            Window.FollowYaw = -30f;
            Window.FollowDistance = 0.9f;
            Window.FollowDrop = 0.1f;
            Window.FollowCone = 38f;
            Window.Show(false, true);
            Window.gameObject.SetActive(false);
        }

        public void Show(bool on)
        {
            if (on)
            {
                Window.gameObject.SetActive(true);
                var head = Bridge.UI.Rig.Head;
                if (head != null) Window.PlaceInFront(head, Window.FollowDistance, Window.FollowYaw, Window.FollowDrop);
            }
            Window.Show(on);
        }

        public void Recentre()
        {
            var head = Bridge.UI.Rig.Head;
            if (head != null && Window.Visible) Window.PlaceInFront(head, Window.FollowDistance, Window.FollowYaw, Window.FollowDrop);
        }

        void Reset()
        {
            if (page != null) UnityEngine.Object.Destroy(page.gameObject);
            page = new GameObject("page").transform;
            page.gameObject.layer = Layers.UI;
            page.SetParent(Window.Content, false);
            options.Clear();
            dots.Clear();
            why = null;
            action = null;
        }

        TextMeshPro Txt(string text, float size, Color color, string font, float y, float h, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = Bridge.UI.Text.Make(page, text, size, color, align, Wd - UIStyle.Pad * 2, h, font, true, new Vector3(0, y - h / 2, 0));
            return t;
        }

        /// <summary>Lays out the current step; the window grows to fit it.</summary>
        public void ShowStep()
        {
            Reset();
            var lesson = runner.Current;
            var s = runner.Step;
            if (lesson == null || s == null) return;
            float top = 0;
            float y = top - UIStyle.Pad;
            kicker = Txt($"MODULE {runner.LessonIndex + 1}  ·  {lesson.Title.ToUpperInvariant()}", UIStyle.Tiny, Pal.Brand, "Figtree-Bold", y, 0.025f);
            kicker.characterSpacing = 6;
            counter = Txt($"{runner.StepIndex + 1} / {lesson.Steps.Count}", UIStyle.Tiny, Pal.InkFaint, "IBMPlexMono-Regular", y, 0.025f, TextAlignmentOptions.TopRight);
            counter.rectTransform.sizeDelta = new Vector2(Wd - UIStyle.Pad * 2 - 0.05f, 0.025f);
            counter.rectTransform.localPosition += new Vector3(-0.025f, 0, 0);
            exit = Button.Make(page, "", new Vector2(0.036f, 0.036f), () => runner.Exit(), ButtonStyle.Ghost, "x", UIStyle.Tiny, new Vector3(Wd / 2 - UIStyle.Pad, y - 0.008f, 0));
            y -= 0.034f;
            title = Txt(s.Title, UIStyle.Heading, Pal.Ink, "Figtree-Bold", y, 0.08f);
            float th = Bridge.UI.Text.Height(title);
            Fit(title, y, th);
            y -= th + 0.012f;
            if (s.Quiz != null)
            {
                body = Txt(s.Quiz.Question, UIStyle.Body, Pal.Ink, "Figtree-SemiBold", y, 0.2f);
                float bh = Bridge.UI.Text.Height(body);
                Fit(body, y, bh);
                y -= bh + 0.016f;
                for (int i = 0; i < s.Quiz.Options.Length; i++)
                {
                    int k = i;
                    var o = Button.Make(page, $"{(char)('A' + i)}.  {s.Quiz.Options[i]}", new Vector2(Wd - UIStyle.Pad * 2, 0.05f), () => runner.Answer(k), ButtonStyle.Secondary, null, UIStyle.Small, new Vector3(0, y - 0.025f, 0), null, TextAlignmentOptions.Left);
                    options.Add(o);
                    y -= 0.058f;
                }
                why = Txt("", UIStyle.Small, Pal.InkSoft, "Figtree-Medium", y - 0.004f, 0.08f);
                y -= 0.07f;
            }
            else
            {
                body = Txt(s.Body, UIStyle.Body, Pal.InkSoft, "Figtree-Regular", y, 0.3f);
                float bh = Bridge.UI.Text.Height(body);
                Fit(body, y, bh);
                y -= bh + 0.012f;
                status = Txt("", UIStyle.Small, Pal.Info, "Figtree-SemiBold", y, 0.03f);
                y -= s.Status != null ? 0.035f : 0;
                hint = Txt("", UIStyle.Small, Pal.Warn, "Figtree-Medium", y, 0.05f);
                y -= 0.01f;
            }
            // The done chip, the step dots and the buttons along the bottom.
            y -= 0.02f;
            doneChip = Pane.Make(page, "done", new Vector2(0.15f, 0.036f), new Color(Pal.Ok.r, Pal.Ok.g, Pal.Ok.b, 0.0f), 0.018f, blur: false, pos: new Vector3(-Wd / 2 + UIStyle.Pad + 0.075f, y, -0.001f));
            done = Bridge.UI.Text.Make(page, "", UIStyle.Small, Color.white, TextAlignmentOptions.Center, 0.15f, 0.036f, "Figtree-Bold", false, new Vector3(-Wd / 2 + UIStyle.Pad + 0.075f, y, 0));
            if (s.ActionLabel != null)
                action = Button.Make(page, s.ActionLabel, new Vector2(0.2f, 0.036f), () => s.OnAction?.Invoke(), ButtonStyle.Secondary, s.ActionIcon, UIStyle.Tiny, new Vector3(-Wd / 2 + UIStyle.Pad + 0.26f, y, 0));
            int n = lesson.Steps.Count;
            float dw = Mathf.Min(0.016f, 0.22f / n);
            for (int i = 0; i < n; i++)
            {
                var d = Pane.Make(page, "dot", new Vector2(dw * 0.7f, 0.007f), i < runner.StepIndex ? Pal.Brand : i == runner.StepIndex ? Pal.Ink : new Color(0.5f, 0.55f, 0.6f, 0.4f), 0.0035f, blur: false,
                    pos: new Vector3(Wd / 2 - UIStyle.Pad - 0.29f - (n - 1 - i) * dw, y, -0.001f));
                dots.Add(d);
            }
            back = Button.Make(page, "", new Vector2(0.05f, 0.05f), () => runner.Back(), ButtonStyle.Ghost, "arrow-left", UIStyle.Small, new Vector3(Wd / 2 - UIStyle.Pad - 0.22f, y, 0));
            repeat = Button.Make(page, "", new Vector2(0.05f, 0.05f), () => runner.Repeat(), ButtonStyle.Ghost, "rotate-ccw", UIStyle.Small, new Vector3(Wd / 2 - UIStyle.Pad - 0.165f, y, 0));
            next = Button.Make(page, "Next", new Vector2(0.13f, 0.05f), () => runner.Next(true), ButtonStyle.Primary, "arrow-right", UIStyle.Small, new Vector3(Wd / 2 - UIStyle.Pad - 0.065f, y, 0));
            y -= 0.025f + UIStyle.Pad;
            float height = -y;
            Window.Resize(new Vector2(Wd, height));
            page.localPosition = new Vector3(0, height / 2, 0);
            Tick(0);
        }

        static void Fit(TextMeshPro t, float y, float h)
        {
            t.rectTransform.sizeDelta = new Vector2(t.rectTransform.sizeDelta.x, h);
            t.rectTransform.localPosition = new Vector3(t.rectTransform.localPosition.x, y - h / 2, t.rectTransform.localPosition.z);
        }

        public void ShowAnswer(int option, bool right)
        {
            var q = runner.Step?.Quiz;
            if (q == null) return;
            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                if (i == q.Answer && right) { o.Style = ButtonStyle.Success; }
                else if (i == option && !right) { o.Style = ButtonStyle.Danger; }
            }
            if (why != null) why.text = (right ? "<color=#1FA463><b>Right.</b></color> " : "<color=#E5383B><b>Not quite.</b></color> Try again. ") + (right ? q.Why : "");
            if (right) ShowStepDone();
        }

        public void ShowStepDone()
        {
            if (doneChip == null) return;
            doneChip.SetTint(new Color(Pal.Ok.r, Pal.Ok.g, Pal.Ok.b, 1));
            done.text = "Done";
        }

        public void Nudge() => nudge = 1f;

        public void ShowFinished()
        {
            Reset();
            var lesson = runner.Current;
            float y = -UIStyle.Pad;
            Txt("MODULE COMPLETE", UIStyle.Tiny, Pal.Ok, "Figtree-Bold", y, 0.025f).characterSpacing = 6;
            y -= 0.03f;
            Icon.Make(page, "badge-check", 0.08f, Pal.Ok, new Vector3(-Wd / 2 + UIStyle.Pad + 0.04f, y - 0.045f, 0));
            var t = Bridge.UI.Text.Make(page, lesson.Title, UIStyle.Title, Pal.Ink, TextAlignmentOptions.Left, Wd - 0.17f, 0.09f, "Figtree-ExtraBold", true, new Vector3(0.05f, y - 0.045f, 0));
            y -= 0.1f;
            string score = runner.QuizAsked > 0 ? $"Quick checks: {runner.QuizRight} of {runner.QuizAsked} right first time." : "";
            Txt($"Well done. {score}", UIStyle.Body, Pal.InkSoft, "Figtree-Regular", y, 0.05f);
            y -= 0.06f;
            bool more = runner.LessonIndex + 1 < runner.Curriculum.Count;
            int nextIndex = runner.LessonIndex + 1;
            if (more)
                Button.Make(page, "Next module: " + runner.Curriculum[nextIndex].Title, new Vector2(Wd - UIStyle.Pad * 2, 0.06f), () => runner.Start(nextIndex), ButtonStyle.Primary, "arrow-right", UIStyle.Small, new Vector3(0, y - 0.03f, 0));
            else
                Button.Make(page, "See your certificate", new Vector2(Wd - UIStyle.Pad * 2, 0.06f), () => { runner.Exit(); app.Certificate.Show(); }, ButtonStyle.Primary, "award", UIStyle.Small, new Vector3(0, y - 0.03f, 0));
            y -= 0.07f;
            Button.Make(page, "Back to the Academy", new Vector2(Wd - UIStyle.Pad * 2, 0.05f), () => { runner.Exit(); app.Menus.Open(app.Menus.Academy); }, ButtonStyle.Secondary, "graduation-cap", UIStyle.Small, new Vector3(0, y - 0.025f, 0));
            y -= 0.05f + UIStyle.Pad;
            Window.Resize(new Vector2(Wd, -y));
            page.localPosition = new Vector3(0, -y / 2, 0);
        }

        public void Tick(float dt)
        {
            var s = runner.Step;
            if (s == null || runner.Finished || next == null) return;
            next.Interactable = runner.CanGoNext;
            back.Interactable = runner.StepIndex > 0;
            if (status != null) status.text = s.Status != null ? s.Status() : "";
            if (hint != null) hint.text = !runner.StepComplete && s.Hint != null && runner.StepClock > s.HintAfter ? "Tip: " + s.Hint : "";
            nudge = Mathf.MoveTowards(nudge, 0, dt * 3);
            if (nudge > 0) Window.transform.position += Window.transform.right * Mathf.Sin(nudge * 30) * 0.002f;
        }
    }
}
