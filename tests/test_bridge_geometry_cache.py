# SPDX-License-Identifier: LicenseRef-BRIDGE
"""The geometry blob a browser kept is never taken for a later run of the service.

Generations count from 1 again each time the service starts, so ``/api/scene/geometry?gen=1``
can name other meshes than a copy kept from before a restart (an asset changed, and the page
read the new manifest's offsets into the old blob). The blob is sent ``no-cache`` with an ETag
that names the run: an unchanged one costs a 304, and a new run never answers to an old tag.
"""

from __future__ import annotations

import tempfile
import time
import unittest
from pathlib import Path

from tests.test_bridge_fast import _StubHost

TIER = "fast"


class _OneBlob(_StubHost):
    def geometry(self):
        return 1, bytes(range(256)) * 16


class TestGeometryCache(unittest.TestCase):
    def test_the_blob_is_checked_again_and_tagged_with_its_run(self) -> None:
        from fastapi.testclient import TestClient

        from bridge_studio.config import BridgeConfig
        from bridge_studio.server import create_app

        tags = []
        with tempfile.TemporaryDirectory() as tmp:
            for _ in range(2):
                app = create_app(BridgeConfig(scenario="", data_dir=Path(tmp)), host=_OneBlob())
                with TestClient(app) as client:
                    first = client.get("/api/scene/geometry?gen=1")
                    self.assertEqual(first.status_code, 200)
                    self.assertEqual(first.content, bytes(range(256)) * 16)
                    self.assertEqual(first.headers["cache-control"], "no-cache")
                    tag = first.headers["etag"]
                    again = client.get("/api/scene/geometry?gen=1", headers={"If-None-Match": tag})
                    self.assertEqual(again.status_code, 304)
                    self.assertEqual(again.content, b"")
                    self.assertEqual(client.get("/api/scene/geometry?gen=2").status_code, 409)
                    tags.append(tag)
                time.sleep(0.01)  # the second run starts later
        self.assertNotEqual(tags[0], tags[1], "a new run must not answer to an old run's tag")


if __name__ == "__main__":
    unittest.main()
