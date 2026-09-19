# Crateless

**Your ASUS ROG laptop, without the Crate.**

A small, portable control tool for ASUS laptops. Performance modes, fan curves, GPU
switching, power limits and lighting — in a single executable that installs nothing, runs
no background services, and sends no telemetry.

> [!WARNING]
> **Status: early fork — no release published yet.**
> Crateless currently tracks [G-Helper](https://github.com/seerge/g-helper) upstream while
> rebranding and the new UI are in progress. If you want a stable, code-signed, widely
> tested build *today*, use [G-Helper](https://github.com/seerge/g-helper/releases) — this
> fork exists because of it, not instead of it.
>
> Roadmap: [`docs/crateless/PLAN.md`](docs/crateless/PLAN.md) ·
> Changes vs upstream: [`docs/crateless/FORK.md`](docs/crateless/FORK.md)

---

## What it is

ASUS ships its laptops with operating modes, fan curves and power limits already stored in
the BIOS. Armoury Crate is one way to select among them — at the cost of a dozen
background services, an account prompt and a multi-gigabyte install.

Crateless is the other way: one executable, around 10 MB, that talks to the same ASUS
System Control Interface driver and exposes the same modes. Close the window and nothing
keeps running except a tray icon.

It is not an operating system, a driver or firmware. Think of it as a remote control for
settings your laptop already has.

## Features

**Performance**
- Silent / Balanced / Turbo modes, as defined by the manufacturer in BIOS
- Custom fan curve editor per mode, for CPU and GPU independently
- Power limits (SPL / SPPT / FPPT) and CPU boost behaviour per mode
- AMD CPU undervolting and temperature limits
- NVIDIA GPU clock offsets, power, Dynamic Boost and temperature target

**Graphics**
- GPU modes: **Eco** (dGPU off) · **Standard** (MS Hybrid) · **Ultimate** (dGPU drives the
  display) · **Optimized** (Eco on battery, Standard when plugged in)
- Refresh rate control with display overdrive
- Mini-LED multi-zone switching, flicker-free dimming, visual modes

**Power**
- Each performance mode pinned to an explicit Windows power plan *and* Windows power mode,
  applied once and left alone
- Battery charge limit to preserve battery health
- Automatic mode switching on battery / AC

**Monitoring**
- CPU and GPU temperature, fan RPM, power draw, battery status
- In-game metrics overlay (FPS, temps, usage, power) for DX10+ titles

**Lighting and input**
- RGB backlight modes and colours, Anime Matrix and Slash Lighting
- FN-Lock, hotkey handling, custom key bindings
- ASUS mice, keyboards and headsets

**Maintenance**
- BIOS and driver update checker, pulling directly from the official ASUS site for your model

## Performance modes and Windows power

Every mode is bound to both a Windows power plan and a Windows power mode, and the pairing
is applied once per switch:

| Mode | Windows power mode |
|---|---|
| Silent | Best power efficiency |
| Balanced | Balanced |
| Turbo | Best performance |

The power plan used by each mode is configurable, including a separate plan for USB-C
charging. Nothing re-applies or overrides the choice afterwards.

## Supported hardware

Device support is inherited from upstream and covers most of the modern ASUS lineup —
ROG Zephyrus G14 / G15 / G16 / M16 / X13 / X16 / Z13 / DUO, TUF, Strix, Scar, ProArt,
Vivobook, Zenbook, Expertbook, ROG Ally and Ally X.

Crateless is developed and regression-tested on a **ROG Zephyrus G16 GA605WI**
(Ryzen AI 9 HX 370 / RTX 4070). Other models should behave exactly as they do in upstream;
if something is broken there too, please report it upstream first.

## Requirements

- Windows 10 or 11, x64
- ASUS System Control Interface driver — ships with the laptop. Keep it installed if you
  remove Armoury Crate.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for the
  portable build

## Build from source

```bash
git clone https://github.com/artswanted/Crateless.git
cd Crateless
dotnet publish app/GHelper.sln -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained
```

Output lands in `app/bin/x64/Release/net10.0-windows/win-x64/publish/`.

For a build with no runtime prerequisite — larger file, nothing to install:

```bash
dotnet publish app/GHelper.sln -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

`IncludeNativeLibrariesForSelfExtract` is required — the Ryzen SMU and Intel MSR native
blobs are embedded resources. Do **not** enable `PublishTrimmed`: WinForms relies on
reflection and trimming breaks it at runtime.

Needs the .NET 10 SDK. `app/GHelper.sln` opens in Visual Studio 2022 or newer.

## Keybindings

```
Fn + F5                          cycle performance modes
Ctrl + Shift + F12               open the main window
Fn + C  /  Fn + Esc              FN-Lock
Fn + V                           visual modes
Ctrl + Shift + Alt + O           toggle the in-game overlay
Ctrl + Shift + Alt + F14 / F15   Eco / Standard GPU mode
Ctrl + Shift + Alt + F16…F18     Silent / Balanced / Turbo
```

## Roadmap

Crateless follows upstream for everything that touches hardware, and diverges on
application identity, release pipeline and interface. The plan, phase by phase, lives in
[`docs/crateless/PLAN.md`](docs/crateless/PLAN.md).

## Credits

Crateless is a fork of **[G-Helper](https://github.com/seerge/g-helper)** by
[seerge](https://github.com/seerge), licensed under GPL-3.0. The hardware interface, the
ACPI/WMI reverse engineering and the entire device support matrix are their work. If this
tool is useful to you, [support the upstream project](https://g-helper.com/support).

Upstream in turn builds on
[Linux Kernel asus-wmi](https://github.com/torvalds/linux/blob/master/include/linux/platform_data/x86/asus-wmi.h),
[NvAPIWrapper](https://github.com/falahati/NvAPIWrapper),
[Starlight](https://github.com/vddCore/Starlight),
[UXTU](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility),
[PawnIO](https://github.com/namazso/PawnIO),
[asusctl](https://gitlab.com/asus-linux/asusctl) and
[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB).

## Privacy

Crateless does not transfer any information to other networked systems. The only outbound
requests it makes are the update check against its own GitHub releases and, when you open
the Updates section, BIOS and driver lookups against the official ASUS site.

## License

GPL-3.0 — see [LICENSE](LICENSE). As a derivative work Crateless stays GPL-3.0 and will
not be closed.

## Disclaimers

Crateless is **not affiliated with, endorsed by or sponsored by ASUSTeK Computer Inc.**
"ASUS", "ROG", "TUF" and "Armoury Crate" are trademarks of ASUSTeK Computer Inc., used
here for identification purposes only.

THE SOFTWARE IS PROVIDED "AS IS" AND WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. MISUSE OF THIS SOFTWARE COULD CAUSE SYSTEM INSTABILITY OR
MALFUNCTION.
