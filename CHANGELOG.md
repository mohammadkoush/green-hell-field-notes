# Field Notes — changelog

> **Bleeding edge.** This is an experimental build, not a stable release. It is played daily on one
> save with one set of mods — which is not your save, and I have no way to test your game.
>
> **Field Notes does not touch your save.** Its notebook is a plain text file next to the DLL, keyed
> to your save slot, which you can read, hand-edit or delete. Nothing here can cost you anything in
> game. That is a deliberate design constraint, not luck.

## Current

**The minimap waits until the game is actually playable.**
It used to be drawn over the loading screen, mapping a world that had not arrived yet. It now holds
back until the game's own signals agree that loading is finished — including the terrain around you
having streamed in, which is the difference between "loaded" and "there is ground under your feet".
This is not a timer: a fixed delay is right on one machine and wrong on every other, and it cannot
tell loading from sitting in the main menu. Both the switch and a small extra settle are at the
bottom of the MINIMAP tab.

**The minimap is a circle.**
The square panel and its border are gone. A translucent disc fills the ring instead, and anything
beyond the ring floats over the world with nothing behind it.

**A compass.**
N, E, S and W sit around the ring and turn with your heading, outlined so they stay readable against
sunlit ground and dark canopy alike. GPS coordinates read out underneath, taken from the same call
your watch uses — so the map and the watch can never disagree.

**Containers appear only once you have lost track of them.**
Put a coconut shell down at camp and it stays off the map. Leave it for an hour of game time and it
appears, because by then you have probably forgotten it. Walk back to it and it goes quiet again.
This is about what *you* know, not about the object.

**Frogs are their own category.**
A poison dart frog is only dangerous if you reach out and pick it up, which is not the same kind of
danger as a snake that can come at you. Its own colour, its own switch.

**Smaller changes**
- Player marker at half size — it was covering the middle of its own map.
- Small threats measured in metres; remembered spawn marks fade back so live things read first.
- Settings window on **Keypad1**, tabbed, with every setting explaining itself when you hover it.
- Item names are asked of the game rather than read off a hand-typed list. Twenty-four of the
  forty-three names in that list had never existed — and the code had been written to skip them
  silently rather than say so.

## Icons

The 41 map icons come from [game-icons.net](https://game-icons.net) under CC BY 3.0. Attribution for
every one is in `ATTRIBUTIONS.md`. You can drop your own PNG into the `icons` folder to replace any of
them — two of the icons shipped here arrived exactly that way.

## Requirements

Green Hell 1.x, BepInEx 5.4.x (x64). Independent of my other mods — install it alone if you like.
