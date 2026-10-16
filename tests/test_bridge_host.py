# SPDX-License-Identifier: LicenseRef-BRIDGE
"""BRIDGE's host on a real scene: what each command does to the simulation, and what it refuses.

One host, the Panda pick-and-place cell, driven through ``SimHost.execute``
exactly as the page and an agent drive it:

* the manifest describes the built model and its geometry blob holds every mesh;
* a joint target inside the limits is reached, one outside is refused with the
  limits named; a repeated command id is answered without running again; a
  command written against an older build is refused;
* the gripper closes and opens by measurement, not by echo;
* a far target is reported unreachable with the residual;
* a step advances exactly one control period; reset returns time to zero;
* a mission sequence (home, approach, grasp, lift, place, release) puts the cube
  in the zone by friction, and the arm it drives is reserved while it runs;
* a workcell document builds into a new generation, and one its preset cannot
  simulate is refused before anything is built.
"""

from __future__ import annotations

import math
import time
import unittest
import uuid

TIER = "medium"
"""Builds the Panda cell on the GPU and steps it for a few hundred control periods."""


class TestBridgeHost(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        from bridge_studio.config import BridgeConfig
        from bridge_studio.host import SimHost
        from bridge_studio.scenarios import builtin_registry

        cls.host = SimHost(BridgeConfig(scenario="pick_place", autoplay=False), builtin_registry())
        cls.host.start()
        deadline = time.time() + 900
        while cls.host.generation < 1 and time.time() < deadline:
            if cls.host.phase == "error":
                raise RuntimeError(cls.host.build_error)
            time.sleep(0.2)
        assert cls.host.generation >= 1, "the pick-and-place cell did not build"
        cls.cmd(cls, "sim.speed", value=0.0)

    @classmethod
    def tearDownClass(cls) -> None:
        cls.host.stop()

    # ---------------------------------------------------------------- helpers
    def cmd(self, command: str, /, *, cid: str | None = None, **args) -> dict:
        return self.host.execute({"id": cid or str(uuid.uuid4()), "name": command, "args": args}, client="test")

    def frame(self) -> dict:
        from bridge_studio.frames import decode_frame

        _, data = self.host.broadcast.frame_after(-1)
        header, _ = decode_frame(data)
        return header

    def settle(self, steps: int) -> dict:
        before = self.frame()["step"]
        ack = self.cmd("sim.step", count=steps)
        self.assertEqual(ack["status"], "completed", ack["message"])
        deadline = time.time() + 60
        while time.time() < deadline:
            header = self.frame()
            if header["step"] >= before + steps:
                return header
            time.sleep(0.05)
        self.fail(f"{steps} steps did not complete")

    # ---------------------------------------------------------------- the model
    def test_manifest(self) -> None:
        manifest = self.host.manifest()
        blob = self.host.export.geometry
        self.assertEqual(manifest["geometryBytes"], len(blob))
        for mesh in manifest["meshes"]:
            end = mesh["index"] + 4 * mesh["indices"]
            self.assertLessEqual(end, len(blob))
            self.assertLessEqual(mesh["position"] + 12 * mesh["vertices"], len(blob))
        (arm,) = manifest["arms"]
        self.assertEqual((arm["name"], len(arm["joints"])), ("right", 7))
        self.assertTrue(all(j["lower"] is not None and j["upper"] is not None for j in arm["joints"]))
        self.assertIn("region:place_zone", [s["key"] for s in manifest["subjects"]])
        header = self.frame()
        self.assertEqual(len(header["arms"]["right"]["q"]), 7)

    # ------------------------------------------------------------- the commands
    def test_joint_targets_limits_ids_and_generations(self) -> None:
        self.cmd("sim.reset")
        ack = self.cmd("arm.joint", arm="right", joint=0, value=0.35)
        self.assertEqual(ack["status"], "completed")
        header = self.settle(60)
        self.assertAlmostEqual(header["arms"]["right"]["q"][0], 0.35, delta=0.01)
        self.assertEqual(header["arms"]["right"]["mode"], "joint")

        refused = self.cmd("arm.joint", arm="right", joint=0, value=9.0)
        self.assertEqual(refused["status"], "rejected")
        self.assertIn("limits", refused["message"])
        self.assertEqual(self.cmd("arm.joint", arm="left", joint=0, value=0.0)["status"], "rejected")

        cid = str(uuid.uuid4())
        first = self.cmd("arm.joint", cid=cid, arm="right", joint=1, value=0.3)
        again = self.cmd("arm.joint", cid=cid, arm="right", joint=1, value=0.3)
        self.assertEqual(first["status"], "completed")
        self.assertTrue(again.get("duplicate"))

        stale = self.cmd("arm.home", arm="right", generation=self.host.generation - 1)
        self.assertEqual(stale["status"], "rejected")
        self.assertIn("rebuilt", stale["message"])

    def test_gripper_by_measurement(self) -> None:
        self.cmd("sim.reset")
        self.assertEqual(self.cmd("arm.grip", arm="right", value=1.0)["status"], "completed")
        closed = self.settle(40)["arms"]["right"]["grip"]
        self.assertLess(closed["width"], 0.005)
        self.cmd("arm.grip", arm="right", value=0.0)
        opened = self.settle(40)["arms"]["right"]["grip"]
        self.assertGreater(opened["width"], 0.07)
        self.assertEqual(self.cmd("arm.grip", arm="right", value=1.5)["status"], "rejected")

    def test_reach_and_jog(self) -> None:
        self.cmd("sim.reset")
        far = self.cmd("arm.reach", arm="right", position=[2.5, 0.0, 0.9], quat=[1, 0, 0, 0])
        self.assertEqual(far["status"], "completed")
        self.assertFalse(far["result"]["reach"]["reachable"])
        self.assertIn("short", far["result"]["reach"]["why"])
        z0 = self.frame()["arms"]["right"]["ee"][2]
        jog = self.cmd("arm.jog", arm="right", space="cartesian", axis="z", delta=0.05)
        self.assertEqual(jog["status"], "completed", jog["message"])
        z1 = self.settle(80)["arms"]["right"]["ee"][2]
        self.assertAlmostEqual(z1 - z0, 0.05, delta=0.006)
        self.assertEqual(self.cmd("arm.jog", arm="right", space="cartesian", axis="z", delta=1.0)["status"], "rejected")

    def test_step_pause_reset(self) -> None:
        self.cmd("sim.reset")
        self.cmd("sim.pause")
        t0 = self.frame()["t"]
        t1 = self.settle(1)["t"]
        self.assertAlmostEqual(t1 - t0, 0.02, places=6)
        time.sleep(0.5)
        self.assertAlmostEqual(self.frame()["t"], t1, places=9)
        self.cmd("sim.reset")
        self.assertEqual(self.frame()["t"], 0.0)

    # ----------------------------------------------------------------- the task
    def test_mission_sequence_places_the_cube(self) -> None:
        self.cmd("sim.reset")
        steps = [
            {"op": "grip", "arm": "right", "value": 0, "seconds": 0.4},
            {
                "op": "move",
                "arm": "right",
                "target": {"kind": "object", "name": "cube", "offset": [0, 0, 0.12]},
                "orientation": "grasp",
                "speed": 0.2,
            },
            {
                "op": "move",
                "arm": "right",
                "target": {"kind": "object", "name": "cube", "offset": [0, 0, 0]},
                "orientation": "grasp",
                "speed": 0.08,
            },
            {"op": "grip", "arm": "right", "value": 1, "seconds": 0.8},
            {
                "op": "move",
                "arm": "right",
                "target": {"kind": "relative", "offset": [0, 0, 0.14]},
                "speed": 0.12,
                "tolerance": 0.012,
            },
            {
                "op": "move",
                "arm": "right",
                "target": {"kind": "region", "name": "place_zone", "height": 0.16},
                "speed": 0.15,
                "tolerance": 0.012,
            },
            {
                "op": "move",
                "arm": "right",
                "target": {"kind": "region", "name": "place_zone", "height": 0.03},
                "speed": 0.06,
                "tolerance": 0.012,
            },
            {"op": "grip", "arm": "right", "value": 0, "seconds": 0.6},
            {"op": "move", "arm": "right", "target": {"kind": "relative", "offset": [0, 0, 0.12]}, "speed": 0.15},
        ]
        ack = self.cmd("task.run", name="test", steps=steps)
        self.assertEqual(ack["status"], "accepted", ack["message"])
        locked = self.cmd("arm.grip", arm="right", value=1.0)
        self.assertEqual(locked["status"], "rejected")
        self.assertIn("task", locked["message"])
        deadline = time.time() + 180
        while self.host.runner is not None and time.time() < deadline:
            time.sleep(0.2)
        last = self.host.last_runner
        self.assertEqual(last["status"], "completed", last.get("message"))
        self.assertTrue(all(step["status"] == "done" for step in last["plan"]))
        time.sleep(1.2)  # the success measure is read twice a second
        self.assertTrue(self.host.success and self.host.success["done"], self.host.success)

    # ------------------------------------------------------------- workcells
    def test_workcell_documents(self) -> None:
        from bridge_studio.workcell import default_workcell

        doc = default_workcell().model_dump(by_alias=True, mode="json")
        bad = {**doc, "objects": [*doc["objects"], {"kind": "sheet", "name": "towel"}]}
        refused = self.cmd("workcell.build", doc=bad)
        self.assertEqual(refused["status"], "rejected")
        self.assertIn("cannot simulate", refused["message"])

        doc["objects"].append({"kind": "cylinder", "name": "can", "at": [0.05, 0.12, 0.052], "mass": 0.15})
        generation = self.host.generation
        self.assertEqual(self.cmd("workcell.build", doc=doc)["status"], "accepted")
        deadline = time.time() + 300
        while (self.host.generation == generation or self.host.phase != "ready") and time.time() < deadline:
            time.sleep(0.2)
        self.assertEqual(self.host.generation, generation + 1)
        names = {s["name"] for s in self.host.manifest()["subjects"]}
        self.assertTrue({"cube", "can"} <= names)
        self.assertEqual(self.host.document["objects"][-1]["name"], "can")
        # Back to the scenario for the tests that follow in this class.
        self.cmd("scenario.load", id="pick_place")
        while (self.host.generation == generation + 1 or self.host.phase != "ready") and time.time() < deadline + 300:
            time.sleep(0.2)
        self.cmd("sim.speed", value=0.0)
        self.assertTrue(math.isfinite(self.frame()["t"]))


if __name__ == "__main__":
    unittest.main()
