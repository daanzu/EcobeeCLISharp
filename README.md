# EcobeeCLISharp

[![Build and Release](https://github.com/daanzu/EcobeeCLISharp/actions/workflows/release.yml/badge.svg)](https://github.com/daanzu/EcobeeCLISharp/actions/workflows/release.yml)
[![GitHub release (latest by date)](https://img.shields.io/github/v/release/daanzu/EcobeeCLISharp)](https://github.com/daanzu/EcobeeCLISharp/releases)
[![License](https://img.shields.io/github/license/daanzu/EcobeeCLISharp)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/download/dotnet/10.0)

A command-line interface for controlling Ecobee thermostats.

## Features

- Cross-platform support (Windows, Linux, macOS)
- Set heat and cool temperatures (absolute or relative values)
- Control fan modes (auto/on)
- Daemon mode for continuous temperature monitoring and adjustment
- Query thermostat status before/after changes

## Example

```bash
> ./EcobeeCLISharp.exe --infobefore --infoafter --heat=-0.5 --fan=off
[2025-12-30 21:58:03] Current Status:
[2025-12-30 21:58:03]   Temperature: 751
[2025-12-30 21:58:03]   Humidity: 25
[2025-12-30 21:58:03]   Mode: auto
[2025-12-30 21:58:03]   Desired Temperature Range: 755 - 850
[2025-12-30 21:58:03]   Desired Fan: auto
[2025-12-30 21:58:03]   Equipment Status:
[2025-12-30 21:58:03]   Last Status Modified: 2025-12-31 02:57:40
[2025-12-30 21:58:03]   Last Modified: 2025-12-31 02:56:25
[2025-12-30 21:58:03]   Current Event: End Time: 21:55:08
[2025-12-30 21:58:03] {"selection":{"selectionType":"registered"},"functions":[{"type":"setHold","params":{"coolHoldTemp":850,"heatHoldTemp":750,"fan":"auto","holdType":"nextTransition"}}]}
[2025-12-30 21:58:03] {"status":{"code":0,"message":""}}
[2025-12-30 21:58:03] Current Status:
[2025-12-30 21:58:03]   Temperature: 751
[2025-12-30 21:58:03]   Humidity: 25
[2025-12-30 21:58:03]   Mode: auto
[2025-12-30 21:58:03]   Desired Temperature Range: 750 - 850
[2025-12-30 21:58:03]   Desired Fan: auto
[2025-12-30 21:58:03]   Equipment Status:
[2025-12-30 21:58:03]   Last Status Modified: 2025-12-31 02:57:40
[2025-12-30 21:58:03]   Last Modified: 2025-12-31 02:56:25
[2025-12-30 21:58:03]   Current Event: End Time: 21:58:04
[2025-12-30 21:58:03] Waiting for thermostat to update
```

## Installation

1. Download and extract the release for your platform (win-x64, linux-x64, or osx-x64).
   - **Standalone** versions include the .NET runtime and do not require any pre-installed software. Choose this if you want the simplest setup or don't want to install .NET.
   - **Runtime dependent** versions are much smaller but require the [.NET 10.0 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to be installed on your system. Choose this if you already have .NET installed or want to save disk space.
2. Create `ecobee_credentials.txt` in the same directory as the executable.
3. Add your Ecobee Developer API key as the first line.

## Usage

### Basic Commands

Set temperatures:
```bash
# Windows
EcobeeCLISharp.exe --heat 68 --cool 72

# Linux/macOS
./EcobeeCLISharp --heat 68 --cool 72
```

Relative adjustments:
```bash
EcobeeCLISharp.exe --heat +2 --cool -1
```

Control fan:
```bash
EcobeeCLISharp.exe --fan on
```

View status:
```bash
EcobeeCLISharp.exe --infobefore --infoafter
```

### Daemon Mode

Run continuously to maintain temperature range:
```bash
EcobeeCLISharp.exe --daemon --heat 68 --cool 72 --daemonendtime 22:00
```

Options:
- `--daemonstartdelay`: Delay before starting (HH:mm or mm)
- `--daemonendtime`: End time in 24-hour format (HH:mm)
- `--daemonminsetinterval`: Minimum minutes between adjustments

### Options

- `-h, --heat`: Set heat temperature
- `-c, --cool`: Set cool temperature
- `-f, --fan`: Set fan mode (auto/on)
- `--holdtype`: Hold type (nextTransition/indefinite)
- `--daemon`: Run as daemon
- `--infobefore`: Print status before changes
- `--infoafter`: Print status after changes
- `-v, --verbose`: Verbose output
- `--hide`: Hide console window
- `--wait`: Wait for keypress before exit

## First Run

On first run, you'll receive a PIN code. Enter this at https://www.ecobee.com/consumerportal/ under "My Apps" → "Add Application" within the time limit.

## Requirements

- .NET 10.0 Runtime (only for **runtime dependent** versions, or for building from source)
- Ecobee Developer API key from https://www.ecobee.com/developers/

## License

This project is licensed under the GNU General Public License v3.0.
See [LICENSE](LICENSE) file for details.
