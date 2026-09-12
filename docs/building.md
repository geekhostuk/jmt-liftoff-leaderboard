# Building from source

## Requirements

- **.NET SDK** 8.0 or later. The project targets `net472`, and any modern SDK builds it.
- A **Steam copy of Liftoff**. The project references the game's managed DLLs and
  BepInEx's core DLLs directly from your install.
  - **No game or BepInEx assemblies are committed to this repository**, and none may
    ever be: they are copyrighted.
  - `<Private>false</Private>` on every reference keeps them out of the build output too.
- **BepInEx 5.x** in the Liftoff folder. An inactive install renamed to `_BepInEx` is
  also detected.

## Environment variables

| Variable | Default / detection | Purpose |
|---|---|---|
| `LIFTOFF_DIR` | Tried in order: `$HOME/.local/share/Steam/…/Liftoff`, `$HOME/.steam/steam/…/Liftoff`, `C:\Program Files (x86)\Steam\steamapps\common\Liftoff` | The Liftoff install folder (the one containing `Liftoff_Data`). |
| `BEPINEX_CORE_DIR` | Tried in order: `$(LIFTOFF_DIR)\BepInEx\core`, `$(LIFTOFF_DIR)\_BepInEx\core` | The folder containing `BepInEx.dll` and `0Harmony.dll`. |

A standard Steam install on Linux or Windows is found without any setup.

## Build

```bash
dotnet build JmtLiftoffLeaderboard.slnx -c Release
```

The output is `src/JmtLiftoffLeaderboard/bin/Release/net472/JmtLiftoffLeaderboard.dll`.
Copy just that DLL into `Liftoff\BepInEx\plugins\` (see [install.md](install.md)).

To stamp a build with its commit, pass `BuildRevision`:

```bash
dotnet build src/JmtLiftoffLeaderboard/JmtLiftoffLeaderboard.csproj -c Release -p:BuildRevision=a1b2c3d
```

## Notes

- **Obfuscation.** Liftoff's `Assembly-CSharp` is obfuscated, so the plugin doesn't
  reference it at all.
  - It finds game types at runtime by names that survive obfuscation: serialized
    field names, Unity GameObject names, and the few class names left readable, such
    as `InGameMenuMainPanel` and `CurrentContentContainer`.
  - Every lookup fails soft. When a game update moves something, the feature it
    supports switches off and says so in the BepInEx log, and the game keeps running.
- **One DLL for both platforms.** The plugin is plain IL targeting `net472`, so the
  same DLL runs under BepInEx on both the Windows and the native Linux builds.
- **Checking the layout.** In game, press **Ctrl+Shift+F9** on the main menu. The plugin
  opens the board and then your profile, scrolls each one to the top, middle and bottom,
  and saves a screenshot of every step to `BepInEx/JmtLeaderboardScreens/`.
- **Releases.** To build and publish the binary for a release, use
  [`scripts/release-dll.sh`](../scripts/release-dll.sh). See
  [versioning.md](versioning.md#release-assets).
