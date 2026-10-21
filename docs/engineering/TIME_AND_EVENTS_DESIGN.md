# Time, day/night and event rotation (M9)

Maintainer request 2026-10-20: an in-game time and calendar with day and night, at 1x speed only, seasons named
Spring, Summer, Fall and Winter; R-08 answer: bring back the original event calendar broadly. This file is the plan.

## 1. In-game clock (T-01, T-02)
- Units at 1x: 60 ticks a real second; an hour is 2,500 ticks (about 42 s), a day 60,000 (about 16.7 min), a season
  15 days (about 4.2 h), a year 4 seasons = 60 days (about 16.7 h). No faster speeds.
- Seasons in order: Spring, Summer, Fall, Winter.
- The clock runs only while the game runs and is kept in the save (`offline-state` row `WorldTicks`); a new save starts
  at 06:00, Day 1 of Spring, Year 1. World keeps it; Game reads the same base, so both show the same time.
- `/time` shows it, for example `14:30, Day 3 of Summer, Year 1`.
- One world clock: no time zones, no daylight by latitude.

## 2. Day/night (T-02 prototype, T-03)
- The Global client draws no night of its own (R-07, harness probe), so the server draws it: it pushes ambient and
  directional light to outdoor maps by in-game hour (the same field-property packets map scripts use).
- Daylight by season: Spring 12 h, Summer 15 h, Fall 12 h, Winter 9 h, with dusk and dawn transitions.
- Indoor maps, dungeons and maps whose scripts set their own light are left alone.
- A prototype with screenshots (day, dusk, night) goes to the maintainer before the full cycle is built: it is a light
  change, not a new sky.

## 3. Event rotation (T-04)
Today no seasonal event runs: the 1,615 dated events in the game data (2015-2024) are dropped as expired; only 3
undated ones load. Maintainer 2026-10-20: the seasons bring a lineup on their own, and a manager NPC lets the player
switch to any lineup at any time, so a missed event is never far away.
- A lineup is one season of one original year: every event whose original start falls in that season (Mar-May Spring,
  Jun-Aug Summer, Sep-Nov Fall, Dec-Feb Winter) of that year, for example Winter 2019. Its events run together for as long
  as the lineup is active, as they did then (no shortened windows).
- Default: each in-game season starts its lineup by itself. In-game Year 1 uses the 2016 lineups, Year 2 the 2017 ones,
  and so on to 2023, then it starts over.
- The event manager NPC (in the main towns; the chat command `/events` does the same) lists every lineup with its events
  and switches to the one the player picks. A picked lineup stays until the player picks another or chooses "follow
  the seasons" again.
- Switching never resets progress: attendance claims, play-time rewards and other event progress belong to the event
  and stay (W-04), and daily limits stay on real days, so switching back and forth gives nothing twice.
- Inside an active event, its own schedule stays on the real clock (day-of-week and hour windows such as "Saturday
  20:00-22:00 exp x2"): an in-game hour is 42 s, too short for those.
- Unchanged: the mini-game portals (every 3 real hours), hourly world bosses and field chests keep their real-time cycles.
- Event types without server logic get new code where players meet them (hot times, event NPCs, event drops, dungeon
  bonus rewards first); Korean event texts are replaced by English ones over time.

## 4. Order
RFF-06, M2c-06 and B-01 (the 1.2.0 path) first; then T-01, T-02 with the prototype, T-03 after the
maintainer's OK on the look, T-04.
