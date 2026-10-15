# SPDX-License-Identifier: Apache-2.0
"""One arm driven from the browser: the studio's arm panel, without viser, plus joint mode.

:class:`ArmDriver` is :class:`~robosimstudio_studio.panels.PanelArm` re-stated for
a page that is not a viser page. The Cartesian half is the same code path: a
cursor the operator moves, an :class:`~robosimstudio.solvers.OscSetpoint` that
walks a setpoint toward it at the law's own rate and lead, and
``ArmIK.solve_into`` writing the arm's joint targets every control step. The
fingers are ``Gripper.command`` and nothing else, so what a hand holds is what
its friction holds.

It also offers the surface the shipped routines drive -- ``aim``,
``cursor_pos``, ``cursor_quat``, ``grip_request``, ``ik``, ``suspend`` and
``resume`` -- so :func:`robosimstudio_studio.routine.reach` and every demo routine
built on it run here unmodified.

Two things the studio panel does not do, both because a command centre is asked
for them:

* **Joint mode.** Each joint's target is set directly and the arm servoes to it.
  The written target walks toward the requested one at a joint speed limit, so a
  slider dragged across its range does not slam the arm across it in one step.
* **Reachability.** A requested pose is solved off-line first
  (``ArmIK.solve_q_full`` and ``pose_errors``), and the residual is reported.
  The arm still tracks the cursor as closely as it can -- that is what a person
  dragging a gizmo expects -- but the page says how far short it will stop and
  why, instead of quietly parking the hand somewhere else.
"""

from __future__ import annotations

import math
from typing import Any

import numpy as np

__all__ = ["ARRIVED", "ArmDriver", "HostState"]

ARRIVED = 0.004
"""How close the hand has to be to the cursor to count as on target [m]: the
studio's ``POS_TOLERANCE``, the OSC arrival ball."""

REACH_POSITION = 0.005
"""Position residual above which an off-line solve calls a pose unreachable [m]."""

REACH_ANGLE = math.radians(5.0)
"""Orientation residual above which an off-line solve calls a pose unreachable [rad]."""


class _HostArray:
    """A host array that answers ``.numpy()``, so library code reading a state reads the snapshot."""

    __slots__ = ("_value",)

    def __init__(self, value: np.ndarray) -> None:
        self._value = value

    def numpy(self) -> np.ndarray:
        return self._value


class HostState:
    """A ``newton.State`` look-alike over one :class:`~robosimstudio.core.readback.HostSnapshot`.

    ``ArmIK.ee_pose`` and ``Gripper.width`` read ``state.body_q.numpy()`` and
    ``state.joint_q.numpy()``; each such call on a device state is a
    synchronize. The pipeline already copied both to the host once this step,
    so the readouts are computed from that copy through the library's own
    methods -- the same arithmetic, without a second device round trip.
    """

    __slots__ = ("body_q", "joint_q")

    def __init__(self, snapshot: Any) -> None:
        self.body_q = _HostArray(np.asarray(snapshot.body_q, dtype=np.float32))
        self.joint_q = _HostArray(np.asarray(snapshot.joint_q, dtype=np.float32))


def _quat_angle(a: np.ndarray, b: np.ndarray) -> float:
    """The rotation between two unit quaternions [rad]."""
    d = abs(float(np.dot(a / np.linalg.norm(a), b / np.linalg.norm(b))))
    return 2.0 * math.acos(min(1.0, d))


class ArmDriver:
    """One arm: a cursor the arm tracks, joint targets it holds, a grip it squeezes.

    Args:
        sim: The built scene.
        name: The arm's name in the embodiment (``"right"``, ``"left"``).
        joint_speed: Fastest a joint target moves in joint mode [rad/s or m/s].
    """

    def __init__(self, sim: Any, name: str, *, joint_speed: float = 1.0) -> None:
        from robosimstudio.solvers import OscSetpoint

        self.sim = sim
        self.name = name
        self.ik = sim.arm(name)
        embodiment = sim.scene.embodiment
        self.gripper = embodiment.grippers.get(name)
        self.index = embodiment.arms[name]
        self.control_hz = max(1.0, float(sim.request.physics.control_hz))
        self._osc_cls = OscSetpoint
        self.cartesian_scale = 1.0
        """Fraction of the law's own cruise speed (0.31 m/s) the hand moves at."""
        self.law = OscSetpoint(control_dt=1.0 / self.control_hz)
        self.law.reset(*self.ik.home_pose)
        self.joint_speed = float(joint_speed)

        model = sim.model
        self.coords = np.asarray(self.index.arm_coords, dtype=np.int64)
        self.dofs = np.asarray(self.index.arm_dofs, dtype=np.int64)
        lower = model.joint_limit_lower.numpy()[self.dofs].astype(np.float64)
        upper = model.joint_limit_upper.numpy()[self.dofs].astype(np.float64)
        self.lower = np.where(np.abs(lower) >= 1.0e6, -np.inf, lower)
        self.upper = np.where(np.abs(upper) >= 1.0e6, np.inf, upper)

        self.mode = "cartesian"
        """``cartesian`` (the cursor drives the hand through IK) or ``joint`` (the
        joint targets are set directly)."""
        self.cursor_pos, self.cursor_quat = self.ik.home_pose
        self.dragging = False
        self.joint_goal: np.ndarray | None = None
        """The joint targets asked for, in joint mode."""
        self.joint_command: np.ndarray | None = None
        """The joint targets written this step: the goal, reached at :attr:`joint_speed`."""
        self.gripper_command = 0.0
        self.grip_request: float | None = None
        """A grip asked for between steps, applied by the next :meth:`drain` (the routines' door)."""
        self.suspended = False
        """A routine that drives the scene itself has the arm; nothing here writes it."""
        self.status = "on target"
        self.gap = 0.0
        """How far the hand is from the cursor [m], last step."""
        self.reach: dict[str, Any] | None = None
        """The last off-line verdict on the cursor: reachable or not, and by how much."""
        self.source = "operator"
        """Who last moved this arm: ``operator``, ``routine:<name>``, ``task:<name>``."""
        self._seated: np.ndarray | None = None
        self._last_commanded: tuple[np.ndarray, np.ndarray] | None = None

    # ----------------------------------------------------------------- reading
    def ee_pose(self, state: Any) -> tuple[np.ndarray, np.ndarray]:
        """Tool-centre pose from a state or a :class:`HostState`, through ``ArmIK.ee_pose``."""
        position, quat = self.ik.ee_pose(state)
        return np.asarray(position, dtype=np.float64), np.asarray(quat, dtype=np.float64)

    def joint_q(self, snapshot: Any) -> np.ndarray:
        return np.asarray(snapshot.joint_q, dtype=np.float64)[self.coords]

    # --------------------------------------------------- the routines' surface
    def aim(self, position, quat=None, *, moving: bool = True) -> None:
        """Put the cursor somewhere, as a drag of the gizmo does (``PanelArm.aim``).

        Switches the arm to Cartesian mode if it was holding joint targets,
        re-seeding the law where the hand is so the hand-over is continuous.
        """
        if self.mode != "cartesian":
            self._enter_cartesian()
        self.cursor_pos = np.asarray(position, dtype=np.float64).copy()
        if quat is not None:
            q = np.asarray(quat, dtype=np.float64)
            self.cursor_quat = q / np.linalg.norm(q)
        self.dragging = bool(moving)

    def suspend(self) -> None:
        """Give the arm up to something that drives the scene itself."""
        self.suspended = True

    def resume(self) -> None:
        """Take the arm back from wherever that left it, holding what it holds."""
        self.suspended = False
        if self.gripper is not None:
            self.grip_request = float(self.gripper.command)
        self.take_hand(self.sim.state)

    # ------------------------------------------------------------- operator
    def set_joint(self, joint: int, value: float, snapshot: Any) -> float:
        """Ask for one joint's target, holding the others where they are. Returns the value used.

        Raises:
            IndexError: For a joint this arm does not have.
            ValueError: For a value outside the joint's limits.
        """
        if not 0 <= joint < len(self.coords):
            raise IndexError(f"arm {self.name!r} has joints 0..{len(self.coords) - 1}, not {joint}")
        value = float(value)
        lo, hi = float(self.lower[joint]), float(self.upper[joint])
        if not math.isfinite(value):
            raise ValueError("a joint target must be a finite number")
        if value < lo - 1.0e-6 or value > hi + 1.0e-6:
            raise ValueError(f"{value:.4f} is outside this joint's limits [{lo:.4f}, {hi:.4f}]")
        if self.mode != "joint":
            self._enter_joint(snapshot)
        assert self.joint_goal is not None
        self.joint_goal[joint] = value
        return value

    def set_joints(self, values, snapshot: Any) -> np.ndarray:
        """Ask for every joint's target at once. Out-of-limit values are refused, not clamped."""
        goal = np.asarray(values, dtype=np.float64).reshape(-1)
        if goal.shape[0] != len(self.coords):
            raise ValueError(f"arm {self.name!r} has {len(self.coords)} joints; got {goal.shape[0]} values")
        if not np.isfinite(goal).all():
            raise ValueError("joint targets must be finite numbers")
        bad = np.nonzero((goal < self.lower - 1.0e-6) | (goal > self.upper + 1.0e-6))[0]
        if len(bad):
            k = int(bad[0])
            raise ValueError(
                f"joint {k} target {goal[k]:.4f} is outside its limits [{self.lower[k]:.4f}, {self.upper[k]:.4f}]"
            )
        if self.mode != "joint":
            self._enter_joint(snapshot)
        self.joint_goal = goal.copy()
        return goal

    def home(self, snapshot: Any) -> None:
        """Send every joint to the arm's home posture, in joint mode at the joint speed limit."""
        home = np.asarray(self.index.home_q, dtype=np.float64)
        if home.shape[0] != len(self.coords):
            # An arm that declares no home posture goes back to its compiled one.
            home = np.asarray(self.sim.model.joint_q.numpy(), dtype=np.float64)[self.coords]
        self.set_joints(np.clip(home, self.lower, self.upper), snapshot)

    def set_grip(self, value: float) -> float:
        if self.gripper is None:
            raise ValueError(f"arm {self.name!r} has no gripper")
        value = float(value)
        if not math.isfinite(value) or value < 0.0 or value > 1.0:
            raise ValueError("a grip command is between 0 (open) and 1 (closed)")
        self.grip_request = value
        return value

    def set_cartesian_speed(self, fraction: float) -> None:
        """Scale the law's cruise speed and lead together (``OscSetpoint.speed_scale_for``)."""
        from robosimstudio.solvers.motion.osc import OSC_OUTPUT_MAX

        fraction = float(np.clip(fraction, 0.05, 1.0))
        setpoint = self.law.setpoint
        self.law = self._osc_cls(
            control_dt=1.0 / self.control_hz, output_max=np.asarray(OSC_OUTPUT_MAX, dtype=np.float64) * fraction
        )
        self.law.reset(*setpoint)
        self.cartesian_scale = fraction

    @property
    def cartesian_speed(self) -> float:
        """The hand's cruise speed at the current scale [m/s]."""
        return float(self.law.pos_speed)

    def check_reach(self, position, quat, snapshot: Any) -> dict[str, Any]:
        """Solve the pose off-line from the arm's current configuration and report the residual.

        Not collision aware, as the tracking itself is not: a pose through the
        bench is reported reachable if the joints can put the hand there.
        """
        position = np.asarray(position, dtype=np.float64)
        quat = np.asarray(quat, dtype=np.float64)
        quat = quat / np.linalg.norm(quat)
        seed = np.asarray(snapshot.joint_q, dtype=np.float64)
        q = self.ik.solve_q_full(seed, position, quat)
        pos_err, rot_err = self.ik.pose_errors(q, position, quat)
        arm_q = np.asarray(q, dtype=np.float64)[self.coords]
        at_limit = [
            int(k)
            for k in range(len(arm_q))
            if math.isfinite(self.lower[k])
            and math.isfinite(self.upper[k])
            and (arm_q[k] - self.lower[k] < 1.0e-3 or self.upper[k] - arm_q[k] < 1.0e-3)
        ]
        reachable = pos_err <= REACH_POSITION and rot_err <= REACH_ANGLE
        why = ""
        if not reachable:
            parts = []
            if pos_err > REACH_POSITION:
                parts.append(f"the hand stops {pos_err * 1000:.0f} mm short")
            if rot_err > REACH_ANGLE:
                parts.append(f"{math.degrees(rot_err):.0f} deg off the requested orientation")
            if at_limit:
                parts.append(f"joint {', '.join(str(k + 1) for k in at_limit)} at its limit")
            why = "; ".join(parts)
        self.reach = {
            "reachable": bool(reachable),
            "positionError": round(float(pos_err), 6),
            "angleError": round(float(rot_err), 6),
            "atLimit": at_limit,
            "solver": self.ik.solver_name or "newton",
            "target": [round(float(v), 6) for v in (*position, *quat)],
            "why": why,
        }
        return self.reach

    # ------------------------------------------------------------------ modes
    def _enter_joint(self, snapshot: Any) -> None:
        q = self.joint_q(snapshot)
        self.joint_goal = q.copy()
        self.joint_command = q.copy()
        self.mode = "joint"
        self.dragging = False

    def _enter_cartesian(self) -> None:
        self.mode = "cartesian"
        self.joint_goal = self.joint_command = None
        self.take_hand(self.sim.state)

    def take_hand(self, state: Any) -> None:
        """Put the cursor on the tool centre and start tracking from there (``PanelArm._take_hand``)."""
        position, quat = self.ee_pose(state)
        self.cursor_pos, self.cursor_quat = position, quat
        self._seated = position.copy()
        self.law.reset(position, quat)
        self._last_commanded = None
        self.dragging = False

    # ------------------------------------------------------------------- loop
    def drain(self, env: Any, *, paused: bool, host_state: HostState) -> None:
        """Write this arm's targets for the coming step. Owner thread only.

        Paused holds the move rather than consuming it, as the studio does: the
        law would otherwise walk its setpoint away from an arm that is not
        moving and spend the whole lead the moment the scene resumed.
        """
        if self.suspended:
            return
        if not paused:
            if self.mode == "joint" and self.joint_goal is not None:
                self._drive_joints(env)
            else:
                self._drive_cartesian(env, host_state)
        if self.gripper is not None:
            if self.grip_request is not None:
                self.gripper_command, self.grip_request = float(self.grip_request), None
            self.gripper.command = self.gripper_command
            self.gripper.apply(env.control.joint_target_q)

    def _drive_joints(self, env: Any) -> None:
        assert self.joint_goal is not None
        if self.joint_command is None:
            self.joint_command = self.joint_goal.copy()
        limit = self.joint_speed / self.control_hz
        self.joint_command = self.joint_command + np.clip(self.joint_goal - self.joint_command, -limit, limit)
        self.ik.set_joint_targets(self.joint_command.astype(np.float32), env.control.joint_target_q)
        self.status = "joint targets reached" if np.allclose(self.joint_command, self.joint_goal) else "moving"

    def _drive_cartesian(self, env: Any, host_state: HostState) -> None:
        """Track the cursor (``PanelArm._drive``): the law's setpoint, then the IK solve into the targets."""
        position, quat = self.ee_pose(host_state)
        if self.ik.solver is None:
            # LM-hostile models solve on the host; solve only when the cursor moved, as the studio does.
            moved = self._last_commanded is None or (
                float(np.linalg.norm(self.cursor_pos - self._last_commanded[0])) > 1.0e-4
                or float(np.linalg.norm(self.cursor_quat - self._last_commanded[1])) > 1.0e-3
            )
            if moved:
                self.ik.set_target(self.cursor_pos, self.cursor_quat)
                self._last_commanded = (self.cursor_pos.copy(), self.cursor_quat.copy())
                self.ik.solve_into(env.state.joint_q, env.control.joint_target_q)
        else:
            self.ik.set_target(*self.law.advance(position, quat, self.cursor_pos, self.cursor_quat))
            self.ik.solve_into(env.state.joint_q, env.control.joint_target_q)

        self.gap = float(np.linalg.norm(position - self.cursor_pos))
        if not self.dragging and self.gap < ARRIVED:
            if self._seated is None or not np.array_equal(self._seated, self.cursor_pos):
                # Seated once per move, so a held load's sag never becomes the next target.
                self.cursor_pos, self.cursor_quat = position, quat
                self._seated = position.copy()
            self.status = "on target"
        elif not self.dragging:
            self.status = f"{self.gap * 1000:.0f} mm behind the cursor"
        else:
            self.status = "tracking"

    def on_reset(self, state: Any) -> None:
        """A reset opened the hand and put the arm home: start from there."""
        self.gripper_command = 0.0
        self.grip_request = None
        self.mode = "cartesian"
        self.joint_goal = self.joint_command = None
        self.reach = None
        self.suspended = False
        self.source = "operator"
        self.take_hand(state)

    # ----------------------------------------------------------------- report
    def telemetry(self, snapshot: Any, host_state: HostState, effort: np.ndarray | None) -> dict[str, Any]:
        """What the inspector and the plots show for this arm this frame. Measured, not commanded, where both exist."""
        q = np.asarray(snapshot.joint_q, dtype=np.float64)[self.coords]
        qd = np.asarray(snapshot.joint_qd, dtype=np.float64)[self.dofs]
        targets = np.asarray(snapshot.joint_target_q, dtype=np.float64)
        target = targets[self.coords] if targets.size > int(self.coords.max(initial=0)) else np.zeros(0)
        position, quat = self.ee_pose(host_state)
        out: dict[str, Any] = {
            "mode": self.mode,
            "q": [round(float(v), 5) for v in q],
            "qd": [round(float(v), 5) for v in qd],
            "target": [round(float(v), 5) for v in target],
            "effort": None if effort is None else [round(float(v), 4) for v in effort[self.dofs]],
            "ee": [round(float(v), 5) for v in (*position, *quat)],
            "cursor": [round(float(v), 5) for v in (*self.cursor_pos, *self.cursor_quat)],
            "gap": round(self.gap, 5),
            "status": self.status,
            "suspended": self.suspended,
            "source": self.source,
            "speed": {"joint": self.joint_speed, "cartesian": round(self.cartesian_speed, 4)},
            "reach": self.reach,
        }
        if self.mode == "joint" and self.joint_goal is not None:
            out["goal"] = [round(float(v), 5) for v in self.joint_goal]
        if self.gripper is not None:
            width = float(self.gripper.width(host_state))
            out["grip"] = {
                "command": round(float(self.gripper.command), 4),
                "width": round(width, 5),
                "closure": round(float(np.clip(1.0 - width / self.gripper.span, 0.0, 1.0)), 4)
                if self.gripper.span > 0
                else None,
            }
        return out


def angle_between(a, b) -> float:
    """Public alias used by the task runner: the rotation between two xyzw quaternions [rad]."""
    return _quat_angle(np.asarray(a, dtype=np.float64), np.asarray(b, dtype=np.float64))
