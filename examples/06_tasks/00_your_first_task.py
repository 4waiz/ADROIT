# SPDX-License-Identifier: Apache-2.0
"""Your first task

A task is a short document: what is on the bench, and when it counts as done.

``bench.task`` reads the document and ``bench.Env`` builds it, robot and all.
The studio opens with the goal beside the scene. Drag the cube into the square
and the goal turns true.
"""

import robosimstudio as rss
import robosimstudio_bench as bench
from robosimstudio.assetlib import register_asset
from robosimstudio.domains.rigid import Body

TASK = """
(define (problem cube_in_square)
  (:language put the cube in the square)
  (:staging (:scene bench_075) (:embodiment panda) (:physics mujoco_500hz@v1))
  (:objects cube - cube_50mm)
  (:regions (square (:ranges ((0.05 -0.20 0.15 -0.10)))))
  (:goal (>= (containment cube square) 1.0)))
"""


def main() -> None:
    # The document places a "cube_50mm"; this says what one is.
    cube = rss.box(name="cube", half=(0.025, 0.025, 0.025), at=(0.0, 0.1, 0.03))
    register_asset(Body.from_spec(cube, name="cube_50mm"))

    env = bench.Env(bench.task(TASK))
    env.reset()
    rss.view(env)


if __name__ == "__main__":
    main()
