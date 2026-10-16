# SPDX-License-Identifier: LicenseRef-BRIDGE
"""BRIDGE's service, checked without a GPU: the wire formats, the document rules, the contracts.

* The frame format round-trips and never carries a NaN as JSON.
* A workcell document is refused, with its reason, when it asks a physics
  preset for something the preset cannot simulate, repeats a name, or is out
  of range -- and the default document passes.
* Every command the host answers is documented in the catalog, and the reverse.
* The manifest's shape-flag bits are Newton's; a demo scenario rebuilt reuses its modules.
* The English and Arabic dictionaries have the same keys and placeholders.
* A task is compiled against the scene it would run on, and refused by step.
* The reference store refuses what it cannot read and adopts a photo.
* The REST API serves the gallery, the builder's catalog, validation, and a
  saved workcell's round trip, over a host that never starts a simulation.
"""

from __future__ import annotations

import json
import math
import re
import tempfile
import types
import unittest
from pathlib import Path

import numpy as np

TIER = "fast"

ROOT = Path(__file__).resolve().parent.parent


class TestFrames(unittest.TestCase):
    def test_round_trip(self) -> None:
        from bridge_studio.frames import decode_frame, encode_frame

        body_q = np.arange(21, dtype=np.float32).reshape(3, 7)
        particles = np.linspace(0, 1, 12, dtype=np.float32).reshape(4, 3)
        frame = encode_frame({"type": "frame", "step": 7}, [("body_q", body_q), ("particles", particles)])
        self.assertEqual(frame[:4], b"BRF1")
        self.assertEqual(len(frame) % 4, 0)
        header, arrays = decode_frame(frame)
        self.assertEqual(header["step"], 7)
        np.testing.assert_array_equal(arrays["body_q"], body_q)
        np.testing.assert_array_equal(arrays["particles"], particles)

    def test_nan_is_no_reading(self) -> None:
        from bridge_studio.frames import decode_frame, encode_frame, sanitize

        self.assertEqual(sanitize({"a": [1.0, math.nan, math.inf]}), {"a": [1.0, None, None]})
        header, _ = decode_frame(encode_frame({"q": [math.nan, 0.5]}, []))
        self.assertEqual(header["q"], [None, 0.5])


class TestWorkcell(unittest.TestCase):
    def doc(self, **changes):
        from bridge_studio.workcell import default_workcell

        data = default_workcell().model_dump(by_alias=True, mode="json")
        data.update(changes)
        return data

    def refused(self, data) -> str:
        from pydantic import ValidationError

        from bridge_studio.workcell import WorkcellDoc

        with self.assertRaises(ValidationError) as caught:
            WorkcellDoc.model_validate(data)
        return str(caught.exception)

    def test_default_is_valid_and_versioned(self) -> None:
        from bridge_studio.workcell import SCHEMA, WorkcellDoc

        doc = WorkcellDoc.model_validate(self.doc())
        self.assertEqual(doc.model_dump(by_alias=True)["schema"], SCHEMA)
        again = WorkcellDoc.model_validate(json.loads(json.dumps(doc.model_dump(by_alias=True, mode="json"))))
        self.assertEqual(again, doc)

    def test_preset_refuses_what_it_cannot_simulate(self) -> None:
        sheet = {"kind": "sheet", "name": "towel", "at": [0, 0, 0.01]}
        message = self.refused(self.doc(objects=[sheet]))
        self.assertIn("cannot simulate", message)
        self.assertIn("sheet", message)
        rope = {"kind": "rope", "name": "cable", "at": [0, 0, 0.02]}
        self.assertIn("cannot simulate", self.refused(self.doc(physics="cloth_grasp", objects=[rope])))

    def test_names_and_ranges(self) -> None:
        cube = {"kind": "box", "name": "cube", "at": [0, 0, 0.03]}
        self.assertIn("unique", self.refused(self.doc(objects=[cube, dict(cube)])))
        self.assertIn("half", self.refused(self.doc(objects=[{**cube, "half": [5, 5, 5]}])))
        self.assertIn("name", self.refused(self.doc(objects=[{**cube, "name": "2cube"}])))
        self.assertIn("finite", self.refused(self.doc(objects=[{**cube, "at": [float("nan"), 0, 0]}])))
        self.assertIn("extra", self.refused(self.doc(objects=[{**cube, "script": "import os"}])).lower())
        thick = {"kind": "rope", "name": "rope", "length": 0.1, "segments": 20, "radius": 0.01}
        self.assertIn("spacing", self.refused(self.doc(physics="deformable", objects=[thick])))

    def test_needs_something_that_moves(self) -> None:
        slab = {"kind": "slab", "name": "plate", "at": [0, 0, 0.01]}
        self.assertIn("moves", self.refused(self.doc(robot=None, objects=[slab])))

    def test_unknown_robot_and_cell(self) -> None:
        self.assertIn("registered robot", self.refused(self.doc(robot={"model": "not_a_robot"})))
        self.assertIn("registered cell", self.refused(self.doc(cell="the_moon")))

    def test_catalog_is_consistent(self) -> None:
        from bridge_studio.workcell import KINDS, PRESETS, catalog

        offered = {kind for preset in PRESETS.values() for kind in preset["kinds"]}
        self.assertEqual(offered, set(KINDS), "every kind is simulated by some preset, and only known kinds are")
        cat = catalog()
        self.assertIn("panda", cat["robots"])
        self.assertIn("bench_075", cat["cells"])
        self.assertEqual(set(cat["cellFrames"]), set(cat["cells"]))

    def test_rpy_quaternion(self) -> None:
        from bridge_studio.workcell import _quat

        np.testing.assert_allclose(_quat((0, 0, 0)), (0, 0, 0, 1), atol=1e-12)
        x, y, z, w = _quat((0, 0, 90))
        np.testing.assert_allclose((x, y, z, w), (0, 0, math.sin(math.pi / 4), math.cos(math.pi / 4)), atol=1e-12)


class TestContracts(unittest.TestCase):
    def test_every_command_is_documented(self) -> None:
        from bridge_studio.catalog import COMMANDS
        from bridge_studio.config import BridgeConfig
        from bridge_studio.host import SimHost
        from bridge_studio.scenarios import builtin_registry

        host = SimHost(BridgeConfig(scenario=""), builtin_registry())
        documented = {c["name"] for c in COMMANDS}
        self.assertEqual(documented, set(host.commands))

    def test_unknown_and_malformed_commands_are_refused_without_a_simulator(self) -> None:
        from bridge_studio.config import BridgeConfig
        from bridge_studio.host import SimHost
        from bridge_studio.scenarios import builtin_registry

        host = SimHost(BridgeConfig(scenario=""), builtin_registry())
        self.assertEqual(host.execute({"id": "a", "name": "rm -rf", "args": {}})["status"], "rejected")
        self.assertEqual(host.execute({"id": "", "name": "sim.play"})["status"], "rejected")
        self.assertEqual(host.execute({"id": "b", "name": "sim.play", "args": [1, 2]})["status"], "rejected")
        # The same id is answered with its first outcome, not run again.
        first = host.execute({"id": "a", "name": "rm -rf", "args": {}})
        self.assertTrue(first.get("duplicate"))

    def test_shape_flags_are_newtons(self) -> None:
        import newton

        from bridge_studio import manifest

        self.assertEqual(manifest._VISIBLE, int(newton.ShapeFlags.VISIBLE))
        self.assertEqual(manifest._COLLIDE_SHAPES, int(newton.ShapeFlags.COLLIDE_SHAPES))
        self.assertEqual(manifest._SITE, int(newton.ShapeFlags.SITE))
        for name, value in (("BOX", 7), ("MESH", 8), ("CONVEX_MESH", 10), ("CAPSULE", 4), ("PLANE", 1)):
            self.assertEqual(int(getattr(newton.GeoType, name)), value)

    def test_scenarios_are_described(self) -> None:
        from bridge_studio.scenarios import builtin_registry

        ids = [s.id for s in builtin_registry().all()]
        self.assertEqual(len(ids), len(set(ids)))
        for required in ("pick_place", "surface_wiping", "cloth_fold", "cable_routing", "dual_arm"):
            self.assertIn(required, ids)
        for scenario in builtin_registry().all():
            info = scenario.describe()
            self.assertTrue(info["title"] and info["summary"] and info["physics"] and info["successLabel"])
            if scenario.media:
                self.assertTrue((ROOT / scenario.media).is_file(), scenario.media)
            for folder in ("wipe_the_bar", "fold_the_towel", "route_the_cable"):
                if folder in scenario.source:
                    self.assertTrue((ROOT / scenario.source).is_dir())

    def test_a_demo_is_imported_once_and_reused_on_a_rebuild(self) -> None:
        import sys

        from bridge_studio.scenarios import _demo_module

        # A rebuilt scenario asks for its demo's modules again: it gets the same ones, and the
        # neighbour the demo imports by bare name (``import board``) is that same module too.
        first = _demo_module("route_the_cable", "route_the_cable")
        self.assertIs(_demo_module("route_the_cable", "route_the_cable"), first)
        self.assertIs(_demo_module("route_the_cable", "board"), first.board)
        self.assertIs(sys.modules["board"], first.board)


class TestTranslations(unittest.TestCase):
    PAIR = re.compile(r'^\s*"([^"]+)":\s*"((?:[^"\\]|\\.)*)",?\s*$', re.M)

    def table(self, name: str) -> dict[str, str]:
        text = (ROOT / "frontend" / "src" / "i18n" / f"{name}.ts").read_text(encoding="utf-8")
        return dict(self.PAIR.findall(text))

    def test_same_keys_and_placeholders(self) -> None:
        en, ar = self.table("en"), self.table("ar")
        self.assertGreater(len(en), 400)
        self.assertEqual(set(en) - set(ar), set(), "Arabic lacks these keys")
        self.assertEqual(set(ar) - set(en), set(), "Arabic has keys English does not")
        holes = re.compile(r"\{(\w+)\}")
        for key in en:
            self.assertEqual(sorted(holes.findall(en[key])), sorted(holes.findall(ar[key])), key)
            self.assertTrue(ar[key].strip(), f"{key} is empty in Arabic")

    def test_attribution(self) -> None:
        en = self.table("en")
        self.assertEqual(en["about.builtBy"], "Built by Awaiz Ahmed")
        source = (ROOT / "frontend" / "src" / "components" / "Dialogs.tsx").read_text(encoding="utf-8")
        self.assertIn('"https://kanbanstudios.ae/team-kanban"', source)
        self.assertIn('rel="noopener noreferrer"', source)


class _FakeSim:
    def __init__(self) -> None:
        self.scene = types.SimpleNamespace(handles=types.SimpleNamespace(regions={"place_zone": object()}))

    def names(self) -> list[str]:
        return ["cube", "table", "place_zone"]


class TestTasks(unittest.TestCase):
    def host(self):
        arm = types.SimpleNamespace(gripper=object())
        return types.SimpleNamespace(sim=_FakeSim(), arms={"right": arm})

    def test_compiles_a_pick_and_place(self) -> None:
        from bridge_studio.tasks import compile_task

        steps = [
            {"op": "home", "arm": "right"},
            {"op": "grip", "arm": "right", "value": 0},
            {
                "op": "move",
                "arm": "right",
                "target": {"kind": "object", "name": "cube", "offset": [0, 0, 0.1]},
                "orientation": "grasp",
            },
            {"op": "move", "arm": "right", "target": {"kind": "region", "name": "place_zone", "height": 0.1}},
            {"op": "wait", "seconds": 0.5},
        ]
        plan, make, arms = compile_task(self.host(), steps)
        self.assertEqual(arms, ("right",))
        self.assertEqual([s["status"] for s in plan], ["pending"] * 5)
        self.assertTrue(callable(make))

    def test_refuses_by_step(self) -> None:
        from bridge_studio.tasks import TaskError, compile_task

        host = self.host()
        for bad, word in (
            ([{"op": "fly"}], "op"),
            ([{"op": "home", "arm": "left"}], "no arm"),
            ([{"op": "move", "arm": "right", "target": {"kind": "object", "name": "ghost"}}], "no object"),
            ([{"op": "move", "arm": "right", "target": {"kind": "region", "name": "nowhere"}}], "no region"),
            ([{"op": "grip", "arm": "right", "value": 3}], "between 0"),
            ([{"op": "move", "arm": "right", "target": {"kind": "relative", "offset": [2, 0, 0]}}], "at most"),
            (
                [
                    {
                        "op": "move",
                        "arm": "right",
                        "target": {"kind": "region", "name": "place_zone"},
                        "orientation": "grasp",
                    }
                ],
                "grasp",
            ),
            ([], "non-empty"),
        ):
            with self.assertRaises(TaskError) as caught:
                compile_task(host, bad)
            self.assertIn(word, str(caught.exception))


class TestReference(unittest.TestCase):
    def test_refuses_unknown_types_and_adopts_a_photo(self) -> None:
        from PIL import Image

        from bridge_studio.reference import ReferenceStore

        with tempfile.TemporaryDirectory() as tmp:
            store = ReferenceStore(Path(tmp))
            with self.assertRaises(ValueError):
                store.incoming("notes.exe")
            path = store.incoming("bench.png")
            Image.new("RGB", (64, 48), (200, 80, 20)).save(path)
            meta = store.adopt("bench.png", path)
            self.assertEqual((meta["kind"], meta["width"], meta["height"]), ("image", 64, 48))
            self.assertEqual(len(meta["stills"]), 1)
            self.assertTrue(store.still_path(meta["id"], meta["stills"][0]["file"]).is_file())
            with self.assertRaises(ValueError):
                store.still_path(meta["id"], "../../etc/passwd")
            self.assertEqual([m["id"] for m in store.list()], [meta["id"]])


class _StubHost:
    """Enough of a host for the REST layer: no thread, no simulator."""

    def __init__(self) -> None:
        from bridge_studio.host import Broadcast

        self.broadcast = Broadcast()
        self.phase = "idle"
        self.status_snapshot = {"phase": "idle"}
        self.sim = None
        self.document = None

    def start(self) -> None:
        pass

    def stop(self) -> None:
        pass

    def manifest(self):
        return None

    def geometry(self):
        return 0, b""

    def execute(self, command, client=""):
        return {
            "id": command.get("id"),
            "name": command.get("name"),
            "status": "rejected",
            "message": "stub",
            "result": {},
        }

    def recent_events(self, limit=200):
        return []

    def recent_commands(self, limit=200):
        return []


class TestRest(unittest.TestCase):
    def test_api_without_a_simulator(self) -> None:
        from fastapi.testclient import TestClient

        from bridge_studio.config import BridgeConfig
        from bridge_studio.server import create_app
        from bridge_studio.workcell import default_workcell

        with tempfile.TemporaryDirectory() as tmp:
            app = create_app(BridgeConfig(scenario="", data_dir=Path(tmp)), host=_StubHost())
            with TestClient(app) as client:
                self.assertTrue(client.get("/api/health").json()["ok"])
                scenarios = client.get("/api/scenarios").json()
                self.assertIn("pick_place", [s["id"] for s in scenarios])
                self.assertEqual(client.get("/api/scene/manifest").status_code, 404)
                catalog = client.get("/api/workcell/catalog").json()
                self.assertIn("rigid_grasp", catalog["presets"])
                commands = client.get("/api/commands/catalog").json()["commands"]
                self.assertTrue(any(c["name"] == "arm.joint" for c in commands))

                doc = default_workcell().model_dump(by_alias=True, mode="json")
                self.assertTrue(client.post("/api/workcell/validate", json=doc).json()["ok"])
                bad = {**doc, "objects": [{"kind": "sheet", "name": "towel"}]}
                verdict = client.post("/api/workcell/validate", json=bad).json()
                self.assertFalse(verdict["ok"])
                self.assertIn("cannot simulate", verdict["message"])

                self.assertEqual(client.put("/api/workcells/My Cell", json=doc).status_code, 400)
                self.assertEqual(client.put("/api/workcells/my-cell", json=bad).status_code, 422)
                saved = client.put("/api/workcells/my-cell", json=doc).json()
                self.assertTrue(saved["ok"])
                listed = client.get("/api/workcells").json()
                self.assertEqual([w["slug"] for w in listed], ["my-cell"])
                loaded = client.get("/api/workcells/my-cell").json()
                self.assertEqual(loaded["objects"], doc["objects"])
                self.assertEqual(client.delete("/api/workcells/my-cell").json(), {"ok": True})
                self.assertEqual(client.get("/api/workcells/my-cell").status_code, 404)

                ack = client.post("/api/command", json={"name": "sim.play"}).json()
                self.assertEqual(ack["status"], "rejected")
                licences = client.get("/api/licenses").json()
                self.assertIn("BRIDGE License", licences["bridgeLicense"])
                self.assertIn("Awaiz Ahmed", licences["bridgeLicense"])
                self.assertIn("Apache License", licences["license"])
                self.assertIn("RoboSimStudio", licences["notice"])
                self.assertIn("Copyright 2026 Kaiyuan Chen", licences["notice"])


if __name__ == "__main__":
    unittest.main()
