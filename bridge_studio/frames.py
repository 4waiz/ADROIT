# SPDX-License-Identifier: Apache-2.0
"""The binary frame the host streams: a JSON header and float32 buffers behind it.

One frame per publish, never a backlog: a client that falls behind gets the
newest frame and skips the rest, which is what a live view wants and what a
recording does not -- recordings are the recorder's job, not this socket's.

Layout, little-endian::

    b"BRF1"                       4 bytes, the format
    uint32 header_length          4 bytes
    header                        UTF-8 JSON, space-padded to a multiple of 4
    payload                       float32 arrays, back to back

``header["buffers"]`` lists each array as ``{"name", "offset", "count", "width"}``
with ``offset`` in bytes from the start of the payload and ``count`` in rows of
``width`` floats. The two the client always reads are ``body_q`` (``width`` 7:
position, then quaternion xyzw) and ``particles`` (``width`` 3).
"""

from __future__ import annotations

import json
import struct
from typing import Any

import numpy as np

__all__ = ["MAGIC", "decode_frame", "encode_frame", "sanitize"]

MAGIC = b"BRF1"


def sanitize(value: Any) -> Any:
    """``value`` with every non-finite float replaced by ``None``.

    A diverged solver produces NaN, and JSON has no NaN. ``None`` is what the
    client shows as "no reading", which is the truth about a NaN: there is no
    physical value there. The finite flag in the header says why.
    """
    if isinstance(value, float):
        return value if np.isfinite(value) else None
    if isinstance(value, dict):
        return {k: sanitize(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [sanitize(v) for v in value]
    if isinstance(value, np.generic):
        return sanitize(value.item())
    return value


def encode_frame(header: dict[str, Any], buffers: list[tuple[str, np.ndarray]]) -> bytes:
    """Pack a header and named float arrays into one frame."""
    described = []
    payload: list[bytes] = []
    offset = 0
    for name, array in buffers:
        data = np.ascontiguousarray(np.asarray(array, dtype=np.float32))
        width = int(data.shape[1]) if data.ndim == 2 else 1
        raw = data.tobytes()
        described.append(
            {"name": name, "offset": offset, "count": int(data.shape[0]) if data.ndim else 0, "width": width}
        )
        payload.append(raw)
        offset += len(raw)
    text = json.dumps(sanitize({**header, "buffers": described}), separators=(",", ":"), allow_nan=False)
    text = text.encode("utf-8")
    text += b" " * ((-len(text)) % 4)
    return MAGIC + struct.pack("<I", len(text)) + text + b"".join(payload)


def decode_frame(frame: bytes) -> tuple[dict[str, Any], dict[str, np.ndarray]]:
    """The inverse of :func:`encode_frame`, for tests and for clients written in Python."""
    if frame[:4] != MAGIC:
        raise ValueError("not a BRIDGE frame")
    (length,) = struct.unpack("<I", frame[4:8])
    header = json.loads(frame[8 : 8 + length].decode("utf-8"))
    base = 8 + length
    arrays: dict[str, np.ndarray] = {}
    for item in header.get("buffers", []):
        count, width = int(item["count"]), int(item["width"])
        start = base + int(item["offset"])
        values = np.frombuffer(frame, dtype=np.float32, count=count * width, offset=start)
        arrays[item["name"]] = values.reshape(count, width) if width > 1 else values
    return header, arrays
