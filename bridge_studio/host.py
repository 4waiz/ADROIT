# SPDX-License-Identifier: Apache-2.0
"""The simulation host: one thread that owns the scene, and everything that talks to it.

Warp has one writer. Every build, every step, every IK solve and every read of
device state happens on the host's own thread, and nowhere else. A web handler
that wants something done posts it through
:class:`~robosimstudio.core.bus.SimBus` and waits; the host pumps that queue
between control steps, where touching the scene is safe -- the same contract the
graph runtime and the live page already keep.

**One loop iteration** is the studio's ``tick`` re-stated without viser:

1. Service queued requests (commands from the browser).
2. Apply the latest cursor each arm was dragged to.
3. If playing (or a step was asked for): drive each arm (IK or joint targets,
   then the gripper), advance the active routine or task by one step, run the
   scenario's per-step hooks, and step the physics.
4. Publish a frame if one is due, at most ``frame_hz`` a second.
5. Sleep to hold the control rate times the playback speed.

**Commands answer for themselves.** Each carries a client-generated id; the
host answers ``completed`` with a result, ``rejected`` with the reason the
command could not be accepted, ``failed`` with what went wrong while doing it,
or -- for work that takes longer than one pump (a build, a routine) --
``accepted`` now and ``completed``, ``failed`` or ``interrupted`` later. An id
seen before is answered with its first outcome and not run again, so a client
that reconnects and resends cannot make a robot repeat a move.
"""

from __future__ import annotations

import contextlib
import gc
import inspect
import io
import json
import math
import threading
import time
import traceback
from collections import OrderedDict, deque
from collections.abc import Callable, Iterator
from dataclasses import dataclass, field
from typing import Any

import numpy as np

from .arms import ArmDriver, HostState
from .config import BridgeConfig
from .frames import encode_frame, sanitize
from .manifest import SceneExport, export_scene
from .scenarios import BuiltScenario, RoutineSession, ScenarioRegistry

__all__ = ["Broadcast", "CommandError", "Deferred", "SimHost"]

SPEEDS = (0.1, 0.25, 0.5, 1.0, 2.0, 4.0, 0.0)
"""Playback rates the transport offers; 0 is as fast as the machine steps."""


class CommandError(ValueError):
    """A command that cannot be carried out as asked. Its message is shown to the operator."""


@dataclass
class Deferred:
    """A command accepted now and finished later; the host sends its outcome when it has one."""

    message: str = "accepted"
    result: dict[str, Any] = field(default_factory=dict)


class Broadcast:
    """Host -> every connected client: a ring of JSON messages and the newest frame.

    Thread-safe on the publishing side; each client keeps its own cursor into the
    ring, so a slow client misses frames (by design) and, if it falls a whole
    ring behind, old messages (and then re-reads the status, which is complete).
    """

    def __init__(self, capacity: int = 4000) -> None:
        self._lock = threading.Lock()
        self._messages: deque[tuple[int, str]] = deque(maxlen=capacity)
        self._seq = 0
        self._frame: bytes | None = None
        self._frame_seq = 0
        self._waiters: list[Callable[[], None]] = []

    def publish(self, message: dict[str, Any]) -> None:
        text = json.dumps(sanitize(message), separators=(",", ":"), allow_nan=False)
        with self._lock:
            self._seq += 1
            self._messages.append((self._seq, text))
            waiters = list(self._waiters)
        for wake in waiters:
            wake()

    def publish_frame(self, frame: bytes) -> None:
        with self._lock:
            self._frame = frame
            self._frame_seq += 1
            waiters = list(self._waiters)
        for wake in waiters:
            wake()

    def messages_after(self, seq: int) -> tuple[int, list[str]]:
        with self._lock:
            out = [text for s, text in self._messages if s > seq]
            return self._seq, out

    def frame_after(self, seq: int) -> tuple[int, bytes | None]:
        with self._lock:
            if self._frame_seq > seq:
                return self._frame_seq, self._frame
            return self._frame_seq, None

    @property
    def seq(self) -> int:
        return self._seq

    def subscribe(self, wake: Callable[[], None]) -> None:
        with self._lock:
            self._waiters.append(wake)

    def unsubscribe(self, wake: Callable[[], None]) -> None:
        with self._lock:
            if wake in self._waiters:
                self._waiters.remove(wake)


@dataclass
class Runner:
    """A routine or a task, advancing one control step per loop pass while the scene plays."""

    kind: str
    name: str
    gen: Iterator[None]
    arms: tuple[str, ...]
    drives_page: bool
    command_id: str | None
    started_wall: float
    started_sim: float
    suspended: list[ArmDriver] = field(default_factory=list)
    steps: int = 0
    phase: str = ""
    log: list[str] = field(default_factory=list)
    plan: list[dict[str, Any]] = field(default_factory=list)
    """For a task: one entry per step, with its status, so the page can draw the sequence."""
    current: int = -1

    def summary(self, control_hz: float) -> dict[str, Any]:
        return {
            "kind": self.kind,
            "name": self.name,
            "arms": list(self.arms),
            "steps": self.steps,
            "seconds": round(self.steps / max(control_hz, 1.0), 2),
            "phase": self.phase,
            "plan": self.plan,
            "current": self.current,
            "log": self.log[-12:],
        }


class SimHost:
    """Owns the scene and runs the loop. Everything public is safe to call from any thread.

    Args:
        config: The service's configuration.
        registry: The scenario gallery.
    """

    def __init__(self, config: BridgeConfig, registry: ScenarioRegistry) -> None:
        from robosimstudio.core.bus import SimBus

        self.config = config
        self.registry = registry
        self.bus = SimBus(timeout=900.0)
        self.broadcast = Broadcast()
        self._thread = threading.Thread(target=self._main, name="bridge-sim", daemon=True)
        self._stop = threading.Event()
        self._ready = threading.Event()

        # Scene state -- written on the owner thread only.
        self.sim: Any = None
        self.built: BuiltScenario | None = None
        self.export: SceneExport | None = None
        self.scenario: dict[str, Any] = {}
        self.document: dict[str, Any] | None = None
        self.generation = 0
        self.phase = "starting"
        self.phase_detail = ""
        self.build_error = ""
        self.playing = False
        self.speed = 1.0
        self.pending_steps = 0
        self.arms: dict[str, ArmDriver] = {}
        self.session: RoutineSession | None = None
        self.runner: Runner | None = None
        self.last_runner: dict[str, Any] | None = None
        self.success: dict[str, Any] | None = None
        # When the success measure first read done since the last build or reset: a goal met in
        # passing (a lift held, then set down) stays met, with the simulated time it was seen.
        self.goal_met: dict[str, Any] | None = None
        self.notes: list[str] = []
        self._effort_map: tuple[Any, np.ndarray] | None = None
        self._contacts: dict[str, Any] = {"available": False, "count": 0, "items": []}
        self._contacts_at = 0.0
        self._success_at = 0.0
        self._cursors: dict[str, tuple[np.ndarray, np.ndarray, str, str]] = {}
        self._cursor_lock = threading.Lock()

        # Rates, measured.
        self._step_times: deque[float] = deque(maxlen=120)
        self._ik_times: deque[float] = deque(maxlen=120)
        self._rate_window = (time.perf_counter(), 0, 0.0)
        self.steps_per_second = 0.0
        self.realtime_factor = 0.0
        self._last_frame_at = 0.0
        self._frame_seq = 0
        self._last_status_at = 0.0
        self._status_dirty = True
        self.status_snapshot: dict[str, Any] = {}

        # Command bookkeeping.
        self._outcomes: OrderedDict[str, dict[str, Any]] = OrderedDict()
        self._outcome_lock = threading.Lock()
        self.history: deque[dict[str, Any]] = deque(maxlen=400)
        self.events: deque[dict[str, Any]] = deque(maxlen=600)
        self._event_seq = 0
        self._pending_build: tuple[str, Any, str | None] | None = None
        self._handlers: dict[str, Callable[[dict[str, Any], dict[str, Any]], Any]] = {
            "sim.play": self._cmd_play,
            "sim.pause": self._cmd_pause,
            "sim.step": self._cmd_step,
            "sim.reset": self._cmd_reset,
            "sim.speed": self._cmd_speed,
            "scenario.load": self._cmd_scenario,
            "workcell.build": self._cmd_workcell,
            "arm.joint": self._cmd_joint,
            "arm.joints": self._cmd_joints,
            "arm.home": self._cmd_home,
            "arm.grip": self._cmd_grip,
            "arm.cursor": self._cmd_cursor,
            "arm.jog": self._cmd_jog,
            "arm.reach": self._cmd_reach,
            "arm.speed": self._cmd_arm_speed,
            "arm.take": self._cmd_take,
            "routine.start": self._cmd_routine,
            "routine.stop": self._cmd_stop_runner,
            "task.run": self._cmd_task,
            "task.stop": self._cmd_stop_runner,
            "physics.gravity": self._cmd_gravity,
        }

    # =================================================================== public
    def start(self) -> None:
        self._thread.start()

    def stop(self, timeout: float = 10.0) -> None:
        self._stop.set()
        self._thread.join(timeout)

    def wait_ready(self, timeout: float | None = None) -> bool:
        return self._ready.wait(timeout)

    @property
    def commands(self) -> list[str]:
        return sorted(self._handlers)

    def call(self, fn: Callable[..., Any], *args: Any, **kwargs: Any) -> Any:
        """Run ``fn`` on the owner thread and return its result (blocking)."""
        return self.bus.call(fn, *args, **kwargs)

    def execute(self, command: dict[str, Any], *, client: str = "") -> dict[str, Any]:
        """Run one command and return its acknowledgement. Blocks until the owner thread has run it.

        ``command`` is ``{"id": str, "name": str, "args": {...}}``. The answer is
        ``{"id", "name", "status", "message", "result"}``.
        """
        cid = str(command.get("id") or "")
        name = str(command.get("name") or "")
        args = command.get("args") or {}
        if not cid or len(cid) > 80:
            return self._ack(cid, name, "rejected", "a command needs an id of at most 80 characters")
        with self._outcome_lock:
            seen = self._outcomes.get(cid)
        if seen is not None:
            return {**seen, "duplicate": True}
        if name not in self._handlers:
            return self._remember(cid, self._ack(cid, name, "rejected", f"unknown command {name!r}"))
        if not isinstance(args, dict):
            return self._remember(cid, self._ack(cid, name, "rejected", "args must be an object"))
        try:
            return self.bus.call(self._run_command, cid, name, args, client)
        except Exception as exc:  # the bus closed or timed out
            return self._remember(cid, self._ack(cid, name, "failed", f"{type(exc).__name__}: {exc}"))

    def post_cursor(self, arm: str, position, quat, phase: str, client: str = "") -> None:
        """A gizmo drag sample: the latest one per arm is applied before the next step. Any thread."""
        p = np.asarray(position, dtype=np.float64).reshape(3)
        q = np.asarray(quat, dtype=np.float64).reshape(4)
        if not (np.isfinite(p).all() and np.isfinite(q).all()) or np.linalg.norm(q) < 1.0e-6:
            return
        with self._cursor_lock:
            self._cursors[str(arm)] = (p, q / np.linalg.norm(q), str(phase), client)

    def geometry(self) -> tuple[int, bytes]:
        export = self.export
        return (self.generation, export.geometry) if export is not None else (self.generation, b"")

    def manifest(self) -> dict[str, Any] | None:
        export = self.export
        if export is None:
            return None
        return {
            **export.manifest,
            "notes": list(self.notes),
            "routines": list(self.built.routines if self.built else {}),
        }

    def recent_events(self, limit: int = 200) -> list[dict[str, Any]]:
        return list(self.events)[-limit:]

    def recent_commands(self, limit: int = 200) -> list[dict[str, Any]]:
        return list(self.history)[-limit:]

    # ============================================================== the loop
    def _main(self) -> None:
        try:
            import warp as wp

            wp.config.quiet = True
            wp.init()
        except Exception as exc:  # pragma: no cover -- depends on the machine
            self._event("error", "host", f"Warp did not initialise: {type(exc).__name__}: {exc}")
        self.phase = "idle"
        self._ready.set()
        if self.config.scenario:
            self._pending_build = ("scenario", self.config.scenario, None)
        next_step = time.perf_counter()
        while not self._stop.is_set():
            try:
                self.bus.pump(budget=64)
                if self._pending_build is not None:
                    kind, what, cid = self._pending_build
                    self._pending_build = None
                    self._build(kind, what, cid)
                    next_step = time.perf_counter()
                    continue
                sim = self.sim
                if sim is None:
                    self._maybe_status()
                    time.sleep(0.02)
                    continue
                self._apply_cursors()
                stepping = self.playing or self.pending_steps > 0
                now = time.perf_counter()
                if stepping:
                    paced = self.playing and self.pending_steps == 0 and self.speed > 0.0
                    if paced and now < next_step:
                        self._maybe_publish(now)
                        time.sleep(min(next_step - now, 0.003))
                        continue
                    self._advance()
                    if self.pending_steps > 0:
                        self.pending_steps -= 1
                        if self.pending_steps == 0:
                            self._publish_frame()
                            self._status_dirty = True
                    dt = 1.0 / float(sim.request.physics.control_hz)
                    if paced:
                        next_step += dt / self.speed
                        if next_step < now - 0.25:
                            next_step = now  # drop the debt rather than catching up
                    else:
                        next_step = time.perf_counter()
                else:
                    self._idle()
                    time.sleep(0.01)
                    next_step = time.perf_counter()
                self._maybe_publish(time.perf_counter())
                self._maybe_success()
                self._maybe_status()
            except Exception as exc:
                # The loop must outlive a bad step: report it, hold the scene, keep serving.
                traceback.print_exc()
                self.playing = False
                self.pending_steps = 0
                self._event("error", "host", f"the loop stopped the scene: {type(exc).__name__}: {exc}")
                self._end_runner("failed", f"{type(exc).__name__}: {exc}")
                self._status_dirty = True
                time.sleep(0.05)
        self.bus.close()
        # Give the scene back on the thread that owns it, so its device memory and any
        # co-simulated engine are released before the process goes.
        if self.sim is not None:
            try:
                self.sim.close()
            except Exception:
                pass

    def _advance(self) -> None:
        """One control step: arms, the runner, the scenario's hooks, physics."""
        sim = self.sim
        host_state = HostState(sim.pipeline.snapshot)
        t0 = time.perf_counter()
        for driver in self.arms.values():
            driver.drain(sim, paused=False, host_state=host_state)
        t1 = time.perf_counter()
        if self.runner is not None:
            self._advance_runner()
        if self.built is not None:
            for hook in self.built.each_step:
                hook(sim)
        sim.step_teleop()
        # A step only enqueues the device's work; reading its result back is where that work is
        # waited for. Wait here, so the physics time is what the step costs and not what it took
        # to launch it. The frame and the next step read this same copy, so nothing is lost.
        _ = sim.pipeline.snapshot
        t2 = time.perf_counter()
        self._ik_times.append(t1 - t0)
        self._step_times.append(t2 - t1)
        started, steps, sim_start = self._rate_window
        if steps == 0:
            sim_start = float(sim.sim_time)
        steps += 1
        now = time.perf_counter()
        if now - started >= 1.0:
            self.steps_per_second = steps / (now - started)
            self.realtime_factor = (float(sim.sim_time) - sim_start) / (now - started)
            self._rate_window = (now, 0, 0.0)
            self._status_dirty = True
        else:
            self._rate_window = (started, steps, sim_start)

    def _idle(self) -> None:
        """Paused: the grip still answers (it moves nothing until the scene runs), the rates read zero."""
        sim = self.sim
        host_state = HostState(sim.pipeline.snapshot)
        for driver in self.arms.values():
            driver.drain(sim, paused=True, host_state=host_state)
        if self.steps_per_second:
            self.steps_per_second = 0.0
            self.realtime_factor = 0.0
            self._rate_window = (time.perf_counter(), 0, 0.0)
            self._status_dirty = True

    def _apply_cursors(self) -> None:
        with self._cursor_lock:
            cursors, self._cursors = self._cursors, {}
        for arm, (position, quat, phase, client) in cursors.items():
            driver = self.arms.get(arm)
            if driver is None or self._owned_by_runner(arm):
                continue
            driver.aim(position, quat, moving=phase != "end")
            driver.source = "operator"
            if phase == "end":
                started = time.time()
                try:
                    verdict = driver.check_reach(position, quat, self.sim.pipeline.snapshot)
                except Exception as exc:
                    verdict = {"reachable": None, "why": f"reach check failed: {type(exc).__name__}: {exc}"}
                self._history(
                    {
                        "id": f"drag-{int(started * 1000)}-{arm}",
                        "name": "arm.cursor",
                        "client": client,
                        "target": arm,
                        "args": {"position": [round(float(v), 4) for v in position]},
                        "status": "completed",
                        "message": "target reachable"
                        if verdict.get("reachable")
                        else f"target not reachable: {verdict.get('why') or 'unknown'}",
                        "issuedAt": started,
                        "finishedAt": time.time(),
                    }
                )
                if verdict.get("reachable") is False:
                    self._event("warning", f"arm:{arm}", f"Target not reachable: {verdict.get('why')}")

    # ============================================================ publishing
    def _maybe_publish(self, now: float) -> None:
        # A held scene changes only by what an operator does to it, so it is drawn less often.
        interval = 1.0 / self.config.frame_hz if (self.playing or self.pending_steps) else 0.2
        if now - self._last_frame_at >= interval:
            self._publish_frame()

    def _publish_frame(self) -> None:
        sim = self.sim
        if sim is None or self.export is None:
            return
        self._last_frame_at = time.perf_counter()
        snapshot = sim.pipeline.snapshot
        host_state = HostState(snapshot)
        effort = self._efforts()
        arms = {}
        for name, driver in self.arms.items():
            try:
                arms[name] = driver.telemetry(snapshot, host_state, effort)
            except Exception as exc:
                arms[name] = {"error": f"{type(exc).__name__}: {exc}"}
        if time.perf_counter() - self._contacts_at > 1.0 / 15.0:
            self._contacts = self._read_contacts()
            self._contacts_at = time.perf_counter()
        self._frame_seq += 1
        body_q = np.asarray(snapshot.body_q, dtype=np.float32).reshape(-1, 7)
        gather = self.export.particle_gather
        particles = (
            np.asarray(snapshot.particle_q, dtype=np.float32).reshape(-1, 3)[gather]
            if len(gather)
            else np.zeros((0, 3), dtype=np.float32)
        )
        control_hz = float(sim.request.physics.control_hz)
        header = {
            "type": "frame",
            "gen": self.generation,
            "seq": self._frame_seq,
            "step": int(sim.step_count),
            "t": float(sim.sim_time),
            "wall": time.time(),
            "playing": self.playing,
            "speed": self.speed,
            "finite": bool(snapshot.is_finite()),
            "rate": {
                "steps": round(self.steps_per_second, 1),
                "realtime": round(self.realtime_factor, 3),
                "stepMs": round(1000.0 * float(np.mean(self._step_times)), 3) if self._step_times else None,
                "controlMs": round(1000.0 * float(np.mean(self._ik_times)), 3) if self._ik_times else None,
                "controlHz": control_hz,
            },
            "arms": arms,
            "contacts": self._contacts,
            "effortSource": "MuJoCo qfrc_actuator" if effort is not None else None,
            "runner": None if self.runner is None else self.runner.summary(control_hz),
        }
        self.broadcast.publish_frame(encode_frame(header, [("body_q", body_q), ("particles", particles)]))

    def _efforts(self) -> np.ndarray | None:
        """Actuator generalized forces per Newton DOF, from MuJoCo's own ``qfrc_actuator``; ``None`` if the solver has none."""
        sim = self.sim
        try:
            solver = sim.scene.program.mujoco_solver if sim.scene.program is not None else None
            if solver is None:
                return None
            if self._effort_map is None or self._effort_map[0] is not solver:
                mapping = np.asarray(solver.mjc_dof_to_newton_dof.numpy(), dtype=np.int64)
                mapping = mapping[0] if mapping.ndim == 2 else mapping
                self._effort_map = (solver, mapping)
            mapping = self._effort_map[1]
            qfrc = np.asarray(solver.mjw_data.qfrc_actuator.numpy(), dtype=np.float64)
            qfrc = qfrc[0] if qfrc.ndim == 2 else qfrc
            if qfrc.shape[0] != mapping.shape[0]:
                return None
            out = np.full(int(sim.model.joint_dof_count), np.nan)
            valid = (mapping >= 0) & (mapping < out.shape[0])
            out[mapping[valid]] = qfrc[valid]
            return out
        except Exception:
            return None

    def _read_contacts(self, limit: int = 64) -> dict[str, Any]:
        """This step's contacts from the MuJoCo solver: pairs, points and forces, strongest first."""
        sim = self.sim
        probe = getattr(sim, "contacts", None)
        if probe is None:
            return {"available": False, "count": 0, "items": [], "why": "no MuJoCo contact table in this scene"}
        try:
            probe.update(int(sim.step_count))
            n = len(probe.geoms)
            if n == 0:
                return {"available": True, "count": 0, "items": []}
            shapes = probe.geom_to_shape[probe.geoms]
            force = np.asarray(probe.impulses, dtype=np.float64) / max(float(probe.dt), 1.0e-9)
            data = probe.solver.mjw_data
            points = np.asarray(data.contact.pos.numpy()[:n], dtype=np.float64)
            magnitude = np.linalg.norm(force, axis=1)
            order = np.argsort(-magnitude)[:limit]
            items = [
                [
                    int(shapes[i, 0]),
                    int(shapes[i, 1]),
                    *[round(float(v), 5) for v in points[i]],
                    *[round(float(v), 4) for v in force[i]],
                    round(float(-probe.depths[i]), 6),
                ]
                for i in order
            ]
            return {
                "available": True,
                "count": int(n),
                "items": items,
                "totalForce": round(float(magnitude.sum()), 4),
                "layout": ["shapeA", "shapeB", "px", "py", "pz", "fx", "fy", "fz", "penetration"],
            }
        except Exception as exc:
            return {"available": False, "count": 0, "items": [], "why": f"{type(exc).__name__}: {exc}"}

    def _maybe_success(self) -> None:
        built = self.built
        if built is None or built.success is None or self.sim is None:
            return
        now = time.perf_counter()
        if now - self._success_at < 0.5:
            return
        self._success_at = now
        try:
            verdict = built.success(self.sim)
        except Exception as exc:
            verdict = {"done": False, "detail": f"the success measure raised {type(exc).__name__}: {exc}"}
        before = (self.success or {}).get("done")
        self.success = sanitize(verdict)
        if verdict.get("done") and not before:
            self._event("success", "scenario", f"Success: {verdict.get('detail', '')}")
        if verdict.get("done") and self.goal_met is None:
            self.goal_met = {
                "simTime": round(float(self.sim.sim_time), 3),
                "step": int(self.sim.step_count),
                "detail": str(verdict.get("detail", "")),
            }
        self._status_dirty = True

    def _maybe_status(self) -> None:
        now = time.perf_counter()
        if self._status_dirty or now - self._last_status_at > 1.0:
            self._status_dirty = False
            self._last_status_at = now
            self.status_snapshot = self._status()
            self.broadcast.publish({"type": "status", **self.status_snapshot})

    def _status(self) -> dict[str, Any]:
        sim = self.sim
        control_hz = float(sim.request.physics.control_hz) if sim is not None else 0.0
        return {
            "phase": self.phase,
            "phaseDetail": self.phase_detail,
            "buildError": self.build_error,
            "generation": self.generation,
            "scenario": self.scenario,
            "document": self.document,
            "playing": self.playing,
            "speed": self.speed,
            "speeds": list(SPEEDS),
            "step": int(sim.step_count) if sim is not None else 0,
            "simTime": float(sim.sim_time) if sim is not None else 0.0,
            "controlHz": control_hz,
            "stepsPerSecond": round(self.steps_per_second, 1),
            "realtimeFactor": round(self.realtime_factor, 3),
            "runner": None if self.runner is None else self.runner.summary(control_hz),
            "lastRunner": self.last_runner,
            "success": self.success,
            "goalMet": self.goal_met,
            "routines": list(self.built.routines) if self.built else [],
            "arms": {
                name: {"mode": d.mode, "source": d.source, "suspended": d.suspended, "owner": self._owner(name)}
                for name, d in self.arms.items()
            },
            "notes": list(self.notes),
            "target": "simulation",
        }

    def _owner(self, arm: str) -> str:
        runner = self.runner
        if runner is not None and arm in runner.arms:
            return f"{runner.kind}:{runner.name}"
        return "operator"

    def _owned_by_runner(self, arm: str) -> bool:
        return self.runner is not None and arm in self.runner.arms

    # ============================================================== building
    def _build(self, kind: str, what: Any, cid: str | None) -> None:
        """Build a scenario or a workcell document, then swap it in. Owner thread.

        The new scene is built before the old one is given up, so a scene that
        fails to build leaves the running one up, with the reason.
        """
        title = what if kind == "scenario" else (what.get("name") if isinstance(what, dict) else "workcell")
        if kind == "scenario":
            scenario = self.registry.get(what)
            title = scenario.title
        self.phase = "building"
        self.phase_detail = f"Building {title}"
        self.build_error = ""
        self.playing = False
        self._status_dirty = True
        self._maybe_status()
        self._event("info", "build", f"Building {title}")
        started = time.perf_counter()
        captured = io.StringIO()
        try:
            with contextlib.redirect_stdout(captured):
                if kind == "scenario":
                    ok, why = scenario.availability()
                    if not ok:
                        raise CommandError(why)
                    built = scenario.build()
                    info = scenario.describe()
                    document = None
                else:
                    from .workcell import WorkcellDoc, to_scene

                    doc = WorkcellDoc.model_validate(what)
                    sim = to_scene(doc).build(control_mode="joint_position")
                    built = BuiltScenario(sim=sim, notes=[_preset_note(doc.physics)])
                    document = doc.model_dump(by_alias=True, mode="json")
                    info = {
                        "id": "workcell",
                        "title": doc.name,
                        "summary": doc.description,
                        "robot": doc.robot.model if doc.robot else "",
                        "physics": doc.physics,
                        "category": "custom",
                    }
                sim = built.sim
                arms = {
                    name: ArmDriver(sim, name) for name in (sim.scene.embodiment.iks if sim.scene.embodiment else {})
                }
                # The first IK solve compiles the solver (JAX, seconds); pay for it here rather than on the first drag.
                snapshot = sim.pipeline.snapshot
                for driver in arms.values():
                    try:
                        driver.check_reach(*driver.ik.home_pose, snapshot)
                        driver.reach = None
                    except Exception as exc:
                        self._event("warning", f"arm:{driver.name}", f"IK warm-up failed: {type(exc).__name__}: {exc}")
                export = export_scene(sim, generation=self.generation + 1, scenario=info)
        except Exception as exc:
            detail = str(exc) if isinstance(exc, CommandError) else f"{type(exc).__name__}: {exc}"
            if not isinstance(exc, CommandError):
                traceback.print_exc()
            self._forward_output(captured.getvalue(), "build")
            self.build_error = detail
            self.phase = "ready" if self.sim is not None else "error"
            self.phase_detail = ""
            self._event("error", "build", f"{title} did not build: {detail}")
            self._status_dirty = True
            if cid:
                self._finish(cid, "failed", f"{title} did not build: {detail}")
            return
        self._forward_output(captured.getvalue(), "build")

        # Swap: end what was running on the old scene, then give the old scene back.
        self._end_runner("interrupted", "the scene was rebuilt")
        old = self.sim
        self.sim = sim
        self.built = built
        self.arms = arms
        self.session = RoutineSession(sim, arms)
        self.export = export
        self.scenario = info
        self.document = document
        self.notes = list(built.notes)
        self.generation += 1
        self.success = None
        self.goal_met = None
        self._success_at = 0.0
        self._effort_map = None
        self._contacts = {"available": False, "count": 0, "items": []}
        self.pending_steps = 0
        self.last_runner = None
        if old is not None:
            try:
                old.close()
            except Exception:
                pass
            del old
            gc.collect()
        seconds = time.perf_counter() - started
        self.phase = "ready"
        self.phase_detail = ""
        self.playing = bool(self.config.autoplay)
        self._event("info", "build", f"{title} built in {seconds:.1f} s (generation {self.generation})")
        self.broadcast.publish({"type": "scene", "generation": self.generation, "scenario": info})
        self._publish_frame()
        self._status_dirty = True
        if cid:
            self._finish(cid, "completed", f"{title} built in {seconds:.1f} s", {"generation": self.generation})

    def _forward_output(self, text: str, source: str) -> None:
        """Lines the library printed while building, into the event log (Warp's module lines dropped)."""
        for raw in text.splitlines():
            line = raw.strip()
            if not line or line.startswith("Module ") or ("took" in line and "ms" in line):
                continue
            self._event("info", source, line[:400])

    # ============================================================== runners
    def _start_runner(
        self,
        kind: str,
        name: str,
        make: Callable[..., Iterator[None]],
        arms: tuple[str, ...],
        cid: str | None,
        *,
        drives_page: bool,
        plan: list[dict[str, Any]] | None = None,
    ) -> Runner:
        if self.runner is not None:
            raise CommandError(f"{self.runner.kind} {self.runner.name!r} is running; stop it first")
        suspended: list[ArmDriver] = []
        if not drives_page:
            suspended = [d for d in self.arms.values() if not d.suspended]
            for driver in suspended:
                driver.suspend()
        gen = make()
        if not inspect.isgenerator(gen):
            for driver in suspended:
                driver.resume()
            raise CommandError(f"{name} returned {type(gen).__name__}, not a generator")
        for arm in arms:
            if arm in self.arms:
                self.arms[arm].source = f"{kind}:{name}"
        runner = Runner(
            kind=kind,
            name=name,
            gen=gen,
            arms=arms,
            drives_page=drives_page,
            command_id=cid,
            started_wall=time.time(),
            started_sim=float(self.sim.sim_time),
            suspended=suspended,
            plan=plan or [],
        )
        self.runner = runner
        self.last_runner = None
        self._event("info", f"{kind}:{name}", f"Started {kind} {name!r}")
        self._status_dirty = True
        return runner

    def _advance_runner(self) -> None:
        runner = self.runner
        assert runner is not None
        out = io.StringIO()
        try:
            with contextlib.redirect_stdout(out):
                next(runner.gen)
        except StopIteration:
            self._forward_runner_output(runner, out.getvalue())
            self._end_runner("completed", f"{runner.kind} {runner.name!r} finished")
            return
        except Exception as exc:
            self._forward_runner_output(runner, out.getvalue())
            self._end_runner("failed", f"{type(exc).__name__}: {exc}")
            return
        self._forward_runner_output(runner, out.getvalue())
        runner.steps += 1

    def _forward_runner_output(self, runner: Runner, text: str) -> None:
        for raw in text.splitlines():
            line = raw.strip()
            if line:
                runner.log.append(line)
                self._event("info", f"{runner.kind}:{runner.name}", line[:400])

    def _end_runner(self, status: str, message: str) -> None:
        runner = self.runner
        if runner is None:
            return
        self.runner = None
        try:
            runner.gen.close()
        except Exception:
            pass
        for driver in runner.suspended:
            try:
                driver.resume()
            except Exception:
                pass
        for arm in runner.arms:
            if arm in self.arms:
                self.arms[arm].source = "operator"
        if runner.plan and 0 <= runner.current < len(runner.plan):
            step = runner.plan[runner.current]
            if step.get("status") == "running":
                step["status"] = {"completed": "done", "failed": "failed"}.get(status, "stopped")
                if status == "failed":
                    step["error"] = message
        control_hz = float(self.sim.request.physics.control_hz) if self.sim is not None else 60.0
        self.last_runner = {**runner.summary(control_hz), "status": status, "message": message}
        level = {"completed": "success", "failed": "error"}.get(status, "warning")
        self._event(
            level, f"{runner.kind}:{runner.name}", f"{runner.kind.capitalize()} {runner.name!r} {status}: {message}"
        )
        if runner.command_id:
            self._finish(runner.command_id, status, message, {"steps": runner.steps})
        self._status_dirty = True

    # ============================================================== commands
    def _run_command(self, cid: str, name: str, args: dict[str, Any], client: str) -> dict[str, Any]:
        """Owner thread: run one command and record its outcome."""
        started = time.time()
        meta = {"id": cid, "name": name, "client": client, "target": str(args.get("arm", "")), "issuedAt": started}
        try:
            result = self._handlers[name](args, meta)
        except CommandError as exc:
            ack = self._ack(cid, name, "rejected", str(exc))
        except Exception as exc:
            traceback.print_exc()
            ack = self._ack(cid, name, "failed", f"{type(exc).__name__}: {exc}")
        else:
            if isinstance(result, Deferred):
                ack = self._ack(cid, name, "accepted", result.message, result.result)
            else:
                message = ""
                if isinstance(result, dict):
                    message = str(result.pop("message", "") if "message" in result else "")
                ack = self._ack(cid, name, "completed", message or "done", result if isinstance(result, dict) else {})
        self._remember(cid, ack)
        self._history(
            {
                **meta,
                "args": _short_args(args),
                "status": ack["status"],
                "message": ack["message"],
                "finishedAt": time.time() if ack["status"] != "accepted" else None,
            }
        )
        if ack["status"] in ("rejected", "failed"):
            self._event("warning" if ack["status"] == "rejected" else "error", name, ack["message"])
        self.broadcast.publish({"type": "ack", **ack})
        self._status_dirty = True
        return ack

    def _finish(self, cid: str, status: str, message: str, result: dict[str, Any] | None = None) -> None:
        """A deferred command's outcome, once there is one."""
        with self._outcome_lock:
            first = self._outcomes.get(cid, {})
        ack = self._ack(cid, first.get("name", ""), status, message, result or {})
        self._remember(cid, ack)
        for entry in reversed(self.history):
            if entry.get("id") == cid:
                entry.update(status=status, message=message, finishedAt=time.time())
                break
        self.broadcast.publish({"type": "ack", **ack})

    @staticmethod
    def _ack(cid: str, name: str, status: str, message: str, result: dict[str, Any] | None = None) -> dict[str, Any]:
        return {"id": cid, "name": name, "status": status, "message": message, "result": result or {}}

    def _remember(self, cid: str, ack: dict[str, Any]) -> dict[str, Any]:
        if cid:
            with self._outcome_lock:
                self._outcomes[cid] = ack
                while len(self._outcomes) > 2000:
                    self._outcomes.popitem(last=False)
        return ack

    def _history(self, entry: dict[str, Any]) -> None:
        self.history.append(entry)
        self.broadcast.publish({"type": "command", **entry})

    def _event(self, level: str, source: str, message: str) -> None:
        self._event_seq += 1
        sim = self.sim
        entry = {
            "seq": self._event_seq,
            "level": level,
            "source": source,
            "message": message,
            "wall": time.time(),
            "simTime": float(sim.sim_time) if sim is not None else None,
        }
        self.events.append(entry)
        self.broadcast.publish({"type": "event", **entry})

    # ------------------------------------------------------------ handlers
    def _need_sim(self) -> Any:
        if self.sim is None:
            raise CommandError("no scene is built yet")
        return self.sim

    def _arm(self, args: dict[str, Any], *, operator: bool = True) -> ArmDriver:
        self._need_sim()
        name = args.get("arm")
        if not isinstance(name, str) or name not in self.arms:
            raise CommandError(f"no arm {name!r} in this scene; it has {sorted(self.arms)}")
        generation = args.get("generation")
        if generation is not None and int(generation) != self.generation:
            raise CommandError("the scene was rebuilt since this command was sent; it was not applied")
        if operator and self._owned_by_runner(name):
            runner = self.runner
            raise CommandError(f"arm {name!r} is being driven by {runner.kind} {runner.name!r}; stop it first")
        driver = self.arms[name]
        if operator:
            driver.source = "operator"
        return driver

    def _cmd_play(self, args, meta):
        self._need_sim()
        self.playing = True
        self.pending_steps = 0
        return {"message": "playing", "playing": True}

    def _cmd_pause(self, args, meta):
        self._need_sim()
        self.playing = False
        self.pending_steps = 0
        self._publish_frame()
        return {"message": f"paused at t = {self.sim.sim_time:.3f} s", "playing": False}

    def _cmd_step(self, args, meta):
        sim = self._need_sim()
        count = int(args.get("count", 1))
        if not 1 <= count <= 600:
            raise CommandError("step between 1 and 600 control steps at a time")
        self.playing = False
        self.pending_steps = count
        dt = 1.0 / float(sim.request.physics.control_hz)
        return {"message": f"stepping {count} x {dt * 1000:.1f} ms", "count": count, "dt": dt}

    def _cmd_reset(self, args, meta):
        sim = self._need_sim()
        self._end_runner("interrupted", "the scene was reset")
        sim.reset()
        state = sim.state
        for driver in self.arms.values():
            driver.on_reset(state)
        self.pending_steps = 0
        self.success = None
        self.goal_met = None
        self._success_at = 0.0
        self.last_runner = None
        self._contacts = {"available": False, "count": 0, "items": []}
        self._publish_frame()
        self._event("info", "sim", "Reset to the start state")
        return {"message": "reset to the start state", "step": int(sim.step_count)}

    def _cmd_speed(self, args, meta):
        self._need_sim()
        value = float(args.get("value", 1.0))
        if value not in SPEEDS:
            raise CommandError(f"speed is one of {list(SPEEDS)} (0 is as fast as possible)")
        self.speed = value
        return {"message": "max" if value == 0 else f"{value:g}x", "speed": value}

    def _cmd_scenario(self, args, meta):
        scenario_id = str(args.get("id", ""))
        if scenario_id not in self.registry:
            raise CommandError(f"no scenario {scenario_id!r}")
        ok, why = self.registry.get(scenario_id).availability()
        if not ok:
            raise CommandError(why)
        self._pending_build = ("scenario", scenario_id, meta["id"])
        return Deferred(f"building {self.registry.get(scenario_id).title}")

    def _cmd_workcell(self, args, meta):
        from pydantic import ValidationError

        from .workcell import WorkcellDoc

        doc = args.get("doc")
        try:
            WorkcellDoc.model_validate(doc)
        except ValidationError as exc:
            raise CommandError(_validation_message(exc)) from None
        self._pending_build = ("workcell", doc, meta["id"])
        return Deferred("building the workcell")

    def _cmd_joint(self, args, meta):
        driver = self._arm(args)
        try:
            value = driver.set_joint(int(args.get("joint", -1)), float(args.get("value")), self.sim.pipeline.snapshot)
        except (IndexError, ValueError, TypeError) as exc:
            raise CommandError(str(exc)) from None
        return {"message": f"{driver.name} joint {int(args['joint']) + 1} -> {value:.4f}", "value": value}

    def _cmd_joints(self, args, meta):
        driver = self._arm(args)
        try:
            values = driver.set_joints(args.get("values"), self.sim.pipeline.snapshot)
        except (ValueError, TypeError) as exc:
            raise CommandError(str(exc)) from None
        return {"message": f"{driver.name}: {len(values)} joint targets set", "values": [float(v) for v in values]}

    def _cmd_home(self, args, meta):
        driver = self._arm(args)
        driver.home(self.sim.pipeline.snapshot)
        return {"message": f"{driver.name} returning home at {driver.joint_speed:.2f} rad/s"}

    def _cmd_grip(self, args, meta):
        driver = self._arm(args)
        try:
            value = driver.set_grip(float(args.get("value")))
        except (ValueError, TypeError) as exc:
            raise CommandError(str(exc)) from None
        word = "close" if value >= 0.999 else ("open" if value <= 0.001 else f"{value:.0%}")
        return {"message": f"{driver.name} gripper {word}", "value": value}

    def _cmd_cursor(self, args, meta):
        driver = self._arm(args)
        position, quat = _pose_args(args)
        driver.aim(position, quat, moving=False)
        verdict = driver.check_reach(position, quat, self.sim.pipeline.snapshot)
        message = "target reachable" if verdict["reachable"] else f"target not reachable: {verdict['why']}"
        return {"message": message, "reach": verdict}

    def _cmd_jog(self, args, meta):
        driver = self._arm(args)
        space = str(args.get("space", "cartesian"))
        delta = float(args.get("delta", 0.0))
        if not math.isfinite(delta):
            raise CommandError("a jog is a finite number")
        snapshot = self.sim.pipeline.snapshot
        if space == "joint":
            joint = int(args.get("joint", -1))
            if not 0 <= joint < len(driver.coords):
                raise CommandError(f"arm {driver.name!r} has joints 1..{len(driver.coords)}")
            if abs(delta) > 0.5:
                raise CommandError("a joint jog is at most 0.5 rad (or 0.5 m) at a time")
            base = (
                driver.joint_goal[joint]
                if driver.mode == "joint" and driver.joint_goal is not None
                else driver.joint_q(snapshot)[joint]
            )
            value = float(np.clip(base + delta, driver.lower[joint], driver.upper[joint]))
            driver.set_joint(joint, value, snapshot)
            return {"message": f"{driver.name} joint {joint + 1} -> {value:.4f}", "value": value}
        axis = str(args.get("axis", ""))
        if axis not in ("x", "y", "z", "rx", "ry", "rz"):
            raise CommandError("a Cartesian jog axis is x, y, z, rx, ry or rz")
        frame = str(args.get("frame", "world"))
        limit = 0.1 if axis in ("x", "y", "z") else 0.5
        if abs(delta) > limit:
            raise CommandError(f"a Cartesian jog is at most {limit} {'m' if limit == 0.1 else 'rad'} at a time")
        if driver.mode != "cartesian":
            driver.aim(*driver.ee_pose(HostState(snapshot)), moving=False)
        position = np.array(driver.cursor_pos, dtype=np.float64)
        quat = np.array(driver.cursor_quat, dtype=np.float64)
        from robosimstudio.core.math import quat_from_axis_angle, quat_multiply, quat_to_mat

        k = "xyz".index(axis[-1])
        unit = np.zeros(3)
        unit[k] = 1.0
        if frame == "tool":
            unit = quat_to_mat(quat) @ unit
        if axis in ("x", "y", "z"):
            position = position + unit * delta
        else:
            turn = quat_from_axis_angle(unit, delta)
            quat = np.asarray(quat_multiply(turn, quat), dtype=np.float64)
        driver.aim(position, quat, moving=False)
        verdict = driver.check_reach(position, quat, snapshot)
        message = f"{driver.name} jog {axis} {delta:+.3f} ({frame})"
        if not verdict["reachable"]:
            message += f"; not reachable: {verdict['why']}"
        return {"message": message, "reach": verdict}

    def _cmd_reach(self, args, meta):
        driver = self._arm(args, operator=False)
        position, quat = _pose_args(args)
        verdict = driver.check_reach(position, quat, self.sim.pipeline.snapshot)
        return {"message": "reachable" if verdict["reachable"] else verdict["why"], "reach": verdict}

    def _cmd_arm_speed(self, args, meta):
        driver = self._arm(args)
        if "joint" in args:
            value = float(args["joint"])
            if not 0.05 <= value <= 3.0:
                raise CommandError("joint speed is between 0.05 and 3.0 rad/s")
            driver.joint_speed = value
        if "cartesian" in args:
            value = float(args["cartesian"])
            if not 0.05 <= value <= 1.0:
                raise CommandError("Cartesian speed is a fraction of the law's cruise, 0.05 to 1.0")
            driver.set_cartesian_speed(value)
        return {
            "message": f"{driver.name}: joints {driver.joint_speed:.2f} rad/s, hand {driver.cartesian_speed:.3f} m/s",
            "joint": driver.joint_speed,
            "cartesian": driver.cartesian_scale,
        }

    def _cmd_take(self, args, meta):
        driver = self._arm(args)
        driver.mode = "cartesian"
        driver.joint_goal = driver.joint_command = None
        driver.take_hand(self.sim.state)
        return {"message": f"{driver.name}: cursor on the hand"}

    def _cmd_routine(self, args, meta):
        self._need_sim()
        name = str(args.get("name", ""))
        routines = self.built.routines if self.built else {}
        if name not in routines:
            raise CommandError(f"no routine {name!r} in this scenario; it has {sorted(routines)}")
        fn = routines[name]
        drives_page = "session" in inspect.signature(fn).parameters
        session = self.session

        def make():
            return fn(self.sim, session=session) if drives_page else fn(self.sim)

        self._start_runner("routine", name, make, tuple(self.arms), meta["id"], drives_page=drives_page)
        if not self.playing:
            self.playing = True
        return Deferred(f"routine {name!r} started")

    def _cmd_task(self, args, meta):
        from .tasks import TaskError, compile_task

        self._need_sim()
        steps = args.get("steps")
        name = str(args.get("name") or "Mission")[:60]
        try:
            plan, make, arms = compile_task(self, steps)
        except TaskError as exc:
            raise CommandError(str(exc)) from None
        runner = self._start_runner(
            "task", name, lambda: make(lambda: self.runner), arms, meta["id"], drives_page=True, plan=plan
        )
        del runner
        if not self.playing:
            self.playing = True
        return Deferred(f"task {name!r} started: {len(plan)} steps")

    def _cmd_stop_runner(self, args, meta):
        if self.runner is None:
            raise CommandError("nothing is running")
        name = self.runner.name
        self._end_runner("interrupted", "stopped by the operator")
        return {"message": f"stopped {name!r}"}

    def _cmd_gravity(self, args, meta):
        sim = self._need_sim()
        value = float(args.get("value", -9.81))
        if not -30.0 <= value <= 0.0:
            raise CommandError("gravity along z is between -30 and 0 m/s^2")
        import newton

        sim.model.set_gravity((0.0, 0.0, value))
        program = sim.scene.program
        try:
            program.notify(int(newton.solvers.SolverNotifyFlags.MODEL_PROPERTIES))
        except Exception as exc:
            raise CommandError(
                f"this scene's solvers did not accept a live gravity change ({exc}); rebuild instead"
            ) from None
        sim.pipeline.invalidate_graph(recapture=True)
        self._event("info", "physics", f"Gravity set to {value:.2f} m/s^2 (live)")
        return {"message": f"gravity {value:.2f} m/s^2", "gravity": value}


def _pose_args(args: dict[str, Any]) -> tuple[np.ndarray, np.ndarray]:
    try:
        position = np.asarray(args.get("position"), dtype=np.float64).reshape(3)
        quat = np.asarray(args.get("quat"), dtype=np.float64).reshape(4)
    except Exception:
        raise CommandError("a pose is position [x, y, z] in metres and quat [x, y, z, w]") from None
    if not (np.isfinite(position).all() and np.isfinite(quat).all()) or np.linalg.norm(quat) < 1.0e-6:
        raise CommandError("a pose must be finite, with a non-zero quaternion")
    if np.abs(position).max() > 5.0:
        raise CommandError("a target more than 5 m from the origin is outside any cell")
    return position, quat / np.linalg.norm(quat)


def _short_args(args: dict[str, Any]) -> dict[str, Any]:
    """Arguments as the history shows them: numbers rounded, documents summarised."""
    out: dict[str, Any] = {}
    for key, value in args.items():
        if key == "doc" and isinstance(value, dict):
            out[key] = f"workcell {value.get('name', '')!r} ({len(value.get('objects', []))} objects)"
        elif key == "steps" and isinstance(value, list):
            out[key] = f"{len(value)} steps"
        elif isinstance(value, float):
            out[key] = round(value, 5)
        elif isinstance(value, list) and len(value) <= 8:
            out[key] = [round(v, 5) if isinstance(v, float) else v for v in value]
        else:
            out[key] = value if isinstance(value, (int, str, bool)) or value is None else str(value)[:80]
    return out


def _validation_message(exc: Any) -> str:
    parts = []
    for error in exc.errors()[:6]:
        where = ".".join(str(p) for p in error.get("loc", ()))
        parts.append(f"{where}: {error.get('msg')}" if where else str(error.get("msg")))
    return "; ".join(parts)


def _preset_note(preset: str) -> str:
    from .workcell import PRESETS

    return PRESETS[preset]["summary"]
