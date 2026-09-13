# JMT Liftoff Leaderboard

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for
[Liftoff](https://store.steampowered.com/app/410340/) that puts the
[JMT FPV](https://test.geekhost.uk) leaderboards and pilot profiles inside the game's menus,
and your Consistency Rating, your delta against your best lap and the race in your room on
screen while you fly.

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
- **Your delta against your best lap while you fly**, with sectors. See
  [below](#delta-bar-while-you-fly).
- **The race in your room while you fly.** See [below](#the-race-in-your-room).
- **A lap review on Ctrl+F7:** your laps gate by gate, and where they lose time. See
  [below](#lap-review).
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

## Delta bar while you fly

A panel at the top of the screen measures the lap you're flying against your best lap on the
course:

- **The running delta:** -0.234 in green when you're ahead, +0.120 in red when you're behind,
  and whether the last stretch gained or lost time.
- **A bar** that fills left of centre when you're ahead and right of it when you're behind. A
  second either way fills it; it can be half a second or two in edit mode.
- **The lap's sectors:** by default one for every stretch between gates, as many as the course
  has. They're drawn as a strip whose segments are as long as each stretch, with the last one
  flown spelled out under it: "Gate 7 of 23 · -0.08". Or pick 3 to 6 even sectors, each a box
  with its delta. Purple is the quickest you've ever flown it, green is quicker than the lap
  you're measured against, yellow is slower.
- **The lap line:** the lap so far, the lap to beat, and your best possible lap, which is your
  quickest stretch between each pair of gates added up.

When a lap ends, its time and final delta stay up for three seconds, marked **NEW BEST** when it
is one.

**How it's measured.** The game marks off each race checkpoint your drone passes, in order,
and when. The plugin reads those from the game's own checkpoint tracking and compares them with
the same gates on your best lap. Every lap is measured, the first after a reset included:
crossing the start line always starts a fresh one. Between
gates the bar still moves: once your lap has run past the time your best lap reached the next
gate, you're behind by at least that much.

**Your best lap is kept on this computer.** The first lap you fly cleanly through every gate on
a course becomes the one to beat, and every quicker one replaces it. It works in any room, JMT
or not. A lap that misses a gate, is joined part way, or ends in a reset doesn't count. If a
course's gates change, the bar learns the new ones after two clean laps through them. The laps
are kept in `BepInEx/config/JmtLiftoffLeaderboard/splits/`, one file per course, so they're there
next time. In edit mode you can measure against your best tonight instead, and **Forget this
course's best** starts a course again.

It's on screen all the time by default. Like every panel, it can show only at the start and
after a reset instead, or be switched off.

## Lap review

Press **Ctrl+F7** while flying, or with the pause menu open, which is easiest, to dig into your
laps on the course. Press it again, **Esc** or **Close** to go back. The delta bar's toolbar in
Ctrl+F8 has a **Review laps** button too.

- **Your laps this session, newest first:** each one's time and its delta, **PB** on a new best,
  and attempts you reset part way, with the gate they got to. Click one to take it apart. The
  newest is shown until you pick another, and new laps join the list as you fly them.
- **Compared with** your best ever, your best tonight, your possible lap (your best stretch
  between each pair of gates, flown as one lap) or the lap before it.
- **Where it lost time:** "Lost most at Gate 7 +0.312, Gate 15 +0.221, Finish +0.180. Gained
  most at Gate 3 -0.120."
- **The delta trace:** how far ahead or behind the lap was at every gate, red where a stretch lost
  time and green where it gained, with your other recent laps faintly behind it so an odd one
  out shows.
- **A row for every stretch:** the split, the stretch's time (purple when it's your best ever),
  a bar and the time gained or lost against the lap it's compared with, the delta so far, and
  your best for the stretch.
- **Where the time goes lap after lap.** Each row also has a cell for each of your last 10 clean
  laps, coloured by how that lap flew the stretch against your best for it: purple your best,
  green within 3%, yellow within 8%, red more. **Avg lost** is what they give away there on
  average. The three worst are picked out: "Your last 10 clean laps give away 0.842 a lap to
  your best stretches, most at Gate 7 +0.280, Gate 15 +0.220".
- **Your best, best tonight, possible lap, the average and spread of your last 10 clean laps,**
  and how many of your laps were clean.

Your best lap on each course is kept on this computer, but the laps in the review are kept only
until the game closes: the last 20 on each course you fly.

## The race in your room

Another panel shows the race being flown in the room you're in, ranked the way the room's
timing screen ranks it: by each pilot's best lap this race.

- **Place, name, laps and best lap**, with the gap to the quickest lap of the race or each
  pilot's last lap. The quickest lap is purple and your row is highlighted.
- **Every lap the moment it's flown**, in any room: the game shares each pilot's laps with
  everyone in the room.
- **In a JMT room, the site's side too** (it shows **LIVE**): laps flown before you joined,
  pilots who have since left (dimmed), and each pilot's failed attempts this race. The site hears
  about laps a few seconds after the room does, so when a new race starts its rows come back
  once it has the new race.
- **"UP TO P2"** flashes up when you take a place, and "DOWN TO P3" when you lose one.

To put the site's rows beside the pilots in a JMT room, the plugin looks up the id each pilot's
game signed in with, the way it looks you up, once per pilot. It only does this in JMT rooms,
where those pilots' laps already go to the site.

## Moving and setting up the panels

Press **Ctrl+F8** while flying, or with the pause menu open, which is easiest. The panels take
the mouse: click one to edit it, drag it anywhere, and scroll over it to make it bigger or
smaller. Drop a panel near the middle of the screen and it stays centred on the top or bottom
edge. The toolbar beside it has its size and opacity, how long it stays on screen (Off, 5, 10,
20 or 30 seconds, or Always), and the panel's own settings: for the track board, how many places
to show above and below yours and whether to draw the ruler; for the delta bar, what it's
measured against, its parts, sectors and range; for the race, how many pilots, the last column
and failed attempts. **Reset this panel** puts it back as it came. Press **Done** or Ctrl+F8 to
finish. It's all saved, and each panel keeps its place at any resolution.

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
| `Hud` | `ReviewKey` | `Ctrl+F7` | Open the lap review. |
| `Hud`, `HudBoard`, `HudDelta`, `HudRace` | `Show` | `BetweenAttempts`; `Always` for `HudDelta` | The Consistency Rating (`Hud`), track board (`HudBoard`), delta bar (`HudDelta`) or race (`HudRace`) while flying: `BetweenAttempts`, `Always` or `Off`. |
| `Hud`, `HudBoard`, `HudDelta`, `HudRace` | `ShowSeconds` | `10` | With `BetweenAttempts`, how long it stays up: `5`, `10`, `20` or `30`. |
| `Hud`, `HudBoard`, `HudDelta`, `HudRace` | `Corner` | `TopRight`, `TopLeft`, `TopCenter`, `BottomLeft` | Where it keeps to: a corner, or `TopCenter` / `BottomCenter`. Set by dragging it. |
| `Hud`, `HudBoard`, `HudDelta`, `HudRace` | `OffsetX`, `OffsetY` | `24` (`HudDelta`: `0`, `24`) | How far in from that corner, in pixels at 1080p. Set by dragging it. |
| `Hud`, `HudBoard`, `HudDelta`, `HudRace` | `Scale` | `0.8` | Its size, from `0.4` to `1.6`. |
| `Hud`, `HudBoard`, `HudDelta`, `HudRace` | `Opacity` | `0.9` | How solid it's drawn, from `0.2` to `1`. |
| `HudBoard` | `Above` | `3` | Places shown above yours, `1` to `5`. |
| `HudBoard` | `Below` | `0` | Places shown below yours, `0` to `2`. |
| `HudBoard` | `Ruler` | `true` | Draw the time ruler. |
| `HudDelta` | `Compare` | `BestEver` | What the lap is measured against: `BestEver`, kept on this computer, or `Tonight`. |
| `HudDelta` | `Bar` | `true` | Draw the delta bar. |
| `HudDelta` | `Sectors` | `Gates` | `Gates` for one per stretch between gates, however many the course has; `3` to `6` for even sectors; `Off` to hide them. |
| `HudDelta` | `LapLine` | `true` | The lap so far, the lap to beat and your best possible lap. |
| `HudDelta` | `Range` | `1` | Seconds ahead or behind that fill the bar: `0.5`, `1` or `2`. |
| `HudRace` | `Rows` | `8` | Most pilots shown: `5`, `8` or `12`. With more in the race, the top places and yours. |
| `HudRace` | `Column` | `Gap` | The last column: `Gap` to the quickest lap of the race, or each pilot's `Last` lap. |
| `HudRace` | `Failed` | `true` | Failed attempts this race, in JMT rooms. |

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

## When something's wrong

The plugin writes what it's doing to `BepInEx/LogOutput.log`, on lines starting `HUD:` for
the panels you fly with.

- **The delta bar never gets a lap to beat.** At startup the log says how many of the game's
  checkpoint trackers the plugin follows (`HUD: following your checkpoints through …`); none
  means a game update has changed them. For the first few laps of each race it then shows each
  checkpoint as it arrives (`HUD: gate id=… lap=… t=…`) and each lap (`HUD: lap …`).
- **The race panel has no LIVE.** The log says whether the room reports to the JMT site
  (`HUD: this room reports to the JMT site`).

## Building

See [docs/building.md](docs/building.md). Releases are described in
[docs/versioning.md](docs/versioning.md).

## License

[MIT](LICENSE).
