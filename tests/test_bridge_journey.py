# SPDX-License-Identifier: Apache-2.0
"""BRIDGE's acceptance journey, in a real browser against a real simulation.

Starts ``python -m bridge_studio`` on a spare port, opens the page in a headless
Chromium (the system's Edge or Chrome when Playwright has no browser of its own),
and walks the operator's path: the page loads, a workcell appears, the camera
orbits and zooms, the robot is selected and its joints listed, a joint slider
moves the simulated joint, the end effector jogs, the gripper opens and closes,
the robot picks the cube up, the simulation pauses, steps exactly one control
period, resets, the telemetry agrees with the simulator, a workcell is saved and
reopened, the About page names the author with the right link, and the BRIDGE
wordmark is the one for the theme.

Every assertion reads the simulator's own state (``GET /api/scene/state`` and the
page's read-only ``window.__BRIDGE__``), never a widget's echo of what was typed.
Screenshots go to ``outputs/bridge/journey/``.

Needs a CUDA GPU, the built client (``npm run build`` in ``frontend/``) and the
``media`` dependency group (Playwright).
"""

from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
import unittest
import urllib.request
from pathlib import Path

TIER = "slow"

ROOT = Path(__file__).resolve().parent.parent
SHOTS = ROOT / "outputs" / "bridge" / "journey"


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


def _get(url: str, timeout: float = 10.0) -> dict:
    with urllib.request.urlopen(url, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def _post(url: str, body: dict, timeout: float = 30.0) -> dict:
    request = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def _launch(playwright):
    for channel in ("msedge", "chrome", None):
        try:
            kwargs = {"headless": True, "args": ["--ignore-gpu-blocklist", "--enable-gpu"]}
            if channel:
                kwargs["channel"] = channel
            return playwright.chromium.launch(**kwargs)
        except Exception:
            continue
    raise unittest.SkipTest("no Chromium-family browser is available to Playwright")


@unittest.skipUnless((ROOT / "bridge_studio" / "static" / "index.html").is_file(), "the client is not built")
class TestBridgeJourney(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        try:
            import playwright  # noqa: F401
        except ImportError:
            raise unittest.SkipTest("Playwright is not installed (uv sync --group media)") from None
        cls.port = _free_port()
        cls.base = f"http://127.0.0.1:{cls.port}"
        cls.data = ROOT / "outputs" / "bridge" / "journey-data"
        env = dict(os.environ, PYTHONUNBUFFERED="1")
        cls.server = subprocess.Popen(
            [
                sys.executable,
                "-m",
                "bridge_studio",
                "--port",
                str(cls.port),
                "--scenario",
                "pick_place",
                "--data-dir",
                str(cls.data),
            ],
            cwd=ROOT,
            env=env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        deadline = time.time() + 600
        while time.time() < deadline:
            try:
                status = _get(f"{cls.base}/api/status", timeout=2)
                if status.get("phase") == "ready" and status.get("generation", 0) >= 1:
                    break
            except Exception:
                pass
            time.sleep(1.0)
        else:
            cls.server.kill()
            raise RuntimeError("the BRIDGE service did not build its first scenario in 10 minutes")
        SHOTS.mkdir(parents=True, exist_ok=True)

    @classmethod
    def tearDownClass(cls) -> None:
        cls.server.terminate()
        try:
            cls.server.wait(15)
        except subprocess.TimeoutExpired:
            cls.server.kill()

    # ---------------------------------------------------------------- helpers
    def state(self) -> dict:
        return _get(f"{self.base}/api/scene/state")

    def arm(self) -> dict:
        return self.state()["arms"]["right"]

    def wait_until(self, check, timeout: float = 20.0, what: str = "condition") -> None:
        deadline = time.time() + timeout
        last = None
        while time.time() < deadline:
            try:
                last = check()
                if last:
                    return
            except Exception as exc:  # a transient read during a rebuild
                last = exc
            time.sleep(0.25)
        self.fail(f"timed out waiting for {what} (last: {last!r})")

    def shot(self, page, name: str) -> None:
        page.screenshot(path=str(SHOTS / f"{name}.png"))

    # ---------------------------------------------------------------- journey
    def test_journey(self) -> None:
        from playwright.sync_api import sync_playwright

        with sync_playwright() as p:
            browser = _launch(p)
            context = browser.new_context(viewport={"width": 1920, "height": 1080})
            page = context.new_page()
            errors: list[str] = []
            page.on("pageerror", lambda e: errors.append(str(e)))
            page.goto(self.base, wait_until="networkidle")

            # 1. The BRIDGE interface loads.
            page.wait_for_function(
                "window.__BRIDGE__ && window.__BRIDGE__.state().connection === 'open'", timeout=30000
            )
            self.assertIn("BRIDGE", page.title())

            # 2. A real simulated workcell appears: the manifest is loaded and frames arrive.
            page.wait_for_function("window.__BRIDGE__.manifest() && window.__BRIDGE__.frame()", timeout=60000)
            manifest = page.evaluate("window.__BRIDGE__.manifest()")
            self.assertEqual(manifest["arms"][0]["joints"], 7)
            self.assertIn("object:cube", manifest["subjects"])
            time.sleep(1.5)
            self.shot(page, "01-loaded")

            # 3. Orbit and zoom: dragging and scrolling on the canvas move the camera.
            canvas = page.locator(".viewport canvas")
            box = canvas.bounding_box()
            before = page.evaluate("window.__BRIDGE__.camera()")
            cx, cy = box["x"] + box["width"] * 0.7, box["y"] + box["height"] * 0.3
            page.mouse.move(cx, cy)
            page.mouse.down()
            page.mouse.move(cx + 140, cy + 30, steps=8)
            page.mouse.up()
            page.mouse.wheel(0, -400)
            time.sleep(0.8)
            after = page.evaluate("window.__BRIDGE__.camera()")
            moved = sum((a - b) ** 2 for a, b in zip(after["eye"], before["eye"], strict=True)) ** 0.5
            self.assertGreater(moved, 0.05, "the camera did not move on orbit and zoom")
            page.keyboard.press("1")  # back to the three-quarter view

            # 4. Select the robot (the tree row; a click on a link selects the same subject).
            page.get_by_role("treeitem", name="Arm · right").click()
            page.wait_for_function("window.__BRIDGE__.state().selected === 'arm:right'")

            # 5. Its actual joints are in the inspector.
            for k in range(1, 8):
                self.assertTrue(page.get_by_label(f"Joint {k} target value").is_visible())

            # 6. A joint slider target changes the simulated joint.
            field = page.get_by_label("Joint 1 target value")
            field.fill("25")
            field.press("Enter")
            self.wait_until(lambda: abs(self.arm()["q"][0] - 0.43633) < 0.01, 15, "joint 1 to reach 25 degrees")
            self.assertEqual(self.arm()["mode"], "joint")
            self.shot(page, "02-joint")

            # 7. The end effector moves: Hold here, then jog +Z by 50 mm.
            page.get_by_role("button", name="Hold here").click()
            self.wait_until(lambda: self.arm()["mode"] == "cartesian", 10, "Cartesian mode")
            z0 = self.arm()["ee"][2]
            page.get_by_label("Jog step size", exact=True).select_option(label="50 mm")
            page.get_by_role("button", name="+Z", exact=True).click()
            self.wait_until(lambda: self.arm()["ee"][2] - z0 > 0.04, 15, "the tool centre to rise 50 mm")
            self.shot(page, "03-jog")

            # 8. The gripper closes and opens; the measured opening follows.
            page.get_by_role("button", name="Close", exact=True).click()
            self.wait_until(lambda: self.arm()["grip"]["width"] < 0.005, 10, "the gripper to close")
            page.get_by_role("button", name="Open", exact=True).click()
            self.wait_until(lambda: self.arm()["grip"]["width"] > 0.07, 10, "the gripper to open")

            # 9. The robot handles the cube: reset, then run the scenario's pick-and-place routine.
            page.get_by_role("button", name="Reset to start state").click()
            self.wait_until(lambda: self.state()["t"] < 0.5, 10, "the reset")
            page.get_by_role("tab", name="Tasks").click()
            page.get_by_role("button", name="Pick and place").click()
            self.wait_until(
                lambda: _get(f"{self.base}/api/status").get("runner") is not None, 10, "the routine to start"
            )
            time.sleep(5.0)
            self.shot(page, "04-routine")
            self.wait_until(
                lambda: (_get(f"{self.base}/api/status").get("success") or {}).get("done") is True,
                120,
                "the cube to rest in the place zone",
            )
            self.shot(page, "05-placed")

            # 10. Pause holds the simulation.
            page.get_by_role("button", name="Pause").click()
            self.wait_until(lambda: self.state()["playing"] is False, 10, "pause")
            t_paused = self.state()["t"]
            time.sleep(1.0)
            self.assertAlmostEqual(self.state()["t"], t_paused, places=6)

            # 11. Step advances exactly one control period (50 Hz: 20 ms).
            page.get_by_role("button", name="Step one control period").click()
            self.wait_until(lambda: self.state()["t"] > t_paused, 10, "one step")
            self.assertAlmostEqual(self.state()["t"] - t_paused, 0.02, places=6)

            # 12. Reset restores the start state: time zero and the cube back on its start spot.
            page.get_by_role("button", name="Reset to start state").click()
            self.wait_until(lambda: self.state()["t"] == 0.0, 10, "time zero")
            status = _get(f"{self.base}/api/status")
            self.assertFalse((status.get("success") or {}).get("done", False), "the reset left the cube in the zone")

            # 13. Telemetry reflects the simulator: the page's newest frame matches the service's.
            page.get_by_role("button", name="Play", exact=True).click()
            time.sleep(1.0)
            page_q = page.evaluate("window.__BRIDGE__.frame().arms.right.q")
            service_q = self.arm()["q"]
            for a, b in zip(page_q, service_q, strict=True):
                self.assertAlmostEqual(a, b, delta=0.02)

            # 14. Save a workcell: build the default document with an extra box, then save it.
            page.get_by_role("tab", name="Builder").click()
            page.get_by_role("button", name="Box add").click()
            page.get_by_role("button", name="Build workcell").click()
            self.wait_until(
                lambda: _get(f"{self.base}/api/status").get("generation", 0) >= 2, 180, "the workcell build"
            )
            self.wait_until(lambda: _get(f"{self.base}/api/status").get("phase") == "ready", 60, "ready")
            page.keyboard.press("Control+s")
            page.get_by_label("Name").last.fill("Journey cell")
            page.get_by_role("button", name="Save", exact=True).click()
            saved = self.data / "workcells" / "journey-cell.json"
            self.wait_until(saved.is_file, 10, "the saved workcell file")
            doc = json.loads(saved.read_text(encoding="utf-8"))
            self.assertEqual(doc["schema"], "bridge.workcell/1")
            self.assertEqual(len(doc["objects"]), 2)

            # 15. Reopen it: a load rebuilds the same workcell.
            generation = _get(f"{self.base}/api/status")["generation"]
            page.keyboard.press("Control+o")
            page.get_by_role("row", name="Journey cell").get_by_role("button", name="Open").click()
            self.wait_until(
                lambda: _get(f"{self.base}/api/status").get("generation", 0) > generation, 180, "the reload"
            )
            self.wait_until(lambda: _get(f"{self.base}/api/status").get("phase") == "ready", 60, "ready")
            reopened = _get(f"{self.base}/api/scene/manifest")
            names = {s["name"] for s in reopened["subjects"]}
            self.assertTrue({o["name"] for o in doc["objects"]} <= names)
            self.shot(page, "06-reopened")

            # 16-17. About names the author and links to the right page, opening a new tab safely.
            page.get_by_role("button", name="About and licences").click()
            link = page.get_by_role("link", name="Built by Awaiz Ahmed").first
            self.assertEqual(link.get_attribute("href"), "https://kanbanstudios.ae/team-kanban")
            self.assertEqual(link.get_attribute("target"), "_blank")
            self.assertIn("noopener", link.get_attribute("rel") or "")
            self.shot(page, "07-about")
            page.keyboard.press("Escape")

            # 18. The wordmark matches the theme: light.png on dark, dark.png on light.
            logo = page.locator("img.brand-logo")
            self.assertIn("light", logo.get_attribute("src"))
            page.get_by_role("button", name="Light mode").click()
            page.wait_for_function("window.__BRIDGE__.state().theme === 'light'")
            self.assertIn("dark", logo.get_attribute("src"))
            time.sleep(1.0)
            self.shot(page, "08-light")
            page.get_by_role("button", name="Dark mode").click()

            self.assertEqual(errors, [], f"the page raised: {errors}")
            browser.close()


if __name__ == "__main__":
    unittest.main()
