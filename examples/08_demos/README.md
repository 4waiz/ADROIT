# Demos

Three bimanual YAM jobs from the [project page](https://robosimstudio.github.io/). Each opens the studio, and
**Run** under *Routine* plays the job by dragging the arms' gizmos and pressing
their **Grip** buttons the way a person would; you can stop it and take over by
hand.

| Demo | The job | What holds the thing |
| --- | --- | --- |
| [`wipe_the_bar/`](wipe_the_bar/README.md) | Take a sponge off the mat, wipe a marker stripe off a grab bar while the other arm steadies it, and put the sponge back. | The jaws close on the foam by contact; once they have, a latch holds it through the strokes and lets go on **Open**. |
| [`route_the_cable/`](route_the_cable/README.md) | Two arms lay a 600 mm cable past two spools and seat its end in a port. | Friction alone. |
| [`fold_the_towel/`](fold_the_towel/README.md) | Pinch the far edge of a terry towel, fold it over in a low arc and lay it down. | Friction alone. |

```bash
uv run examples/08_demos/route_the_cable/route_the_cable.py
```

Each scene follows a task from the project's real-to-sim work; its README says
where it differs and why. `wipe_the_bar`'s sponge is a SuperDex body, so it
needs the `[superdex]` extra (Python 3.12); the other two run on a base install.
