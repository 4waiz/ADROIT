# SPDX-License-Identifier: Apache-2.0
"""Core concepts

A scene you declare, a sim that runs it, and a ref on each thing you added.

A :class:`robosimstudio.Scene` is a list of specs in a named place;
:meth:`robosimstudio.Scene.build` compiles it into a :class:`robosimstudio.Sim`,
which owns the state and steps it; each ``add`` returns a
:class:`robosimstudio.Ref`, which reads the live state each time it is asked.
Every ``at=``, the robot's mount included, is measured from the middle of the
place's work surface, so the same scene works on ``"bench_075"`` and ``"void"``.
"""

import robosimstudio as rss


def main() -> None:
    # 100 mm over the spot the Panda's hand points down at, wherever the work surface is.
    release = (-0.143, -0.115, 0.1)

    sims = {}
    for cell in ("bench_075", "void"):  # a bench 750 mm high, then a bare floor
        # A Scene is a declaration. Nothing is simulated until build(). MUJOCO_500HZ
        # puts the arm and the free bodies in one engine, so the hand can lift the cube.
        scene = rss.Scene(cell=cell, name=cell, physics=rss.MUJOCO_500HZ)
        cube = scene.add(rss.box(name="cube", half=(0.025, 0.025, 0.025), at=release))
        ball = scene.add(rss.sphere(name="ball", radius=0.03, at=(0.0, 0.12, 0.2)))
        scene.add(rss.robot("panda"))

        # build() compiles it into a Sim, and the refs from add() now read that Sim.
        sim = scene.build()
        surface = rss.get_cell(cell).surface_z
        print(f"{cell}: the work surface is at z={surface:.3f} m and the scene holds {sim.names()}")
        print(f"  released  cube z={cube.position[2]:.3f}  ball z={ball.position[2]:.3f}")

        sim.run(150).sync()  # 150 control steps are 3 s at 50 Hz; then wait for the GPU before reading
        print(f"  at rest   cube z={cube.position[2]:.3f}  ball z={ball.position[2]:.3f}  t={sim.time:.2f} s")

        # A name finds the same handle, and finds the place's own furniture too.
        print(f"  by name   sim['cube'] is cube: {sim['cube'] is cube}")

        sim.reset()  # everything back where the scene declared it
        print(f"  reset     cube z={cube.position[2]:.3f}  t={sim.time:.2f} s")
        sims[cell] = sim

    rss.view(sims["bench_075"])


if __name__ == "__main__":
    main()
