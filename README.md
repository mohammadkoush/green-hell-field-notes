# Field Notes

A Green Hell map you have to earn. Nothing is on it until you've been there.

## Two surfaces, one notebook

**The game's own map** gets everything you've found — permanently, by category, with the stock of
each larder point as of the last time you stood close enough to see it.

**The minimap is a halo, not a map.** A threat is legible at exactly one distance — its own
detection radius — and then goes quiet. Walk toward a snake and its mark slides onto the ring, sits
there a moment, and drops off the inside edge. You're left knowing something was detected, roughly
which way, and nothing else.

Three things fall out of that, and all three are the point:

- It's a **sensor**, not a display. A sonar ping.
- **You** do the remembering. The information is real but perishable, so the value sits in your
  attention rather than in the UI.
- It **rewards movement**. Standing still surfaces nothing; the band only sweeps things up as you
  walk. The opposite of a radar you can camp on.

And it answers the obvious objection to putting a minimap in this game at all: Green Hell is built
on being lost, and a minimap normally deletes that. **A ring that forgets doesn't.**

Resources don't ping. They're drawn plainly, because they're your own larder and there's nothing to
spoil — you already went there.

## Keys

| Key | Does |
|---|---|
| **Keypad 3** | Minimap on / off |
| **Keypad 8** | Size — Small / Medium / Large |
| **Keypad 9** | What do I know? A quick count |
| **Keypad 4** | Drop your own pin here |
| **Keypad 0** | Remove your nearest pin (never touches anything you discovered) |
| **Keypad 2** | Markers on the game's own map, on / off |
| **Keypad 1** | Map diagnostics → `mapdump.txt` |

Sizes are a **share of screen height** — 16% / 22% / 30% — not pixel counts. A fixed 220px box is a
postage stamp on a 4K panel and half the screen on a laptop.

## What it is not

- It doesn't track animals. It marks the place a jaguar **comes from**, never the jaguar.
- It doesn't tell you about places you haven't visited.
- It doesn't tell you a coconut regrew across the island. Stock is **as-of-last-seen**, so the map
  can be out of date. That's a feature: a notebook that's always right is just a HUD.
- **It never writes to your save.** Your notebook is `fieldnotes-<save>.txt` next to the DLL — plain
  text, one place per line. Read it, hand-edit it, delete a line to forget something.
- Wood is deliberately not marked. Abundance is the disqualifier, not usefulness: marking what's
  everywhere marks nothing. The rule is **worth walking to**.

## Detection radii are the difficulty

Under `[Detection]`. Big threats ping early because you need room to react; small ones ping late,
because the fright *is* the content — close, but not close enough to give the game away.

Defaults: predators 55 m, savages 60 m, snakes 22 m, critters 16 m.

`BandMetres` is how thick the detection band is, and `PingHoldSeconds` how long a ping lingers and
fades. Too thin a band and a running player crosses it between frames and never sees it.

## Known gaps

**Savages have no spawn points to mark.** This was checked, not assumed: animals come from
`AIs.AISpawner` objects placed in the level, each carrying a species. Savages don't work that way —
`EnemyAISpawnManager.SpawnWave` puts them around a firecamp group, or at *your* position, on a
timer. There is no fixed point, so nothing was invented. The category and its ring exist; nothing
feeds them yet. Making it mean something (the camps they come from? a history of where one attacked
you?) is a design decision, not a lookup.

**Map marker orientation.** The world→map maths is derived from the game's own
`Player.GetGPSCoordinates`, but which way the map page's own axes run isn't written down anywhere.
If markers come out mirrored or rotated, `[Map] FlipU / FlipV / SwapUV` fix it without a rebuild —
and **Keypad 1** writes a diagnostic dump that says where you are in map space.

## Build

```
powershell -ExecutionPolicy Bypass -File build.ps1            # build + deploy
powershell -ExecutionPolicy Bypass -File build.ps1 -NoDeploy  # build only
```

Stock .NET Framework `csc.exe` — no Visual Studio or SDK. Reference assemblies come from the game
install, so the build always matches the installed version. Close the game before deploying; it
holds the DLL locked and the script says so rather than failing opaquely.

Source is C# 5 (no `?.`, no `$""`, no `??=`) because that's what the stock compiler accepts.
