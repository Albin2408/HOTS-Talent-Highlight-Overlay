# HOTS Talent Highlight Overlay

A Windows overlay for **Heroes of the Storm**. It works out which hero you're playing, and when the
talent panel opens it draws a gold **★ PICK THIS** box around the talent a popular build recommends
(plus an orange **OR CONSIDER** box on situational alternatives). A small panel shows your whole build
and lets you switch builds without alt-tabbing. It only draws on screen: it never clicks, automates the
game, or reads game memory.

> Unofficial fan project. Not affiliated with or endorsed by Blizzard Entertainment, Icy Veins or
> HeroesProfile. Heroes of the Storm is a trademark of Blizzard Entertainment.

## Quick start

You need Windows 10/11, the [.NET 8 SDK](https://dotnet.microsoft.com/download), and a bash shell with
`curl` (Git Bash or WSL) for the one-off data step.

```
git clone https://github.com/Albin2408/HOTS-Talent-Highlight-Overlay.git
cd HOTS-Talent-Highlight-Overlay
tools/refresh-builds.sh          # downloads build data for your own use (see "Build data" below)
dotnet run --project src/HotsOverlay.App
```

Set HOTS to **Windowed (Fullscreen)** (Options > Video > Display Mode): true exclusive fullscreen can't
be screen-captured. Leave the picker window open (minimize it) and start a match. The overlay waits
quietly; when the talent panel opens at the start of the match it recognizes your hero and highlights
your pick. Closing the picker quits the app.

## How it works

1. **Builds**: each build records the recommended talent *name* for every tier, plus the guide's
   situational alternatives.
2. **Your hero**: every ~0.7s the overlay OCRs the left side of the game window (Windows' built-in OCR).
   Talent names are almost unique per hero, so the talent cards on screen (you choose one the moment a
   match starts) identify who you're playing. After two scans agree it switches hero by itself and picks
   the build you last used for them, or the recommended one.
3. **The highlight**: it finds that build's talent among the on-screen text (fuzzy-matched to tolerate OCR
   typos) and frames the card. No calibration, and it doesn't depend on resolution or ultrawide.
4. **The build panel** (top-right of the game, draggable): your hero, one button per build (click to
   switch), and the build's pick for every tier with the tier on screen lit up. While HOTS has focus,
   **numpad 1-6** also switch builds. Click "auto-detect hero" to turn detection off.

The panel also tells you when it can't work: Windows lacks English text recognition, or HOTS is in
exclusive fullscreen. To see exactly what the OCR reads, run with `HOTS_DEBUG=1` and watch the console.
If a highlight never appears, tick **Scan whole screen** in the picker.

## Build data

The build data is **not in this repository**: it is Icy Veins' editorial content, so it isn't
redistributed here. `tools/refresh-builds.sh` downloads it onto your machine for your own use:

- one request every ~1.5s, an honest User-Agent, only paths `robots.txt` allows, and it **stops at the
  first 403/challenge page** instead of retrying or working around it;
- the pages are parsed into `src/HotsOverlay.App/SeedBuilds` (git-ignored), and it prints which heroes'
  builds changed since the last run.

Please respect Icy Veins' terms and keep what you download for personal use. (The app reads that local
snapshot only; Icy Veins' Cloudflare rejects the app's own web requests, so it doesn't scrape live.)

**Staying current:** the panel and picker show when the snapshot was collected, and warn when a newer game
patch exists (checked once a day against HeroesProfile's free patch list) or the data is over 45 days old.
Re-run `tools/refresh-builds.sh` after a patch.

**Win rates (optional):** with your own paid [HeroesProfile API](https://api.heroesprofile.com/docs/1.0)
token, the refresh can also attach real win rates and add each hero's top win-rate builds:

```
HEROESPROFILE_API_TOKEN=<your token> tools/refresh-builds.sh --win-rates
```

The token is read from the environment only and is never saved; the app itself never calls the paid
endpoint, only the snapshot tool does. This path is tested against HeroesProfile's documented response
format but not against the live API, which needs a paid token.

**Your own builds** work for every hero, with no data download: select a hero, click **+ Add my own build
for this hero**, and paste the 7 talent names (one per line, spelled as the game spells them).

## Building a standalone exe

```
dotnet publish src/HotsOverlay.App -p:PublishProfile=win-x64
```

Writes `dist/HotsOverlay/HotsOverlay.exe` (self-contained, no .NET install needed). Keep the
`SeedBuilds` folder next to the exe. Depending on your Windows 11 setup, Smart App Control or SmartScreen
may warn about an unsigned build.

## Project layout

- `src/HotsOverlay.Core`: build models, OCR box types, `TalentNameMatcher`, `HeroDetector`.
- `src/HotsOverlay.Scraper`: Icy Veins parser, local cache, optional HeroesProfile client.
- `src/HotsOverlay.Vision`: GDI screen capture + Windows OCR (`TextScanner`).
- `src/HotsOverlay.App`: WPF picker, click-through overlay, build panel.
- `tools/`: `fetch-pages.sh`, `refresh-builds.sh`, and `SeedBuilder` (pages to snapshot).
- `tests/`: Core, Scraper (parser tests run on invented pages) and Vision (renders a talent-card list and
  runs it through the real Windows OCR engine).

```
dotnet test tests/HotsOverlay.Core.Tests
dotnet test tests/HotsOverlay.Scraper.Tests
dotnet test tests/HotsOverlay.Vision.Tests
```

Tests that exercise real data (the snapshot, saved guide pages) skip themselves unless you've generated
that data locally.

## Limitations

- Needs the talent panel's card layout (the name printed on each card), which is the current HOTS UI.
- English game client only (the OCR reads English talent names).
- Verified against rendered imitations of the panel and OCR logs from a real match, but the in-game
  highlight is still best checked in your own match, hence the debug logging.

## Ideas not built yet

A tray icon with start-with-Windows, and ranking builds by win rate by default.
