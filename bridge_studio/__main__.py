# SPDX-License-Identifier: Apache-2.0
"""``python -m bridge_studio``: serve BRIDGE, or check what this machine can run.

::

    uv run python -m bridge_studio                     # http://127.0.0.1:8800, the pick-and-place cell
    uv run python -m bridge_studio --scenario dual_arm  # start on another scenario
    uv run python -m bridge_studio doctor              # Python, GPU, solvers, renderer, client build
"""

from __future__ import annotations

import argparse
import os
import sys
import traceback
import webbrowser

from . import PRODUCT, __version__
from .config import DEFAULT_PORT, BridgeConfig


def _serve(args: argparse.Namespace) -> int:
    import uvicorn

    from .server import create_app

    config = BridgeConfig(
        host=args.host,
        port=args.port,
        scenario="" if args.scenario == "none" else args.scenario,
        frame_hz=args.frame_hz,
        dev=args.dev,
        autoplay=not args.paused,
        **({"data_dir": args.data_dir} if args.data_dir else {}),
    )
    url = f"http://{'127.0.0.1' if args.host in ('0.0.0.0', '::') else args.host}:{args.port}"
    print(f"[bridge] {PRODUCT} {__version__}", flush=True)
    print(f"[bridge] serving {url}  (API reference: {url}/docs)", flush=True)
    if args.host not in ("127.0.0.1", "localhost", "::1"):
        print(
            f"[bridge] listening on {args.host}: anyone who can reach port {args.port} can drive the scene", flush=True
        )
    if args.open:
        webbrowser.open(url)
    code = 0
    try:
        uvicorn.run(create_app(config), host=args.host, port=args.port, log_level="warning", ws_ping_interval=20.0)
    except KeyboardInterrupt:
        pass  # Ctrl+C: uvicorn has shut the app down already, then re-raised the signal.
    except SystemExit as exc:  # uvicorn's own exit, such as a port already taken
        code = exc.code if isinstance(exc.code, int) else 1
    except BaseException:
        traceback.print_exc()
        code = 1
    # The app's shutdown has stopped the host and released the scene by now. A native solver
    # library (a co-simulated FEM engine) can leave a thread the interpreter would wait on
    # forever at exit, so the process ends here rather than in finalisation.
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)


def _doctor(_args: argparse.Namespace) -> int:
    from .doctor import run_checks

    worst = 0
    marks = {"ok": "OK  ", "warn": "WARN", "fail": "FAIL"}
    for check in run_checks():
        print(f"[{marks[check['status']]}] {check['name']}: {check['detail']}")
        if check.get("fix"):
            print(f"        -> {check['fix']}")
        worst = max(worst, {"ok": 0, "warn": 1, "fail": 2}[check["status"]])
    return 1 if worst == 2 else 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="bridge_studio", description=f"{PRODUCT} {__version__}")
    sub = parser.add_subparsers(dest="command")
    serve = sub.add_parser("serve", help="serve the studio (the default)")
    for p in (parser, serve):
        p.add_argument("--host", default="127.0.0.1", help="interface to bind (default 127.0.0.1)")
        p.add_argument("--port", type=int, default=DEFAULT_PORT)
        p.add_argument("--scenario", default="pick_place", help="scenario built at start, or 'none'")
        p.add_argument("--frame-hz", type=float, default=60.0, help="most frames a second sent to the browser")
        p.add_argument("--paused", action="store_true", help="build the first scenario paused")
        p.add_argument("--dev", action="store_true", help="allow the Vite dev server (localhost:5173)")
        p.add_argument("--open", action="store_true", help="open the page in a browser")
        p.add_argument(
            "--data-dir", default="", help="where saved workcells, references and captures go (default outputs/bridge)"
        )
    sub.add_parser("doctor", help="check Python, GPU, solvers, renderer and the client build")
    args = parser.parse_args(argv)
    if args.command == "doctor":
        return _doctor(args)
    return _serve(args)


if __name__ == "__main__":
    sys.exit(main())
