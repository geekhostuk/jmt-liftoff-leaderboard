# Installing JMT Liftoff Leaderboard

## With Liftoff Control

If you run Liftoff through Liftoff Control, install **JMT Leaderboard** from a profile's mod
list. It downloads the latest release, checks it against `SHA256SUMS.txt`, and puts it in that
profile's `BepInEx/plugins/`.

## By hand

1. **BepInEx 5.** Liftoff needs BepInEx 5.4 or later installed.
   - On Windows, unzip BepInEx into the Liftoff folder (the one with `Liftoff.exe`) and start
     the game once.
   - On Linux, use the native build's `run_bepinex.sh`, or Liftoff Control, which sets
     BepInEx up for you.
2. **The plugin.** Download `JmtLiftoffLeaderboard.dll` from the
   [latest release](https://github.com/geekhostuk/jmt-liftoff-leaderboard/releases/latest) and
   put it in `Liftoff/BepInEx/plugins/`.
3. **Check it.** Start Liftoff. The main menu gains a **JMT Profile** button, and
   `BepInEx/LogOutput.log` has a line starting `JMT Liftoff Leaderboard`.

To check the download first:

```bash
sha256sum -c SHA256SUMS.txt
```

## Settings

Everything works with the defaults. The config file is
`BepInEx/config/uk.co.geekhost.jmtliftoffleaderboard.cfg`; the [README](../README.md#settings)
lists what's in it.

## Removing it

Delete `JmtLiftoffLeaderboard.dll` from `BepInEx/plugins/`. The menus go back to Liftoff's own
the next time the game starts.

## If something doesn't show up

Look in `BepInEx/LogOutput.log` for lines from `JMT Liftoff Leaderboard`. If a Liftoff update
moves a menu button the plugin hooks, it leaves that button as the game made it, logs a warning
saying so, and carries on with the rest.
