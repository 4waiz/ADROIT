# SPDX-License-Identifier: Apache-2.0
"""Hello world

The smallest RoboSimStudio program: a bench, a robot arm, and a cube that lands under its hand.

``build()`` compiles the scene, 150 steps let the cube land, and ``rss.view``
opens the studio. Drag the hand down onto the cube, press **Close** and lift: the
fingers hold it by friction, which needs ``rss.MUJOCO_500HZ``; under the default
physics a hand can push a body but not pinch it. Or click the cube to drag it by
a handle; it stays fully dynamic.
"""

import robosimstudio as rss


def main() -> None:
    scene = rss.Scene(cell="bench_075", physics=rss.MUJOCO_500HZ)  # a bench 750 mm high
    # ``at`` is measured from the middle of the bench top: 100 mm above the
    # spot under the Panda's hand, which starts 231 mm over the bench, pointing down.
    cube = scene.add(rss.box(name="cube", half=(0.025, 0.025, 0.025), at=(-0.143, -0.115, 0.1)))
    scene.add(rss.robot("panda"))

    sim = scene.build()
    sim.run(150).sync()  # 150 control steps are 3 s at 50 Hz; sync before reading

    height = cube.position[2]  # a Ref reads the live state
    print(f"the cube came to rest at z={height:.3f} m, on a bench whose top is at 0.750 m")

    rss.view(sim)


if __name__ == "__main__":
    main()
