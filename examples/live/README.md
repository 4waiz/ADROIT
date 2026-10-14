# live: a page that rebuilds `scene.py` every time you save it

Point a coding agent at this folder: it edits `scene.py`, the open page
rebuilds on every save, and the agent checks the result with snapshots. The
page is the studio, so you can drag things on it between edits.

What is here:

- `scene.py`: the starter scene and the agent's working file. Bind `routine`
  (or `routines = {name: fn}`) to get a Run button.
- `ctl`: the command line onto the running page; it also runs from the
  repository root as `examples/live/ctl`. It uses the checkout's `.venv`, else
  `$PYTHON` or `python3`, so it also works after
  `pip install "robosimstudio @ git+https://github.com/robosimstudio/robosimstudio"`.
- `run/`: made on first use and not committed. It holds the socket, the
  server's log and every snapshot.

## Start

```bash
cd examples/live
./ctl up                                    # the page; prints its URL, http://localhost:8080 or the next free port (--share adds a public URL)
claude --permission-mode acceptEdits        # Claude Code; edits go through without a prompt
codex -a on-request -s workspace-write      # or Codex; the sandbox may write here and reach the socket
```

The page opens on the starter scene: two arms on a bare floor. On a remote GPU
box, pick an idle card with `CUDA_VISIBLE_DEVICES` and use `--share` or an SSH
tunnel (`ssh -L 8080:localhost:8080 <box>`). The page listens on all interfaces
(viser's default), so on a shared network firewall the port or use the tunnel.
`./ctl quit` stops the server.

Both agents read the procedure from [`AGENTS.md`](../../AGENTS.md), so the
prompt only has to say what to build. Start each prompt below with this
line:

```
A live page is running: it rebuilds scene.py on every save. Use ./ctl to inspect it (see ./ctl --help).
```

## The prompt, with a clip

Drop the clip beside the checkout (not in it) and say:

```
../../../<clip>.mov is a real clip of a person doing one thing at a table.
Rebuild it in scene.py with this library's core API: the table, two YAM
arms where the person sat, what is on the table, a camera where the phone
was, what stands behind the table, and then the arms doing what the person
did, bound as `routine` in scene.py so I get a Run button on the page. Look
at the pairs, the look sheets and the strip with your own eyes, not only the
numbers: the things in their detail, the background, what happened. I am
watching the page. When you are done, report the readings and what you saw:
the first frame against the clip, the gate, and the take against the clip's
timeline. Say in one line what you are about to do before each edit.
```

## The prompt, with your own photo

Make a `ref/` folder here, drop your photo into `ref/`, and say:

```
ref/<your photo> is a real photo of a robot bench. Rebuild that scene in
scene.py with this library's core API, one thing per save so I can watch each
one appear on the page: the bench, the robot, what is on the bench, and a
camera where the photo was taken from. Measure sizes and placements from the
photo. After each save, snapshot through your camera beside the photo
(./ctl snapshot --beside ref/<your photo>), look at the pair, and adjust until
they line up. When the scene matches, make the robot do something to an object
and step it on the page so I can watch. Say in one line what you are about to
do before each edit.
```

## The prompt, with no photo

```
Build this in scene.py with this library's core API, one thing per save so I
can watch each one appear on the page: two YAM arms, a towel and a box on a
bench. After each save, take a snapshot and look at it before the next
edit. When the scene is built, make an arm push the box and step it on the
page so I can watch. Say in one line what you are about to do before each
edit.
```

## Where things go

Everything the page writes lands in `run/` (snapshots, comparisons, takes,
`log.jsonl`, `serve.log`); clip stills and `timeline.json` go in `ref/`. The
full list, every `ctl` command, the contract for `scene.py` and the frames and
units are in [`REFERENCE.md`](REFERENCE.md#ctl).

The result is `scene.py` itself. Copy it aside to keep it;
`git checkout examples/live/scene.py` puts the starter back.
