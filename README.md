# JMT Liftoff Leaderboard

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for
[Liftoff](https://store.steampowered.com/app/410340/) that puts the
[JMT FPV](https://test.geekhost.uk) leaderboards and pilot profiles inside the game's menus,
and your Consistency Rating on screen while you fly.

- **Leaderboard, in the main menu, opens the JMT board.** It's laid out like a track page on
  the JMT site: the course, its stats, the podium, the board with Top 10 / 25 / All and a
  pilot search, and the latest laps. A list on the left holds every course with a JMT board.
- **The pause menu's leaderboard opens the JMT board for the course you're flying.**
- **JMT Profile** is a new main-menu button. It opens your profile as the site shows it: JMT
  Rating, Consistency Rating, where you place, your latest personal bests, and your best lap on
  every course. Click any pilot on a board to see theirs.
- **Your Consistency Rating while you fly.** See [below](#consistency-rating-while-you-fly).
- **The course's board around your place while you fly**, with the time to beat for the
  next place. See [below](#track-board-while-you-fly).
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

## Consistency Rating while you fly

A panel in the corner of the screen shows your Consistency Rating (CR) as it stands with
tonight's flying counted in:

- **The rating and your license**, and how far it has moved tonight.
- **A bar to the next license.** The tick on it is where you started the night.
- **Your last 20 attempts as pips:** green for a finished lap, red for a failed attempt.
- **What the next license takes:** "9 clean laps to A license · a crash costs 2 more". When
  the license you hold is close to slipping, it says "2 crashes from C license" instead.
- **Tonight's tally and your clean-lap streak.**

CR is worked out from failed attempts per lap over your last hundred or so laps, and a license
comes with it: Rookie, then D, C, B, A and Pro. The site's profile page explains the scale.

**How it counts.** The plugin watches your own laps and resets in the game, by the same rules
the JMT rooms use: a reset part way through an attempt, between 5 seconds and 2 minutes in, is a
failed attempt, and a reset just after finishing a lap isn't. So the rating moves the moment you
finish a lap or crash, before the room has sent anything to the site. The site's own numbers are
fetched when you start flying, and again after a few quiet minutes.

**Only JMT rooms count.** CR is built from rooms whose Liftoff Control reports to the JMT site.
The panel shows **LIVE** in one of those. Anywhere else it shows **Practice · not counted**, and
nothing you fly there moves your rating, though the pips still show your attempts.

**When it's on screen.** By default it shows while you sit at the start (arriving on a track, and
after each reset) and fades out 10 seconds later, so it's never over the video for long. It can
stay 5, 10, 20 or 30 seconds, or always: pick one in edit mode (below). It stays hidden while a
menu is open. Press **F8** to pin it on screen, and again to let it fade.

## Track board while you fly

A second panel shows the JMT board for the course you're flying, around your place:

- **The places above yours** (3 by default, up to 5), with each one's time and the gap to
  yours. The next place to take is highlighted, and **HERE** marks a pilot who's in your room
  right now.
- **The time to beat:** "Beat 41.590 for P8 · 0.225 to find". A tie doesn't take the place:
  the board keeps the earlier of two equal laps ahead.
- **A time ruler:** lap times along a line, quickest on the left, with a tick for each place
  shown and your laps tonight as dots. A lap quick enough for the next place shows in green,
  so you can see how close your laps are landing.
- **Your last lap** against the target, and your best tonight.

Your laps move you on it the moment you fly them: beat P8's time and you're P8 straight away,
with P7 as the next target, marked `*` until the site has the lap (a few seconds in a JMT room;
never, in practice). A new best flashes up: "NEW PB 41.402 · UP 2 PLACES TO P7". If you've never
flown the course in a JMT room, it shows the bottom of the board, since any lap puts you on it.

## Moving and setting up the panels

Press **Ctrl+F8** while flying, or with the pause menu open, which is easiest. The panels take
the mouse: click one to edit it, drag it anywhere, and scroll over it to make it bigger or
smaller. The toolbar beside it has its size and opacity, how long it stays on screen (Off, 5,
10, 20 or 30 seconds, or Always), and for the track board how many places to show above and
below yours and whether to draw the ruler. **Reset this panel** puts it back as it came. Press
**Done** or Ctrl+F8 to finish. It's all saved, and each panel keeps its place at any resolution.

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
| `Hud` | `PinKey` | `F8` | Keeps the panels on screen until pressed again. |
| `Hud` | `EditKey` | `Ctrl+F8` | Move, resize and set up the panels with the mouse. |
| `Hud`, `HudBoard` | `Show` | `BetweenAttempts` | The Consistency Rating (`Hud`) or track board (`HudBoard`) while flying: `BetweenAttempts`, `Always` or `Off`. |
| `Hud`, `HudBoard` | `ShowSeconds` | `10` | With `BetweenAttempts`, how long it stays up: `5`, `10`, `20` or `30`. |
| `Hud`, `HudBoard` | `Corner` | `TopRight`, `TopLeft` | The corner it keeps to. Set by dragging it. |
| `Hud`, `HudBoard` | `OffsetX`, `OffsetY` | `24` | How far in from that corner, in pixels at 1080p. Set by dragging it. |
| `Hud`, `HudBoard` | `Scale` | `0.8` | Its size, from `0.4` to `1.6`. |
| `Hud`, `HudBoard` | `Opacity` | `0.9` | How solid it's drawn, from `0.2` to `1`. |
| `HudBoard` | `Above` | `3` | Places shown above yours, `1` to `5`. |
| `HudBoard` | `Below` | `0` | Places shown below yours, `0` to `2`. |
| `HudBoard` | `Ruler` | `true` | Draw the time ruler. |

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
