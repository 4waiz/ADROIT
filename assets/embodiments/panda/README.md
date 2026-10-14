# Franka Panda — robosuite's model, as the VAB tool-sort export carries it

What the `panda`, `panda_robotiq` and `panda_robotiq_roll` embodiments import
(`robosimstudio/robots/embodiments/panda.py`). The library registers `panda`,
so the library ships its geometry. The kinematics URDF's parity measurements
are in `assets/PROVENANCE.md`.

## Where it came from

* **robosuite 1.4.0** (`ARISE-Initiative/robosuite`, MIT): its Panda arm
  (`robosuite/models/assets/robots/panda/`) and its Franka hand
  (`robosuite/models/assets/grippers/`, `panda_gripper`). `LICENSE` beside this
  file is robosuite's licence text, fetched on 2026-10-04 from
  `https://raw.githubusercontent.com/ARISE-Initiative/robosuite/master/LICENSE`;
  the `v1.4.0` tag's file is byte-identical to it.
* **Exported** from VAB-industrial's `ToolSortPickPlace` scene (robosuite 1.4.0,
  MuJoCo 3.3.3), which merges the robot with its arena, and dumped by the
  benchmark collection's scene-dump script. Bodies, joints, inertias, limits and
  names are robosuite's, `robot0_` and `gripper0_` prefixes included. The export
  and cut scripts are not in this repo; the checked-in files are the source of
  truth.
* **The mesh geometry originates from Franka's `franka_ros` robot description**
  (`franka_description`, Apache-2.0), which robosuite's Panda meshes were made
  from.

## Files

| File | What it is |
| --- | --- |
| `panda_standalone.xml` | The arm with its Franka hand: the export's `robot0_base` subtree at its own origin, with its nine `<position>` actuators and two sensors. What `panda` imports. |
| `panda_nohand_standalone.xml` | The same arm ending at `robot0_right_hand`, with the seven arm actuators, so a hand can be welded on at build time. What `panda_robotiq` and `panda_robotiq_roll` import. |
| `assets/` | The 61 meshes those two files name, and nothing else: eleven STL collision meshes and fifty OBJ visual meshes, 39.9 MB. |
| `LICENSE` | robosuite's MIT licence, which the two MJCFs and the meshes arrive under. |
| `panda_kinematics.urdf` | **A different source**: `example-robot-data`'s `panda_description` (itself Franka's `franka_description`, Apache-2.0), stripped to joints, links, limits and inertials. Never simulated, only solved against. See `assets/PROVENANCE.md`. |
| `LICENSE.example-robot-data` | example-robot-data's BSD 3-Clause licence, which `panda_kinematics.urdf` arrives under. |

Both MJCFs say `meshdir="."` and name every mesh as `assets/<file>`, so they
resolve against this directory as they stand.

## What was changed

1. **`<position>` actuators instead of robosuite's `<motor>`s.** Newton reads a
   joint target only for a position-driven DOF, so a torque-actuated export
   takes zero torque from every target. The force limits in the files are
   ±80 N·m on the first five joints, ±12 on the last two and ±20 N on each
   finger.
2. **Cut out of the arena.** The merged scene holds bins, a workbench and tools
   besides the robot. `panda_standalone.xml` keeps the `robot0_base` subtree and
   drops the `mount0_*` pedestal. Its height is now `EmbodimentSpec.base_offset`.
3. **The hand removed, for the second file.** `panda_nohand_standalone.xml` is
   the standalone with the `gripper0_right_gripper` subtree and every mesh,
   actuator and sensor naming it dropped: the seven arm actuators remain, and 57
   of the 61 meshes are named.
4. **Mesh paths flattened** by the dump into one `assets/` directory, where
   robosuite keeps one directory per link.

The meshes themselves are robosuite's bytes. Checked against the `v1.4.0` tag on
2026-10-04, all 61: the eleven STLs are byte-identical, and the fifty OBJs are
identical apart from line endings (CRLF here, LF upstream). Their `mtllib` lines
name `.mtl` files that neither the export nor this directory carries; the
colours come from the MJCFs' own `<material>` elements.

## The copy the benchmark's collection keeps

The two MJCFs and the 61 meshes are byte copies of files in the benchmark
collection (`vabar/`, not in this repo), which keeps the originals beside its
tool-sort export; its merged scene MJCFs name the same meshes at the same
relative paths. `tests/test_panda_vendored.py` holds `assets/` to exactly the
meshes named and, when the collection is present, fails while the copies
differ from the originals.

**Measured across the copy** (2026-10-04): a `panda` and a `panda_robotiq`
alone on `bench_075`, and a level-1 and a level-2 tool-sorting scene, built
from the collection's files and then from these. Every count agrees — 16 bodies,
73 shapes, 16 joints and 19.02 kg for the `panda` scene; 24, 88, 26 and 19.47 kg
for `panda_robotiq` — and so does every model array compared, bit for bit: 124
of them across the four scenes (masses, inertias, shape transforms, joint
frames, limits, gains), with the hash of every mesh's vertices. Over the first
200 steps the joint positions differ by at most 6.7e-13 for `panda` and 6.3e-7
for `panda_robotiq` (radians; metres for the fingers), no more than two runs of
one build differ by: 1.5e-13 and 1.2e-6, the 2F-85's linkage being in contact.
