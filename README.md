# JMT Liftoff Leaderboard

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for
[Liftoff](https://store.steampowered.com/app/410340/) that puts the
[JMT FPV](https://test.geekhost.uk) leaderboards and pilot profiles inside the game's menus.

- **Leaderboard, in the main menu, opens the JMT board.** It's laid out like a track page on
  the JMT site: the course, its stats, the podium, the board with Top 10 / 25 / All and a
  pilot search, and the latest laps. A list on the left holds every course with a JMT board.
- **The pause menu's leaderboard opens the JMT board for the course you're flying.**
- **JMT Profile** is a new main-menu button. It opens your profile as the site shows it: JMT
  Rating, Consistency Rating, where you place, your latest personal bests, and your best lap on
  every course. Click any pilot on a board to see theirs.
- **Liftoff's own leaderboard is one click away.** The "Liftoff leaderboard" button at the top
  opens the game's screen exactly as before, with its ghosts and replays.

JMT boards are built from laps flown in JMT rooms, and they're kept for Steam Workshop courses.
Liftoff's own courses have no JMT board, so the plugin says so and offers Liftoff's leaderboard
instead.

## Install

1. Install [BepInEx 5](https://docs.bepinex.dev/articles/user_guide/installation/index.html)
   into Liftoff, if it isn't there already.
2. Download `JmtLiftoffLeaderboard.dll` from the
   [latest release](https://github.com/geekhostuk/jmt-liftoff-leaderboard/releases/latest).
3. Put it in `Liftoff/BepInEx/plugins/`.

There is nothing to set up. [docs/install.md](docs/install.md) has the details, and covers
Liftoff Control, which can install the plugin for you.

## Finding you

The plugin works out which pilot is you from the ids the game already signs you in with: your
Liftoff (Photon) user id, or your Steam id. It asks the JMT site which pilot those ids belong
to, and highlights that pilot on every board. The ids are used only for that lookup; the site
never shows them to anyone.

If it can't find you, open a board you've flown, click your row and press **This is me**. The
choice is saved in the plugin's config file.

## Settings

`BepInEx/config/uk.co.geekhost.jmtliftoffleaderboard.cfg` is created on first launch:

| Section | Key | Default | What it does |
|---|---|---|---|
| `Site` | `Url` | `https://test.geekhost.uk` | The JMT site to read from. |
| `Pilot` | `PublicId` | *(empty)* | Your JMT pilot id. Leave it empty to be found automatically. |
| `Pilot` | `LastGameUserId` | *(filled in)* | The game id you were last found by. You don't need to edit it. |
| `Menu` | `ReplaceMainMenuLeaderboard` | `true` | Main-menu Leaderboard opens the JMT board. |
| `Menu` | `ReplacePauseMenuLeaderboard` | `true` | The pause menu's leaderboard opens the JMT board. |
| `Menu` | `ShowProfileButton` | `true` | Add JMT Profile to the main menu. |

## Other plugins

- **LiftoffReplayPlus:** works as before. Its leaderboard features live on Liftoff's own
  leaderboard screen, which this plugin never changes; "Liftoff leaderboard" gets you there.
- **Liftoff.MovingObjects:** no overlap.
- **JMT Liftoff Mod and Liftoff Control:** independent of this plugin, and happy to run beside
  it. Liftoff Control can install it into a profile.

## Looks

The screens use the JMT site's colours, cards and layout. If you have the site's fonts
(Rajdhani, Inter, JetBrains Mono) installed, they're used; otherwise headings use the game's own
menu font and times use your system's monospace font.

## Building

See [docs/building.md](docs/building.md). Releases are described in
[docs/versioning.md](docs/versioning.md).

## License

[MIT](LICENSE).
