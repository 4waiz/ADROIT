# SPDX-License-Identifier: LicenseRef-BRIDGE
"""What one BRIDGE service is told when it starts."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

__all__ = ["DEFAULT_PORT", "BridgeConfig", "repo_root"]

DEFAULT_PORT = 8800
"""Not 8080: that is the studio's and the live page's, and both may be open beside this."""


def repo_root() -> Path:
    """The checkout this package sits in: the directory holding ``pyproject.toml``."""
    here = Path(__file__).resolve().parent
    for parent in (here, *here.parents):
        if (parent / "pyproject.toml").is_file():
            return parent
    return here.parent


@dataclass
class BridgeConfig:
    """Everything the service needs to know before its first scene.

    Attributes:
        host: Interface to bind. ``127.0.0.1`` by default, unlike viser's
            ``0.0.0.0``: a page that drives a robot should not be reachable from
            the network unless somebody asked for that.
        port: Port to serve on.
        scenario: The scenario built at start, or ``""`` to start empty.
        data_dir: Where saved workcells, reference media and snapshots go.
        frame_hz: Most frames a second published to the browser. The simulation
            steps at its own control rate whatever this says.
        static_dir: The built client. ``None`` uses the package's ``static/``.
        dev: Allow the Vite dev server's origin, for working on the client.
        autoplay: Start stepping as soon as a scenario is built.
    """

    host: str = "127.0.0.1"
    port: int = DEFAULT_PORT
    scenario: str = "pick_place"
    data_dir: Path = field(default_factory=lambda: repo_root() / "outputs" / "bridge")
    frame_hz: float = 60.0
    static_dir: Path | None = None
    dev: bool = False
    autoplay: bool = True

    def __post_init__(self) -> None:
        self.data_dir = Path(self.data_dir)
        if self.static_dir is None:
            self.static_dir = Path(__file__).resolve().parent / "static"
        self.frame_hz = float(min(max(self.frame_hz, 1.0), 120.0))

    @property
    def workcell_dir(self) -> Path:
        return self.data_dir / "workcells"

    @property
    def reference_dir(self) -> Path:
        return self.data_dir / "reference"

    @property
    def capture_dir(self) -> Path:
        return self.data_dir / "captures"
