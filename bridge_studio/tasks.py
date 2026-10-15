# SPDX-License-Identifier: Apache-2.0
"""Mission sequences: a list of steps the operator builds, run as a routine.

A task is data -- ``[{"op": "move", "arm": "right", "target": {...}}, ...]`` --
compiled onto the studio's own routine primitives,
:func:`~robosimstudio_studio.routine.reach`, :func:`~robosimstudio_studio.routine.grip`
and :func:`~robosimstudio_studio.routine.wait`. So a step moves the arm's cursor
the way a hand on the gizmo does, the arm follows through IK, and a step that
cannot finish says how far short it stopped (``RoutineError``) instead of being
marked done. Targets that name an object or a region are resolved when the step
*starts*, from where that thing is then, not when the task was written.

Ops:

* ``home`` -- the arm's home pose.
* ``move`` -- to a target: ``pose`` (world position, optional quaternion),
  ``object`` (a thing's current position plus an offset), ``region`` (a goal
  region's centre at a height over the bench) or ``relative`` (the hand plus an
  offset). Orientation ``keep`` (the cursor's), ``down`` (the home pose's) or
  ``grasp`` (top-down, jaws across the object's faces).
* ``grip`` -- 0 open .. 1 closed, then let the fingers move.
* ``wait`` -- simulated seconds.
"""

from __future__ import annotations

from collections.abc import Callable, Iterator
from typing import Any

import numpy as np

__all__ = ["OPS", "TaskError", "compile_task"]

OPS = ("home", "move", "grip", "wait")
MAX_STEPS = 64


class TaskError(ValueError):
    """A task that cannot be run as written; the message names the step."""


def _vec(value: Any, n: int, what: str) -> np.ndarray:
    try:
        out = np.asarray(value, dtype=np.float64).reshape(n)
    except Exception:
        raise TaskError(f"{what} needs {n} numbers") from None
    if not np.isfinite(out).all():
        raise TaskError(f"{what} must be finite")
    return out


def _label(step: dict[str, Any]) -> str:
    op, arm = step["op"], step.get("arm", "")
    if op == "home":
        return f"{arm}: move to home"
    if op == "grip":
        v = float(step["value"])
        return f"{arm}: {'close' if v >= 0.999 else 'open' if v <= 0.001 else f'grip {v:.0%}'} gripper"
    if op == "wait":
        return f"wait {float(step['seconds']):.1f} s"
    target = step["target"]
    kind = target["kind"]
    if kind == "object":
        dz = float(target.get("offset", (0, 0, 0))[2])
        where = f"{target['name']}" + (f" +{dz * 1000:.0f} mm" if dz > 0 else "")
        return f"{arm}: move to {where}"
    if kind == "region":
        return f"{arm}: move over {target['name']}"
    if kind == "relative":
        off = target["offset"]
        return f"{arm}: move by ({off[0] * 1000:.0f}, {off[1] * 1000:.0f}, {off[2] * 1000:.0f}) mm"
    return f"{arm}: move to pose"


def compile_task(host: Any, steps: Any) -> tuple[list[dict[str, Any]], Callable[..., Iterator[None]], tuple[str, ...]]:
    """Check a task against the live scene and build its runner.

    Returns:
        ``(plan, make, arms)``: the plan the page draws (one entry per step, with
        a status), ``make(get_runner)`` returning the generator, and the arms the
        task drives -- which the host reserves while it runs.
    """
    sim = host.sim
    if not isinstance(steps, list) or not steps:
        raise TaskError("a task is a non-empty list of steps")
    if len(steps) > MAX_STEPS:
        raise TaskError(f"a task holds at most {MAX_STEPS} steps")
    names = set(sim.names())
    regions = set(getattr(sim.scene.handles, "regions", {}) or {})
    plan: list[dict[str, Any]] = []
    arms: list[str] = []
    for i, raw in enumerate(steps):
        where = f"step {i + 1}"
        if not isinstance(raw, dict):
            raise TaskError(f"{where} is not an object")
        op = raw.get("op")
        if op not in OPS:
            raise TaskError(f"{where}: op is one of {OPS}")
        step: dict[str, Any] = {"op": op}
        if op != "wait":
            arm = raw.get("arm")
            if arm not in host.arms:
                raise TaskError(f"{where}: no arm {arm!r}; this scene has {sorted(host.arms)}")
            step["arm"] = arm
            if arm not in arms:
                arms.append(arm)
        if op == "grip":
            value = float(raw.get("value", 1.0))
            if not 0.0 <= value <= 1.0:
                raise TaskError(f"{where}: grip is between 0 (open) and 1 (closed)")
            if host.arms[step["arm"]].gripper is None:
                raise TaskError(f"{where}: arm {step['arm']!r} has no gripper")
            step["value"] = value
            step["seconds"] = float(np.clip(float(raw.get("seconds", 0.6)), 0.1, 5.0))
        elif op == "wait":
            seconds = float(raw.get("seconds", 1.0))
            if not 0.0 < seconds <= 60.0:
                raise TaskError(f"{where}: wait between 0 and 60 s")
            step["seconds"] = seconds
        elif op in ("move", "home"):
            step["speed"] = float(np.clip(float(raw.get("speed", 0.15)), 0.02, 0.4))
            step["tolerance"] = float(np.clip(float(raw.get("tolerance", 0.006)), 0.002, 0.05))
            if op == "move":
                target = raw.get("target")
                if not isinstance(target, dict):
                    raise TaskError(f"{where}: a move needs a target")
                kind = target.get("kind")
                clean: dict[str, Any] = {"kind": kind}
                if kind == "pose":
                    clean["position"] = _vec(target.get("position"), 3, f"{where} position").tolist()
                    if target.get("quat") is not None:
                        clean["quat"] = _vec(target.get("quat"), 4, f"{where} quat").tolist()
                elif kind == "object":
                    name = target.get("name")
                    if name not in names or name in regions:
                        raise TaskError(f"{where}: no object {name!r} in this scene")
                    clean["name"] = name
                    clean["offset"] = _vec(target.get("offset", (0.0, 0.0, 0.0)), 3, f"{where} offset").tolist()
                elif kind == "region":
                    name = target.get("name")
                    if name not in regions:
                        raise TaskError(f"{where}: no region {name!r}; this scene has {sorted(regions)}")
                    clean["name"] = name
                    clean["height"] = float(np.clip(float(target.get("height", 0.1)), 0.0, 0.6))
                elif kind == "relative":
                    clean["offset"] = _vec(target.get("offset"), 3, f"{where} offset").tolist()
                    if np.abs(np.asarray(clean["offset"])).max() > 0.5:
                        raise TaskError(f"{where}: a relative move is at most 0.5 m along each axis")
                else:
                    raise TaskError(f"{where}: target kind is pose, object, region or relative")
                step["target"] = clean
                orientation = raw.get("orientation", "keep")
                if orientation not in ("keep", "down", "grasp"):
                    raise TaskError(f"{where}: orientation is keep, down or grasp")
                if orientation == "grasp" and kind != "object":
                    raise TaskError(f"{where}: a grasp orientation needs an object target")
                step["orientation"] = orientation
        step["label"] = _label(step)
        step["status"] = "pending"
        step["index"] = i
        plan.append(step)

    def make(get_runner: Callable[[], Any]) -> Iterator[None]:
        return _run(host, plan, get_runner)

    return plan, make, tuple(arms)


def _target(host: Any, step: dict[str, Any]) -> tuple[np.ndarray, np.ndarray | None]:
    """Where a move goes, resolved now: world position and (optionally) orientation."""
    from robosimstudio.core.math import quat_yaw

    from .scenarios import _face_grasp_quat

    sim = host.sim
    driver = host.arms[step["arm"]]
    target = step["target"]
    kind = target["kind"]
    quat: np.ndarray | None = None
    if kind == "pose":
        position = np.asarray(target["position"], dtype=np.float64)
        if "quat" in target:
            q = np.asarray(target["quat"], dtype=np.float64)
            quat = q / np.linalg.norm(q)
    elif kind == "object":
        ref = sim[target["name"]]
        try:
            position = np.asarray(ref.position, dtype=np.float64)
        except Exception:
            position = np.asarray(ref.particle_q, dtype=np.float64).mean(axis=0)
        position = position + np.asarray(target["offset"], dtype=np.float64)
        if step.get("orientation") == "grasp":
            try:
                yaw = float(quat_yaw(np.asarray(ref.pose, dtype=np.float64)[3:7]))
            except Exception:
                yaw = 0.0
            quat = _face_grasp_quat(driver, yaw)
    elif kind == "region":
        region = sim.scene.handles.regions[target["name"]]
        centre = region.center(sim.pipeline.snapshot.body_q)
        position = np.array([centre[0], centre[1], float(sim.scene.handles.surface_z) + target["height"]])
    else:  # relative
        position = np.asarray(driver.cursor_pos, dtype=np.float64) + np.asarray(target["offset"], dtype=np.float64)
    if quat is None:
        if step.get("orientation") == "down":
            quat = np.asarray(driver.ik.home_pose[1], dtype=np.float64)
        else:
            quat = np.asarray(driver.cursor_quat, dtype=np.float64)
    return position, quat


def _run(host: Any, plan: list[dict[str, Any]], get_runner: Callable[[], Any]) -> Iterator[None]:
    from robosimstudio_studio.routine import grip, reach, wait

    session = host.session
    for i, step in enumerate(plan):
        runner = get_runner()
        if runner is not None:
            runner.current = i
            runner.phase = step["label"]
        step["status"] = "running"
        host._status_dirty = True
        started = float(host.sim.sim_time)
        op = step["op"]
        if op == "home":
            driver = host.arms[step["arm"]]
            yield from reach(
                session, step["arm"], *driver.ik.home_pose, speed=step["speed"], tolerance=step["tolerance"]
            )
        elif op == "move":
            position, quat = _target(host, step)
            driver = host.arms[step["arm"]]
            verdict = driver.check_reach(position, quat, host.sim.pipeline.snapshot)
            if not verdict["reachable"]:
                step["warning"] = verdict["why"]
            yield from reach(session, step["arm"], position, quat, speed=step["speed"], tolerance=step["tolerance"])
        elif op == "grip":
            yield from grip(session, step["arm"], step["value"], step["seconds"])
        elif op == "wait":
            yield from wait(session, step["seconds"])
        step["status"] = "done"
        step["seconds"] = round(float(host.sim.sim_time) - started, 3)
        host._status_dirty = True
    # One more pass so the last step's state is published before the runner ends.
    yield
