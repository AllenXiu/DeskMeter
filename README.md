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

## Roadmap

- [x] P0: text variables, transparent desktop window, conky.conf parsing, timed refresh
- [ ] P1: bars & graphs, colors, `${exec}` / `${execpi}`, hot reload, layout objects
- [ ] P2: temperature (LibreHardwareMonitor), top processes, mouse events, tray icon, autostart, multi-monitor, themes, settings UI
- [ ] Future: cross-platform (renderer is abstracted; Avalonia migration if ever needed)

> Non-goals: no Lua drawing scripts, no interactive skins, no skin marketplace. Full Lua config execution (MoonSharp) is supported — DeskMeter stays simple on purpose.

## License

Released under the [MIT License](LICENSE).
