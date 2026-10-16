# SPDX-License-Identifier: Apache-2.0
"""The web service: REST for what a page or an agent asks once, a WebSocket for what streams.

Every route is a thin door onto :class:`~bridge_studio.host.SimHost`. Nothing here
touches the scene: reads come from what the host published, and commands go
through :meth:`SimHost.execute`, which runs them on the host's own thread and
answers with an acknowledgement. The OpenAPI document at ``/docs`` is the
reference an agent reads; ``docs/bridge/AGENT_API.md`` says the same in prose.

The server binds ``127.0.0.1`` unless told otherwise: it drives a robot, even a
simulated one, and a page reachable from the network is a choice to make
deliberately.
"""

from __future__ import annotations

import asyncio
import contextlib
import gzip
import json
import mimetypes
import re
import time
import uuid
from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException, Request, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse, Response
from pydantic import BaseModel, Field

from . import PRODUCT, SUBTITLE, __version__
from .config import BridgeConfig, repo_root
from .host import SimHost
from .scenarios import builtin_registry

__all__ = ["create_app"]

ATTRIBUTION = {"name": "Awaiz Ahmed", "url": "https://kanbanstudios.ae/team-kanban"}


class CommandBody(BaseModel):
    """One command, as ``POST /api/command`` takes it and the socket sends it."""

    id: str = Field(default_factory=lambda: str(uuid.uuid4()), max_length=80)
    name: str = Field(..., description="One of GET /api/commands/catalog")
    args: dict[str, Any] = Field(default_factory=dict)


class RenderBody(BaseModel):
    camera: str | None = Field(
        default=None, description="A camera the scene declares; omitted uses a view of the bench."
    )
    eye: tuple[float, float, float] | None = None
    look_at: tuple[float, float, float] | None = Field(default=None, alias="lookAt")
    fov_y: float = Field(default=45.0, alias="fovY")
    width: int = Field(default=960, ge=64, le=2048)
    height: int = Field(default=540, ge=64, le=2048)


class StillsBody(BaseModel):
    times: list[float] = Field(..., max_length=24, description="seconds into the clip")
    labels: list[str] | None = None


class CompareBody(BaseModel):
    still: str
    view: dict[str, Any] = Field(..., description="{eye: [x,y,z], lookAt: [x,y,z], fovY}")
    keys: list[str] = Field(default_factory=list, max_length=12, description="name=colour keys, e.g. cube=orange")


# The client's factory environment ships models, an HDR image and WebP textures; not every
# platform's mimetypes table knows them.
for _type, _ext in (("model/gltf-binary", ".glb"), ("image/vnd.radiance", ".hdr"), ("image/webp", ".webp")):
    mimetypes.add_type(_type, _ext)

SLUG = re.compile(r"^[a-z0-9][a-z0-9_-]{0,63}$")


def _slug(text: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")[:64]
    return slug or "workcell"


def _quiet_resets(loop: asyncio.AbstractEventLoop, context: dict[str, Any]) -> None:
    """The event loop's error report, less a client that hung up while its socket was closing.

    Windows' proactor loop reports that (a closed tab, a poll that gave up) as a traceback;
    it is not an error in the service. Everything else is reported as before.
    """
    if isinstance(context.get("exception"), ConnectionResetError):
        return
    loop.default_exception_handler(context)


def create_app(config: BridgeConfig | None = None, host: SimHost | None = None) -> FastAPI:
    """The application, with its host started when the app starts and stopped when it stops."""
    config = config or BridgeConfig()
    registry = builtin_registry()
    host = host or SimHost(config, registry)
    started_at = time.time()

    @contextlib.asynccontextmanager
    async def lifespan(app: FastAPI):
        asyncio.get_running_loop().set_exception_handler(_quiet_resets)
        host.start()
        try:
            yield
        finally:
            host.stop()
            print("[bridge] simulation stopped", flush=True)

    app = FastAPI(
        title=PRODUCT,
        version=__version__,
        description=f"{SUBTITLE}. The simulation is authoritative; every command is validated and acknowledged.",
        lifespan=lifespan,
    )
    app.state.host = host
    app.state.config = config
    if config.dev:
        app.add_middleware(
            CORSMiddleware,
            allow_origins=["http://localhost:5173", "http://127.0.0.1:5173"],
            allow_methods=["*"],
            allow_headers=["*"],
        )

    # ----------------------------------------------------------------- info
    @app.get("/api/health", tags=["service"])
    def health() -> dict[str, Any]:
        return {"ok": True, "product": PRODUCT, "version": __version__, "phase": host.phase}

    @app.get("/api/info", tags=["service"])
    def info() -> dict[str, Any]:
        from .doctor import machine_summary

        return {
            "product": PRODUCT,
            "subtitle": SUBTITLE,
            "version": __version__,
            "builtBy": ATTRIBUTION,
            "basedOn": {
                "name": "RoboSimStudio",
                "license": "Apache-2.0",
                "url": "https://github.com/robosimstudio/robosimstudio",
                "note": "BRIDGE is built on the RoboSimStudio toolkit; its authors are credited in NOTICE and CITATION.cff.",
            },
            "startedAt": started_at,
            "machine": machine_summary(),
            "commandTarget": "simulation",
            "hardware": {
                "state": "disconnected",
                "detail": "No hardware connector is configured. Commands go to the simulation only.",
            },
        }

    @app.get("/api/status", tags=["service"])
    def status() -> dict[str, Any]:
        return host.status_snapshot or {"phase": host.phase}

    @app.get("/api/doctor", tags=["service"])
    def doctor() -> dict[str, Any]:
        from .doctor import run_checks

        return {"checks": run_checks()}

    @app.get("/api/licenses", tags=["service"])
    def licenses() -> dict[str, Any]:
        from .doctor import license_texts

        return license_texts()

    # -------------------------------------------------------------- scenarios
    @app.get("/api/scenarios", tags=["scenarios"])
    def scenarios() -> list[dict[str, Any]]:
        return [s.describe() for s in registry.all()]

    @app.get("/api/scenarios/{scenario_id}/media", tags=["scenarios"])
    def scenario_media(scenario_id: str):
        if scenario_id not in registry:
            raise HTTPException(404, f"no scenario {scenario_id!r}")
        media = registry.get(scenario_id).media
        path = (repo_root() / media).resolve() if media else None
        if path is None or not path.is_file() or repo_root() not in path.parents:
            raise HTTPException(404, "this scenario has no preview")
        return FileResponse(path, media_type=mimetypes.guess_type(path.name)[0] or "image/webp", headers=_cache(3600))

    # ------------------------------------------------------------------ scene
    @app.get("/api/scene/manifest", tags=["scene"])
    def manifest() -> dict[str, Any]:
        data = host.manifest()
        if data is None:
            raise HTTPException(404, "no scene is built yet")
        return data

    @app.get("/api/scene/geometry", tags=["scene"])
    def geometry(request: Request, gen: int | None = None) -> Response:
        generation, blob = host.geometry()
        if gen is not None and gen != generation:
            raise HTTPException(409, f"geometry is at generation {generation}, not {gen}")
        headers = {"X-Bridge-Generation": str(generation), **_cache(86400 if gen is not None else 0)}
        if "gzip" in request.headers.get("accept-encoding", "") and len(blob) > 1024:
            return Response(
                _gzipped(generation, blob),
                media_type="application/octet-stream",
                headers={**headers, "Content-Encoding": "gzip"},
            )
        return Response(blob, media_type="application/octet-stream", headers=headers)

    @app.get("/api/scene/state", tags=["scene"])
    def state() -> dict[str, Any]:
        """The latest published frame as JSON: what an agent reads instead of the binary stream."""
        from .frames import decode_frame

        _, frame = host.broadcast.frame_after(-1)
        if frame is None:
            raise HTTPException(404, "no frame has been published yet")
        header, arrays = decode_frame(frame)
        header.pop("buffers", None)
        header["bodyQ"] = arrays["body_q"].round(6).tolist()
        header["particleCount"] = int(arrays["particles"].shape[0])
        return header

    @app.get("/api/scene/texture/{key}", tags=["scene"])
    def texture(key: str):
        from robosimstudio.core.render.finish import texture_path

        if not re.fullmatch(r"[A-Za-z0-9_.-]{1,80}", key):
            raise HTTPException(400, "bad texture key")
        try:
            path = Path(texture_path(key))
        except Exception as exc:
            raise HTTPException(404, f"no texture {key!r}: {exc}") from None
        if not path.is_file():
            raise HTTPException(404, f"no texture {key!r}")
        return FileResponse(path, headers=_cache(86400))

    @app.post("/api/render", tags=["scene"])
    async def render(body: RenderBody) -> Response:
        """One picture of the scene through the library's renderer (path traced where the GPU can)."""

        def shoot():
            import imageio.v3 as iio

            from robosimstudio.specs import CameraSpec

            sim = host.sim
            if sim is None:
                raise ValueError("no scene is built yet")
            camera: Any = body.camera
            if camera is None and body.eye is not None and body.look_at is not None:
                camera = CameraSpec(
                    name="bridge_view",
                    width=int(body.width),
                    height=int(body.height),
                    fov_y=float(body.fov_y),
                    eye=tuple(body.eye),
                    look_at=tuple(body.look_at),
                    up=(0.0, 0.0, 1.0),
                    near=0.01,
                    far=10.0,
                )
            from robosimstudio.solvers.render import choose_renderer

            backend, why = choose_renderer(device=sim.model.device)
            started = time.perf_counter()
            frame = sim.render(camera)
            png = iio.imwrite("<bytes>", frame["rgb"], extension=".png")
            return png, backend, why, time.perf_counter() - started

        try:
            png, backend, why, seconds = await asyncio.to_thread(host.call, shoot)
        except Exception as exc:
            raise HTTPException(409, f"{type(exc).__name__}: {exc}") from None
        config.capture_dir.mkdir(parents=True, exist_ok=True)
        name = time.strftime("render-%Y%m%d-%H%M%S.png")
        (config.capture_dir / name).write_bytes(png)
        return Response(
            png,
            media_type="image/png",
            headers={
                "X-Bridge-Renderer": backend,
                "X-Bridge-Renderer-Why": why[:200],
                "X-Bridge-Seconds": f"{seconds:.2f}",
                "X-Bridge-Saved": name,
            },
        )

    # --------------------------------------------------------------- commands
    @app.get("/api/commands/catalog", tags=["commands"])
    def command_catalog() -> dict[str, Any]:
        from .catalog import COMMANDS

        return {"commands": COMMANDS}

    @app.post("/api/command", tags=["commands"])
    async def command(body: CommandBody, request: Request) -> dict[str, Any]:
        client = f"rest:{request.client.host if request.client else '?'}"
        return await asyncio.to_thread(host.execute, body.model_dump(), client=client)

    @app.get("/api/commands", tags=["commands"])
    def commands(limit: int = 200) -> list[dict[str, Any]]:
        return host.recent_commands(max(1, min(limit, 400)))

    @app.get("/api/events", tags=["commands"])
    def events(limit: int = 200) -> list[dict[str, Any]]:
        return host.recent_events(max(1, min(limit, 600)))

    # -------------------------------------------------------------- workcells
    @app.get("/api/workcell/catalog", tags=["workcells"])
    def workcell_catalog() -> dict[str, Any]:
        from .workcell import catalog

        return catalog()

    @app.get("/api/workcell/current", tags=["workcells"])
    def workcell_current() -> dict[str, Any]:
        from .workcell import default_workcell

        return host.document or default_workcell().model_dump(by_alias=True, mode="json")

    @app.post("/api/workcell/validate", tags=["workcells"])
    def workcell_validate(doc: dict[str, Any]) -> dict[str, Any]:
        from pydantic import ValidationError

        from .host import _validation_message
        from .workcell import WorkcellDoc

        try:
            WorkcellDoc.model_validate(doc)
        except ValidationError as exc:
            return {"ok": False, "message": _validation_message(exc)}
        return {"ok": True, "message": "valid"}

    @app.get("/api/workcells", tags=["workcells"])
    def workcells() -> list[dict[str, Any]]:
        folder = config.workcell_dir
        out = []
        if folder.is_dir():
            for path in sorted(folder.glob("*.json"), key=lambda p: p.stat().st_mtime, reverse=True):
                try:
                    doc = json.loads(path.read_text(encoding="utf-8"))
                except (OSError, ValueError):
                    continue
                out.append(
                    {
                        "slug": path.stem,
                        "name": doc.get("name", path.stem),
                        "description": doc.get("description", ""),
                        "robot": (doc.get("robot") or {}).get("model", ""),
                        "objects": len(doc.get("objects", [])),
                        "physics": doc.get("physics", ""),
                        "savedAt": doc.get("savedAt") or path.stat().st_mtime,
                    }
                )
        return out

    @app.get("/api/workcells/{slug}", tags=["workcells"])
    def workcell_get(slug: str) -> dict[str, Any]:
        path = _workcell_path(config, slug)
        if not path.is_file():
            raise HTTPException(404, f"no saved workcell {slug!r}")
        return json.loads(path.read_text(encoding="utf-8"))

    @app.put("/api/workcells/{slug}", tags=["workcells"])
    def workcell_put(slug: str, doc: dict[str, Any]) -> dict[str, Any]:
        from pydantic import ValidationError

        from .host import _validation_message
        from .workcell import WorkcellDoc

        try:
            clean = WorkcellDoc.model_validate(doc).model_dump(by_alias=True, mode="json")
        except ValidationError as exc:
            raise HTTPException(422, _validation_message(exc)) from None
        path = _workcell_path(config, slug)
        path.parent.mkdir(parents=True, exist_ok=True)
        clean["savedAt"] = time.time()
        clean["savedBy"] = f"BRIDGE {__version__}"
        tmp = path.with_suffix(".json.tmp")
        tmp.write_text(json.dumps(clean, indent=2), encoding="utf-8")
        tmp.replace(path)
        return {
            "ok": True,
            "slug": slug,
            "path": str(path.relative_to(repo_root())) if repo_root() in path.parents else str(path),
        }

    @app.delete("/api/workcells/{slug}", tags=["workcells"])
    def workcell_delete(slug: str) -> dict[str, Any]:
        path = _workcell_path(config, slug)
        if not path.is_file():
            raise HTTPException(404, f"no saved workcell {slug!r}")
        path.unlink()
        return {"ok": True}

    # -------------------------------------------------------------- reference
    from .reference import MAX_BYTES, ReferenceStore

    references = ReferenceStore(config.reference_dir)

    def _ref_errors(fn):
        try:
            return fn()
        except FileNotFoundError as exc:
            raise HTTPException(404, str(exc)) from None
        except ValueError as exc:
            raise HTTPException(400, str(exc)) from None

    @app.get("/api/reference", tags=["reference"])
    def reference_list() -> list[dict[str, Any]]:
        return references.list()

    @app.put("/api/reference/upload", tags=["reference"])
    async def reference_upload(request: Request, name: str) -> dict[str, Any]:
        """Stream a clip or photo in as the raw request body; ``name`` carries its file name (and so its type)."""
        incoming = _ref_errors(lambda: references.incoming(name))
        size = 0
        try:
            with incoming.open("wb") as out:
                async for chunk in request.stream():
                    size += len(chunk)
                    if size > MAX_BYTES:
                        raise HTTPException(413, "a reference is at most 1 GB")
                    out.write(chunk)
        except BaseException:
            incoming.unlink(missing_ok=True)
            raise
        if size == 0:
            incoming.unlink(missing_ok=True)
            raise HTTPException(400, "the upload was empty")
        return await asyncio.to_thread(lambda: _ref_errors(lambda: references.adopt(name, incoming)))

    @app.get("/api/reference/{ref_id}", tags=["reference"])
    def reference_get(ref_id: str) -> dict[str, Any]:
        return _ref_errors(lambda: references.meta(ref_id))

    @app.delete("/api/reference/{ref_id}", tags=["reference"])
    def reference_delete(ref_id: str) -> dict[str, Any]:
        _ref_errors(lambda: references.delete(ref_id))
        return {"ok": True}

    @app.get("/api/reference/{ref_id}/media", tags=["reference"])
    def reference_media(ref_id: str):
        path = _ref_errors(lambda: references.source(ref_id))
        return FileResponse(path, media_type=mimetypes.guess_type(path.name)[0] or "application/octet-stream")

    @app.post("/api/reference/{ref_id}/stills", tags=["reference"])
    async def reference_stills(ref_id: str, body: StillsBody) -> dict[str, Any]:
        return await asyncio.to_thread(lambda: _ref_errors(lambda: references.cut(ref_id, body.times, body.labels)))

    @app.get("/api/reference/{ref_id}/still/{file}", tags=["reference"])
    def reference_still(ref_id: str, file: str):
        return FileResponse(_ref_errors(lambda: references.still_path(ref_id, file)), headers=_cache(3600))

    @app.get("/api/reference/{ref_id}/artifact/{file}", tags=["reference"])
    def reference_artifact(ref_id: str, file: str):
        return FileResponse(_ref_errors(lambda: references.artifact_path(ref_id, file)), headers=_cache(3600))

    @app.post("/api/reference/{ref_id}/compare", tags=["reference"])
    async def reference_compare(ref_id: str, body: CompareBody) -> dict[str, Any]:
        def run():
            if host.sim is None:
                raise ValueError("no scene is built yet")
            return references.compare(host.sim, ref_id, body.still, body.view, body.keys)

        try:
            return await asyncio.to_thread(host.call, lambda: _ref_errors(run))
        except HTTPException:
            raise
        except Exception as exc:
            raise HTTPException(409, f"{type(exc).__name__}: {exc}") from None

    # ---------------------------------------------------------------- socket
    @app.websocket("/ws")
    async def socket(ws: WebSocket) -> None:
        await ws.accept()
        client = f"ws:{uuid.uuid4().hex[:8]}"
        loop = asyncio.get_running_loop()
        wake = asyncio.Event()

        def notify() -> None:
            loop.call_soon_threadsafe(wake.set)

        host.broadcast.subscribe(notify)
        message_seq = host.broadcast.seq
        frame_seq = 0
        await ws.send_text(
            json.dumps({"type": "hello", "client": client, "version": __version__, "status": host.status_snapshot})
        )

        async def receive() -> None:
            while True:
                text = await ws.receive_text()
                try:
                    message = json.loads(text)
                except ValueError:
                    continue
                kind = message.get("type")
                if kind == "command":
                    command = {"id": message.get("id"), "name": message.get("name"), "args": message.get("args") or {}}
                    ack = await asyncio.to_thread(host.execute, command, client=client)
                    if ack.get("duplicate"):
                        await ws.send_text(json.dumps({"type": "ack", **ack}))
                elif kind == "cursor":
                    host.post_cursor(
                        message.get("arm", ""),
                        message.get("position", (0, 0, 0)),
                        message.get("quat", (0, 0, 0, 1)),
                        str(message.get("phase", "update")),
                        client,
                    )
                elif kind == "ping":
                    await ws.send_text(json.dumps({"type": "pong", "t": message.get("t"), "server": time.time()}))

        receiver = asyncio.create_task(receive())
        try:
            while not receiver.done():
                with contextlib.suppress(TimeoutError):
                    await asyncio.wait_for(wake.wait(), timeout=1.0)
                wake.clear()
                message_seq, messages = host.broadcast.messages_after(message_seq)
                for text in messages:
                    await ws.send_text(text)
                frame_seq, frame = host.broadcast.frame_after(frame_seq)
                if frame is not None:
                    await ws.send_bytes(frame)
        except (WebSocketDisconnect, RuntimeError):
            pass
        finally:
            host.broadcast.unsubscribe(notify)
            receiver.cancel()
            try:
                await receiver
            except (asyncio.CancelledError, WebSocketDisconnect):
                pass
            except Exception as exc:
                # A message the service could not handle ends this socket and the page reconnects;
                # say why, so a reconnect with no visible cause leaves a trace.
                print(f"[bridge] socket {client} closed: {type(exc).__name__}: {exc}", flush=True)

    # ---------------------------------------------------------------- client
    static_dir = Path(config.static_dir)

    @app.get("/{path:path}", include_in_schema=False)
    def client(path: str):
        if path.startswith("api/") or path == "ws":
            raise HTTPException(404)
        if static_dir.is_dir():
            candidate = (static_dir / path).resolve()
            if path and candidate.is_file() and static_dir.resolve() in candidate.parents:
                cache = (
                    _cache(31536000)
                    if "/assets/" in f"/{path}"
                    else _cache(86400 if path.startswith("factory/") else 0)
                )
                return FileResponse(candidate, headers=cache)
            index = static_dir / "index.html"
            if index.is_file():
                return FileResponse(index, headers=_cache(0))
        return HTMLResponse(_NO_CLIENT, status_code=503)

    return app


_GZIP_CACHE: dict[int, bytes] = {}


def _gzipped(generation: int, blob: bytes) -> bytes:
    if generation not in _GZIP_CACHE:
        _GZIP_CACHE.clear()
        _GZIP_CACHE[generation] = gzip.compress(blob, compresslevel=5)
    return _GZIP_CACHE[generation]


def _cache(seconds: int) -> dict[str, str]:
    return {"Cache-Control": f"public, max-age={seconds}" if seconds else "no-cache"}


def _workcell_path(config: BridgeConfig, slug: str) -> Path:
    if not SLUG.match(slug):
        raise HTTPException(400, "a workcell slug is lowercase letters, digits, - and _ (64 at most)")
    return config.workcell_dir / f"{slug}.json"


_NO_CLIENT = """<!doctype html><html><head><meta charset="utf-8"><title>BRIDGE | Digital Twin Studio</title>
<style>body{font:15px/1.5 system-ui,sans-serif;background:#0e1114;color:#e6e9ec;display:grid;place-items:center;height:100vh;margin:0}
main{max-width:560px;padding:24px}code{background:#1b2026;padding:2px 6px}</style></head><body><main>
<h1>The BRIDGE client is not built</h1>
<p>The simulation service is running, but its browser client has not been built yet. From the repository root:</p>
<p><code>cd frontend &amp;&amp; npm install &amp;&amp; npm run build</code></p>
<p>Then reload this page. The API is up meanwhile: <a style="color:#ff5622" href="/docs">/docs</a>.</p>
</main></body></html>"""
