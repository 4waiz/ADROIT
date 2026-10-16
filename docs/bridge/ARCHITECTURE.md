# BRIDGE | Digital Twin Studio: architecture

BRIDGE is a browser command centre over the RoboSimStudio simulation stack in
this repository. The Python simulator stays the single source of physical
truth; the browser draws what the simulator reports and sends validated
commands back. Nothing in the browser integrates physics.

## What was here (assessment, 2026-10-16)

| Layer | What exists | Kept as |
| --- | --- | --- |
| `robosimstudio/` | `Scene` → `Sim` over Newton 1.6 + MuJoCo Warp, IK (PyRoKi, Newton LM), grippers, OSC setpoint law, cloth, soft (SuperDex FEM), rope, contact probes, path-traced renders (ovrtx) | Unchanged. BRIDGE calls its public API. |
| `robosimstudio_studio/` | A viser page: gizmo-driven arms, Grip, Pause/Speed/Reset, routines, recording | Unchanged and still runnable (`rss.view`). BRIDGE reuses its routine primitives (`reach`, `grip`, `wait`) and `SceneIndex`. |
| `robosimstudio_real2sim_agent/` | The live page and `./ctl` (Unix socket, bash) | Unchanged. BRIDGE calls its frame extraction and comparison code directly. |
| `examples/` | Numbered scripts and three bimanual demos with measured routines | Unchanged. BRIDGE scenarios import the demo scenes and routines from their folders. |

Measured on the development machine (Windows 11, RTX 5080 Laptop, driver 591.91):
the whole stack runs natively. No WSL2 is needed. A Panda + cube scene under
`MUJOCO_500HZ` builds in ~9 s once Warp's kernel cache is warm (132 s on the very
first build) and steps at ~300 control steps/s (50 Hz control, 10 substeps).

**Why not extend viser.** Viser's GUI is one fixed sidebar of Mantine widgets. A
command centre needs a scene tree, a contextual inspector, a docked engineering
panel, PBR rendering with shadows and post-processing, and custom gizmos, none
of which viser's client exposes. Embedding viser in an iframe would mean two
3D runtimes and two sockets. BRIDGE therefore ships its own client and keeps
viser where it already works (the studio, the live page).

## Shape of the system

```
browser (React + TypeScript + three.js)                 python (one process)
┌─────────────────────────────────────────┐   HTTP    ┌──────────────────────────────┐
│ shell: top bar, scene tree, inspector,  │ ───────── │ FastAPI app (bridge_studio)  │
│ engineering dock, gallery, editor       │           │  REST: status, scenarios,    │
│ viewport: three.js scene built from the │   WS      │  manifest, geometry, saves   │
│ manifest; body poses applied per frame  │ ═════════ │  WS: commands in, frames out │
└─────────────────────────────────────────┘           └──────────────┬───────────────┘
                                                                     │ SimBus (queue)
                                                      ┌──────────────▼───────────────┐
                                                      │ SimHost thread: owns Scene/  │
                                                      │ Sim, ArmDrivers (IK + OSC),  │
                                                      │ routines, tasks, telemetry   │
                                                      └──────────────────────────────┘
```

* **One owner thread.** Warp has one writer. Every build, step, IK solve and read
  happens on the SimHost thread. Web handlers post requests through
  `robosimstudio.core.bus.SimBus` and wait, exactly as the graph runtime does.
* **Manifest + frames.** After each build the host exports a manifest: every
  shape (primitive parameters or a mesh id), its body, local transform, colour,
  finish, role (visual / collision / site) and owner entity; every body; every
  arm with its joints, limits and gripper; deformable topology. Meshes go once,
  as one binary blob. Each frame is then a small binary message: body poses
  (`float32[n,7]`), particle positions for deformables, and a JSON header with
  telemetry. The client composes `body ∘ shape_local` itself.
* **Commands.** Every command carries a client-generated id. The host answers
  `accepted`, `rejected` (with the reason), `completed`, `failed` or
  `interrupted`. Duplicate ids are dropped, so a reconnect cannot replay one.
* **Arms.** An `ArmDriver` per arm reproduces the studio's `PanelArm`: Cartesian
  mode tracks a cursor through `OscSetpoint` + `ArmIK.solve_into`; joint mode
  writes `ArmIK.set_joint_targets`; the gripper writes `Gripper.command`. The
  shipped routines drive these drivers unmodified, because the driver offers the
  same `aim` / `cursor_pos` / `grip_request` surface as the studio panel.
* **Honest telemetry.** Joint positions and velocities come from `state`,
  efforts from MuJoCo's actuator forces where the solver exposes them, contacts
  from `ContactProbe`. A value the backend cannot produce is reported as
  unavailable, never synthesised. Every stream is labelled `Simulated`.
  The per-step physics time includes waiting for the device to finish the step
  and copy its result back, so a GPU-bound scene reads as such instead of
  showing only the time it took to launch the step.
* **Goals.** A scenario's success measure is read twice a second. The first time
  it reads done since the last build or reset is kept, with the simulated time,
  so a goal met in passing (a lift held, then set down) is reported as met.
* **Shutdown.** Ctrl+C stops the host loop, which closes the scene on its own
  thread; the process then exits directly, because a co-simulated native engine
  can leave a thread the interpreter would otherwise wait on forever.
* **Scenes as data.** User workcells are versioned JSON documents
  (`bridge.workcell/1`) mapped onto the public constructors through a whitelist
  (`rss.box`, `rss.sphere`, `rss.cylinder`, `rss.capsule`, `rss.slab`,
  `rss.post`, `rss.sheet`, `rss.sponge`, `rss.robot`). Nothing executes user code.
* **Hardware.** Simulation is the only command target. A connector interface
  exists with a disconnected default; no hardware is claimed.

## Packages added

| Path | What |
| --- | --- |
| `bridge_studio/` | The service: host thread, manifest export, commands, scenarios, workcell documents, telemetry, environment doctor, FastAPI app. |
| `frontend/` | The client source (Vite). `npm run build` writes `bridge_studio/static/`. |
| `docs/bridge/` | This file, the plan, the agent API reference. |

The original packages, their public APIs and the examples are untouched.
