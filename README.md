# SDR-IQ-Streamer

SDR-IQ-Streamer is an Avalonia desktop app that launches and synchronizes CW Skimmer with FlexRadio DAX-IQ streams.
Support for other SDR IQ streams may be feasible by replacing the `FlexLib_API_v4` integration when equivalent APIs are available from other SDR platforms.
License: MIT (see `LICENSE`)

This project is a work in progress. Development status is tracked in the roadmap below.

## Quick Start

### Release Package (for alpha testers)

- Download the current release zip and run the included `SDRIQStreamer.exe`. There is nothing to install.
- The current alpha publish is a self-contained `win-x64` build, so .NET runtime installation is not required on the test PC.
- Runtime prerequisites still apply: SmartSDR + DAX and CW Skimmer must already be installed.
- `FlexLib_API_v4.1.5.39794` is required to build from source, but is not required on a tester machine using the published release zip.


### First-Time Setup / Get Started

1. Launch `SDRIQStreamer.exe`. If Windows prompts for firewall access, allow the app through Windows Firewall.
2. In the app's CW Skimmer section, use **Browse...** to set the local path to `CwSkimmer.exe` (typically `C:\Program Files (x86)\Afreet\CwSkimmer\`).
3. Set the `cwskimmer.ini` path to the INI created by running CW Skimmer manually at least once.
4. In **Available Radio / Station Targets**, select the radio+station row you want to control and click **Connect**.
5. Click **Launch** to start CW Skimmer. In CW Skimmer, open **View > Settings** and verify the **Radio**, **Audio**, and **Operator** tabs for your station.
6. In the CW Skimmer toolbar, click **Start Radio** to begin decoding.
7. Use the footer status tags (`[STREAMER]`, `[SKIMMER]`, `[TELNET]`) to verify connect, launch, and sync direction (VFO vs Skimmer click-tune).

### Development Prerequisites

- Windows 10/11
- .NET SDK 8.x
- SmartSDR + DAX installed and running
- CW Skimmer installed
- FLEX-6x00/8x00 radio reachable on local network
- A local radio is required in the current implementation. VPN and SmartLink support are planned for a future release.
- FlexLib API package downloaded and extracted to `FlexLib_API_v4.1.5.39794` in the project root (required to build).

### Build

```powershell
dotnet build
```

### Run (development)

```powershell
dotnet run
```

### Run (without `dotnet run` host overhead)

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\SDRIQStreamer.exe
```

### Tests

```powershell
dotnet test tests
```

## Project Layout

- Root app `.csproj`: Avalonia app entry point and UI
- `src/*FlexRadio`: FlexLib adapter layer
- `src/*CWSkimmer`: CW Skimmer config/launch/telnet integration
- `FlexLib_API_v4.1.5.39794`: local FlexLib source/projects folder required by project references (not checked in)
- `tests/*CWSkimmer.Tests`: unit tests for INI generation and mapping

## Roadmap and Delivery Status

- **Phase 1 (Foundation)**: COMPLETE — local discovery, connect/disconnect, pan/slice visibility, and DAX-IQ stream request flow are implemented.
- **Phase 2.1 (CW config + INI write)**: COMPLETE — unit-tested CW Skimmer INI generation is in place.
- **Phase 2.2 (CW launch)**: COMPLETE — launch path and validated DAX device mappings are in place.
- **Phase 2.3 (Runtime sync)**: COMPLETE for alpha-2 baseline — bidirectional QSY and runtime LO/QSY sync are functioning, including multi-station control gating.
- **Phase 3 (Polish / hardening)**: IN PROGRESS — long-run stability, persistence edge cases, and UX/error refinement.
- **Phase 3.1 (Bridge spots)**: COMPLETE (baseline) — CW Skimmer spots are parsed and forwarded to radio spots with configurable enable/disable, lifetime, text color, and background color.
- **Phase 3.2 (RIT fine tuning sync)**: UPCOMING — propagate radio RIT fine-tuning offsets to CW Skimmer so receive tuning can move by small Hz offsets without changing transmit slice frequency.
- **Phase 3.3 (Network quality monitor)**: UPCOMING — display radio-reported `current RTT` (real-time round-trip latency between station and radio) and `max RTT` (highest RTT value since reset).
- **Phase 3.4 (Configuration pages)**: IN PROGRESS — tabbed `Config` and `Logs` views are in place with CW Skimmer path controls, spot controls, and Telnet INI/status visibility; additional refinements continue.
- **Phase 3.5 (Operating page simplification)**: ITERATIVE ACROSS PHASE 3 — continuously polish the main operating page so it shows only essential operator information while 3.1-3.4 are delivered.

## Notes

- `FlexLib_API_v4.1.5.39794` is intentionally excluded from version control; download the API package and extract it to the project root before building.
- Download FlexLib API (SmartSDR v4): [https://www.flexradio.com/software/smartsdr-v4-x-api-flexlib/](https://www.flexradio.com/software/smartsdr-v4-x-api-flexlib/)
- Build currently emits legacy FlexLib warnings on `net8.0-windows`; this is tracked separately.
- CW Skimmer per-channel INI and diagnostic files are written under `artifacts/cwskimmer/ini`.
- On each launch, the app starts from the selected `cwskimmer.ini` template and writes channel-specific runtime settings.
- The app updates only `[Audio]` and `[Telnet]` sections in the CW Skimmer INI and preserves CW Skimmer-managed sections (for example `[Windows]` and `[Radio]`).
