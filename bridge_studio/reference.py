# SPDX-License-Identifier: LicenseRef-BRIDGE
"""Reference media for real-to-sim: a clip or a photo, its stills, and the scene held against them.

Everything that measures here is the repository's real-to-sim agent's own code,
called rather than copied: :mod:`robosimstudio_real2sim_agent.frames` probes a clip
and cuts stills at chosen seconds, and :func:`robosimstudio_real2sim_agent.checks.compare_still`
finds each keyed thing in the photograph by colour and in the render by its
segmentation id, then reports size, offset and overlap per thing -- the numbers
``./ctl compare`` prints. The render is taken through a camera built from the
viewport's own eye and target, at the still's own resolution, so the two
pictures are of one view.

What a comparison does **not** claim: that the twin is right. A colour key finds
pixels of a colour; a thing the key misses, or a second thing of the same colour,
changes the reading. The page shows the overlay sheet beside every number so the
person reading it can see what was measured.
"""

from __future__ import annotations

import json
import re
import shutil
import time
import uuid
from pathlib import Path
from typing import Any

import numpy as np

__all__ = ["MEDIA_TYPES", "ReferenceStore"]

MEDIA_TYPES = {
    ".mp4": "video",
    ".mov": "video",
    ".webm": "video",
    ".mkv": "video",
    ".avi": "video",
    ".png": "image",
    ".jpg": "image",
    ".jpeg": "image",
    ".webp": "image",
}
MAX_BYTES = 1024 * 1024 * 1024
"""A gigabyte: a long phone clip fits, a whole drive does not."""

ID = re.compile(r"^[a-f0-9]{12}$")
STILL = re.compile(r"^[A-Za-z0-9_.\-]{1,80}\.(jpg|png)$")


class ReferenceStore:
    """One folder per reference under ``root``: the source file, its stills, and what was compared."""

    def __init__(self, root: Path) -> None:
        self.root = Path(root)

    # ----------------------------------------------------------------- files
    def folder(self, ref_id: str) -> Path:
        if not ID.match(ref_id):
            raise ValueError("not a reference id")
        path = self.root / ref_id
        if not path.is_dir():
            raise FileNotFoundError(f"no reference {ref_id}")
        return path

    def source(self, ref_id: str) -> Path:
        meta = self.meta(ref_id)
        return self.folder(ref_id) / meta["file"]

    def meta(self, ref_id: str) -> dict[str, Any]:
        return json.loads((self.folder(ref_id) / "meta.json").read_text(encoding="utf-8"))

    def save_meta(self, ref_id: str, meta: dict[str, Any]) -> None:
        (self.folder(ref_id) / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")

    def list(self) -> list[dict[str, Any]]:
        if not self.root.is_dir():
            return []
        out = []
        for path in sorted(self.root.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True):
            meta_path = path / "meta.json"
            if meta_path.is_file():
                try:
                    out.append(json.loads(meta_path.read_text(encoding="utf-8")))
                except ValueError:
                    continue
        return out

    # --------------------------------------------------------------- import
    def incoming(self, name: str) -> Path:
        """Where an upload is streamed before it is adopted; refuses an unsupported type up front."""
        suffix = Path(name).suffix.lower()
        if suffix not in MEDIA_TYPES:
            raise ValueError(f"{suffix or 'that file'} is not a supported reference: {', '.join(sorted(MEDIA_TYPES))}")
        folder = self.root / "_incoming"
        folder.mkdir(parents=True, exist_ok=True)
        return folder / f"{uuid.uuid4().hex}{suffix}"

    def adopt(self, name: str, uploaded: Path) -> dict[str, Any]:
        """Take a streamed upload into its own folder and read what it is."""
        suffix = uploaded.suffix.lower()
        ref_id = uuid.uuid4().hex[:12]
        folder = self.root / ref_id
        folder.mkdir(parents=True, exist_ok=True)
        target = folder / f"source{suffix}"
        shutil.move(str(uploaded), target)
        size = target.stat().st_size
        kind = MEDIA_TYPES[suffix]
        meta: dict[str, Any] = {
            "id": ref_id,
            "name": Path(name).name[:120],
            "file": target.name,
            "kind": kind,
            "bytes": size,
            "addedAt": time.time(),
            "stills": [],
            "comparisons": [],
        }
        try:
            if kind == "video":
                from robosimstudio_real2sim_agent.frames import probe

                info = probe(target)
                meta.update({k: info[k] for k in ("fps", "width", "height", "duration", "frames")})
            else:
                from PIL import Image

                with Image.open(target) as image:
                    meta.update({"width": image.width, "height": image.height})
                still = folder / "stills" / f"still{suffix if suffix in ('.jpg', '.png') else '.png'}"
                still.parent.mkdir(exist_ok=True)
                if suffix in (".jpg", ".png"):
                    shutil.copyfile(target, still)
                else:
                    with Image.open(target) as image:
                        image.convert("RGB").save(still.with_suffix(".png"))
                    still = still.with_suffix(".png")
                meta["stills"] = [{"t": 0.0, "file": still.name, "label": "photo"}]
        except Exception as exc:
            shutil.rmtree(folder, ignore_errors=True)
            raise ValueError(f"could not read {name}: {type(exc).__name__}: {exc}") from None
        (folder / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
        return meta

    def delete(self, ref_id: str) -> None:
        shutil.rmtree(self.folder(ref_id))

    # --------------------------------------------------------------- stills
    def cut(self, ref_id: str, times: list[float], labels: list[str] | None = None) -> dict[str, Any]:
        """Stills at chosen seconds of a clip, named by their time (the real2sim agent's ``extract``)."""
        from robosimstudio_real2sim_agent.frames import extract

        meta = self.meta(ref_id)
        if meta["kind"] != "video":
            raise ValueError("a photo is its own still")
        duration = float(meta.get("duration") or 0.0)
        clean = []
        for raw in times[:24]:
            t = float(raw)
            if not np.isfinite(t) or t < 0:
                raise ValueError("a still's time is a non-negative number of seconds")
            clean.append(round(min(t, max(0.0, duration - 1e-3)), 3))
        paths = extract(self.source(ref_id), clean, self.folder(ref_id) / "stills")
        stills = {s["file"]: s for s in meta.get("stills", [])}
        for k, (t, path) in enumerate(zip(clean, paths, strict=True)):
            label = (labels[k] if labels and k < len(labels) else "") or ""
            stills[path.name] = {"t": t, "file": path.name, "label": label[:40]}
        meta["stills"] = sorted(stills.values(), key=lambda s: s["t"])
        self.save_meta(ref_id, meta)
        return meta

    def still_path(self, ref_id: str, file: str) -> Path:
        if not STILL.match(file):
            raise ValueError("not a still")
        path = self.folder(ref_id) / "stills" / file
        if not path.is_file():
            raise FileNotFoundError(file)
        return path

    def artifact_path(self, ref_id: str, file: str) -> Path:
        if not STILL.match(file):
            raise ValueError("not an artifact")
        path = self.folder(ref_id) / "compare" / file
        if not path.is_file():
            raise FileNotFoundError(file)
        return path

    # -------------------------------------------------------------- compare
    def compare(self, sim: Any, ref_id: str, still: str, view: dict[str, Any], keys: list[str]) -> dict[str, Any]:
        """Render the scene through ``view`` at the still's size and measure it against the still. Owner thread."""
        from PIL import Image

        from robosimstudio.specs import CameraSpec
        from robosimstudio_real2sim_agent import checks
        from robosimstudio_real2sim_agent import compare as C
        from robosimstudio_real2sim_agent import stills as S

        photo = self.still_path(ref_id, still)
        with Image.open(photo) as image:
            width, height = image.size
        scale = min(1.0, 1280.0 / max(width, height))
        width, height = max(64, int(width * scale)) // 2 * 2, max(64, int(height * scale)) // 2 * 2
        parsed = [C.parse_key(k) for k in keys[:12] if str(k).strip()]
        eye = tuple(float(v) for v in view["eye"])
        look_at = tuple(float(v) for v in view["lookAt"])
        camera = CameraSpec(
            name="bridge_reference",
            width=width,
            height=height,
            fov_y=float(view.get("fovY", 45.0)),
            eye=eye,
            look_at=look_at,
            up=(0.0, 0.0, 1.0),
            near=0.01,
            far=10.0,
        )
        started = time.perf_counter()
        sim.sync()
        names = sim.scene.handles.segmentation_names()
        renderer = sim.make_renderer("tiled", cameras=[camera], channels=["seg"])
        try:
            seg = np.asarray(renderer.render(sim.state)[camera.name]["seg"]).astype(np.int32)
        finally:
            renderer.close()
        rgb = S.render_still(sim, camera)
        result = checks.compare_still(rgb, seg, names, photo, parsed)
        out_dir = self.folder(ref_id) / "compare"
        out_dir.mkdir(exist_ok=True)
        stamp = time.strftime("%Y%m%d-%H%M%S")
        base = out_dir / f"cmp-{stamp}.png"
        pictures = checks.write_compare_pictures(base, rgb, result)
        render_file = S.save_png(rgb, out_dir / f"render-{stamp}.png")
        record = {
            "at": time.time(),
            "still": still,
            "simTime": float(sim.sim_time),
            "view": {"eye": list(eye), "lookAt": list(look_at), "fovY": camera.fov_y, "width": width, "height": height},
            "keys": [k for k in keys if str(k).strip()],
            "score": result["score"],
            "stretch": result["stretch"],
            "objects": {name: _plain(reading) for name, reading in result["objects"].items()},
            "lines": result["lines"],
            "render": Path(render_file).name,
            "beside": Path(pictures["beside"]).name,
            "look": Path(pictures["look"]).name,
            "seconds": round(time.perf_counter() - started, 2),
            "renderer": getattr(sim, "_still_key", (None,))[0] if getattr(sim, "_still_key", None) else "",
        }
        meta = self.meta(ref_id)
        meta.setdefault("comparisons", []).append(record)
        meta["comparisons"] = meta["comparisons"][-30:]
        self.save_meta(ref_id, meta)
        return record


def _plain(value: Any) -> Any:
    if isinstance(value, dict):
        return {str(k): _plain(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [_plain(v) for v in value]
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, np.ndarray):
        return value.tolist()
    if isinstance(value, Path):
        return str(value)
    return value
