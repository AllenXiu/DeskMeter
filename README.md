# DeskMeter

> A Conky-style desktop system monitor for Windows.
> Lightweight. Transparent. Blends into your wallpaper. No ads, no bloat.

English | [中文](README_zh.md)

## Features

- **System metrics as plain text** — CPU, per-core, memory, disk, network up/down, uptime, clock, hostname, Windows version — driven by simple `${var}` placeholders, just like Conky
- **Visual gauges** — progress bars (`${cpubar}`) and history graphs (`${cpugraph}`, `${downspeedgraph}`), vector-drawn like Conky's Cairo renderer
- **Blends into the desktop** — transparent, borderless, click-through window pinned to the desktop layer
- **Flexible layout** — corner alignment presets with X/Y gap offsets, custom fonts and per-line colors
- **`${exec}` extensibility** — run any command and display its output
- **Conky-compatible config** — drop in your existing `conky.conf` (full Lua, executed via MoonSharp) and it just works
- **Hot reload** — edit the config file and watch the desktop update live
- **Lightweight by design** — single process, ~2s default refresh, negligible footprint
- **Open source. No ads. No telemetry. No installer junk.**

## Docs

- [Variable reference (96 variables, Conky compatibility)](docs/VARIABLES.md)
- [Design doc](docs/DESIGN.md)

## Install / Releases

- Download `DeskMeter-<version>-win-x64.zip` from [GitHub Releases](https://github.com/AllenXiu/DeskMeter/releases) and unzip — self-contained single-file, no .NET runtime required.
- On first run the app imports `samples/conky.conf` as the default config (config library lives at `%APPDATA%\DeskMeter\configs`).
- Release flow: push a `v*` tag (e.g. `v0.1.0`) → GitHub Actions builds, tests, packages and creates the Release.

## Roadmap

- [x] P0: text variables, transparent desktop window, conky.conf parsing, timed refresh
- [x] P1: vector bars & graphs, colors, `${exec}` / `${execpi}`, hot reload, layout objects
- [x] P2: temperature (LibreHardwareMonitor), top processes, tray icon, autostart, multi-monitor, multi-config management (replaces the cancelled "themes"), settings UI
- [x] Edge cases: inline `${font}`, scroll right/wait, graph flags (-l/-m/-i/-y), offset/voffset/tab pixel semantics
- [ ] Future: cross-platform (renderer is abstracted; Avalonia migration if ever needed)

> Non-goals: no Lua drawing scripts, no interactive skins, no skin marketplace. Full Lua config execution (MoonSharp) is supported — DeskMeter stays simple on purpose.

## License

Released under the [MIT License](LICENSE).
