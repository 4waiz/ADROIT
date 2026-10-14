# SPDX-License-Identifier: Apache-2.0
"""Add a robot

A robot the library never registered, declared in this script over the Panda's MJCF and driven from the studio.

Handing the spec to :func:`robosimstudio.robot` builds it as a registered name
would: an arm panel with a gizmo, IK, and a hand that grips. Drag the gizmo down
onto the cube, press **Close** and lift. To use your own robot, point ``asset``
at its MJCF or URDF and rename the joints.
"""

import robosimstudio as rss


def main() -> None:
    home = (0.0, 0.196, 0.0, -2.618, 0.0, 2.942, 0.785)  # the posture it starts in [rad]

    my_panda = rss.EmbodimentSpec(
        name="my_panda",  # the scene's name for it; not registered
        asset=rss.AssetSpec(path="embodiments/panda/panda_standalone.xml"),
        # The base, in the work-surface frame: 600 mm behind the middle of the
        # bench, 115 mm to one side and 132 mm up, which is its pedestal's height.
        base_offset=(-0.6, -0.115, 0.132, 0.0, 0.0, 0.0, 1.0),
        hull_colliders=True,  # convex hulls for the mesh colliders: cheaper, and invisible on camera
        arms=(
            rss.ArmSpec(
                name="right",
                joint_names=tuple(f"robot0_joint{i}" for i in range(1, 8)),  # the file's own labels, in order
                ee_body_name="robot0_link7",  # the wrist, where the hand bolts on
                gripper="franka_hand_robosuite",  # a registered hand: its fingers and tool centre are measured facts
                home_q=home,
            ),
        ),
    )

    # MUJOCO_500HZ puts the arm and the cube in one engine, so the fingers hold by friction.
    scene = rss.Scene(cell="bench_075", name="add_a_robot", physics=rss.MUJOCO_500HZ)
    # On the bench, under the hand: ``home`` points the tool centre down from 231 mm over this spot.
    scene.add(rss.box(name="cube", half=(0.025, 0.025, 0.025), at=(-0.143, -0.115, 0.025)))
    scene.add(rss.robot(my_panda))
    sim = scene.build(control_mode="joint_position")

    sim.run(60).sync()
    tool = sim.arm("right").ee_pose(sim.state)[0]
    print(f"{my_panda.name} stands with its tool centre at ({tool[0]:.3f}, {tool[1]:.3f}, {tool[2]:.3f}) m")

    rss.view(sim)


if __name__ == "__main__":
    main()
