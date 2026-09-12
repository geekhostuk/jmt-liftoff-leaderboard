# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows
[Semantic Versioning](https://semver.org/) — see [docs/versioning.md](docs/versioning.md).

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
