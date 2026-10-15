# SPDX-License-Identifier: Apache-2.0
"""The scenario gallery: what BRIDGE can build, and what each one can do once built.

A :class:`Scenario` is a description and a builder. The builder returns a
:class:`BuiltScenario`: the built :class:`~robosimstudio.sim.Sim`, the routines
it can run, anything that must happen every control step, and the success
measure it is judged by. Three of the five are the repository's own demos
(``examples/08_demos``), imported from their folders and run unmodified -- the
scene, the measured routine and the measured goal are theirs, which is the point:
a scenario here is the same physics the paper's clips show, not a re-staging.
The other two are written here on the public API and say so.

**Every scenario is real physics end to end.** Routines move the arms through the
same cursors and IK an operator's drag does (:mod:`bridge_studio.arms`); what a
hand carries is what its friction carries, except where a demo's own README
says otherwise (the wipe's latch, which that demo documents and measures).
"""

from __future__ import annotations

import importlib.util
import math
import sys
from collections.abc import Callable
from dataclasses import dataclass, field
from typing import Any

import numpy as np

from .config import repo_root

__all__ = ["BuiltScenario", "RoutineSession", "Scenario", "ScenarioRegistry", "builtin_registry"]


@dataclass
class BuiltScenario:
    """A built scene and what comes with it.

    Attributes:
        sim: The built scene.
        routines: ``name -> fn(sim, session)``: generators yielding once per control
            step, the studio's routine contract (:mod:`robosimstudio_studio.routine`).
        each_step: Called once per control step on the owner thread, before the step.
        success: ``fn(sim) -> {"done": bool, "detail": str, "metrics": {...}}``.
        camera: A suggested view, ``(eye, target)`` in world metres.
        notes: Sentences the page shows about how this scene is simulated.
    """

    sim: Any
    routines: dict[str, Callable[..., Any]] = field(default_factory=dict)
    each_step: list[Callable[[Any], None]] = field(default_factory=list)
    success: Callable[[Any], dict[str, Any]] | None = None
    camera: tuple[tuple[float, float, float], tuple[float, float, float]] | None = None
    notes: list[str] = field(default_factory=list)


@dataclass(frozen=True)
class Scenario:
    """One entry in the gallery.

    Attributes:
        id: Stable key, used in URLs and saved state.
        title: What the gallery card says.
        summary: One or two sentences on what happens in it.
        robot: The embodiment, as registered.
        physics: How it is simulated, in words.
        category: ``manipulation``, ``deformable`` or ``multi-arm``.
        requires: What the machine needs: ``cuda``, ``superdex``.
        media: A preview clip or still, relative to the repository root.
        success_label: What the success measure checks, in words.
        source: Where the scene and routine are defined.
        build: Returns the built scene.
    """

    id: str
    title: str
    summary: str
    robot: str
    physics: str
    category: str
    build: Callable[[], BuiltScenario]
    requires: tuple[str, ...] = ("cuda",)
    media: str = ""
    success_label: str = ""
    source: str = ""

    def availability(self) -> tuple[bool, str]:
        """Whether this machine can build it, and if not, the specific reason."""
        for need in self.requires:
            ok, why = _CHECKS[need]()
            if not ok:
                return False, why
        return True, ""

    def describe(self) -> dict[str, Any]:
        ok, why = self.availability()
        return {
            "id": self.id,
            "title": self.title,
            "summary": self.summary,
            "robot": self.robot,
            "physics": self.physics,
            "category": self.category,
            "requires": list(self.requires),
            "available": ok,
            "unavailableReason": why,
            "media": f"/api/scenarios/{self.id}/media" if self.media else "",
            "successLabel": self.success_label,
            "source": self.source,
        }


class RoutineSession:
    """What a routine written for the studio page reads off the page: ``arms`` and ``env``."""

    def __init__(self, sim: Any, arms: dict[str, Any]) -> None:
        self.env = sim
        self.arms = arms


class ScenarioRegistry:
    def __init__(self, scenarios: list[Scenario]) -> None:
        self._by_id = {s.id: s for s in scenarios}

    def get(self, scenario_id: str) -> Scenario:
        try:
            return self._by_id[scenario_id]
        except KeyError:
            raise KeyError(f"no scenario {scenario_id!r}; there is {sorted(self._by_id)}") from None

    def __contains__(self, scenario_id: str) -> bool:
        return scenario_id in self._by_id

    def all(self) -> list[Scenario]:
        return list(self._by_id.values())


# ---------------------------------------------------------------------------
# What a machine must have
# ---------------------------------------------------------------------------


def _has_cuda() -> tuple[bool, str]:
    try:
        import warp as wp

        if wp.is_cuda_available():
            return True, ""
        return False, "No CUDA GPU was found. These scenes run on Newton and MuJoCo Warp, which need one."
    except Exception as exc:  # pragma: no cover -- depends on the install
        return False, f"Warp did not initialise: {type(exc).__name__}: {exc}"


def _has_superdex() -> tuple[bool, str]:
    if importlib.util.find_spec("superdex") is not None:
        return True, ""
    return False, (
        "The sponge is a SuperDex FEM body and SuperDex is not installed. "
        "Install it with `uv sync --extra superdex` (Python 3.12)."
    )


_CHECKS: dict[str, Callable[[], tuple[bool, str]]] = {"cuda": _has_cuda, "superdex": _has_superdex}


# ---------------------------------------------------------------------------
# The shipped demos, imported from their folders
# ---------------------------------------------------------------------------


def _demo_module(folder: str, name: str) -> Any:
    """Import ``examples/08_demos/<folder>/<name>.py``, with its folder importable for its siblings.

    The demos import their neighbours by bare name (``import bar``), as a script
    run from its folder does, so the folder goes on ``sys.path`` for the import.
    A module already imported is reused: a scenario rebuilt is the same code.
    """
    directory = repo_root() / "examples" / "08_demos" / folder
    key = f"bridge_demo_{folder}_{name}"
    if key in sys.modules:
        return sys.modules[key]
    path = directory / f"{name}.py"
    if not path.is_file():
        raise FileNotFoundError(f"the demo module {path} is missing from this checkout")
    added = str(directory) not in sys.path
    if added:
        sys.path.insert(0, str(directory))
    try:
        spec = importlib.util.spec_from_file_location(key, path)
        assert spec is not None and spec.loader is not None
        module = importlib.util.module_from_spec(spec)
        sys.modules[key] = module
        spec.loader.exec_module(module)
    except BaseException:
        sys.modules.pop(key, None)
        raise
    finally:
        if added and str(directory) in sys.path:
            sys.path.remove(str(directory))
    return module


def _build_wipe() -> BuiltScenario:
    bar = _demo_module("wipe_the_bar", "bar")
    routine = _demo_module("wipe_the_bar", "routine")
    sim = bar.scene().build(control_mode="joint_position")
    bar.latch(sim)
    stripe = sim["stripe"].entity

    def success(sim) -> dict[str, Any]:
        cleaned = float(stripe.cleaned)
        return {
            "done": cleaned >= float(routine.CLEANED),
            "detail": f"{cleaned:.0%} of the marker stripe cleaned (goal {routine.CLEANED:.0%})",
            "metrics": {
                "cleaned": round(cleaned, 4),
                "pressN": round(float(stripe.press), 3),
                "peakKPa": round(float(stripe.peak) / 1000.0, 3),
            },
        }

    return BuiltScenario(
        sim=sim,
        routines={"Wipe the bar": routine.routine},
        each_step=[bar.latch],
        success=success,
        camera=(tuple(bar.EYE), tuple(bar.LOOK)),
        notes=[
            "The sponge is a SuperDex FEM body co-simulated beside the MuJoCo arms.",
            "Once the jaws grip the foam by contact, the demo's latch pins it to the hand until Open "
            "(examples/08_demos/wipe_the_bar/README.md says why and what it measured).",
        ],
    )


def _build_towel() -> BuiltScenario:
    towel = _demo_module("fold_the_towel", "fold_the_towel")
    sim = towel.build()
    x, y, z = (float(v) for v in sim["towel"].particle_q.mean(axis=0))

    def success(sim) -> dict[str, Any]:
        m = towel.fold_measures(sim)
        done = m["fold_extent"] <= 0.6 and m["fold_score"] >= 0.85
        return {
            "done": bool(done),
            "detail": f"fold extent {m['fold_extent']:.3f} (goal <= 0.6), score {m['fold_score']:.3f} (goal >= 0.85)",
            "metrics": {k: round(float(v), 5) for k, v in m.items()},
        }

    return BuiltScenario(
        sim=sim,
        routines={"Fold the towel": towel.routine},
        success=success,
        camera=((x + 0.45, y, z + 0.35), (x, y, z)),
        notes=[
            "Cloth on Newton's VBD solver, the arms on MuJoCo, coupled one way with full-surface contact.",
            "The pads hold the towel edge by friction alone.",
        ],
    )


def _build_cable() -> BuiltScenario:
    import robosimstudio as rss

    cable = _demo_module("route_the_cable", "route_the_cable")
    board = cable.board
    # The scene as route_the_cable.main() stages it; the numbers and why are there.
    physics = rss.replace(
        rss.GENERIC.with_backend(
            "*", rss.backend("vbd", rigid_body_contact_buffer_size=256, rigid_contact_history=True)
        ),
        collision=rss.CollisionSpec(contact_matching="sticky"),
    )
    fingers = rss.ActuationSpec(target_ke=6000.0, target_kd=120.0)
    scene = rss.Scene(cell=board.CELL, physics=physics, name="route_the_cable")
    scene.add(rss.robot("yam_i2rt", gripper=fingers))
    for part in board.scenery():
        scene.add(part)
    scene.add(board.cable())
    sim = scene.build(control_mode="joint_position")

    def success(sim) -> dict[str, Any]:
        hands = [np.asarray(sim.arm(name).ee_pose(sim.state)[0]) for name in sim.scene.embodiment.iks]
        m = board.done(sim, hands)
        return {
            "done": bool(m["routed"]),
            "detail": (
                f"seats {m['seat0']:.2f} / {m['seat1']:.2f} (>= 0.04), port {m['port']:.2f} (>= 0.06), "
                f"end in port {m['end']:.0f}, hands clear {m['hands'] * 1000:.0f} mm"
            ),
            "metrics": {k: (round(float(v), 4) if not isinstance(v, bool) else v) for k, v in m.items()},
        }

    return BuiltScenario(
        sim=sim,
        routines={"Route the cable": cable.routine},
        success=success,
        camera=((0.95, -0.10, 1.10), (0.53, -0.12, 0.76)),
        notes=[
            "The cable is a 7 mm capsule chain on Newton's VBD solver with sticky (anchored) contacts.",
            "Nothing latches the cable: the pads hold it by friction.",
        ],
    )


# ---------------------------------------------------------------------------
# Written here: a Panda pick and place, and a dual-arm cell
# ---------------------------------------------------------------------------

CUBE_HALF = 0.025
"""The Panda's cube [m]: the hello-world example's 50 mm cube."""
PANDA_CUBE_AT = (-0.143, -0.115)
"""Under the Panda's home tool centre, in the work-surface frame [m] (hello world's spot)."""
PANDA_PLACE_AT = (-0.143, 0.115)
"""The place zone's centre, mirrored across the arm's sagittal plane [m]."""
ZONE_HALF = 0.04
"""Half-width of a place zone [m]."""


def _face_grasp_quat(driver: Any, yaw: float) -> np.ndarray:
    """Top-down, with the jaws closing across a box's faces rather than its diagonal.

    The home orientation already points down; this turns it about world z by the
    smallest angle that lines the jaws' closing axis up with one of the box's
    face normals (``yaw`` plus a multiple of 90 degrees).
    """
    from robosimstudio.core.math import quat_multiply, quat_to_mat

    home_quat = np.asarray(driver.ik.home_pose[1], dtype=np.float64)
    close_world = quat_to_mat(home_quat) @ np.asarray(driver.index.close_axis, dtype=np.float64)
    angle = math.atan2(float(close_world[1]), float(close_world[0]))
    quarter = math.pi / 2.0
    want = yaw + round((angle - yaw) / quarter) * quarter
    turn = want - angle
    return np.asarray(quat_multiply([0.0, 0.0, math.sin(turn / 2), math.cos(turn / 2)], home_quat), dtype=np.float64)


def pick_and_place(
    sim: Any,
    session: Any,
    arm: str,
    thing: str,
    place_xy_world: tuple[float, float],
    *,
    lift: float = 0.14,
    grasp_drop: float = 0.0,
    log: Callable[[str], None] | None = None,
):
    """Pick ``thing`` up from the top and set it down at ``place_xy_world``: one yield per control step.

    Every motion is a :func:`~robosimstudio_studio.routine.reach` of the arm's
    cursor, so the hand moves as a hand on the gizmo would and holds the box by
    friction alone. The phases are reported through ``log`` as they start.
    """
    from robosimstudio.core.math import quat_yaw
    from robosimstudio_studio.routine import grip, reach, wait

    say = log or (lambda _text: None)
    driver = session.arms[arm]
    pose = np.asarray(sim[thing].pose, dtype=np.float64)
    position, yaw = pose[:3], quat_yaw(pose[3:7])
    quat = _face_grasp_quat(driver, yaw)
    half = float(abs(sim[thing].pose[2] - sim.scene.handles.surface_z))
    above = position + np.array([0.0, 0.0, lift])
    at = position + np.array([0.0, 0.0, -grasp_drop])
    surface = float(sim.scene.handles.surface_z)
    place_at = np.array([place_xy_world[0], place_xy_world[1], surface + half + 0.004])
    place_above = place_at + np.array([0.0, 0.0, lift])

    say(f"{arm}: open the gripper")
    yield from grip(session, arm, 0.0, 0.4)
    say(f"{arm}: approach above {thing}")
    yield from reach(session, arm, above, quat, speed=0.2)
    say(f"{arm}: descend to the grasp")
    yield from reach(session, arm, at, quat, speed=0.08)
    say(f"{arm}: close the gripper")
    yield from grip(session, arm, 1.0, 0.8)
    say(f"{arm}: lift")
    yield from reach(session, arm, above, quat, speed=0.12, tolerance=0.012)
    say(f"{arm}: carry to the place zone")
    yield from reach(session, arm, place_above, quat, speed=0.15, tolerance=0.012)
    say(f"{arm}: lower onto the zone")
    yield from reach(session, arm, place_at, quat, speed=0.06, tolerance=0.012)
    say(f"{arm}: release")
    yield from grip(session, arm, 0.0, 0.6)
    say(f"{arm}: retreat")
    yield from reach(session, arm, place_above, quat, speed=0.15)
    say(f"{arm}: return home")
    yield from reach(session, arm, *driver.ik.home_pose, speed=0.2)
    yield from wait(session, 0.5)


def _in_zone(sim: Any, thing: str, zone: str) -> tuple[bool, float, float]:
    """Whether a box lies in a region, how far from its centre [m], and how fast it is moving [m/s]."""
    region = sim.scene.handles.regions[zone]
    body_q = sim.pipeline.snapshot.body_q
    position = np.asarray(sim[thing].position, dtype=np.float64)
    origin = region.origin_now(body_q)
    x0, y0, x1, y1 = region.xy_ranges
    lo_z, hi_z = region.z_range
    rel = position - origin
    inside = bool(x0 <= rel[0] <= x1 and y0 <= rel[1] <= y1 and lo_z <= rel[2] <= hi_z)
    centre = region.center(body_q)
    distance = float(np.linalg.norm((position - centre)[:2]))
    speed = float(np.linalg.norm(np.asarray(sim[thing].velocity, dtype=np.float64)[:3]))
    return inside, distance, speed


def _zone(name: str, xy: tuple[float, float], half: float = ZONE_HALF):
    import robosimstudio as rss

    return rss.RegionSpec(
        name=name, frame="", xy_ranges=(xy[0] - half, xy[1] - half, xy[0] + half, xy[1] + half), z_range=(0.0, 0.08)
    )


def _build_pick_place() -> BuiltScenario:
    import robosimstudio as rss

    scene = rss.Scene(cell="bench_075", physics=rss.MUJOCO_500HZ, name="pick_place")
    scene.add(rss.robot("panda"))
    scene.add(
        rss.box(
            name="cube",
            half=(CUBE_HALF,) * 3,
            at=(*PANDA_CUBE_AT, CUBE_HALF + 0.002),
            mass=0.1,
            color=(0.95, 0.34, 0.13),
        )
    )
    scene.add(_zone("place_zone", PANDA_PLACE_AT))
    sim = scene.build(control_mode="joint_position")
    origin = np.array([0.6, 0.0])
    target = tuple(float(v) for v in origin + np.asarray(PANDA_PLACE_AT))

    def routine(sim, session):
        yield from pick_and_place(sim, session, "right", "cube", target)

    def success(sim) -> dict[str, Any]:
        inside, distance, speed = _in_zone(sim, "cube", "place_zone")
        return {
            "done": bool(inside and speed < 0.02),
            "detail": f"cube {distance * 1000:.0f} mm from the zone centre, "
            + ("inside the zone" if inside else "outside the zone")
            + (", at rest" if speed < 0.02 else f", moving {speed * 1000:.0f} mm/s"),
            "metrics": {"inZone": inside, "distanceM": round(distance, 4), "speedMps": round(speed, 4)},
        }

    return BuiltScenario(
        sim=sim,
        routines={"Pick and place": routine},
        success=success,
        camera=((1.55, -0.85, 1.35), (0.45, 0.0, 0.8)),
        notes=[
            "Arm and cube share one MuJoCo island at 500 Hz, so closed fingers hold the cube by friction.",
        ],
    )


YAM_CUBE_HALF = 0.02
"""The dual-arm cell's cubes [m]: 40 mm, inside the YAM jaws' opening."""


def _build_dual_arm() -> BuiltScenario:
    import robosimstudio as rss

    scene = rss.Scene(cell="bench_075", physics=rss.MUJOCO_500HZ, name="dual_arm_cell")
    scene.add(rss.robot("yam_bimanual"))
    # Each cube under its arm's home tool centre (world y = +-0.31 over the bench),
    # and each zone 170 mm inboard. Both spots were checked with ArmIK.solve_q_full
    # on a 50 mm grid, grasp height and 100 mm above it: the YAM reaches a narrower
    # band than a Panda, and a spot outside it stops the hand short (RoutineError).
    spots = {"left": (0.03, 0.30), "right": (0.03, -0.30)}
    colors = {"left": (0.95, 0.34, 0.13), "right": (0.16, 0.52, 0.86)}
    for side, (x, y) in spots.items():
        scene.add(
            rss.box(
                name=f"cube_{side}",
                half=(YAM_CUBE_HALF,) * 3,
                at=(x, y, YAM_CUBE_HALF + 0.002),
                mass=0.06,
                color=colors[side],
            )
        )
    places = {"left": (0.12, 0.13), "right": (0.12, -0.13)}
    for side, xy in places.items():
        scene.add(_zone(f"zone_{side}", xy, half=0.035))
    sim = scene.build(control_mode="joint_position")
    origin = np.array([0.6, 0.0])

    def routine(sim, session):
        from robosimstudio_studio.routine import wait

        moves = [
            pick_and_place(
                sim,
                session,
                side,
                f"cube_{side}",
                tuple(float(v) for v in origin + np.asarray(places[side])),
                lift=0.10,
            )
            for side in ("left", "right")
        ]
        yield from _together(*moves)
        yield from wait(session, 1.0)

    def success(sim) -> dict[str, Any]:
        verdicts = {side: _in_zone(sim, f"cube_{side}", f"zone_{side}") for side in ("left", "right")}
        done = all(inside and speed < 0.02 for inside, _, speed in verdicts.values())
        detail = ", ".join(
            f"{side} cube {'in' if inside else 'out of'} its zone ({distance * 1000:.0f} mm)"
            for side, (inside, distance, _) in verdicts.items()
        )
        return {
            "done": bool(done),
            "detail": detail,
            "metrics": {f"{side}InZone": v[0] for side, v in verdicts.items()},
        }

    return BuiltScenario(
        sim=sim,
        routines={"Dual-arm transfer": routine},
        success=success,
        camera=((1.45, 0.0, 1.45), (0.55, 0.0, 0.78)),
        notes=[
            "Two YAM arms on one bench, one MuJoCo island: each arm has its own joints, gripper and cursor.",
            "Both transfers run at once, stepped together by one control loop.",
        ],
    )


def _together(*moves):
    """Advance several routines one control step at a time until every one is done (fold_the_towel's helper)."""
    live = list(moves)
    while live:
        for move in list(live):
            try:
                next(move)
            except StopIteration:
                live.remove(move)
        yield


def _build_sponge() -> BuiltScenario:
    from robosimstudio.domains.soft import FIRM_FOAM

    import robosimstudio as rss

    # examples/02_soft/00_sponge.py, with its measured numbers.
    foam = rss.replace(FIRM_FOAM, friction_falloff=1.0e-3)
    grip = rss.ActuationSpec(target_ke=1500.0, target_kd=60.0, effort_limit=8.0)
    physics = rss.GENERIC.with_backend("soft", rss.backend("superdex", substeps=2, feedback_dv_max=20.0))
    scene = rss.Scene(cell="bench_075", physics=physics, name="sponge")
    scene.add(rss.robot("panda", gripper=grip))
    scene.add(
        rss.sponge(name="sponge", size=(0.090, 0.050, 0.035), cells=(9, 5, 4), at=(-0.143, -0.115, 0.0), material=foam)
    )
    sim = scene.build(control_mode="joint_position")

    def routine(sim, session):
        from robosimstudio_studio.routine import grip, reach, wait

        driver = session.arms["right"]
        home_pos, home_quat = driver.ik.home_pose
        surface = float(sim.scene.handles.surface_z)
        middle = np.asarray(sim["sponge"].particle_q, dtype=np.float64).mean(axis=0)
        press = np.array([middle[0], middle[1], surface + 0.020])
        yield from grip(session, "right", 0.0, 0.3)
        yield from reach(session, "right", press + np.array([0.0, 0.0, 0.08]), home_quat, speed=0.15)
        yield from reach(session, "right", press, home_quat, speed=0.05, tolerance=0.01)
        yield from grip(session, "right", 1.0, 1.2)
        yield from reach(session, "right", press + np.array([0.0, 0.0, 0.10]), home_quat, speed=0.05, tolerance=0.015)
        yield from wait(session, 1.5)
        yield from reach(session, "right", press + np.array([0.0, 0.0, 0.01]), home_quat, speed=0.05, tolerance=0.015)
        yield from grip(session, "right", 0.0, 1.0)
        yield from reach(session, "right", home_pos, home_quat, speed=0.15)

    def success(sim) -> dict[str, Any]:
        q = np.asarray(sim["sponge"].particle_q, dtype=np.float64)
        lift = float(q[:, 2].min() - float(sim.scene.handles.surface_z))
        return {
            "done": lift > 0.03,
            "detail": f"sponge underside {lift * 1000:.0f} mm over the bench (goal: lifted 30 mm)",
            "metrics": {"liftM": round(lift, 4)},
        }

    return BuiltScenario(
        sim=sim,
        routines={"Squeeze and lift": routine},
        success=success,
        camera=((1.05, -0.55, 1.1), (0.46, -0.115, 0.78)),
        notes=["A volumetric FEM foam stepped by SuperDex, held by the Panda's jaws through friction alone."],
    )


def builtin_registry() -> ScenarioRegistry:
    """The gallery BRIDGE ships."""
    return ScenarioRegistry(
        [
            Scenario(
                id="pick_place",
                title="Robotic Pick and Place",
                summary="A Franka Panda lifts a 50 mm cube off the bench and sets it down inside a marked "
                "zone, holding it by friction in the fingers.",
                robot="panda",
                physics="MuJoCo Warp, arm and cube in one island, 50 Hz control over 500 Hz physics",
                category="manipulation",
                build=_build_pick_place,
                media="examples/00_getting_started/media/00_hello_world.webp",
                success_label="The cube rests inside the place zone.",
                source="bridge_studio/scenarios.py",
            ),
            Scenario(
                id="surface_wiping",
                title="Surface Wiping",
                summary="Two YAM arms take a foam sponge off the mat and wipe a marker stripe off a chrome "
                "grab bar while the other arm steadies it.",
                robot="yam_i2rt",
                physics="SuperDex FEM sponge co-simulated with MuJoCo Warp arms",
                category="deformable",
                build=_build_wipe,
                requires=("cuda", "superdex"),
                media="examples/08_demos/wipe_the_bar/media/wipe_the_bar.webp",
                success_label="At least 96% of the stripe is wiped clean.",
                source="examples/08_demos/wipe_the_bar",
            ),
            Scenario(
                id="cloth_fold",
                title="Cloth Manipulation",
                summary="A bimanual YAM pinches the far edge of a terry towel and folds it in half in a low "
                "arc while the other hand pins the near half.",
                robot="yam_bimanual",
                physics="VBD cloth (self-contact) coupled one way to MuJoCo Warp arms, 60 Hz over 600 Hz",
                category="deformable",
                build=_build_towel,
                media="examples/08_demos/fold_the_towel/media/fold_the_towel.webp",
                success_label="Fold extent at most 0.6 and fold score at least 0.85 (the cloth_fold suite).",
                source="examples/08_demos/fold_the_towel",
            ),
            Scenario(
                id="cable_routing",
                title="Cable Routing",
                summary="Two arms lay a 600 mm cable past two spools and seat its end in a port, holding it "
                "by friction alone.",
                robot="yam_i2rt",
                physics="VBD capsule-chain cable with sticky contacts, MuJoCo Warp arms",
                category="deformable",
                build=_build_cable,
                media="examples/08_demos/route_the_cable/media/route_the_cable.webp",
                success_label="Both spool seats, the port and the cable end hold their share of capsules, "
                "hands clear, cable at rest.",
                source="examples/08_demos/route_the_cable",
            ),
            Scenario(
                id="dual_arm",
                title="Multi-Robot Workcell",
                summary="Two YAM arms share one bench: each picks its own cube and sets it in its own zone, "
                "both at once, each with its own joints, gripper and command stream.",
                robot="yam_bimanual",
                physics="MuJoCo Warp, both arms and both cubes in one island, 50 Hz over 500 Hz",
                category="multi-arm",
                build=_build_dual_arm,
                media="",
                success_label="Each cube rests inside its own zone.",
                source="bridge_studio/scenarios.py",
            ),
            Scenario(
                id="sponge_press",
                title="Soft Sponge Grasp",
                summary="A Panda closes on a 90 mm foam sponge, squeezes it and lifts it off the bench.",
                robot="panda",
                physics="SuperDex FEM foam beside the MuJoCo Warp arm",
                category="deformable",
                build=_build_sponge,
                requires=("cuda", "superdex"),
                media="examples/02_soft/media/00_sponge.webp",
                success_label="The sponge's underside is lifted 30 mm off the bench.",
                source="examples/02_soft/00_sponge.py",
            ),
        ]
    )
