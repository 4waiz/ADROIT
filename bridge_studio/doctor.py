# SPDX-License-Identifier: Apache-2.0
"""What this machine can run, checked rather than assumed.

``python -m bridge_studio doctor`` prints the checks; ``GET /api/doctor`` serves
them to the page's Diagnostics tab. Each check says ``ok``, ``warn`` or ``fail``
and, when it is not ``ok``, what to do about it.
"""

from __future__ import annotations

import functools
import importlib.util
import platform
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from .config import repo_root

__all__ = ["license_texts", "machine_summary", "run_checks"]


def _check(name: str, status: str, detail: str, fix: str = "") -> dict[str, str]:
    return {"name": name, "status": status, "detail": detail, "fix": fix}


@functools.lru_cache(maxsize=1)
def _gpu() -> dict[str, Any]:
    try:
        import warp as wp

        wp.config.quiet = True
        wp.init()
        devices = list(wp.get_cuda_devices()) if wp.is_cuda_available() else []
        return {
            "warp": wp.__version__,
            "cuda": bool(devices),
            "devices": [
                {"name": d.name, "arch": f"sm_{d.arch}", "memoryGB": round(d.total_memory / 2**30, 1)} for d in devices
            ],
            "driver": ".".join(str(v) for v in wp.get_cuda_driver_version()) if devices else "",
        }
    except Exception as exc:
        return {"warp": None, "cuda": False, "devices": [], "error": f"{type(exc).__name__}: {exc}"}


def machine_summary() -> dict[str, Any]:
    gpu = _gpu()
    return {
        "os": f"{platform.system()} {platform.release()}",
        "python": platform.python_version(),
        "gpu": gpu.get("devices", []),
        "cuda": gpu.get("cuda", False),
        "warp": gpu.get("warp"),
        "renderer": _renderer(),
    }


@functools.lru_cache(maxsize=1)
def _renderer() -> dict[str, str]:
    try:
        from robosimstudio.solvers.render import choose_renderer

        backend, why = choose_renderer(device="cuda:0" if _gpu().get("cuda") else "cpu")
        return {"backend": backend, "why": why}
    except Exception as exc:
        return {"backend": "unknown", "why": f"{type(exc).__name__}: {exc}"}


def run_checks() -> list[dict[str, str]]:
    checks: list[dict[str, str]] = []
    major, minor = sys.version_info[:2]
    checks.append(
        _check("Python", "ok", platform.python_version())
        if (major, minor) >= (3, 12)
        else _check("Python", "fail", platform.python_version(), "Use Python 3.12: `uv python install 3.12`.")
    )
    checks.append(_check("Operating system", "ok", f"{platform.system()} {platform.release()} ({platform.machine()})"))

    gpu = _gpu()
    if gpu.get("cuda"):
        names = ", ".join(f"{d['name']} ({d['arch']}, {d['memoryGB']} GB)" for d in gpu["devices"])
        checks.append(_check("CUDA GPU", "ok", f"{names}; driver {gpu.get('driver')}; Warp {gpu.get('warp')}"))
    else:
        checks.append(
            _check(
                "CUDA GPU",
                "fail",
                gpu.get("error") or "No CUDA device is visible to Warp.",
                "The simulation scenes run on Newton and MuJoCo Warp, which need an NVIDIA GPU with a current driver.",
            )
        )

    for module, label, fix in (
        ("newton", "Newton physics", "`uv sync` installs it."),
        ("mujoco_warp", "MuJoCo Warp", "`uv sync` installs it (newton[sim])."),
        ("pyroki", "PyRoKi IK", "`uv sync` installs it; without it arms fall back to Newton's IK."),
        ("fastapi", "Web service (FastAPI)", "`uv sync` installs the bridge group."),
        ("uvicorn", "Web server (uvicorn)", "`uv sync` installs the bridge group."),
    ):
        found = importlib.util.find_spec(module) is not None
        checks.append(
            _check(label, "ok" if found else "fail", "installed" if found else "not installed", "" if found else fix)
        )
    sdx = importlib.util.find_spec("superdex") is not None
    checks.append(
        _check(
            "SuperDex FEM (sponges)",
            "ok" if sdx else "warn",
            "installed" if sdx else "not installed: the sponge scenarios are unavailable",
            "" if sdx else "`uv sync --extra superdex` (Python 3.12).",
        )
    )
    renderer = _renderer()
    status = "ok" if renderer["backend"] == "rtx" else "warn"
    checks.append(
        _check(
            "Snapshot renderer",
            status,
            f"{renderer['backend']}: {renderer['why']}",
            "" if status == "ok" else "Path-traced snapshots need ovrtx and an RTX GPU; tiled renders colliders only.",
        )
    )
    static = Path(__file__).resolve().parent / "static" / "index.html"
    checks.append(
        _check(
            "Browser client",
            "ok" if static.is_file() else "fail",
            "built" if static.is_file() else "not built",
            "" if static.is_file() else "`cd frontend && npm install && npm run build`",
        )
    )
    node = shutil.which("node")
    if node:
        try:
            version = subprocess.run(
                [node, "--version"], capture_output=True, text=True, timeout=10, check=False
            ).stdout.strip()
        except Exception:
            version = "unknown"
        checks.append(_check("Node.js (to build the client)", "ok", version))
    else:
        checks.append(
            _check(
                "Node.js (to build the client)",
                "ok" if static.is_file() else "warn",
                "not found",
                "Only needed to rebuild the client: https://nodejs.org (LTS).",
            )
        )
    if "OneDrive" in str(repo_root()):
        checks.append(
            _check(
                "Checkout location",
                "warn",
                "The repository is inside a OneDrive folder.",
                "Keep the Python environment outside it (UV_PROJECT_ENVIRONMENT, as scripts/bridge.ps1 does) so "
                "gigabytes of packages are not synced.",
            )
        )
    checks.append(
        _check(
            "Hardware connector",
            "ok",
            "None configured: every command goes to the simulation. No physical robot can be moved from this build.",
        )
    )
    return checks


def license_texts() -> dict[str, Any]:
    root = repo_root()

    def read(name: str) -> str:
        path = root / name
        return path.read_text(encoding="utf-8") if path.is_file() else ""

    third_party = []
    for path in sorted((root / "assets").glob("embodiments/**/LICENSE*")):
        third_party.append(
            {
                "path": str(path.relative_to(root)).replace("\\", "/"),
                "text": path.read_text(encoding="utf-8", errors="replace"),
            }
        )
    return {
        "license": read("LICENSE"),
        "notice": read("NOTICE"),
        "citation": read("CITATION.cff"),
        "thirdParty": third_party,
        "client": [
            {"name": "React", "license": "MIT"},
            {"name": "three.js", "license": "MIT"},
            {"name": "zustand", "license": "MIT"},
            {"name": "uPlot", "license": "MIT"},
            {"name": "Lucide icons", "license": "ISC"},
            {"name": "Figtree (font)", "license": "SIL OFL 1.1"},
            {"name": "IBM Plex Mono / IBM Plex Sans Arabic (fonts)", "license": "SIL OFL 1.1"},
        ],
    }
