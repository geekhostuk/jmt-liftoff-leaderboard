# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows
[Semantic Versioning](https://semver.org/) — see [docs/versioning.md](docs/versioning.md).

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
