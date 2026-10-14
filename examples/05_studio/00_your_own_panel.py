# SPDX-License-Identifier: Apache-2.0
"""Your own panel

A folder of your own on the studio page: a slider, a button that tosses the cube, and a readout of its height.

``page=`` on :func:`robosimstudio.view` is called once after the studio's panels
are built; ``studio.gui`` is viser's GUI API, so code for a bare ``ViserServer``
drops in. A viser callback runs on viser's thread and may only assign; touch the
simulation in ``studio.each_step``, which runs on the main thread once per control
step. To pick the cube up, drag the hand's gizmo over it and down, press **Close**
under **Grip** and lift.
"""

import numpy as np

import robosimstudio as rss


def main() -> None:
    bench = rss.get_cell("bench_075").surface_z

    # The arm and the cube in one MuJoCo island, so the hand can lift what it closes on.
    scene = rss.Scene(cell="bench_075", name="your_own_panel", physics=rss.MUJOCO_500HZ)
    cube = scene.add(rss.box(name="cube", half=(0.025, 0.025, 0.025), at=(0.0, 0.0, 0.2)))
    scene.add(rss.robot("panda"))
    sim = scene.build()

    # MuJoCo steps joint coordinates: a toss written to ``body_qd`` moved the cube
    # 0.0 mm. So it goes to ``state.joint_qd`` at the cube's free joint, the one
    # whose child is its body.
    body = int(cube.bodies[0])
    joint = int(np.flatnonzero(sim.model.joint_child.numpy() == body)[0])
    dof = int(sim.model.joint_qd_start.numpy()[joint])

    def page(studio):
        wanted = {"toss": False}  # written on viser's thread, read on the main thread
        with studio.gui.add_folder("Cube"):
            speed = studio.gui.add_slider("toss speed (m/s)", min=0.5, max=3.0, step=0.1, initial_value=1.5)
            button = studio.gui.add_button("Toss the cube")
            height = studio.gui.add_number("height above the bench (mm)", 0.0, disabled=True)
        button.on_click(lambda _: wanted.update(toss=True))

        def each_step() -> None:
            if wanted["toss"]:
                wanted["toss"] = False
                # Both halves of the double buffer, so the next step reads it whichever it starts from.
                for state in (sim.pipeline.state_0, sim.pipeline.state_1):
                    velocity = state.joint_qd.numpy()
                    # A free joint's six dofs, in Newton's order: linear, then angular.
                    velocity[dof : dof + 6] = (0.0, 0.0, speed.value, 0.0, 0.0, 0.0)
                    state.joint_qd.assign(velocity)
            height.value = round(1000.0 * (cube.position[2] - bench), 1)

        studio.each_step(each_step)

    rss.view(sim, page=page)


if __name__ == "__main__":
    main()
