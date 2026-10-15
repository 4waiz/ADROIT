# BRIDGE implementation plan

Working order: vertical slices that run end to end before breadth. Each
milestone is committed once it builds, lints and passes its checks.

## M1. Service core and live viewport
- `bridge_studio.host.SimHost`: owner thread, SimBus, pacing at control rate x speed, play/pause/step/reset.
- Manifest export from the compiled Newton model (primitives, meshes, colours, finishes, roles, owners).
- FastAPI app: status, manifest, geometry blob, WebSocket frames and commands with acks.
- Client: three.js viewport built from the manifest; body poses streamed; orbit, presets, selection.

## M2. Robot control
- `ArmDriver` per arm (studio-equivalent IK tracking, joint mode, gripper).
- Inspector: real joints, limits, units; sliders; Open/Close; EE gizmo with reachability residual.
- Jog buttons and keyboard jogging; command feedback in the command history.

## M3. Engineering surfaces
- Telemetry plots (joint q / qd / effort, EE pose, gripper, contacts, step rate) with pause.
- Event log, command history, diagnostics (backend, GPU, solver summary, problems).
- Physics inspector: arrangement, islands, live gravity; rebuild-required parameters flagged.
- Debug overlays: collision geometry, joint frames, EE target vs actual, contacts, COM.

## M4. Scenarios and workcells
- Scenario gallery: pick and place, surface wiping, cloth fold, cable routing, dual-arm cell.
- Workcell documents: add / remove / duplicate / move / resize objects, undo/redo, save/load, versioned.
- Routines and mission sequences through the shipped `reach` / `grip` / `wait` primitives.

## M5. Advanced
- Reference media workspace: import a clip or image, extract stills, render the sim from a camera beside it, compare.
- Agent API reference (OpenAPI at `/docs`), hardware connector interface (disconnected default).
- Rendering profiles (Performance / Balanced / Ultra), measured frame time.

## M6. Hardening
- Tests: service unit tests in the fast tier; GPU end-to-end through the HTTP API; browser journey with Playwright.
- Accessibility pass, 1366x768 layout pass, English/Arabic, docs, launch scripts, final commits.
