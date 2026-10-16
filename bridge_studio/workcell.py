# SPDX-License-Identifier: LicenseRef-BRIDGE
"""A workcell as a document: what the builder edits, what Save writes, what Load rebuilds.

``bridge.workcell/1`` is a JSON document naming a cell, a physics preset, a robot
and a list of things. :func:`to_scene` maps it onto the public constructors --
``rss.box``, ``rss.sphere``, ``rss.cylinder``, ``rss.capsule``, ``rss.slab``,
``rss.post``, ``rss.sheet``, ``rss.sponge``, ``rss.rod`` -- through a whitelist.
Nothing in a document is executed: a name is a name and a number is a number.

**A preset says what it can simulate, and the document is checked against it
before anything is built.** The pairings are the library's own, from
``robosimstudio/specs/arrangement.py``:

* ``rigid_grasp`` -- ``MUJOCO_500HZ`` with sponges on SuperDex. Arm and rigid
  bodies in one MuJoCo island: closed fingers hold a box by friction. No cloth,
  no rope -- MuJoCo has no solver for them.
* ``deformable`` -- ``GENERIC``. The arm on MuJoCo, everything else on VBD behind
  a proxy coupler: cloth and rope simulate and can be pushed, and a hand
  **pushes a rigid body but cannot pinch it**.
* ``cloth_grasp`` -- ``GENERIC`` with one-way coupling and full-surface contact,
  the arrangement both shipped cloth tasks stage so the pads can hold fabric.

A document asking for something its preset cannot do is refused with the reason,
rather than built into a scene that quietly does something else.
"""

from __future__ import annotations

import math
import re
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

__all__ = [
    "KINDS",
    "PRESETS",
    "SCHEMA",
    "ObjectDoc",
    "WorkcellDoc",
    "catalog",
    "default_workcell",
    "to_scene",
]

SCHEMA = "bridge.workcell/1"

NAME = re.compile(r"^[A-Za-z][A-Za-z0-9_]{0,47}$")

KINDS: dict[str, dict[str, Any]] = {
    "box": {"label": "Box", "group": "Rigid bodies", "dynamic": True, "fields": ["half", "mass", "friction"]},
    "sphere": {"label": "Sphere", "group": "Rigid bodies", "dynamic": True, "fields": ["radius", "mass", "friction"]},
    "cylinder": {
        "label": "Cylinder",
        "group": "Rigid bodies",
        "dynamic": True,
        "fields": ["radius", "halfHeight", "mass", "friction"],
    },
    "capsule": {
        "label": "Capsule",
        "group": "Rigid bodies",
        "dynamic": True,
        "fields": ["radius", "halfHeight", "mass", "friction"],
    },
    "slab": {"label": "Fixed block / platform", "group": "Fixtures", "dynamic": False, "fields": ["half"]},
    "post": {"label": "Fixed post", "group": "Fixtures", "dynamic": False, "fields": ["radius", "halfHeight"]},
    "sheet": {
        "label": "Cloth sheet",
        "group": "Deformables",
        "dynamic": True,
        "fields": ["size2", "cells2", "material"],
    },
    "sponge": {
        "label": "Foam sponge (FEM)",
        "group": "Deformables",
        "dynamic": True,
        "fields": ["size3", "cells3", "material"],
    },
    "rope": {
        "label": "Cable / rope",
        "group": "Deformables",
        "dynamic": True,
        "fields": ["length", "radius", "segments"],
    },
}
"""Every kind a document may hold: what the library card says and which fields it reads."""

PRESETS: dict[str, dict[str, Any]] = {
    "rigid_grasp": {
        "label": "Rigid grasping (MuJoCo 500 Hz)",
        "summary": "Arm and rigid bodies in one MuJoCo island at 500 Hz: closed fingers hold a box by friction. "
        "Sponges run on SuperDex beside it. No cloth or rope.",
        "kinds": ["box", "sphere", "cylinder", "capsule", "slab", "post", "sponge"],
    },
    "deformable": {
        "label": "Deformables (GENERIC: MuJoCo arm + VBD)",
        "summary": "Cloth, rope and rigid bodies on Newton's VBD solver, the arm on MuJoCo, proxy-coupled. "
        "A hand pushes a rigid body but cannot pinch it.",
        "kinds": ["box", "sphere", "cylinder", "capsule", "slab", "post", "sheet", "sponge", "rope"],
    },
    "cloth_grasp": {
        "label": "Cloth grasping (one-way coupling)",
        "summary": "GENERIC with one-way coupling and full-surface contact, as the shipped cloth tasks stage it, "
        "so the pads can hold fabric by friction.",
        "kinds": ["sheet", "slab", "post"],
    },
}

CLOTH_MATERIALS = ("plain", "terry", "jersey", "fleece")
"""``plain`` is the sheet constructor's own default: a cloth that was not measured."""
SOFT_MATERIALS = ("firm_foam", "soft_foam")

Vec2 = tuple[float, float]
Vec3 = tuple[float, float, float]


def _finite(values: Any, what: str, lo: float, hi: float) -> None:
    for v in values if isinstance(values, (tuple, list)) else (values,):
        if not isinstance(v, (int, float)) or not math.isfinite(float(v)):
            raise ValueError(f"{what} must be finite numbers")
        if not lo <= float(v) <= hi:
            raise ValueError(f"{what} must lie in [{lo}, {hi}], got {v}")


class ObjectDoc(BaseModel):
    """One thing in the workcell. Lengths in metres, angles in degrees, positions in the work-surface frame."""

    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    id: str = Field(default="", max_length=64)
    kind: Literal["box", "sphere", "cylinder", "capsule", "slab", "post", "sheet", "sponge", "rope"]
    name: str
    at: Vec3 = (0.0, 0.0, 0.05)
    rpy: Vec3 = (0.0, 0.0, 0.0)
    half: Vec3 | None = None
    radius: float | None = None
    half_height: float | None = Field(default=None, alias="halfHeight")
    size: tuple[float, ...] | None = None
    cells: tuple[int, ...] | None = None
    length: float | None = None
    segments: int | None = None
    mass: float | None = None
    friction: float | None = None
    color: str | None = None
    material: str | None = None

    @field_validator("name")
    @classmethod
    def _name(cls, value: str) -> str:
        if not NAME.match(value):
            raise ValueError("a name starts with a letter and holds letters, digits and underscores (48 at most)")
        return value

    @field_validator("color")
    @classmethod
    def _color(cls, value: str | None) -> str | None:
        if value is not None and not re.fullmatch(r"#[0-9a-fA-F]{6}", value):
            raise ValueError("a colour is #rrggbb")
        return value

    @model_validator(mode="after")
    def _shape(self) -> ObjectDoc:
        _finite(self.at, "at", -3.0, 3.0)
        _finite(self.rpy, "rpy", -360.0, 360.0)
        k = self.kind
        if k in ("box", "slab"):
            if self.half is None:
                self.half = (0.025, 0.025, 0.025) if k == "box" else (0.1, 0.1, 0.02)
            _finite(self.half, "half", 0.002, 1.5)
        if k in ("sphere", "cylinder", "capsule", "post", "rope"):
            if self.radius is None:
                self.radius = {"sphere": 0.025, "cylinder": 0.02, "capsule": 0.015, "post": 0.02, "rope": 0.011}[k]
            _finite(self.radius, "radius", 0.002, 0.5)
        if k in ("cylinder", "capsule", "post"):
            if self.half_height is None:
                self.half_height = {"cylinder": 0.05, "capsule": 0.04, "post": 0.06}[k]
            _finite(self.half_height, "halfHeight", 0.002, 1.0)
        if k == "sheet":
            self.size = tuple(self.size or (0.3, 0.3))
            self.cells = tuple(self.cells or (20, 20))
            if len(self.size) != 2 or len(self.cells) != 2:
                raise ValueError("a sheet's size and cells are (x, y)")
            _finite(self.size, "size", 0.02, 1.5)
            _finite(self.cells, "cells", 2, 60)
            self.material = self.material or "plain"
            if self.material not in CLOTH_MATERIALS:
                raise ValueError(f"a sheet's material is one of {CLOTH_MATERIALS}")
        if k == "sponge":
            self.size = tuple(self.size or (0.09, 0.05, 0.035))
            self.cells = tuple(self.cells or (9, 5, 4))
            if len(self.size) != 3 or len(self.cells) != 3:
                raise ValueError("a sponge's size and cells are (x, y, z)")
            _finite(self.size, "size", 0.01, 0.5)
            _finite(self.cells, "cells", 2, 16)
            self.material = self.material or "firm_foam"
            if self.material not in SOFT_MATERIALS:
                raise ValueError(f"a sponge's material is one of {SOFT_MATERIALS}")
        if k == "rope":
            self.length = 0.62 if self.length is None else self.length  # examples/03_rope/00_cable.py: 22.1 mm spacing
            self.segments = 28 if self.segments is None else self.segments
            _finite(self.length, "length", 0.05, 3.0)
            _finite(self.segments, "segments", 4, 80)
            if self.length / self.segments <= 2.0 * float(self.radius or 0.0):
                raise ValueError("a rope's capsule spacing (length / segments) must exceed twice its radius")
        if self.mass is not None:
            _finite(self.mass, "mass", 0.001, 50.0)
        if self.friction is not None:
            _finite(self.friction, "friction", 0.0, 3.0)
        return self


class RobotDoc(BaseModel):
    model_config = ConfigDict(extra="forbid")

    model: str
    at: Vec3 | None = None

    @field_validator("model")
    @classmethod
    def _registered(cls, value: str) -> str:
        from robosimstudio.robots.registry import list_embodiments

        if value not in list_embodiments():
            raise ValueError(f"no registered robot {value!r}; there is {list_embodiments()}")
        return value


class RegionDoc(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str
    xy: tuple[float, float, float, float]
    z: Vec2 = (0.0, 0.1)

    @field_validator("name")
    @classmethod
    def _name(cls, value: str) -> str:
        if not NAME.match(value):
            raise ValueError("a region name starts with a letter and holds letters, digits and underscores")
        return value


class CameraDoc(BaseModel):
    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    name: str
    eye: Vec3
    look_at: Vec3 = Field(alias="lookAt")
    fov_y: float = Field(default=45.0, alias="fovY")
    width: int = 960
    height: int = 540


class WorkcellDoc(BaseModel):
    """A whole workcell, versioned."""

    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    schema_: Literal["bridge.workcell/1"] = Field(default=SCHEMA, alias="schema")
    name: str = Field(default="Untitled workcell", max_length=80)
    description: str = Field(default="", max_length=600)
    cell: str = "bench_075"
    physics: Literal["rigid_grasp", "deformable", "cloth_grasp"] = "rigid_grasp"
    robot: RobotDoc | None = None
    objects: list[ObjectDoc] = Field(default_factory=list, max_length=64)
    regions: list[RegionDoc] = Field(default_factory=list, max_length=16)
    cameras: list[CameraDoc] = Field(default_factory=list, max_length=8)

    @field_validator("cell")
    @classmethod
    def _cell(cls, value: str) -> str:
        from robosimstudio.scene.registry import list_cells

        if value not in list_cells():
            raise ValueError(f"no registered cell {value!r}; there is {list_cells()}")
        return value

    @model_validator(mode="after")
    def _consistent(self) -> WorkcellDoc:
        names = [o.name for o in self.objects] + [r.name for r in self.regions] + [c.name for c in self.cameras]
        if self.robot is not None:
            names.append(self.robot.model)
        dupes = sorted({n for n in names if names.count(n) > 1})
        if dupes:
            raise ValueError(f"names must be unique in a workcell; repeated: {dupes}")
        allowed = set(PRESETS[self.physics]["kinds"])
        refused = sorted({o.kind for o in self.objects} - allowed)
        if refused:
            raise ValueError(
                f"the {PRESETS[self.physics]['label']} preset cannot simulate {refused}: "
                f"{PRESETS[self.physics]['summary']}"
            )
        moving = self.robot is not None or any(KINDS[o.kind]["dynamic"] for o in self.objects)
        if not moving:
            raise ValueError("a workcell needs at least one thing that moves: a robot or a free object")
        return self


def _quat(rpy_deg: Vec3) -> tuple[float, float, float, float]:
    """Roll, pitch, yaw in degrees (applied x, then y, then z) as an xyzw quaternion."""
    r, p, y = (math.radians(float(a)) / 2.0 for a in rpy_deg)
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return (
        sr * cp * cy - cr * sp * sy,
        cr * sp * cy + sr * cp * sy,
        cr * cp * sy - sr * sp * cy,
        cr * cp * cy + sr * sp * sy,
    )


def _rgb(color: str | None) -> tuple[float, float, float] | None:
    if color is None:
        return None
    return tuple(int(color[i : i + 2], 16) / 255.0 for i in (1, 3, 5))  # type: ignore[return-value]


def _physics(preset: str):
    import robosimstudio as rss

    if preset == "rigid_grasp":
        return rss.MUJOCO_500HZ.with_backend("soft", rss.backend("superdex", substeps=2))
    if preset == "deformable":
        return rss.GENERIC
    if preset == "cloth_grasp":
        return rss.replace(
            rss.GENERIC,
            coupling=rss.coupler("one_way"),
            collision=rss.CollisionSpec(soft_contact_margin=0.010, full_surface_contact=True),
        )
    raise KeyError(preset)


def _object_spec(obj: ObjectDoc) -> Any:
    import robosimstudio as rss

    extra: dict[str, Any] = {}
    color = _rgb(obj.color)
    if color is not None:
        extra["color"] = color
    quat = _quat(obj.rpy)
    if obj.kind in ("box", "sphere", "cylinder", "capsule"):
        if obj.mass is not None:
            extra["mass"] = float(obj.mass)
        if obj.friction is not None:
            extra["friction"] = float(obj.friction)
        extra["quat"] = quat
        if obj.kind == "box":
            return rss.box(name=obj.name, half=tuple(obj.half), at=tuple(obj.at), **extra)
        if obj.kind == "sphere":
            return rss.sphere(name=obj.name, radius=float(obj.radius), at=tuple(obj.at), **extra)
        if obj.kind == "cylinder":
            return rss.cylinder(
                name=obj.name, radius=float(obj.radius), half_height=float(obj.half_height), at=tuple(obj.at), **extra
            )
        return rss.capsule(
            name=obj.name, radius=float(obj.radius), half_height=float(obj.half_height), at=tuple(obj.at), **extra
        )
    if obj.kind == "slab":
        return rss.slab(name=obj.name, half=tuple(obj.half), at=(*obj.at, *quat), **extra)
    if obj.kind == "post":
        return rss.post(
            name=obj.name, radius=float(obj.radius), half_height=float(obj.half_height), at=(*obj.at, *quat), **extra
        )
    if obj.kind == "sheet":
        material = {
            "plain": None,
            "terry": rss.materials.TERRY_TOWELLING,
            "jersey": rss.materials.SHIRT_JERSEY,
            "fleece": rss.materials.SHIRT_FLEECE,
        }[obj.material or "plain"]
        kwargs = {"material": material} if material is not None else {}
        return rss.sheet(
            name=obj.name, size=tuple(obj.size), cells=tuple(obj.cells), at=tuple(obj.at), **kwargs, **extra
        )
    if obj.kind == "sponge":
        from robosimstudio.domains.soft import FIRM_FOAM, SOFT_FOAM

        material = {"firm_foam": FIRM_FOAM, "soft_foam": SOFT_FOAM}[obj.material or "firm_foam"]
        return rss.sponge(
            name=obj.name, size=tuple(obj.size), cells=tuple(obj.cells), at=tuple(obj.at), material=material, **extra
        )
    if obj.kind == "rope":
        along = _rotate((0.0, 1.0, 0.0), quat)
        return rss.rod(
            name=obj.name,
            length=float(obj.length),
            radius=float(obj.radius),
            at=tuple(obj.at),
            along=along,
            segments=int(obj.segments),
            **extra,
        )
    raise ValueError(f"no constructor for {obj.kind!r}")


def _rotate(v: Vec3, q: tuple[float, float, float, float]) -> Vec3:
    x, y, z, w = q
    vx, vy, vz = v
    # v' = v + 2w(q x v) + 2 q x (q x v)
    cx, cy, cz = y * vz - z * vy, z * vx - x * vz, x * vy - y * vx
    ccx, ccy, ccz = y * cz - z * cy, z * cx - x * cz, x * cy - y * cx
    return (vx + 2 * (w * cx + ccx), vy + 2 * (w * cy + ccy), vz + 2 * (w * cz + ccz))


def to_scene(doc: WorkcellDoc):
    """The document as an ``rss.Scene``, ready to build."""
    import robosimstudio as rss

    scene = rss.Scene(
        cell=doc.cell, physics=_physics(doc.physics), name=re.sub(r"\W+", "_", doc.name)[:40] or "workcell"
    )
    if doc.robot is not None:
        scene.add(rss.robot(doc.robot.model, at=tuple(doc.robot.at) if doc.robot.at is not None else None))
    for obj in doc.objects:
        scene.add(_object_spec(obj))
    for region in doc.regions:
        scene.add(rss.RegionSpec(name=region.name, frame="", xy_ranges=tuple(region.xy), z_range=tuple(region.z)))
    for camera in doc.cameras:
        scene.add(
            rss.CameraSpec(
                name=camera.name,
                eye=tuple(camera.eye),
                look_at=tuple(camera.look_at),
                fov_y=float(camera.fov_y),
                width=int(camera.width),
                height=int(camera.height),
            )
        )
    return scene


def default_workcell() -> WorkcellDoc:
    """A Panda on the 750 mm bench with a cube under its hand: the hello-world cell as a document."""
    return WorkcellDoc(
        name="Panda bench cell",
        description="A Franka Panda on the 750 mm bench with a 50 mm cube under its hand.",
        cell="bench_075",
        physics="rigid_grasp",
        robot=RobotDoc(model="panda"),
        objects=[ObjectDoc(id="cube", kind="box", name="cube", at=(-0.143, -0.115, 0.027), color="#f2571f", mass=0.1)],
    )


def catalog() -> dict[str, Any]:
    """What the builder offers: kinds, presets, robots, cells and materials, from the live registries."""
    from robosimstudio.robots.registry import list_embodiments
    from robosimstudio.scene.registry import get_cell, list_cells

    cells = {}
    for name in list_cells():
        cell = get_cell(name)
        cells[name] = {
            "origin": [float(cell.center[0]), float(cell.center[1]), float(cell.surface_z)],
            "half": None if cell.half is None else [float(v) for v in cell.half],
            "groundZ": float(cell.ground_z),
        }
    return {
        "schema": SCHEMA,
        "kinds": KINDS,
        "presets": PRESETS,
        "robots": list_embodiments(),
        "cells": list_cells(),
        "cellFrames": cells,
        "materials": {"sheet": list(CLOTH_MATERIALS), "sponge": list(SOFT_MATERIALS)},
    }
