#!/usr/bin/env python3
# SPDX-License-Identifier: LicenseRef-BRIDGE
"""Export the repository's own models, textures, fonts and icons for BRIDGE VR.

The VR app draws what the web app draws, from the same files:

- the Franka Panda from ``assets/embodiments/panda`` (its MJCF and meshes),
- the factory's Poly Haven props, textures and HDRI from ``frontend/public/factory``,
- the graphics the web app paints on canvases (signs, hazard stripes, floor
  lettering, cartons), drawn by a port of ``frontend/src/viewport/factory/textures.ts``
  in headless Microsoft Edge,
- the ADROIT wordmarks, the Figtree and IBM Plex Mono fonts and lucide icons the
  page uses.

Everything is written under ``<out>`` (the Unity project's ``Assets/BridgeGen``):

``Resources/Gen/Meshes/<name>.bytes``
    One mesh each in a small binary format (see :func:`write_mesh`), already in
    Unity's frame.
``Resources/Gen/Robots/panda.json``
    The kinematic tree: bodies with their rest pose, joints with axis, limits and
    the sign that turns a MuJoCo angle into a Unity one, visual parts with their
    colours, inertials, and capsule or box colliders fitted to each link.
``Resources/Gen/Props/<name>.json``
    A GLB prop as mesh parts and materials, normalised as the web app's
    ``instances()`` does: centred on x and z, standing on y = 0.
``Resources/Gen/Textures/*.png``, ``factory.hdr``
    Photographed surfaces (WebP to PNG; roughness packed as URP's
    metallic/smoothness map, metal in R and smoothness in A) and canvas graphics.
``Resources/Gen/UI/*``
    Wordmarks, the icon atlas (``icons.png``) and its index (``icons.json``).
``Fonts/*.ttf``
    Static instances of Figtree (the page's UI font) and IBM Plex Mono.

Frames: MuJoCo and the web app are z-up and right-handed, Unity is y-up and
left-handed. A point (x, y, z) maps to (x, z, y). That map is a reflection, so a
rotation by angle a about axis n maps to a rotation by -a about (nx, nz, ny): a
quaternion (w, x, y, z) maps to Unity's (x, y, z, w) = (-x, -z, -y, w), and a
mesh's triangles reverse their winding. glTF props get the frame the web app
gives them (it turns a y-up glTF a quarter about x into its z-up world), which in
Unity is glTF with z negated.

Run (numpy, Pillow, fontTools and brotli are needed)::

    uv run --no-project --with numpy --with pillow --with fonttools --with brotli \
        python vr/tools/export_assets.py --out <unity project>/Assets/BridgeGen
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import math
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import TYPE_CHECKING

import numpy as np

if TYPE_CHECKING:
    from PIL import Image

REPO = Path(__file__).resolve().parents[2]
PANDA = REPO / "assets" / "embodiments" / "panda"
FACTORY = REPO / "frontend" / "public" / "factory"
NODE = REPO / "frontend" / "node_modules"
EDGE_PATHS = [
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
]

MESH_MAGIC = b"BMSH"


# --------------------------------------------------------------------------- frames
def to_unity_points(p: np.ndarray) -> np.ndarray:
    """(x, y, z) z-up right-handed -> (x, z, y) Unity."""
    return np.ascontiguousarray(p[..., [0, 2, 1]])


def to_unity_quat(wxyz) -> list[float]:
    """MuJoCo (w, x, y, z) -> Unity (x, y, z, w) under the y/z swap."""
    w, x, y, z = (float(v) for v in wxyz)
    n = math.sqrt(w * w + x * x + y * y + z * z) or 1.0
    w, x, y, z = w / n, x / n, y / n, z / n
    return [-x, -z, -y, w]


def quat_to_mat(wxyz) -> np.ndarray:
    w, x, y, z = (float(v) for v in wxyz)
    n = math.sqrt(w * w + x * x + y * y + z * z) or 1.0
    w, x, y, z = w / n, x / n, y / n, z / n
    return np.array(
        [
            [1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
            [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
            [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)],
        ]
    )


# --------------------------------------------------------------------------- meshes
def write_mesh(
    path: Path, positions: np.ndarray, normals: np.ndarray, indices: np.ndarray, uvs: np.ndarray | None = None
) -> dict:
    """Writes ``BMSH``: magic, then uint32 version, vertex count, index count, flags
    (1 normals, 2 uvs), then float32 positions, normals, [uvs], uint32 indices.
    Everything is already in Unity's frame and winding."""
    positions = np.ascontiguousarray(positions, dtype="<f4")
    normals = np.ascontiguousarray(normals, dtype="<f4")
    indices = np.ascontiguousarray(indices.reshape(-1), dtype="<u4")
    flags = 1 | (2 if uvs is not None else 0)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as f:
        f.write(MESH_MAGIC)
        f.write(struct.pack("<IIII", 1, len(positions), len(indices), flags))
        f.write(positions.tobytes())
        f.write(normals.tobytes())
        if uvs is not None:
            f.write(np.ascontiguousarray(uvs, dtype="<f4").tobytes())
        f.write(indices.tobytes())
    lo = positions.min(axis=0)
    hi = positions.max(axis=0)
    return {
        "vertices": int(len(positions)),
        "triangles": int(len(indices) // 3),
        "min": lo.round(5).tolist(),
        "max": hi.round(5).tolist(),
    }


def weld(corner_p: np.ndarray, corner_n: np.ndarray, corner_uv: np.ndarray | None = None):
    """Merges identical (position, normal[, uv]) corners into shared vertices."""
    keys = [np.round(corner_p * 1e5).astype(np.int64), np.round(corner_n * 1e3).astype(np.int64)]
    if corner_uv is not None:
        keys.append(np.round(corner_uv * 1e4).astype(np.int64))
    key = np.concatenate(keys, axis=1)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    p = corner_p[first]
    n = corner_n[first]
    n = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    uv = corner_uv[first] if corner_uv is not None else None
    return p, n, inverse.reshape(-1, 3), uv


def read_obj(path: Path) -> np.ndarray:
    """Triangles of an OBJ as (n, 3, 2, 3): per corner, position and normal."""
    vs: list[list[float]] = []
    vns: list[list[float]] = []
    faces: list[tuple[int, int]] = []
    for line in path.read_text().splitlines():
        if line.startswith("v "):
            vs.append([float(t) for t in line.split()[1:4]])
        elif line.startswith("vn "):
            vns.append([float(t) for t in line.split()[1:4]])
        elif line.startswith("f "):
            corners = []
            for token in line.split()[1:]:
                parts = token.split("/")
                vi = int(parts[0])
                ni = int(parts[2]) if len(parts) > 2 and parts[2] else vi
                corners.append((vi, ni))
            for k in range(1, len(corners) - 1):
                faces.extend([corners[0], corners[k], corners[k + 1]])
    V = np.array(vs, dtype=np.float64)
    N = np.array(vns, dtype=np.float64) if vns else None
    idx = np.array(faces, dtype=np.int64)
    vi = np.where(idx[:, 0] > 0, idx[:, 0] - 1, len(V) + idx[:, 0])
    p = V[vi].reshape(-1, 3, 3)
    if N is None:
        n = np.repeat(face_normals(p)[:, None, :], 3, axis=1)
    else:
        ni = np.where(idx[:, 1] > 0, idx[:, 1] - 1, len(N) + idx[:, 1])
        n = N[ni].reshape(-1, 3, 3)
    return np.stack([p, n], axis=2)


def face_normals(tris: np.ndarray) -> np.ndarray:
    n = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-18)


def read_stl(path: Path, smooth_deg: float = 35.0) -> np.ndarray:
    """Triangles of a binary STL with normals smoothed across edges under ``smooth_deg``."""
    data = path.read_bytes()
    count = struct.unpack_from("<I", data, 80)[0]
    dt = np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("attr", "<u2")])
    tris = np.frombuffer(data, dtype=dt, count=count, offset=84)["v"].astype(np.float64)
    return np.stack([tris, smooth_normals(tris, smooth_deg)], axis=2)


def smooth_normals(tris: np.ndarray, smooth_deg: float) -> np.ndarray:
    cross = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    unit = cross / np.maximum(np.linalg.norm(cross, axis=1, keepdims=True), 1e-18)
    corners = tris.reshape(-1, 3)
    _, vid = np.unique(np.round(corners * 1e6).astype(np.int64), axis=0, return_inverse=True)
    vid = vid.reshape(-1)
    face = np.repeat(np.arange(len(tris)), 3)
    order = np.argsort(vid, kind="stable")
    sorted_v = vid[order]
    starts = np.flatnonzero(np.r_[True, sorted_v[1:] != sorted_v[:-1]])
    ends = np.r_[starts[1:], len(sorted_v)]
    cos = math.cos(math.radians(smooth_deg))
    out = np.zeros_like(corners)
    for s, e in zip(starts, ends, strict=True):
        cs = order[s:e]
        fs = face[cs]
        near = (unit[fs] @ unit[fs].T) > cos
        out[cs] = near.astype(np.float64) @ cross[fs]
    out /= np.maximum(np.linalg.norm(out, axis=1, keepdims=True), 1e-18)
    return out.reshape(-1, 3, 3)


def transform_tris(tris: np.ndarray, pos, wxyz) -> np.ndarray:
    """Applies a MuJoCo geom pose (pos, quat) to triangles (n, 3, 2, 3)."""
    R = quat_to_mat(wxyz)
    p = tris[:, :, 0, :] @ R.T + np.asarray(pos, dtype=np.float64)
    n = tris[:, :, 1, :] @ R.T
    return np.stack([p, n], axis=2)


def export_mjcf_mesh(tris: np.ndarray, path: Path) -> dict:
    """z-up triangles -> Unity frame, welded, winding reversed for the reflection."""
    p = to_unity_points(tris[:, :, 0, :].reshape(-1, 3))
    n = to_unity_points(tris[:, :, 1, :].reshape(-1, 3))
    vp, vn, idx, _ = weld(p, n)
    idx = idx[:, [0, 2, 1]]
    return write_mesh(path, vp, vn, idx)


# --------------------------------------------------------------------------- the Panda
def floats(text: str | None, default: list[float]) -> list[float]:
    return [float(t) for t in text.split()] if text else list(default)


def short(name: str) -> str:
    return re.sub(r"^(robot0_|gripper0_)", "", name)


def fit_collider(points: np.ndarray) -> dict:
    """A capsule along the points' principal axis, or a box when they are not long."""
    c = points.mean(axis=0)
    d = points - c
    _, _, vt = np.linalg.svd(d, full_matrices=False)
    axis = vt[0]
    t = d @ axis
    radial = np.linalg.norm(d - np.outer(t, axis), axis=1)
    r = float(np.percentile(radial, 92))
    length = float(t.max() - t.min())
    lo = points.min(axis=0)
    hi = points.max(axis=0)
    if length < 2.2 * r:
        return {"type": "box", "center": ((lo + hi) / 2).round(5).tolist(), "size": (hi - lo).round(5).tolist()}
    a = c + axis * (t.min() + r * 0.6)
    b = c + axis * (t.max() - r * 0.6)
    return {"type": "capsule", "a": a.round(5).tolist(), "b": b.round(5).tolist(), "r": round(r, 5)}


def export_panda(gen: Path) -> None:
    root = ET.parse(PANDA / "panda_standalone.xml").getroot()
    mesh_files = {m.get("name"): m.get("file") for m in root.find("asset").iter("mesh")}
    colors = {m.get("name"): floats(m.get("rgba"), [1, 1, 1, 1]) for m in root.find("asset").iter("material")}
    bodies: list[dict] = []
    stats = {"triangles": 0, "meshes": 0}

    def walk(el: ET.Element, parent: int) -> None:
        name = short(el.get("name"))
        pos = floats(el.get("pos"), [0, 0, 0])
        quat = floats(el.get("quat"), [1, 0, 0, 0])
        body = {
            "name": name,
            "parent": parent,
            "pos": to_unity_points(np.array(pos)).round(6).tolist(),
            "rot": [round(v, 7) for v in to_unity_quat(quat)],
            "visuals": [],
            "colliders": [],
        }
        joint = el.find("joint")
        if joint is not None:
            jtype = joint.get("type", "hinge")
            axis = floats(joint.get("axis"), [0, 0, 1])
            rng = floats(joint.get("range"), [-math.pi, math.pi])
            body["joint"] = {
                "name": short(joint.get("name")),
                "type": jtype,
                "axis": to_unity_points(np.array(axis)).round(6).tolist(),
                # A MuJoCo hinge angle turns the other way in Unity's mirrored frame;
                # a slide moves the same distance along the mapped axis.
                "sign": -1 if jtype == "hinge" else 1,
                "range": [round(v, 6) for v in rng],
            }
        inertial = el.find("inertial")
        if inertial is not None:
            body["mass"] = float(inertial.get("mass", "0"))
            body["com"] = to_unity_points(np.array(floats(inertial.get("pos"), [0, 0, 0]))).round(5).tolist()
        collider_points = []
        for k, geom in enumerate(el.findall("geom")):
            if geom.get("group") != "1" or geom.get("type") != "mesh":
                continue
            mesh = geom.get("mesh")
            file = PANDA / mesh_files[mesh]
            tris = read_obj(file) if file.suffix == ".obj" else read_stl(file)
            tris = transform_tris(tris, floats(geom.get("pos"), [0, 0, 0]), floats(geom.get("quat"), [1, 0, 0, 0]))
            mesh_name = f"panda_{name}_{k}"
            info = export_mjcf_mesh(tris, gen / "Meshes" / f"{mesh_name}.bytes")
            stats["triangles"] += info["triangles"]
            stats["meshes"] += 1
            rgba = colors.get(geom.get("material"), floats(geom.get("rgba"), [1, 1, 1, 1]))
            body["visuals"].append(
                {"mesh": mesh_name, "rgba": [round(v, 4) for v in rgba], "material": geom.get("material", "")}
            )
            collider_points.append(to_unity_points(tris[:, :, 0, :].reshape(-1, 3)))
        if collider_points:
            pts = np.concatenate(collider_points)
            if name.endswith("finger") or name in ("right_gripper",):
                lo, hi = pts.min(axis=0), pts.max(axis=0)
                body["colliders"].append(
                    {"type": "box", "center": ((lo + hi) / 2).round(5).tolist(), "size": (hi - lo).round(5).tolist()}
                )
            else:
                body["colliders"].append(fit_collider(pts))
        index = len(bodies)
        bodies.append(body)
        for child in el.findall("body"):
            walk(child, index)

    walk(root.find("worldbody").find("body"), -1)
    eef = next(i for i, b in enumerate(bodies) if b["name"] == "eef")
    robot = {
        "name": "Franka Emika Panda",
        "source": "assets/embodiments/panda/panda_standalone.xml (robosuite 1.4.0, MIT; meshes from franka_description, Apache-2.0)",
        "bodies": bodies,
        "tcp": {"body": eef, "pos": [0, 0, 0]},
        "home": [0.0, 0.19634954084936207, 0.0, -2.617993877991494, 0.0, 2.941592653589793, 0.7853981633974483],
        # Franka's published joint speed limits [rad/s].
        "velocity": [2.175, 2.175, 2.175, 2.175, 2.61, 2.61, 2.61],
    }
    out = gen / "Robots" / "panda.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(robot, indent=1))
    print(f"panda: {len(bodies)} bodies, {stats['meshes']} meshes, {stats['triangles']} triangles")


# --------------------------------------------------------------------------- GLB props
def glb_parts(path: Path):
    data = path.read_bytes()
    _, _, length = struct.unpack_from("<4sII", data, 0)
    offset = 12
    doc = None
    binary = b""
    while offset < length:
        clen, ctype = struct.unpack_from("<I4s", data, offset)
        chunk = data[offset + 8 : offset + 8 + clen]
        if ctype == b"JSON":
            doc = json.loads(chunk)
        elif ctype == b"BIN\x00":
            binary = chunk
        offset += 8 + clen
    return doc, binary


COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
DTYPES = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}


def accessor(doc: dict, binary: bytes, index: int) -> np.ndarray:
    acc = doc["accessors"][index]
    view = doc["bufferViews"][acc["bufferView"]]
    comps = COMPONENTS[acc["type"]]
    dtype = np.dtype(DTYPES[acc["componentType"]])
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride", 0)
    count = acc["count"]
    if stride and stride != comps * dtype.itemsize:
        raw = np.frombuffer(binary, dtype=np.uint8, count=stride * count, offset=start).reshape(count, stride)
        arr = raw[:, : comps * dtype.itemsize].copy().view(dtype).reshape(count, comps)
    else:
        arr = np.frombuffer(binary, dtype=dtype, count=count * comps, offset=start).reshape(count, comps)
    return arr.astype(np.float64) if acc["componentType"] == 5126 else arr.astype(np.int64)


def node_matrix(node: dict) -> np.ndarray:
    if "matrix" in node:
        return np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
    t = np.array(node.get("translation", [0, 0, 0]), dtype=np.float64)
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    s = np.array(node.get("scale", [1, 1, 1]), dtype=np.float64)
    m = np.eye(4)
    m[:3, :3] = quat_to_mat([w, x, y, z]) * s
    m[:3, 3] = t
    return m


def image_bytes(doc: dict, binary: bytes, image_index: int) -> bytes:
    img = doc["images"][image_index]
    view = doc["bufferViews"][img["bufferView"]]
    start = view.get("byteOffset", 0)
    return binary[start : start + view["byteLength"]]


def texture_image(doc: dict, ref: dict | None) -> int | None:
    if not ref:
        return None
    tex = doc["textures"][ref["index"]]
    ext = tex.get("extensions", {}).get("EXT_texture_webp")
    return ext["source"] if ext else tex.get("source")


def export_prop(name: str, gen: Path) -> None:
    from PIL import Image

    doc, binary = glb_parts(FACTORY / "models" / f"{name}.glb")
    tex_dir = gen / "Textures"
    tex_dir.mkdir(parents=True, exist_ok=True)
    # Gather triangles per material, in world space of the glTF scene.
    per_material: dict[int, list] = {}
    scene = doc["scenes"][doc.get("scene", 0)]

    def visit(ni: int, parent: np.ndarray) -> None:
        node = doc["nodes"][ni]
        m = parent @ node_matrix(node)
        if "mesh" in node:
            normal_m = np.linalg.inv(m[:3, :3]).T
            for prim in doc["meshes"][node["mesh"]]["primitives"]:
                attrs = prim["attributes"]
                p = accessor(doc, binary, attrs["POSITION"])
                n = accessor(doc, binary, attrs["NORMAL"]) if "NORMAL" in attrs else None
                uv = accessor(doc, binary, attrs["TEXCOORD_0"]) if "TEXCOORD_0" in attrs else np.zeros((len(p), 2))
                idx = accessor(doc, binary, prim["indices"]).reshape(-1) if "indices" in prim else np.arange(len(p))
                p = p @ m[:3, :3].T + m[:3, 3]
                if n is not None:
                    n = n @ normal_m.T
                per_material.setdefault(prim.get("material", -1), []).append((p, n, uv, idx.reshape(-1, 3)))
        for child in node.get("children", []):
            visit(child, m)

    for ni in scene["nodes"]:
        visit(ni, np.eye(4))

    # Bounds for the normalisation (in the Unity frame: glTF with z negated).
    allp = np.concatenate([np.concatenate([part[0] for part in parts]) for parts in per_material.values()])
    allp = allp * np.array([1, 1, -1])
    lo, hi = allp.min(axis=0), allp.max(axis=0)
    shift = np.array([-(lo[0] + hi[0]) / 2, -lo[1], -(lo[2] + hi[2]) / 2])

    used_images: set[int] = set()
    for mat in doc.get("materials", []):
        pbr = mat.get("pbrMetallicRoughness", {})
        for ref in (pbr.get("baseColorTexture"), pbr.get("metallicRoughnessTexture"), mat.get("normalTexture")):
            img = texture_image(doc, ref)
            if img is not None:
                used_images.add(img)
    # Poly Haven's packs carry a normal map the compressed GLB no longer names; use it when one
    # image is left over and it looks like a tangent-space normal map.
    spare_normal = None
    for i in range(len(doc.get("images", []))):
        if i in used_images:
            continue
        im = Image.open(io.BytesIO(image_bytes(doc, binary, i))).convert("RGB")
        mean = np.asarray(im, dtype=np.float64).reshape(-1, 3).mean(axis=0) / 255.0
        if mean[2] > 0.8 and abs(mean[0] - 0.5) < 0.12 and abs(mean[1] - 0.5) < 0.12:
            spare_normal = i

    parts_out = []
    for mi, parts in per_material.items():
        ps, ns, uvs, idxs = [], [], [], []
        base = 0
        for p, given, uv, idx in parts:
            n = given
            if n is None:
                tris = p[idx]
                fn = face_normals(tris)
                n = np.zeros_like(p)
                for k in range(3):
                    np.add.at(n, idx[:, k], fn)
            ps.append(p)
            ns.append(n)
            uvs.append(uv)
            idxs.append(idx + base)
            base += len(p)
        p = np.concatenate(ps) * np.array([1, 1, -1]) + shift
        n = np.concatenate(ns) * np.array([1, 1, -1])
        n = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
        uv = np.concatenate(uvs)
        # glTF's uv origin is the top-left; Unity samples from the bottom-left.
        uv = np.stack([uv[:, 0], 1.0 - uv[:, 1]], axis=1)
        idx = np.concatenate(idxs)[:, [0, 2, 1]]
        mesh_name = f"prop_{name}_{len(parts_out)}"
        write_mesh(gen / "Meshes" / f"{mesh_name}.bytes", p, n, idx, uv)
        material = {"color": [1, 1, 1, 1], "metallic": 1.0, "roughness": 1.0}
        if mi >= 0:
            m = doc["materials"][mi]
            pbr = m.get("pbrMetallicRoughness", {})
            material["name"] = m.get("name", "")
            material["color"] = pbr.get("baseColorFactor", [1, 1, 1, 1])
            material["metallic"] = pbr.get("metallicFactor", 1.0)
            material["roughness"] = pbr.get("roughnessFactor", 1.0)
            material["transparent"] = material["color"][3] < 0.999 or m.get("alphaMode") == "BLEND"
            base_img = texture_image(doc, pbr.get("baseColorTexture"))
            if base_img is not None:
                im = Image.open(io.BytesIO(image_bytes(doc, binary, base_img)))
                im = im.convert("RGBA" if material["transparent"] else "RGB")
                file = f"{mesh_name}_color.png"
                im.save(tex_dir / file)
                material["baseMap"] = file[:-4]
            mr_img = texture_image(doc, pbr.get("metallicRoughnessTexture"))
            if mr_img is not None:
                mr = (
                    np.asarray(
                        Image.open(io.BytesIO(image_bytes(doc, binary, mr_img))).convert("RGB"), dtype=np.float64
                    )
                    / 255.0
                )
                metal = mr[..., 2] * material["metallic"]
                smooth = 1.0 - mr[..., 1] * material["roughness"]
                packed = np.zeros((*mr.shape[:2], 4), dtype=np.uint8)
                packed[..., 0] = np.clip(metal * 255, 0, 255)
                packed[..., 3] = np.clip(smooth * 255, 0, 255)
                file = f"{mesh_name}_mg.png"
                Image.fromarray(packed).save(tex_dir / file)
                material["metallicGlossMap"] = file[:-4]
            normal_img = texture_image(doc, m.get("normalTexture"))
            if normal_img is None:
                normal_img = spare_normal
            if normal_img is not None:
                file = f"{mesh_name}_normal.png"
                Image.open(io.BytesIO(image_bytes(doc, binary, normal_img))).convert("RGB").save(tex_dir / file)
                material["normalMap"] = file[:-4]
        parts_out.append({"mesh": mesh_name, "material": material})
    size = (hi - lo).round(5).tolist()
    out = gen / "Props" / f"{name}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"name": name, "size": size, "parts": parts_out}, indent=1))
    print(f"prop {name}: {len(parts_out)} parts, size {size}")


# --------------------------------------------------------------------------- textures
def webp(name: str) -> np.ndarray:
    from PIL import Image

    return np.asarray(Image.open(FACTORY / "textures" / f"{name}.webp").convert("RGB"), dtype=np.float64) / 255.0


def save_rgba(arr: np.ndarray, path: Path) -> None:
    from PIL import Image

    Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8)).save(path)


def metallic_gloss(rough: np.ndarray, metal: float, smooth_of_rough) -> np.ndarray:
    out = np.zeros((*rough.shape[:2], 4))
    out[..., 0] = metal
    out[..., 3] = smooth_of_rough(rough[..., 1])
    return out


def export_factory_textures(gen: Path) -> None:
    from PIL import Image

    tex = gen / "Textures"
    tex.mkdir(parents=True, exist_ok=True)
    for name in ("floor_color", "panel_color"):
        Image.open(FACTORY / "textures" / f"{name}.webp").convert("RGB").save(tex / f"{name}.png")
    for name in ("floor_normal", "cladding_normal", "panel_normal"):
        Image.open(FACTORY / "textures" / f"{name}.webp").convert("RGB").save(tex / f"{name}.png")
    # Roughness as the web app's materials read it, as smoothness in URP's map.
    # The epoxy floor: roughness * mix(0.1, 0.5, map) (kit.ts), non-metal.
    save_rgba(metallic_gloss(webp("floor_rough"), 0.0, lambda r: 1.0 - (0.1 + 0.4 * r)), tex / "floor_mg.png")
    # Cladding: roughness 1 * map, metalness 0.18.
    save_rgba(metallic_gloss(webp("cladding_rough"), 0.18, lambda r: 1.0 - r), tex / "cladding_mg.png")
    # Machine panels: roughness 0.9 * map, metalness 0.25.
    save_rgba(metallic_gloss(webp("panel_rough"), 0.25, lambda r: 1.0 - 0.9 * r), tex / "panel_mg.png")
    shutil.copyfile(FACTORY / "factory_1k.hdr", tex / "factory_hdri.hdr")
    # A 32 x 16 copy of the HDRI's radiance, which the app sums into its ambient light.
    rgb = read_hdr(FACTORY / "factory_1k.hdr")
    h, w = rgb.shape[:2]
    small = rgb.reshape(16, h // 16, 32, w // 32, 3).mean(axis=(1, 3))
    (tex / "hdri_samples.json").write_text(
        json.dumps({"w": 32, "h": 16, "rgb": [round(float(v), 5) for v in small.reshape(-1)]})
    )
    print("factory textures: floor, cladding, panel, HDRI and its ambient samples")


def read_hdr(path: Path) -> np.ndarray:
    """A Radiance RGBE (.hdr) image as linear RGB floats, rows from the top."""
    data = path.read_bytes()
    pos = 0
    newline = bytes([10])
    while True:
        end = data.index(newline, pos)
        line = data[pos:end]
        pos = end + 1
        if line.strip() == b"":
            break
    end = data.index(newline, pos)
    parts = data[pos:end].decode().split()
    pos = end + 1
    h, w = int(parts[1]), int(parts[3])
    img = np.zeros((h, w, 4), dtype=np.uint8)
    for y in range(h):
        if data[pos] == 2 and data[pos + 1] == 2 and ((data[pos + 2] << 8) | data[pos + 3]) == w:
            pos += 4
            for c in range(4):
                x = 0
                while x < w:
                    count = data[pos]
                    pos += 1
                    if count > 128:
                        count -= 128
                        img[y, x : x + count, c] = data[pos]
                        pos += 1
                    else:
                        img[y, x : x + count, c] = np.frombuffer(data, np.uint8, count, pos)
                        pos += count
                    x += count
        else:
            img[y] = np.frombuffer(data, np.uint8, w * 4, pos).reshape(w, 4)
            pos += w * 4
    e = img[..., 3].astype(np.int32)
    scale = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0)
    return img[..., :3].astype(np.float64) * scale[..., None]


# --------------------------------------------------------------------------- Edge
def edge() -> Path:
    for p in EDGE_PATHS:
        if p.exists():
            return p
    raise SystemExit("Microsoft Edge was not found; it draws the canvas textures and icons.")


def screenshot(html: str, width: int, height: int, settle_s: float = 4.0) -> Image.Image:
    """Renders a page in headless Edge and returns the transparent screenshot.

    msedge.exe can hand off to another process and return at once, so this waits for
    the file to appear and stop growing (as the desktop launchers' make_icons.py does).
    """
    from PIL import Image

    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        page = Path(tmp) / "page.html"
        shot = Path(tmp) / "shot.png"
        page.write_text(html, encoding="utf-8")
        subprocess.run(
            [
                str(edge()),
                "--headless=new",
                "--disable-gpu",
                "--no-first-run",
                "--hide-scrollbars",
                "--force-device-scale-factor=1",
                f"--user-data-dir={Path(tmp) / 'profile'}",
                "--default-background-color=00000000",
                f"--window-size={width},{height}",
                "--virtual-time-budget=15000",
                f"--screenshot={shot}",
                page.as_uri(),
            ],
            check=True,
            capture_output=True,
            timeout=180,
        )
        deadline = time.monotonic() + 90
        last = -1
        while time.monotonic() < deadline:
            if shot.exists():
                size = shot.stat().st_size
                if size > 0 and size == last:
                    break
                last = size
            time.sleep(settle_s / 4)
        else:
            raise SystemExit("Edge did not write its screenshot")
        return Image.open(shot).convert("RGBA").copy()


# Where each canvas graphic sits on the page Edge renders (x, y, w, h); see canvas_textures.js.
CANVAS_LAYOUT = {
    "macro": (0, 0, 512, 512),
    "floor_arrow": (512, 0, 256, 512),
    "hazard": (768, 0, 256, 256),
    "carton": (768, 256, 256, 256),
    "sign_fire": (1024, 0, 256, 256),
    "blob": (1024, 256, 128, 128),
    "softdot": (1152, 256, 64, 64),
    "sign_exit": (1024, 384, 256, 128),
    "sign_robot_cell": (1280, 0, 512, 384),
    "sign_automatic": (0, 512, 512, 384),
    "sign_glasses": (512, 512, 512, 384),
    "sign_collab": (1024, 512, 512, 384),
    "floor_cell": (0, 896, 1024, 256),
    "floor_machining": (1024, 896, 1024, 256),
    "floor_lab": (0, 1152, 1024, 256),
    "floor_twin": (1024, 1152, 1024, 256),
    "wall_logo": (0, 1408, 1811, 407),
}
CANVAS_PAGE = (2048, 1816)


def export_canvas_textures(gen: Path, tools: Path) -> None:
    """Draws textures.ts's graphics with the page's own canvas code (canvas_textures.js)."""
    font = base64.b64encode(
        (NODE / "@fontsource-variable" / "figtree" / "files" / "figtree-latin-wght-normal.woff2").read_bytes()
    ).decode()
    dark = base64.b64encode((REPO / "dark.png").read_bytes()).decode()
    light = base64.b64encode((REPO / "light.png").read_bytes()).decode()
    script = (tools / "canvas_textures.js").read_text(encoding="utf-8")
    width, height = CANVAS_PAGE
    html = f"""<!doctype html><html><head><meta charset="utf-8"><style>
@font-face {{ font-family: "Figtree Variable"; src: url(data:font/woff2;base64,{font}) format("woff2"); font-weight: 300 900; }}
html, body {{ margin: 0; background: transparent; width: {width}px; height: {height}px; overflow: hidden; }}
canvas {{ position: absolute; display: block; }}
</style></head><body>
<script>const DARK_LOGO = "data:image/png;base64,{dark}"; const LIGHT_LOGO = "data:image/png;base64,{light}";
const LAYOUT = {json.dumps(CANVAS_LAYOUT)};</script>
<script>{script}</script></body></html>"""
    page = screenshot(html, width, height)
    out = gen / "Textures"
    out.mkdir(parents=True, exist_ok=True)
    for name, (x, y, w, h) in CANVAS_LAYOUT.items():
        page.crop((x, y, x + w, y + h)).save(out / f"{name}.png")
    print(f"canvas textures: {', '.join(sorted(CANVAS_LAYOUT))}")


ICONS = [
    "house",
    "graduation-cap",
    "gamepad-2",
    "map",
    "settings",
    "x",
    "check",
    "chevron-right",
    "chevron-left",
    "chevron-up",
    "chevron-down",
    "arrow-right",
    "arrow-left",
    "arrow-up",
    "arrow-down",
    "rotate-ccw",
    "rotate-cw",
    "play",
    "pause",
    "square",
    "skip-forward",
    "step-forward",
    "repeat",
    "list",
    "list-ordered",
    "plus",
    "minus",
    "trash-2",
    "save",
    "info",
    "circle-help",
    "triangle-alert",
    "octagon-x",
    "shield",
    "shield-check",
    "shield-alert",
    "hand",
    "grab",
    "move",
    "move-3d",
    "rotate-3d",
    "axis-3d",
    "crosshair",
    "target",
    "bot",
    "cpu",
    "activity",
    "gauge",
    "thermometer",
    "zap",
    "battery-charging",
    "leaf",
    "users",
    "user",
    "eye",
    "eye-off",
    "layers",
    "box",
    "boxes",
    "package",
    "factory",
    "warehouse",
    "truck",
    "radar",
    "scan",
    "radio-tower",
    "wifi",
    "cloud",
    "database",
    "chart-line",
    "chart-column",
    "timer",
    "clock",
    "award",
    "trophy",
    "star",
    "sparkles",
    "lightbulb",
    "book-open",
    "volume-2",
    "volume-x",
    "sun",
    "moon",
    "footprints",
    "locate",
    "navigation",
    "joystick",
    "magnet",
    "wrench",
    "link",
    "unlink",
    "refresh-cw",
    "copy",
    "ghost",
    "monitor",
    "tablet",
    "glasses",
    "mouse-pointer-click",
    "pointer",
    "circle-dot",
    "power",
    "lock",
    "lock-open",
    "siren",
    "bell",
    "flag",
    "map-pin",
    "route",
    "waypoints",
    "brain",
    "heart-pulse",
    "recycle",
    "globe",
    "hand-helping",
    "scale",
    "ruler",
    "workflow",
    "git-compare",
    "circle-check",
    "circle-x",
    "circle",
    "octagon-alert",
    "hard-hat",
    "sliders-horizontal",
    "fast-forward",
    "gauge-circle",
    "scan-eye",
    "badge-check",
    "medal",
    "send",
    "download",
    "upload",
    "keyboard",
    "headset",
    "person-standing",
    "accessibility",
    "battery-low",
    "flame",
    "snowflake",
    "wind",
    "construction",
    "drill",
    "cog",
    "grip",
    "hand-grab",
    "pointer-off",
    "rocket",
    "milestone",
    "calendar-check",
    "clipboard-check",
]


def js_array_after(source: str, marker: str) -> str | None:
    """The bracketed array literal that follows ``marker`` in a JavaScript module."""
    start = source.find(marker)
    if start < 0:
        return None
    start = source.index("[", start)
    depth = 0
    quote = None
    for i in range(start, len(source)):
        ch = source[i]
        if quote:
            if ch == quote and source[i - 1] != "\\":
                quote = None
        elif ch in "\"'":
            quote = ch
        elif ch == "[":
            depth += 1
        elif ch == "]":
            depth -= 1
            if depth == 0:
                return source[start : i + 1]
    return None


def export_icons(gen: Path, cell: int = 128, cols: int = 16) -> None:
    """Rasterises lucide icons (the page's icon set, ISC) into one white-on-clear atlas."""
    nodes = {}
    icon_dir = NODE / "lucide-react" / "dist" / "esm" / "icons"
    for name in ICONS:
        f = icon_dir / f"{name}.mjs"
        if not f.exists():
            print(f"  icon {name}: not in lucide-react, skipped")
            continue
        js = js_array_after(f.read_text(encoding="utf-8"), "node:")
        if js is None:
            print(f"  icon {name}: no node data, skipped")
            continue
        js = re.sub(r"(\w+):", r'"\1":', js)  # keys to JSON
        js = re.sub(r",\s*([\]}])", r"\1", js)
        nodes[name] = json.loads(js)
    names = sorted(nodes)
    rows = math.ceil(len(names) / cols)
    width, height = cell * cols, cell * rows
    html = f"""<!doctype html><html><head><style>
html, body {{ margin: 0; background: transparent; width: {width}px; height: {height}px; overflow: hidden; }}
img {{ position: absolute; width: {cell}px; height: {cell}px; }}
</style></head><body><script>
const ICONS = {json.dumps({n: nodes[n] for n in names})};
const CELL = {cell}, COLS = {cols};
function svg(node) {{
  const parts = node.map(([tag, attrs]) => '<' + tag + ' ' + Object.entries(attrs).filter(([k]) => k !== 'key').map(([k, v]) => k + '="' + v + '"').join(' ') + '/>').join('');
  return '<svg xmlns="http://www.w3.org/2000/svg" width="' + CELL + '" height="' + CELL + '" viewBox="-1 -1 26 26" fill="none" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' + parts + '</svg>';
}}
Object.keys(ICONS).forEach((name, i) => {{
  const img = document.createElement('img');
  img.src = 'data:image/svg+xml;base64,' + btoa(svg(ICONS[name]));
  img.style.left = (i % COLS) * CELL + 'px';
  img.style.top = Math.floor(i / COLS) * CELL + 'px';
  document.body.appendChild(img);
}});
</script></body></html>"""
    atlas = screenshot(html, width, height)
    uv: list[float] = []
    for i in range(len(names)):
        x, y = (i % cols) * cell, (i // cols) * cell
        # u, v, w, h with v from the bottom, as Unity samples; flat, for JsonUtility.
        uv += [x / width, 1 - (y + cell) / height, cell / width, cell / height]
    ui = gen / "UI"
    ui.mkdir(parents=True, exist_ok=True)
    atlas.save(ui / "icons.png")
    (ui / "icons.json").write_text(json.dumps({"cell": cell, "names": names, "uv": [round(v, 6) for v in uv]}))
    print(f"icons: {len(names)} in a {cols} x {rows} atlas")


# --------------------------------------------------------------------------- fonts, logos
def export_fonts(root: Path) -> None:
    """Static Figtree weights and Plex Mono as TTF, which TextMesh Pro builds its atlases from."""
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer

    out = root / "Fonts"
    out.mkdir(parents=True, exist_ok=True)
    src = NODE / "@fontsource-variable" / "figtree" / "files" / "figtree-latin-wght-normal.woff2"
    for name, wght in (("Regular", 400), ("Medium", 500), ("SemiBold", 600), ("Bold", 700), ("ExtraBold", 800)):
        font = instancer.instantiateVariableFont(TTFont(src), {"wght": wght})
        font.flavor = None
        font.save(out / f"Figtree-{name}.ttf")
    mono = NODE / "@fontsource" / "ibm-plex-mono" / "files"
    for name, weight in (("Regular", 400), ("SemiBold", 600)):
        f = mono / f"ibm-plex-mono-latin-{weight}-normal.woff"
        if f.exists():
            font = TTFont(f)
            font.flavor = None
            font.save(out / f"IBMPlexMono-{name}.ttf")
    print(f"fonts: {', '.join(sorted(p.name for p in out.glob('*.ttf')))}")


def app_icon(white: Image.Image, size: int = 256) -> Image.Image:
    """The desktop shortcut's icon, in the style of the other launchers: the wordmark on a dark
    rounded tile, with an orange visor arc for VR."""
    from PIL import Image, ImageDraw

    scale = 4
    s = size * scale
    tile = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(tile)
    draw.rounded_rectangle(
        (4 * scale, 4 * scale, s - 5 * scale, s - 5 * scale), radius=52 * scale, fill=(13, 16, 20, 255)
    )
    # The visor: an arc over the wordmark and a soft glass band under it.
    draw.arc(
        (28 * scale, 40 * scale, s - 28 * scale, s - 16 * scale),
        start=200,
        end=340,
        fill=(255, 71, 19, 255),
        width=10 * scale,
    )
    draw.rounded_rectangle(
        (44 * scale, 168 * scale, s - 44 * scale, 196 * scale), radius=14 * scale, fill=(255, 255, 255, 34)
    )
    mark = white.copy()
    mark.thumbnail((int(s * 0.66), int(s * 0.66)), Image.LANCZOS)
    tile.alpha_composite(mark, ((s - mark.width) // 2, int(s * 0.47) - mark.height // 2))
    return tile.resize((size, size), Image.LANCZOS)


def export_logos(gen: Path) -> None:
    from PIL import Image

    ui = gen / "UI"
    ui.mkdir(parents=True, exist_ok=True)
    # light.png is the white wordmark (for dark grounds), dark.png the graphite one.
    for src, dst in (("light.png", "wordmark_white.png"), ("dark.png", "wordmark_graphite.png")):
        im = Image.open(REPO / src).convert("RGBA")
        im = im.crop(im.getbbox())
        im.save(ui / dst)
    icon = app_icon(Image.open(ui / "wordmark_white.png"))
    icon.save(ui / "app_icon.png")
    icon.save(
        gen.parent.parent / "app_icon.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print("wordmarks: white, graphite; app icon")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", required=True, type=Path, help="the Unity project's Assets/BridgeGen")
    ap.add_argument("--only", nargs="*", default=None, help="panda props textures canvas icons fonts logos")
    args = ap.parse_args()
    out: Path = args.out
    gen = out / "Resources" / "Gen"
    gen.mkdir(parents=True, exist_ok=True)
    tools = Path(__file__).resolve().parent
    steps = {
        "panda": lambda: export_panda(gen),
        "props": lambda: [
            export_prop(n, gen)
            for n in (
                "cardboard_box",
                "plastic_crate",
                "drum",
                "tool_chest",
                "storage_cart",
                "fire_extinguisher",
                "floor_sign",
            )
        ],
        "textures": lambda: export_factory_textures(gen),
        "canvas": lambda: export_canvas_textures(gen, tools),
        "icons": lambda: export_icons(gen),
        "fonts": lambda: export_fonts(out),
        "logos": lambda: export_logos(gen),
    }
    for key, step in steps.items():
        if args.only is None or key in args.only:
            step()


if __name__ == "__main__":
    sys.exit(main())
