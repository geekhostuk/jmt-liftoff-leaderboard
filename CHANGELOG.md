# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows
[Semantic Versioning](https://semver.org/) — see [docs/versioning.md](docs/versioning.md).

## [0.3.0] — 2026-09-13

### Added

- **Your delta against your best lap, live while you fly.** A panel at the top of the
  screen shows how far ahead or behind the lap you're flying is, from the game's own
  checkpoint times.
  - A bar that fills green left of centre when you're ahead and red right of it when you're
    behind, and whether the last stretch gained or lost time.
  - The lap's sectors: purple for your best ever, green for quicker than the lap you're
    measured against, yellow for slower. By default there's one for every stretch between
    gates, as many as the course has, drawn as a strip sized by each stretch; or 3 to 6 even
    sectors. A line under them has the lap so far, the lap to beat, and your best possible
    lap from your best stretches between gates.
  - Checkpoints are read from the game's own tracking of your drone, which every race mode
    keeps; only some modes share them with the room.
  - Your best lap on each course is kept on this computer, in
    `BepInEx/config/JmtLiftoffLeaderboard/splits/`, so it's there next time. Only clean laps
    through every gate count, and a course whose gates change is learned again after two
    clean laps through the new ones.
  - Ctrl+F8 sets what it's measured against (your best ever or tonight), the bar, sectors
    (one per gate, 3 to 6, or off), the lap line and the bar's range, and can forget a
    course's best.
- **The race in your room.** A panel with everyone's best lap this race, ranked the way the
  room's timing screen ranks it, with the gap to the quickest or each pilot's last lap.
  - Every lap shows the moment it's flown, in any room.
  - In a JMT room it adds the site's side: laps from before you joined, pilots who have left,
    and failed attempts. "UP TO P2" flashes up when you take a place.
  - Ctrl+F8 sets how many pilots it shows, its last column and the failed column.
- **Panels can sit in the middle of the top or bottom edge.** Drop one near the middle in
  Ctrl+F8 and it stays centred at any resolution.
- For the first few laps of each race the log shows each checkpoint and lap as it arrives
  (`HUD: gate`, `HUD: lap`), to check the game is sending them.
- Needs the JMT site's `/api/timing/live/{id}` for the race panel's JMT side, which the site
  already has.

### Changed

- The Ctrl+F8 toolbar's tabs are shorter, to fit four panels: CR, Board, Delta and Race.

## [0.2.0] — 2026-09-12

### Added

- **Your Consistency Rating while you fly.** A panel in the corner of the screen shows your
  CR with tonight's flying counted in, the license it earns, a bar to the next one, and your
  last 20 attempts as pips: green for a lap, red for a failed attempt.
  - It counts your laps and resets itself, by the same rules as the JMT rooms, so it moves
    the moment you finish a lap or crash, before the room has sent anything to the site.
  - "9 clean laps to A license · a crash costs 2 more" says what the next license takes;
    "2 crashes from C license" warns when the one you hold is close to slipping.
  - It knows whether the room you're in reports to the JMT site. Anywhere else it says
    "Practice · not counted", and nothing you fly there moves your CR.
  - By default it shows at the start and after each reset, and fades out 10 seconds later.
    It can stay 5, 10, 20 or 30 seconds, or always. F8 pins it on screen.
  - **Ctrl+F8 lets you move, resize and fade it** with the mouse, in the air or from the
    pause menu. Drag it, scroll over it to resize it, or use the toolbar's size, opacity and
    "stays on screen" buttons. It's saved as a corner and an offset, so it stays put at any resolution.
- **The course's board around your place while you fly.** A second panel shows the places
  above yours with the gap to each, "Beat 41.590 for P8", and a time ruler with your laps
  tonight on it, green when one would have taken the next place. Your laps move you on it
  the moment you fly them, and "HERE" marks a rival in your room. How many places above and
  below, and the ruler, are set in the Ctrl+F8 toolbar, which now edits either panel.
- Needs the JMT site's `/api/pilots/{id}/consistency` and the leaderboard's `around`. Against an older site it falls back
  to the profile's totals, which makes the countdown approximate past 100 laps.

## [0.1.0] — 2026-09-12

### Added

- **The JMT leaderboard in the main menu.** Liftoff's Leaderboard button opens the JMT
  board instead, laid out like a track page on the JMT site: podium, the board with
  Top 10 / 25 / All and a pilot search, and the latest laps. A track picker lists every
  course that has a JMT board. "Liftoff leaderboard" still opens the game's own screen,
  where ghosts and replays live.
- **The JMT board in the pause menu**, opened straight to the course being flown.
- **JMT Profile**, a new main-menu button showing a pilot's profile as the site shows it:
  JMT Rating, Consistency Rating, where they place, recent personal bests and their best
  lap on every course. Any pilot on a board can be opened the same way.
- **Finding "you" without setup.** The plugin looks the local pilot up from the id the
  game signs in with, or their Steam id. Anyone it can't find presses "This is me" on
  their row once, and it is remembered.
