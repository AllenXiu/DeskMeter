# DeskMeter

> A Conky-style desktop system monitor for Windows.
> Lightweight. Transparent. Blends into your wallpaper. No ads, no bloat.

## Features

- **System metrics as plain text** — CPU, per-core, memory, disk, network up/down, uptime, clock, hostname, Windows version — driven by simple `${var}` placeholders, just like Conky
- **Visual gauges** — progress bars (`${cpu_bar}`) and history graphs (`${cpugraph}`, `${downspeedgraph}`)
- **Blends into the desktop** — transparent, borderless, click-through window pinned to the desktop layer
- **Flexible layout** — corner alignment presets with X/Y gap offsets, custom fonts and per-line colors
- **`${exec}` extensibility** — run any command and display its output
- **Conky-compatible config** — drop in your existing `conky.conf` (supported subset) and it just works
- **Hot reload** — edit the config file and watch the desktop update live
- **Lightweight by design** — single process, ~2s default refresh, negligible footprint
- **Open source. No ads. No telemetry. No installer junk.**

## Roadmap

- [x] P0: text variables, transparent desktop window, conky.conf parsing, timed refresh
- [ ] P1: bars & graphs, colors, `${exec}`, hot reload
- [ ] P2: temperature (LibreHardwareMonitor), top processes, tray icon, autostart, multi-monitor, themes
- [ ] Future: Linux support (same config format, /proc + lm-sensors data source)

> Non-goals: no Lua, no mouse-interactive skins, no skin marketplace. DeskMeter stays simple on purpose.
