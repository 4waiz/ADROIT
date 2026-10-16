# SPDX-License-Identifier: LicenseRef-BRIDGE
"""A built scene as data a browser can draw: the manifest and its geometry blob.

The client never sees Newton. After every build the host walks the compiled
model once and writes down everything that does not change while that model
exists -- each shape's kind, size, local transform, colour, finish and owner;
each body; each arm's joints, limits and gripper; each deformable's topology --
as one JSON document, plus every mesh as one binary blob. A running scene then
needs only body poses and particle positions per frame, and the client composes
``body_pose * shape_local`` itself.

**Nothing here invents geometry.** Primitive shapes go as their parameters and
the client builds the same primitive; meshes go as the vertices and indices the
importer gave Newton, with the normals and UVs the asset carried. A shape kind
the client cannot draw (a heightfield, a gaussian splat) is listed as
``unsupported`` rather than approximated, so the scene tree can say it is there.

Roles come from Newton's own shape flags, which is how an imported robot
separates its looks from its colliders: ``VISIBLE`` is drawn, ``COLLIDE_SHAPES``
goes to the collision overlay, ``SITE`` (frames, grip sites, a ten-metre
robosuite aiming cylinder) is not drawn at all.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

__all__ = ["SceneExport", "export_scene"]

_TYPE_NAMES = {
    0: "none",
    1: "plane",
    2: "heightfield",
    3: "sphere",
    4: "capsule",
    5: "ellipsoid",
    6: "cylinder",
    7: "box",
    8: "mesh",
    9: "cone",
    10: "convex",
    11: "gaussian",
}
"""Newton ``GeoType`` value -> the name the client switches on."""

_VISIBLE, _COLLIDE_SHAPES, _SITE = 1, 2, 8
"""Newton ``ShapeFlags`` bits read here. Spelled out rather than imported so the
manifest can be tested without a device; ``test_bridge_manifest`` checks them
against Newton's enum."""


@dataclass
class SceneExport:
    """One build's manifest, its geometry, and how to gather a frame's particles.

    Attributes:
        manifest: JSON-ready description of everything static in the scene.
        geometry: Every mesh, back to back: float32 positions, float32 normals,
            float32 UVs and uint32 indices, each section 4-byte aligned. Offsets
            are in ``manifest["meshes"]``.
        particle_gather: Global particle indices, in the order a frame's
            particle buffer carries them: each deformable's own, concatenated.
    """

    manifest: dict[str, Any]
    geometry: bytes
    particle_gather: np.ndarray = field(default_factory=lambda: np.zeros(0, dtype=np.int64))


class _Blob:
    """Appends arrays to one buffer and remembers where each went."""

    def __init__(self) -> None:
        self.parts: list[bytes] = []
        self.size = 0

    def add(self, array: np.ndarray) -> int:
        data = np.ascontiguousarray(array).tobytes()
        offset = self.size
        self.parts.append(data)
        self.size += len(data)
        pad = (-self.size) % 4
        if pad:
            self.parts.append(b"\0" * pad)
            self.size += pad
        return offset

    def bytes(self) -> bytes:
        return b"".join(self.parts)


def _short(label: str) -> str:
    """The last segment of an importer's path-shaped label: ``robot0_link3`` rather than the whole chain."""
    return str(label).rsplit("/", 1)[-1]


def _floats(values: Any, digits: int = 6) -> list[float]:
    return [round(float(v), digits) for v in np.asarray(values, dtype=np.float64).reshape(-1)]


def _finite(value: float, cap: float = 1.0e6) -> float | None:
    """A limit as JSON: ``None`` for Newton's +-1e10 "unlimited" sentinel."""
    value = float(value)
    return None if not np.isfinite(value) or abs(value) >= cap else round(value, 6)


def _mesh_entry(blob: _Blob, mesh: Any, mesh_id: int) -> dict[str, Any] | None:
    """One Newton mesh into the blob, or ``None`` when it carries no triangles."""
    vertices = np.asarray(getattr(mesh, "vertices", np.zeros((0, 3))), dtype=np.float32).reshape(-1, 3)
    indices = np.asarray(getattr(mesh, "indices", np.zeros(0)), dtype=np.uint32).reshape(-1)
    if not len(vertices) or len(indices) < 3:
        return None
    entry: dict[str, Any] = {
        "id": mesh_id,
        "vertices": int(len(vertices)),
        "indices": int(len(indices)),
        "position": blob.add(vertices),
    }
    normals = getattr(mesh, "normals", None)
    if normals is not None:
        normals = np.asarray(normals, dtype=np.float32).reshape(-1, 3)
        if normals.shape == vertices.shape and np.isfinite(normals).all():
            entry["normal"] = blob.add(normals)
    uvs = getattr(mesh, "uvs", None)
    if uvs is not None:
        uvs = np.asarray(uvs, dtype=np.float32).reshape(-1, 2)
        if len(uvs) == len(vertices) and np.isfinite(uvs).all():
            entry["uv"] = blob.add(uvs)
    entry["index"] = blob.add(indices)
    return entry


def _shape_size(kind: str, scale: np.ndarray) -> list[float]:
    """The primitive's own parameters, in the order the client reads them."""
    if kind == "box":
        return _floats(scale[:3])  # half extents
    if kind == "sphere":
        return _floats(scale[:1])  # radius
    if kind in ("capsule", "cylinder", "cone"):
        return _floats(scale[:2])  # radius, half height (along local z)
    if kind == "ellipsoid":
        return _floats(scale[:3])  # semi-axes
    if kind == "plane":
        return _floats(scale[:2])  # width, length; 0 is unbounded
    return _floats(scale[:3])  # a mesh's per-axis scale


def _texture_key(finish: Any) -> str | None:
    """The finish's diffuse map, as a key the server can resolve to a file, or ``None``."""
    name = str(getattr(finish, "texture", "") or "")
    return name or None


def _subject_key(subject: Any) -> str | None:
    return None if subject is None else str(subject.key)


def export_scene(sim: Any, *, generation: int, scenario: dict[str, Any]) -> SceneExport:
    """Describe a built :class:`~robosimstudio.sim.Sim` for the client.

    Args:
        sim: The built scene.
        generation: The build's sequence number; a frame carrying another one is
            for a different model and is dropped by the client.
        scenario: What was built, as the scenario registry describes it.
    """
    from robosimstudio_studio.selection import SceneIndex

    model = sim.model
    scene = sim.scene
    handles = scene.handles
    index = SceneIndex(handles, scene, getattr(sim, "doc", None))
    blob = _Blob()

    # ------------------------------------------------------------------ shapes
    count = int(model.shape_count)
    types = model.shape_type.numpy() if count else np.zeros(0, dtype=np.int32)
    bodies = model.shape_body.numpy() if count else np.zeros(0, dtype=np.int32)
    flags = model.shape_flags.numpy() if count else np.zeros(0, dtype=np.int32)
    scales = model.shape_scale.numpy() if count else np.zeros((0, 3))
    xforms = model.shape_transform.numpy() if count else np.zeros((0, 7))
    colors = model.shape_color.numpy() if count and getattr(model, "shape_color", None) is not None else None
    opacity = model.shape_opacity.numpy() if count and getattr(model, "shape_opacity", None) is not None else None
    labels = list(getattr(model, "shape_label", ())) or [""] * count
    finishes = getattr(handles, "finishes", {}) or {}
    live = {int(s) for s in np.asarray(getattr(handles, "live_shapes", ()), dtype=np.int64).reshape(-1)}

    meshes: list[dict[str, Any]] = []
    mesh_ids: dict[int, int | None] = {}
    shapes: list[dict[str, Any]] = []
    for s in range(count):
        kind = _TYPE_NAMES.get(int(types[s]), "unsupported")
        if kind in ("heightfield", "gaussian", "none"):
            kind = "unsupported"
        flag = int(flags[s])
        entry: dict[str, Any] = {
            "i": s,
            "body": int(bodies[s]),
            "type": kind,
            "size": _shape_size(kind, np.asarray(scales[s], dtype=np.float64)),
            "xform": _floats(xforms[s]),
            "visible": bool(flag & _VISIBLE) and not flag & _SITE,
            "collide": bool(flag & _COLLIDE_SHAPES),
            "site": bool(flag & _SITE),
            "label": _short(labels[s]) if s < len(labels) else "",
            "owner": _subject_key(index.at_shape(s) or (index.at_body(int(bodies[s])) if bodies[s] >= 0 else None)),
            "live": s in live,
        }
        if colors is not None:
            entry["color"] = _floats(colors[s], 4)
        if opacity is not None:
            entry["opacity"] = round(float(opacity[s]), 4)
        finish = finishes.get(s)
        if finish is not None:
            entry["roughness"] = round(float(finish.roughness), 3)
            entry["metallic"] = round(float(finish.metallic), 3)
            entry["opacity"] = round(min(float(entry.get("opacity", 1.0)), float(finish.opacity)), 4)
            texture = _texture_key(finish)
            if texture is not None:
                entry["texture"] = texture
                entry["uvScale"] = _floats(finish.uv_scale, 4)
                entry["uvOffset"] = _floats(finish.uv_offset, 4)
        if kind in ("mesh", "convex"):
            source = model.shape_source[s]
            key = id(source)
            if key not in mesh_ids:
                made = _mesh_entry(blob, source, len(meshes)) if source is not None else None
                mesh_ids[key] = None if made is None else made["id"]
                if made is not None:
                    meshes.append(made)
            entry["mesh"] = mesh_ids[key]
            if entry["mesh"] is None:
                entry["type"] = "unsupported"
        shapes.append(entry)

    # ------------------------------------------------------------------ bodies
    body_labels = list(getattr(model, "body_label", ()))
    masses = model.body_mass.numpy() if model.body_count else np.zeros(0)
    coms = model.body_com.numpy() if model.body_count else np.zeros((0, 3))
    body_list = [
        {
            "i": b,
            "label": _short(body_labels[b]) if b < len(body_labels) else f"body{b}",
            "owner": _subject_key(index.at_body(b)),
            "mass": round(float(masses[b]), 6),
            "com": _floats(coms[b]),
        }
        for b in range(int(model.body_count))
    ]

    # ---------------------------------------------------------------- subjects
    subjects = []
    for subject in index.subjects:
        handle = subject.handle
        item: dict[str, Any] = {"key": subject.key, "kind": subject.kind, "name": subject.name}
        body = int(getattr(handle, "body", -1) if handle is not None else -1)
        chain = tuple(getattr(handle, "bodies", ()) or ())
        item["bodies"] = [int(b) for b in (chain or ((body,) if body >= 0 else ()))]
        if subject.kind == "object":
            item["static"] = bool(getattr(handle, "static", False))
            item["category"] = str(getattr(handle, "kind", "") or "")
            spec = getattr(subject.entity, "spec", None)
            item["specKind"] = type(spec).__name__ if spec is not None else ""
            if body >= 0:
                item["mass"] = round(float(masses[item["bodies"]].sum()), 6)
        if subject.kind == "cloth":
            item["particles"] = int(len(np.asarray(getattr(handle, "particles", ()), dtype=np.int64).reshape(-1)))
        if subject.asset:
            item["asset"] = subject.asset
        subjects.append(item)

    # -------------------------------------------------------------------- arms
    arms = _arms(sim)

    # ------------------------------------------------------------- deformables
    deformables, gather = _deformables(sim, blob)

    # ---------------------------------------------------------------- cameras
    cameras = [
        {
            "name": c.name,
            "eye": _floats(c.eye) if c.eye is not None else None,
            "lookAt": _floats(c.look_at) if c.look_at is not None else None,
            "up": _floats(getattr(c, "up", (0.0, 0.0, 1.0))),
            "fovY": round(float(getattr(c, "fov_y", 45.0)), 3),
            "width": int(getattr(c, "width", 640)),
            "height": int(getattr(c, "height", 480)),
            "parentBody": int(getattr(c, "parent_body", -1)),
        }
        for c in sim.cameras
    ]

    regions = [
        {
            "name": name,
            "key": f"region:{name}",
            "frame": region.frame,
            "frameBody": int(region.frame_body),
            "origin": _floats(region.origin),
            "xy": _floats(region.xy_ranges),
            "z": _floats(region.z_range),
        }
        for name, region in sorted((getattr(handles, "regions", None) or {}).items())
    ]

    manifest = {
        "schema": "bridge.manifest/1",
        "generation": int(generation),
        "scenario": scenario,
        "cell": _cell(sim),
        "physics": physics_summary(sim),
        "counts": {
            "bodies": int(model.body_count),
            "shapes": count,
            "joints": int(model.joint_count),
            "particles": int(model.particle_count),
            "frameParticles": int(len(gather)),
        },
        "bodies": body_list,
        "shapes": shapes,
        "meshes": meshes,
        "subjects": subjects,
        "arms": arms,
        "deformables": deformables,
        "cameras": cameras,
        "regions": regions,
        "geometryBytes": blob.size,
    }
    return SceneExport(manifest=manifest, geometry=blob.bytes(), particle_gather=gather)


def _coord_joints(model: Any) -> dict[int, int]:
    """``joint_q`` index -> the joint it belongs to."""
    starts = model.joint_q_start.numpy()
    out: dict[int, int] = {}
    for j in range(int(model.joint_count)):
        lo = int(starts[j])
        hi = int(starts[j + 1]) if j + 1 < len(starts) else int(model.joint_coord_count)
        for c in range(lo, hi):
            out[c] = j
    return out


def _arms(sim: Any) -> list[dict[str, Any]]:
    """Every arm the embodiment indexed, with what a control panel needs to drive it."""
    import newton

    embodiment = sim.scene.embodiment
    if embodiment is None:
        return []
    model = sim.model
    coord_joint = _coord_joints(model)
    labels = list(model.joint_label)
    types = model.joint_type.numpy()
    lower = model.joint_limit_lower.numpy()
    upper = model.joint_limit_upper.numpy()
    effort = model.joint_effort_limit.numpy() if getattr(model, "joint_effort_limit", None) is not None else None
    velocity = model.joint_velocity_limit.numpy() if getattr(model, "joint_velocity_limit", None) is not None else None
    work = getattr(sim.handles, "work_arm", "")
    kinds = {int(newton.JointType.REVOLUTE): "revolute", int(newton.JointType.PRISMATIC): "prismatic"}
    out = []
    for name, arm in embodiment.arms.items():
        ik = embodiment.iks.get(name)
        gripper = embodiment.grippers.get(name)
        joints = []
        for k, (coord, dof) in enumerate(zip(arm.arm_coords, arm.arm_dofs, strict=True)):
            joint = coord_joint.get(int(coord), -1)
            kind = kinds.get(int(types[joint]), "other") if joint >= 0 else "other"
            joints.append(
                {
                    "name": _short(labels[joint]) if joint >= 0 else f"q{coord}",
                    "coord": int(coord),
                    "dof": int(dof),
                    "type": kind,
                    "unit": "rad" if kind == "revolute" else "m",
                    "lower": _finite(lower[dof]),
                    "upper": _finite(upper[dof]),
                    "home": round(float(arm.home_q[k]), 6) if k < len(arm.home_q) else None,
                    "effortLimit": _finite(effort[dof]) if effort is not None else None,
                    "velocityLimit": _finite(velocity[dof]) if velocity is not None else None,
                }
            )
        home_pos, home_quat = ik.home_pose if ik is not None else (np.zeros(3), np.array([0.0, 0.0, 0.0, 1.0]))
        out.append(
            {
                "name": name,
                "role": "work" if name == work else ("support" if work else ""),
                "joints": joints,
                "eeBody": int(arm.ee_body),
                "baseBody": int(arm.base_body),
                "bodies": [int(b) for b in arm.bodies],
                "homePose": _floats(np.concatenate([home_pos, home_quat])),
                "tcpOffset": _floats(ik.ee_offset) if ik is not None else [0.0, 0.0, 0.0],
                "gripper": None
                if gripper is None
                else {
                    "kind": gripper.kind,
                    "span": round(float(gripper.span), 6),
                    "coords": [int(c) for c in gripper.coords],
                    "openWidth": round(float(arm.gripper_open), 6),
                },
            }
        )
    return out


def _deformables(sim: Any, blob: _Blob) -> tuple[list[dict[str, Any]], np.ndarray]:
    """Cloth surfaces and rope centrelines: topology into the blob, gather order for frames."""
    if not getattr(sim.scene.handles, "deformables", None):
        return [], np.zeros(0, dtype=np.int64)
    from robosimstudio_studio.garments import garment_visuals

    out: list[dict[str, Any]] = []
    gathered: list[np.ndarray] = []
    offset = 0
    for visual in garment_visuals(sim.scene, getattr(sim, "doc", None)):
        particles = np.asarray(visual.particles, dtype=np.int64).reshape(-1)
        if not len(particles):
            continue
        item: dict[str, Any] = {
            "name": visual.name,
            "owner": f"cloth:{visual.name}",
            "offset": offset,
            "count": int(len(particles)),
            "color": _floats(visual.color, 4),
            "opacity": round(float(visual.opacity), 4),
            "thickness": round(float(visual.thickness), 6),
        }
        finish = visual.finish
        if finish is not None:
            item["roughness"] = round(float(finish.roughness), 3)
            item["metallic"] = round(float(finish.metallic), 3)
        if visual.is_tube:
            item["kind"] = "tube"
            item["radius"] = round(float(visual.tube_radius), 6)
        else:
            triangles = np.asarray(visual.triangles, dtype=np.uint32).reshape(-1)
            if not len(triangles):
                item["kind"] = "points"
            else:
                item["kind"] = "surface"
                item["slab"] = bool(visual.is_slab)
                item["triangles"] = int(len(triangles))
                item["index"] = blob.add(triangles)
        out.append(item)
        gathered.append(particles)
        offset += len(particles)
    gather = np.concatenate(gathered) if gathered else np.zeros(0, dtype=np.int64)
    return out, gather


def _cell(sim: Any) -> dict[str, Any]:
    """The work surface: where its origin is in the world, and how big it is when the cell says."""
    handles = sim.scene.handles
    name = str(getattr(handles, "scene_name", "") or "")
    info: dict[str, Any] = {"name": name, "surfaceZ": round(float(getattr(handles, "surface_z", 0.0) or 0.0), 6)}
    if not name:
        return info
    try:
        from robosimstudio.scene.registry import get_cell

        cell = get_cell(name)
    except KeyError:
        return info
    center = np.asarray(cell.center, dtype=np.float64)
    info["origin"] = _floats([center[0], center[1], cell.surface_z])
    info["groundZ"] = round(float(cell.ground_z), 6)
    info["half"] = None if cell.half is None else _floats(cell.half)
    info["mounts"] = [{"name": str(n), "xform": _floats(x)} for n, x in cell.mounts]
    info["provenance"] = str(cell.provenance or "")
    return info


def physics_summary(sim: Any) -> dict[str, Any]:
    """How the scene is stepped, from the compiled program: rates, gravity, islands, solvers."""
    arrangement = sim.request.physics
    program = getattr(sim.scene, "program", None)
    islands = []
    manifest = getattr(program, "manifest", None)
    for record in getattr(manifest, "islands", ()) or ():
        islands.append(
            {
                "name": record.name,
                "solver": record.backend.solver,
                "solverClass": record.solver_class,
                "substeps": int(record.backend.substeps),
                "domains": list(record.domains),
                "entities": list(record.entities),
                "bodies": len(record.bodies),
                "particles": len(record.particles),
                "hostEngine": bool(record.host_engine),
                "params": {str(k): _jsonable(v) for k, v in record.backend.params},
            }
        )
    collision = arrangement.collision
    summary = ""
    if program is not None:
        try:
            summary = program.summary() if callable(program.summary) else str(program.summary)
        except Exception as exc:  # a summary is a courtesy; a scene that cannot give one still runs
            summary = f"(no summary: {type(exc).__name__}: {exc})"
    problems: list[str] = []
    if program is not None:
        try:
            problems = [str(p) for p in program.problems()]
        except Exception:
            problems = []
    return {
        "controlHz": float(arrangement.control_hz),
        "substeps": int(arrangement.substeps),
        "simDt": float(sim.pipeline.sim_dt),
        "gravity": float(arrangement.gravity),
        "cudaGraph": bool(getattr(sim.pipeline, "captured", False)),
        "device": str(sim.model.device),
        "coupler": str(getattr(manifest, "coupler", "") or ""),
        "islands": islands,
        "collision": {
            # Optional in a CollisionSpec: None means the solver's own default, and is sent as such.
            "softContactMargin": _optional(collision.soft_contact_margin),
            "rigidGap": _optional(collision.rigid_gap),
            "contactKe": _optional(collision.contact_ke),
            "contactKd": _optional(collision.contact_kd),
            "fullSurfaceContact": bool(collision.full_surface_contact),
        },
        "summary": summary,
        "problems": problems,
        "warnings": [str(w) for w in (getattr(program, "warnings", None) or [])],
    }


def _optional(value: Any) -> float | None:
    return None if value is None else float(value)


def _jsonable(value: Any) -> Any:
    if isinstance(value, (bool, int, float, str)) or value is None:
        return value
    if isinstance(value, (tuple, list)):
        return [_jsonable(v) for v in value]
    return str(value)
