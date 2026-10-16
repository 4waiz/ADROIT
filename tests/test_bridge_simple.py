# SPDX-License-Identifier: LicenseRef-BRIDGE
"""BRIDGE's Simple mode, in a real browser against a real simulation.

A first-time visitor, as the page is shipped: the guided tour opens by itself and
walks its steps; the page shows the factory and one plain panel; the panel's
arrows and hand button move the simulated arm (read back from the simulator, not
from the page); a ready-made program meets the scene's goal; a program taught
step by step ("save this spot", "close the hand", ...) runs on the host; Expert
mode brings back every engineering panel; Arabic lays the page out right to left.

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
SHOTS = ROOT / "outputs" / "bridge" / "simple"


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


def _get(url: str, timeout: float = 10.0) -> dict:
    with urllib.request.urlopen(url, timeout=timeout) as response:
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
class TestBridgeSimpleMode(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        try:
            import playwright  # noqa: F401
        except ImportError:
            raise unittest.SkipTest("Playwright is not installed (uv sync --group media)") from None
        cls.port = _free_port()
        cls.base = f"http://127.0.0.1:{cls.port}"
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
                str(ROOT / "outputs" / "bridge" / "simple-data"),
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

    def state(self) -> dict:
        return _get(f"{self.base}/api/scene/state")

    def status(self) -> dict:
        return _get(f"{self.base}/api/status")

    def wait_until(self, check, timeout: float = 20.0, what: str = "condition") -> None:
        deadline = time.time() + timeout
        last = None
        while time.time() < deadline:
            try:
                last = check()
                if last:
                    return
            except Exception as exc:
                last = exc
            time.sleep(0.25)
        self.fail(f"timed out waiting for {what} (last: {last!r})")

    def test_simple_mode(self) -> None:
        from playwright.sync_api import sync_playwright

        with sync_playwright() as p:
            browser = _launch(p)
            context = browser.new_context(viewport={"width": 1920, "height": 1080})
            context.add_init_script(
                "if (!sessionStorage.getItem('bridge.test')) { localStorage.clear(); sessionStorage.setItem('bridge.test', '1'); }"
            )
            page = context.new_page()
            errors: list[str] = []
            page.on("pageerror", lambda e: errors.append(str(e)))
            page.goto(self.base, wait_until="networkidle")
            page.wait_for_function(
                "window.__BRIDGE__ && window.__BRIDGE__.manifest() && window.__BRIDGE__.frame()", timeout=90000
            )

            # 1. A first visit opens the tour by itself; it walks every step and remembers it was seen.
            page.wait_for_selector(".tour-card", timeout=30000)
            steps = 0
            while page.locator(".tour").count():
                steps += 1
                page.locator(".tour-foot .btn.primary").click()
                time.sleep(0.35)
                self.assertLess(steps, 30, "the tour does not end")
            self.assertGreaterEqual(steps, 10)
            self.assertTrue(page.evaluate("JSON.parse(localStorage.getItem('bridge.prefs')).tutorialSeen"))

            # 2. Simple mode: the factory and one plain panel; no engineering dock, no scene tree.
            self.assertTrue(page.locator(".simple-panel").is_visible())
            self.assertEqual(page.locator(".dock").count(), 0)
            self.assertEqual(page.locator(".panel.left").count(), 0)
            self.wait_until(lambda: page.evaluate("window.__BRIDGE__.factory()") is not None, 60, "the factory")
            page.screenshot(path=str(SHOTS / "01-simple.png"))

            # 3. The arrows move the simulated hand: "Up" at the medium step is 5 cm.
            z0 = self.state()["arms"]["right"]["ee"][2]
            page.get_by_role("button", name="Up", exact=True).click()
            self.wait_until(lambda: self.state()["arms"]["right"]["ee"][2] - z0 > 0.04, 15, "the hand to rise 5 cm")
            xy0 = self.state()["arms"]["right"]["ee"][:2]
            page.get_by_role("button", name="Left", exact=True).click()
            self.wait_until(
                lambda: (
                    sum((a - b) ** 2 for a, b in zip(self.state()["arms"]["right"]["ee"][:2], xy0, strict=True)) ** 0.5
                    > 0.04
                ),
                15,
                "the hand to move 5 cm sideways",
            )

            # 4. The hand closes and opens, read back as the measured opening.
            page.get_by_role("button", name="Close the hand").click()
            self.wait_until(lambda: self.state()["arms"]["right"]["grip"]["width"] < 0.005, 10, "the hand to close")
            page.get_by_role("button", name="Open the hand").click()
            self.wait_until(lambda: self.state()["arms"]["right"]["grip"]["width"] > 0.07, 10, "the hand to open")

            # 5. A ready-made program does the whole job and meets the scene's goal.
            page.get_by_role("button", name="Start over").click()
            self.wait_until(lambda: self.state()["t"] < 0.5, 10, "the restart")
            page.locator(".s-program", has_text="Pick and place").click()
            self.wait_until(lambda: self.status().get("runner") is not None, 10, "the program to start")
            time.sleep(4.0)
            page.screenshot(path=str(SHOTS / "02-program.png"))
            self.wait_until(
                lambda: bool((self.status().get("success") or {}).get("done")), 150, "the cube to rest in the zone"
            )
            self.wait_until(lambda: page.locator(".s-goal.met").count() == 1, 10, "the goal to show as reached")

            # 6. A taught program: save the hand's spot, close, wait, go home; it runs on the host.
            page.get_by_role("button", name="Start over").click()
            self.wait_until(lambda: self.status().get("runner") is None and self.state()["t"] < 0.5, 20, "the restart")
            page.get_by_role("button", name="Save this spot").click()
            page.get_by_role("button", name="Close hand", exact=True).click()
            page.get_by_role("button", name="Wait", exact=True).click()
            page.get_by_role("button", name="Go home", exact=True).click()
            self.assertEqual(page.locator(".s-steps li").count(), 4)
            page.get_by_role("button", name="Run my program").click()
            self.wait_until(lambda: self.status().get("runner") is not None, 10, "my program to start")
            self.wait_until(
                lambda: (self.status().get("lastRunner") or {}).get("status") == "completed", 60, "my program to finish"
            )
            last = self.status()["lastRunner"]
            self.assertEqual(len(last["plan"]), 4)
            self.assertTrue(all(step["status"] == "done" for step in last["plan"]))

            # 7. Expert mode brings every engineering panel back; Simple returns to the plain panel.
            page.get_by_role("button", name="Expert", exact=True).click()
            page.wait_for_selector(".panel.left", timeout=5000)
            self.assertTrue(page.locator(".dock").count() == 1)
            page.get_by_role("button", name="Simple", exact=True).click()
            page.wait_for_selector(".simple-panel", timeout=5000)

            # 8. Arabic: the page turns right to left and the panel speaks Arabic.
            page.get_by_role("button", name="العربية").click()
            self.wait_until(lambda: page.evaluate("document.documentElement.dir") == "rtl", 5, "right to left")
            self.assertIn("حرّك الروبوت", page.locator(".simple-panel").inner_text())
            page.screenshot(path=str(SHOTS / "03-arabic.png"))

            self.assertEqual(errors, [], f"the page raised: {errors}")
            browser.close()


if __name__ == "__main__":
    unittest.main()
