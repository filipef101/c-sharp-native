# Troubleshooting

The listener logs these lines and still paints a screen.

## Corrupt state

`state-load rejected reason=corrupt`

Delete `files/nativepoc-state.bin` and start again. Those bytes are not a checkpoint. The app paints the counter.

## Old checkpoint

`state-load rejected reason=old-version`

The file was written by an older build. Delete `files/nativepoc-state.bin` and start again. The app paints the counter.

## Unknown root

`startup root unknown`

The `nativepoc_root` extra is not a staged screen. Omit it, or pass a catalog id. The app paints the counter.

## Stale token

`hotreload rejected reason=stale-token`

The process wrote a new `files/nativepoc-hotreload.token`. The watcher reads that file and retries. Save the screen again.

## Rejected program

`startup render rejected`

The baked program is older than this interpreter. The app paints the compiled counter. Rebuild so the catalog is regenerated.

## Republish needed

`a new binding needs a republish`

A new `[JniMethod]` is not in this binary. Publish the listener again. A literal or a method that is already registered hot-reloads.
