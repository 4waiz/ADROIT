#!/usr/bin/env python3
# SPDX-License-Identifier: LicenseRef-BRIDGE
"""Unpack a .unitypackage into a Unity project, keeping every asset's GUID.

Unity imports a package asynchronously, which a batch-mode run can quit before finishing; this
writes the files (and their .meta files) straight into the project instead, so the next time
Unity opens the project they are simply there. vr/build.ps1 uses it for TextMesh Pro's
essential resources.

    python vr/tools/unpack_unitypackage.py <package> <project folder>
"""

from __future__ import annotations

import sys
import tarfile
from pathlib import Path, PurePosixPath


def unpack(package: Path, project: Path) -> int:
    entries: dict[str, dict[str, bytes]] = {}
    with tarfile.open(package, "r:gz") as tar:
        for member in tar.getmembers():
            if not member.isfile():
                continue
            parts = PurePosixPath(member.name.lstrip("./")).parts
            if len(parts) != 2:
                continue
            guid, kind = parts
            data = tar.extractfile(member).read()
            entries.setdefault(guid, {})[kind] = data
    written = 0
    root = project.resolve()
    for files in entries.values():
        if "pathname" not in files:
            continue
        rel = files["pathname"].decode("utf-8").splitlines()[0].strip()
        target = (project / rel).resolve()
        if root not in target.parents:
            raise SystemExit(f"refusing to write outside the project: {rel}")
        if "asset" in files:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(files["asset"])
            written += 1
        else:
            target.mkdir(parents=True, exist_ok=True)
        if "asset.meta" in files:
            Path(str(target) + ".meta").write_bytes(files["asset.meta"])
    return written


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    n = unpack(Path(sys.argv[1]), Path(sys.argv[2]))
    print(f"unpacked {n} files from {Path(sys.argv[1]).name}")
