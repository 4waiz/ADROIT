# SPDX-License-Identifier: Apache-2.0
"""Cable between two posts

A cable clamped by both ends on top of two posts: hook it with a gripper or drag its middle, and it springs back.

``anchors`` holds each end's position, direction and twist where it was built:
one end clamped makes a cantilever, both make a span that sags under its own
weight. The cable is built 2 mm above the post tops so no contact fights the
clamps.
"""

import robosimstudio as rss


def main():
    span = 0.5  # from post to post [m]
    radius = 0.008  # of the cable [m]
    segments = 24
    post_height = 0.16  # [m]

    scene = rss.Scene(cell="bench_075", name="cable_between_posts")
    scene.add(rss.robot("yam_bimanual"))
    for name, y in (("post_start", -span / 2), ("post_end", span / 2)):
        scene.add(rss.post(name=name, radius=0.02, half_height=post_height / 2, at=(0.0, y, post_height / 2)))
    scene.add(
        rss.rod(
            name="cable",
            length=span,
            radius=radius,
            segments=segments,
            at=(0.0, -span / 2, post_height + radius + 0.002),  # 2 mm clear of the post's top
            along=(0.0, 1.0, 0.0),
            anchors=("start", "end"),
        )
    )
    sim = scene.build(control_mode="joint_position")

    rss.view(sim)


if __name__ == "__main__":
    main()
