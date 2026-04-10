# SDR-IQ-Streamer

SDR-IQ-Streamer is an Avalonia desktop app that launches and synchronizes CW Skimmer with FlexRadio DAX-IQ streams.
Support for other SDR IQ streams may be feasible by replacing the `FlexLib_API_v4` integration when equivalent APIs are available from other SDR platforms.
License: MIT (see `LICENSE`)

This project is a work in progress. Development status is tracked in the roadmap below.

## Quick Start

### Prerequisites

- Windows 10/11
- .NET SDK 8.x
- SmartSDR + DAX installed and running
- CW Skimmer installed
- FLEX-6x00/8x00 radio reachable on local network
- A local radio is required in the current implementation. VPN and SmartLink support are planned for a future release.
- FlexLib API package downloaded and extracted to `FlexLib_API_v4.1.5.39794` in the project root (required to build).

### First-Time Setup / Get Started

1. Launch `SDRIQStreamer.exe`. If Windows prompts for firewall access, allow the app through Windows Firewall.
2. In the app's CW Skimmer section, use **Browse...** to set the local path to `CwSkimmer.exe` (typically `C:\Program Files (x86)\Afreet\CwSkimmer\`).
3. In **Available Radios**, select your radio and click **Connect**.
4. Click **Launch** to start CW Skimmer. In CW Skimmer, open **View > Settings** and verify the **Radio**, **Audio**, and **Operator** tabs for your station.
5. In the CW Skimmer toolbar, click **Start Radio** to begin decoding.

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

- **Phase 1 (Foundation)**: COMPLETE - local discovery, connect/disconnect, pan/slice visibility, and DAX-IQ stream request flow are implemented.
- **Phase 2.1 (CW config + INI write)**: COMPLETE; unit-tested in the CW Skimmer test project.
- **Phase 2.2 (CW launch)**: COMPLETE; launch path and validated working config/device mappings are in place.
- **Phase 2.3 (Decoding)**: COMPLETE; manual tuning and decoding (no CAT control), including dual skimmer launch for DAX-IQ channels 1 and 2.
- **Phase 3 (CAT Control)**: IN PROGRESS - launch-time INI sync and runtime LO/QSY updates are implemented, including VFO tracking and click-to-tune.
- **Phase 4 (Telnet Control)**: PENDING - expanded telnet behavior, additional persistence polish, and UX/error-handling refinement.

## Notes

- `FlexLib_API_v4.1.5.39794` is intentionally excluded from version control; download the API package and extract it to the project root before building.
- Download FlexLib API (SmartSDR v4): [https://www.flexradio.com/software/smartsdr-v4-x-api-flexlib/](https://www.flexradio.com/software/smartsdr-v4-x-api-flexlib/)
- Build currently emits legacy FlexLib warnings on `net8.0-windows`; this is tracked separately.
