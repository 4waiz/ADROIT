# BRIDGE agent and automation API

Everything the BRIDGE page does, a script or a coding agent can do over HTTP
against the same running service. There is no separate "agent mode": the page
is one client of this API. The OpenAPI reference is served at
`http://127.0.0.1:8800/docs` while the service runs.

**What an agent can and cannot do.** It can read the scene, telemetry and
logs; build scenarios and workcells; command the simulated arms; run routines
and task sequences; take path-traced pictures; and measure the scene against
reference media. Every command goes to the simulation. No endpoint reaches
hardware, and none executes code that was sent to it.

Units are metres, radians, seconds and newtons. Positions are in the **world**
frame (z up); quaternions are `[x, y, z, w]`. Workcell documents place things
in the cell's **work-surface frame** (the origin is the middle of the bench top).

## Reading

| Endpoint | What |
| --- | --- |
| `GET /api/health` | Up, and the host's phase. |
| `GET /api/info` | Product, version, machine (OS, Python, GPU, Warp, renderer), command target. |
| `GET /api/status` | Phase, scenario, generation, playing, speed, simulated time, measured step rate, running routine or task, success verdict (`success`, read twice a second) and when it was first met since the last build or reset (`goalMet`: simulated time, step, detail), per-arm owner. |
| `GET /api/scene/manifest` | Everything static about the built scene: bodies, shapes (kind, size, local pose, colour, finish, role, owner), meshes, arms (joints with names, limits, units, home), deformables, cameras, regions, the physics program. |
| `GET /api/scene/geometry?gen=N` | Every mesh as one binary blob; offsets are in the manifest. |
| `GET /api/scene/state` | The newest frame as JSON: time, step, rates, per-arm telemetry (joint q, qd, actuator effort, targets, tool-centre pose, cursor, gripper command/width/closure, reach verdict), contacts, body poses. |
| `GET /api/events?limit=` | The host's event log. |
| `GET /api/commands?limit=` | Every command, who sent it, and how it ended. |
| `GET /api/scenarios` | The gallery, with availability on this machine. |
| `GET /api/doctor` | Environment checks. |

## Commanding

`POST /api/command` with `{"id": "<unique>", "name": "<command>", "args": {...}}`.
The answer is `{"id", "name", "status", "message", "result"}` where `status` is:

* `completed` -- done; `result` says what happened (a reach verdict, a value).
* `rejected` -- not carried out, and `message` says why (out of a joint's limits,
  arm reserved by a routine, scene rebuilt since the command was written).
* `failed` -- it was attempted and went wrong; `message` says how.
* `accepted` -- long work started (a build, a routine); its outcome arrives later
  in `GET /api/commands` (and on the socket).

An `id` is honoured once. Sending the same id again returns the first outcome
and does nothing, so a retried request cannot move a robot twice.

`GET /api/commands/catalog` lists every command with its arguments. The current set:

| Command | Arguments | Notes |
| --- | --- | --- |
| `sim.play`, `sim.pause` | | |
| `sim.step` | `count` 1..600 | Pauses, then advances exactly `count` control steps. |
| `sim.reset` | | Start state; ends a running routine. |
| `sim.speed` | `value` in 0.1, 0.25, 0.5, 1, 2, 4, 0 (max) | |
| `scenario.load` | `id` | `accepted`, then `completed` or `failed`. |
| `workcell.build` | `doc` (`bridge.workcell/1`) | Validated against the physics preset first. |
| `arm.joint` | `arm`, `joint` (0-based), `value` | Joint mode; refused outside the joint's limits. |
| `arm.joints` | `arm`, `values[]` | All joints at once. |
| `arm.home` | `arm` | |
| `arm.grip` | `arm`, `value` 0 open .. 1 closed | |
| `arm.cursor` | `arm`, `position[3]`, `quat[4]` | Cartesian target; answer carries the reach verdict. |
| `arm.jog` | `arm`, `space` joint/cartesian, `joint` or `axis` (x y z rx ry rz), `frame` world/tool, `delta` | |
| `arm.reach` | `arm`, `position[3]`, `quat[4]` | Checks a pose without moving anything. |
| `arm.speed` | `arm`, `joint` rad/s, `cartesian` fraction | |
| `arm.take` | `arm` | Cartesian mode with the target on the hand. |
| `routine.start` / `routine.stop` | `name` | The scenario's routines are in `manifest.routines`. |
| `task.run` / `task.stop` | `name`, `steps[]` | See below. |
| `physics.gravity` | `value` -30..0 | Live, where the solvers accept it. |

Every arm command also takes an optional `generation`; a command written
against an older build is refused instead of applied to a different scene.

### Task sequences

```json
{"name": "task.run", "id": "t-1", "args": {"name": "Pick", "steps": [
  {"op": "home", "arm": "right"},
  {"op": "grip", "arm": "right", "value": 0},
  {"op": "move", "arm": "right", "target": {"kind": "object", "name": "cube", "offset": [0, 0, 0.12]}, "orientation": "grasp"},
  {"op": "move", "arm": "right", "target": {"kind": "object", "name": "cube", "offset": [0, 0, 0]}, "orientation": "grasp", "speed": 0.08},
  {"op": "grip", "arm": "right", "value": 1, "seconds": 0.8},
  {"op": "move", "arm": "right", "target": {"kind": "relative", "offset": [0, 0, 0.14]}},
  {"op": "move", "arm": "right", "target": {"kind": "region", "name": "place_zone", "height": 0.03}},
  {"op": "grip", "arm": "right", "value": 0},
  {"op": "home", "arm": "right"}
]}}
```

Targets: `pose` (`position`, optional `quat`), `object` (`name`, `offset`),
`region` (`name`, `height` over the bench), `relative` (`offset` from the hand).
Orientation: `keep`, `down` (home), `grasp` (top-down, jaws across the object's
faces). Each step reports `done` or `failed` with the reason in
`status.runner.plan`.

## Media

| Endpoint | What |
| --- | --- |
| `POST /api/render` | `{eye, lookAt, fovY, width, height}` or `{camera}` -> a PNG through the library's renderer (path traced where the GPU can; the `X-Bridge-Renderer` header says which). The simulation holds while it renders: about two minutes for the first render (the renderer compiles), seconds after that. |
| `PUT /api/reference/upload?name=clip.mp4` | Raw body. A clip or a photo for real-to-sim. |
| `POST /api/reference/{id}/stills` | `{times: [s, ...]}` -> stills at those seconds. |
| `POST /api/reference/{id}/compare` | `{still, view: {eye, lookAt, fovY}, keys: ["cube=orange"]}` -> per-thing size, offset and IoU, with overlay pictures. |

## Workcells

`GET /api/workcell/catalog` (kinds, presets, robots, cells and their frames),
`POST /api/workcell/validate`, `GET|PUT|DELETE /api/workcells/{slug}`,
`GET /api/workcells`.

## Streaming

`ws://127.0.0.1:8800/ws` sends JSON messages (`hello`, `status`, `scene`,
`ack`, `command`, `event`, `pong`) and binary frames (`bridge_studio/frames.py`
documents the layout). It accepts `{"type": "command", ...}` exactly as the
REST endpoint does, and `{"type": "cursor", "arm", "position", "quat",
"phase": "start"|"update"|"end"}` for continuous target streaming (the gizmo and
the gamepad use this; `end` triggers a reach check).

## Example

```python
import json, urllib.request, uuid

def command(name, **args):
    body = json.dumps({"id": str(uuid.uuid4()), "name": name, "args": args}).encode()
    req = urllib.request.Request("http://127.0.0.1:8800/api/command", body, {"Content-Type": "application/json"})
    return json.load(urllib.request.urlopen(req))

print(command("arm.grip", arm="right", value=1.0))
print(command("arm.joint", arm="right", joint=0, value=0.5))
state = json.load(urllib.request.urlopen("http://127.0.0.1:8800/api/scene/state"))
print(state["arms"]["right"]["q"])
```
