# BRIDGE's Next-Gen Factory Digital Twin Simulation for Robotics (VR)

The virtual-reality companion to the BRIDGE's Next-Generation Industry 5.0 Robotic Arm
Digital Twin Simulation, by Awaiz Ahmed. Its program and folders are called `BRIDGE VR`.

Put on a Meta Quest 2 or 3, connect it to this laptop with Quest Link, and you stand in the
same digital factory the studio draws in the browser: the hall, the racking and machine line,
the robotics lab, and Cell 01 with its Franka Emika Panda. You drive the arm with the
controllers, teach it programs, guide it by hand, walk into its safety fields, and work with
its digital twin. The **Robotics Academy** turns that into ten hands-on modules (about 70
minutes), and a **trainer's view** on the laptop screen lets a room follow along.

It is a separate application from the web studio: it runs on its own, without the Python
service, so it starts in seconds. Its robot motion, safety functions and telemetry are a
simulation, labelled as such; no hardware is connected.

## Start it

Double-click **BRIDGE's Next-Gen Factory Digital Twin VR** on the desktop. Then:

1. Connect the headset with its USB-C Link cable (or turn on Air Link) and, in the headset,
   turn on **Quest Link**. The Meta Horizon Link app must be installed on the laptop and set
   as the active OpenXR runtime (it is, on this machine).
2. The app opens on the laptop first. As soon as Link is up it switches into the headset; if
   Link drops, it falls back to the laptop and switches back when Link returns.
3. The first time, it opens on module 1, **Welcome to the factory**: a narrator talks you
   through pointing, pressing, walking, turning and picking up a part, one step at a time. After
   that it opens on the main menu.
4. Without a headset the app runs on the desktop, with the mouse and keyboard standing in for
   the controllers (see below), so a trainer can preview a module anywhere.

## The voice guide

A narrator reads every lesson step aloud as it opens, says when you have done it and which
button moves you on, reads the tips that appear when you are stuck, and reads each stop of the
factory tour. Press **B** to hear a step again. Turn it off in Settings (Voice guide). The voice
is Kokoro's `af_heart`, the same as the studio's demo video.

## Controls

Both controllers: **trigger** points and selects, **Menu** (left) opens the main menu, **Y**
switches between exploring and driving the robot. Labels beside each button say what it does at
that moment. Walking stops at walls, machines and the robot. If walking with the stick is
uncomfortable, turn off *Walk with the right stick* in Settings: the right stick then jumps
(teleports) and turns instead.

| | Explore | Drive the robot |
| --- | --- | --- |
| Right stick | Walk where you look (forward, back, sideways) | Hand up and down, turn the wrist (joint mode: turn the joint) |
| Left stick | Turn left or right; push forward, aim the arc and let go to jump | Hand across the floor (joint mode: choose a joint) |
| A | Next lesson step; at the end of a module, the next module | Open or close the gripper |
| B | Repeat the step (and hear it again); at the end of a module, the Academy | Jog mode: Cartesian, joint, hand guide |
| X | Show the teach pendant | Save this spot as a waypoint |
| Stick clicks | Left: keep the labels showing; right: bring the lesson panel back | Left: jog frame; right: speed |
| Grips | Pick up parts; in hand-guide mode, take the robot's wrist | Left grip: the enabling switch, when it is required |

The trainer's view on the laptop: **F1** the trainee's view, **F2** overview, **F3** robot
close-up, **F4** hide the overlay; **N** skip a step, **B** back, **R** repeat, **H** menu,
**E** emergency stop, **C** reset the cell (hold Ctrl for these on the desktop).

On the desktop: right mouse to look, WASD to walk, left click to select; **1** A, **2** B,
**3** X, **4** Y, **Tab** Menu, **G** and **H** the grips, arrows and PgUp/PgDn/comma/period the
sticks, **Q**/**E** the stick clicks.

## Build it

```powershell
powershell -ExecutionPolicy Bypass -File vr\build.ps1               # export, set up, build, install, shortcut
powershell -ExecutionPolicy Bypass -File vr\build.ps1 -Test robot   # and run the robot checks
```

Needs Unity 6000.6 with Windows build support (from Unity Hub), uv, and Microsoft Edge. The
script keeps Unity's project outside OneDrive, in `%LOCALAPPDATA%\BridgeVR\project`, mirrors
`vr/project/Assets/Bridge` into it, exports the repository's own models and textures
(`vr/tools/export_assets.py`), lets `BridgeSetup` configure everything from code (URP, OpenXR,
fonts, materials, the scene), builds the player, installs it to
`%LOCALAPPDATA%\Programs\BRIDGE VR` and makes the shortcut. Options: `-SkipExport`,
`-SkipSetup`, `-SkipBuild`, `-SkipVoice`, `-NoInstall`, `-Run`.

The voice step runs the built player with `-autotest narration`, which writes every line the
narrator can say, then `tools/voice.py` speaks the new ones with Kokoro into the player's
`StreamingAssets\Voice` (one OGG per line, named by a hash of its words; lines already spoken
come from a cache). It needs `kokoro-onnx` and `soundfile` (from `BRIDGE_VOICE_PYTHON`, a
`~\.kokoro-venv`, or uv) and Kokoro's model files, `kokoro-v1.0.onnx` and `voices-v1.0.bin`,
in `%LOCALAPPDATA%\BridgeVR\kokoro`. Without them the app is built without its voice.

`-Test robot` runs the installed app with `-autotest robot`: reach, jogging, joint limits, the
bench stopping the hand, the pick and place program, the emergency stop, separation monitoring,
the twin catching a collision, the capstone's batch program, and the frame rate. `-Test shots` saves
screenshots of every screen; `-Test lessons` steps through all ten modules. Results go to
`%LOCALAPPDATA%\BridgeVR\autotest-<scenario>` with a `report.json`.

## How it is made

| Folder | What it holds |
| --- | --- |
| `project/Assets/Bridge/Scripts/World` | The factory hall, the cell's surround and the lab, ported line by line from `frontend/src/viewport/factory` (the web app's own numbers, mapped from its z-up frame to Unity's) |
| `project/Assets/Bridge/Scripts/Robot` | The Panda built from `assets/embodiments/panda/panda_standalone.xml`, its kinematics and damped-least-squares IK, the cell controller (jogging, hand guiding, gripper, programs), the safety functions and the telemetry models |
| `project/Assets/Bridge/Scripts/Twin` | The digital twin, its dashboard and virtual commissioning |
| `project/Assets/Bridge/Scripts/UI` | The glass interface: panels, buttons, the pendant, menus |
| `project/Assets/Bridge/Scripts/Learn` | The Academy's lesson engine, the ten modules, the factory tour |
| `project/Assets/Bridge/Scripts/XR` | OpenXR start-up with desktop fallback, controllers, locomotion |
| `project/Assets/Bridge/Shaders` | URP shaders for the frosted glass, holograms, safety fields |
| `tools/` | The asset exporter and the canvas graphics (a port of `textures.ts`) |

Third-party content in the build: the Panda model from robosuite 1.4.0 (MIT) with
`franka_description` meshes (Apache 2.0), link 5's lettering changed to read BRIDGE (see
`assets/embodiments/panda/README.md`); Poly Haven props, textures and HDRI (CC0); Figtree
and IBM Plex Mono (SIL Open Font License); Lucide icons (ISC); the narrator's voice, Kokoro-82M
(Apache 2.0) through kokoro-onnx (MIT); Unity's runtime under Unity's terms. The app's About
screen lists them too.
