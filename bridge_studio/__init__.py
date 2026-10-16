# SPDX-License-Identifier: LicenseRef-BRIDGE
"""BRIDGE | Digital Twin Studio: a browser command centre over the simulation stack.

The service half. One process holds one built scene on one thread (the *host*)
and serves a web client that draws it and drives it::

    uv run python -m bridge_studio            # serve on http://127.0.0.1:8800
    uv run python -m bridge_studio doctor     # what this machine can run

The simulator is the only source of physical state. The client renders the
poses the host publishes and sends commands the host validates; nothing in the
browser integrates physics. Every piece of robot control goes through the same
objects the studio uses -- :class:`~robosimstudio.solvers.ik.arm_ik.ArmIK`,
:class:`~robosimstudio.solvers.OscSetpoint` and
:class:`~robosimstudio.solvers.motion.gripper.Gripper` -- so a hand moved from
BRIDGE moves exactly as one moved from the studio page.

Built by Awaiz Ahmed on RoboSimStudio (Apache-2.0); see ``NOTICE``.
"""

from __future__ import annotations

__all__ = ["PRODUCT", "SUBTITLE", "__version__"]

__version__ = "0.1.0"

PRODUCT = "BRIDGE | Digital Twin Studio"
SUBTITLE = "Advanced Robotics Simulation & Control"
