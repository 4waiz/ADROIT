// SPDX-License-Identifier: LicenseRef-BRIDGE
// How pointers reach the interface: each controller casts a ray (and the desktop's mouse does too);
// a fingertip that pushes into a button presses it as well. The first widget along a ray is hovered;
// the trigger presses it and releasing over it clicks. A slider follows the ray while held.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bridge.UI
{
    public class Pointer
    {
        public int Id;               // 0 left hand, 1 right hand, 2 the desktop's mouse
        public bool Active;
        public Ray Ray;
        public bool Select;
        public bool SelectDown;
        public bool SelectUp;
        public Vector3? Tip;         // the controller's tip, for poking
        public Widget Hover;
        public Widget Pressed;
        public bool HitUI;
        public float UIDistance;
        public Vector3 HitPoint;
        public bool Poking;
        float pokeCooldown;
        Widget poked;

        internal void TickPoke(float dt) => pokeCooldown -= dt;
        internal bool CanPoke => pokeCooldown <= 0;
        internal void Poked(Widget w) { poked = w; pokeCooldown = 0.35f; }
        internal Widget PokedWidget { get => poked; set => poked = value; }
    }

    public static class UIInput
    {
        public static event Action<Pointer, Widget> HoverEnter;
        public static event Action<Pointer, Widget> Clicked;
        public static event Action<Pointer, Widget> Pressed;
        static readonly HashSet<Widget> hovered = new();
        static readonly HashSet<Widget> now = new();
        static readonly Collider[] touch = new Collider[8];

        public static void Process(IList<Pointer> pointers, float dt)
        {
            now.Clear();
            foreach (var p in pointers)
            {
                p.TickPoke(dt);
                Widget over = null;
                p.HitUI = false;
                p.Poking = false;
                if (p.Tip.HasValue) over = Poke(p);
                if (over == null && p.Active && Physics.Raycast(p.Ray, out var hit, 6f, Layers.UIMask, QueryTriggerInteraction.Collide))
                {
                    over = hit.collider.GetComponent<Widget>() ?? hit.collider.GetComponentInParent<Widget>();
                    p.HitUI = true;
                    p.UIDistance = hit.distance;
                    p.HitPoint = hit.point;
                }
                if (over != null && (!over.Interactable || !over.isActiveAndEnabled)) over = null;
                if (over != null) now.Add(over);
                if (over != p.Hover)
                {
                    if (over != null) HoverEnter?.Invoke(p, over);
                    p.Hover = over;
                }
                if (p.Poking) continue;
                if (p.SelectDown && over != null)
                {
                    p.Pressed = over;
                    over.Pressed = true;
                    Pressed?.Invoke(p, over);
                }
                if (p.Pressed != null && p.Select && p.Pressed.Draggable)
                {
                    var w = p.Pressed;
                    var plane = new Plane(w.transform.forward, w.transform.position);
                    if (plane.Raycast(p.Ray, out float enter)) w.OnDrag(p, w.transform.InverseTransformPoint(p.Ray.GetPoint(enter)));
                }
                if (p.SelectUp && p.Pressed != null)
                {
                    var w = p.Pressed;
                    w.Pressed = false;
                    p.Pressed = null;
                    if (w == over && w.Interactable)
                    {
                        w.OnClick(p);
                        Clicked?.Invoke(p, w);
                    }
                }
                if (!p.Select && p.Pressed != null)
                {
                    p.Pressed.Pressed = false;
                    p.Pressed = null;
                }
            }
            foreach (var w in hovered) if (w != null && !now.Contains(w)) w.Hovered = false;
            foreach (var w in now) w.Hovered = true;
            hovered.Clear();
            foreach (var w in now) hovered.Add(w);
        }

        /// <summary>A fingertip pushing through a widget's face clicks it once.</summary>
        static Widget Poke(Pointer p)
        {
            Vector3 tip = p.Tip.Value;
            int n = Physics.OverlapSphereNonAlloc(tip, 0.012f, touch, Layers.UIMask, QueryTriggerInteraction.Collide);
            Widget found = null;
            for (int i = 0; i < n; i++)
            {
                var w = touch[i].GetComponent<Widget>() ?? touch[i].GetComponentInParent<Widget>();
                if (w == null || !w.Interactable || w.Hit == null) continue;
                Vector3 local = w.transform.InverseTransformPoint(tip);
                var size = w.Hit.size;
                if (Mathf.Abs(local.x) > size.x / 2 || Mathf.Abs(local.y) > size.y / 2) continue;
                // In front of the face by up to 1.2 cm, or pushed through it.
                if (local.z > -0.012f && local.z < 0.03f)
                {
                    found = w;
                    break;
                }
            }
            if (found == null)
            {
                if (p.PokedWidget != null)
                {
                    p.PokedWidget.Pressed = false;
                    p.PokedWidget = null;
                }
                return null;
            }
            p.Poking = true;
            p.HitUI = true;
            p.HitPoint = tip;
            found.Pressed = true;
            if (p.PokedWidget != found && p.CanPoke && !found.Draggable)
            {
                found.OnClick(p);
                Clicked?.Invoke(p, found);
                p.Poked(found);
            }
            p.PokedWidget = found;
            return found;
        }
    }
}
