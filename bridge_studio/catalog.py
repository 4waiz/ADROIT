# SPDX-License-Identifier: LicenseRef-BRIDGE
"""Every command the host takes: what it does, what it reads, what it answers.

The contract an operator's page and an external agent share. ``GET
/api/commands/catalog`` serves it; ``docs/bridge/AGENT_API.md`` is generated in
the same words. A command not listed here is refused by the host.

Units: metres, radians, seconds, newtons. Positions and orientations are in the
**world** frame (z up); quaternions are ``[x, y, z, w]``.
"""

from __future__ import annotations

__all__ = ["COMMANDS"]

_ARM = {"arm": "string -- an arm the scene has (manifest.arms[].name)"}
_GEN = {"generation": "int, optional -- refuse if the scene was rebuilt since (manifest.generation)"}

COMMANDS: list[dict] = [
    {"name": "sim.play", "summary": "Run the simulation at the playback speed.", "args": {}},
    {"name": "sim.pause", "summary": "Hold the simulation where it is.", "args": {}},
    {
        "name": "sim.step",
        "summary": "Pause, then advance exactly this many control steps.",
        "args": {"count": "int 1..600"},
    },
    {
        "name": "sim.reset",
        "summary": "Restore the scene's start state: objects re-placed, arm home, hands open. Ends a running routine.",
        "args": {},
    },
    {
        "name": "sim.speed",
        "summary": "Playback rate relative to real time.",
        "args": {"value": "one of 0.1, 0.25, 0.5, 1, 2, 4, or 0 for as fast as possible"},
    },
    {
        "name": "scenario.load",
        "summary": "Build a gallery scenario and swap it in. Answers accepted, then completed or failed.",
        "args": {"id": "string -- GET /api/scenarios"},
    },
    {
        "name": "workcell.build",
        "summary": "Build a workcell document (bridge.workcell/1) and swap it in. Validated first.",
        "args": {"doc": "object -- see GET /api/workcell/catalog"},
    },
    {
        "name": "arm.joint",
        "summary": "Joint mode: set one joint's target; the arm servoes there at its joint speed limit.",
        "args": {**_ARM, "joint": "int, 0-based", "value": "float, rad or m, inside the joint's limits", **_GEN},
    },
    {
        "name": "arm.joints",
        "summary": "Joint mode: set every joint's target at once.",
        "args": {**_ARM, "values": "float[n], inside each joint's limits", **_GEN},
    },
    {"name": "arm.home", "summary": "Joint mode: send the arm to its home posture.", "args": {**_ARM, **_GEN}},
    {
        "name": "arm.grip",
        "summary": "Command the gripper: 0 open .. 1 closed. What it holds is what friction holds.",
        "args": {**_ARM, "value": "float 0..1", **_GEN},
    },
    {
        "name": "arm.cursor",
        "summary": "Cartesian mode: move the tool-centre target. The arm tracks it through IK; the answer carries "
        "an off-line reachability verdict (residual, joints at limits).",
        "args": {**_ARM, "position": "float[3], world m", "quat": "float[4] xyzw", **_GEN},
    },
    {
        "name": "arm.jog",
        "summary": "Nudge the arm: a joint by delta, or the tool target along / about an axis.",
        "args": {
            **_ARM,
            "space": "'joint' or 'cartesian'",
            "joint": "int (joint space)",
            "axis": "x, y, z, rx, ry or rz (cartesian)",
            "frame": "'world' or 'tool' (cartesian)",
            "delta": "float: <= 0.5 rad per joint jog, <= 0.1 m or 0.5 rad per cartesian jog",
            **_GEN,
        },
    },
    {
        "name": "arm.reach",
        "summary": "Check whether a pose is reachable, without moving anything.",
        "args": {**_ARM, "position": "float[3]", "quat": "float[4] xyzw"},
    },
    {
        "name": "arm.speed",
        "summary": "Joint-mode speed limit and Cartesian cruise fraction for one arm.",
        "args": {**_ARM, "joint": "float 0.05..3.0 rad/s, optional", "cartesian": "float 0.05..1.0, optional"},
    },
    {"name": "arm.take", "summary": "Cartesian mode with the target put back on the hand.", "args": {**_ARM}},
    {
        "name": "routine.start",
        "summary": "Run one of the scenario's routines (manifest.routines). The arms are reserved while it runs.",
        "args": {"name": "string"},
    },
    {"name": "routine.stop", "summary": "Stop the running routine or task where it is.", "args": {}},
    {
        "name": "task.run",
        "summary": "Run a mission sequence: home / move / grip / wait steps, each acknowledged as it finishes.",
        "args": {
            "name": "string, optional",
            "steps": "list -- {op: home|move|grip|wait, arm, target: {kind: pose|object|region|relative, ...}, "
            "orientation: keep|down|grasp, value, seconds, speed, tolerance}",
        },
    },
    {"name": "task.stop", "summary": "Same as routine.stop.", "args": {}},
    {
        "name": "physics.gravity",
        "summary": "Change gravity along z while running, where the scene's solvers accept a live change.",
        "args": {"value": "float -30..0 m/s^2"},
    },
]
