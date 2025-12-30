# EcobeeCLISharp

A command-line interface for controlling Ecobee thermostats.

## Features

- Cross-platform support (Windows, Linux, macOS)
- Set heat and cool temperatures (absolute or relative values)
- Control fan modes (auto/on)
- Daemon mode for continuous temperature monitoring and adjustment
- Query thermostat status before/after changes

## Installation

1. Download and extract the release for your platform (win-x64, linux-x64, or osx-x64)
2. Create `ecobee_credentials.txt` in the same directory as the executable
3. Add your Ecobee Developer API key as the first line

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

- .NET 6.0 Runtime
- Ecobee Developer API key from https://www.ecobee.com/developers/

## License

See LICENSE file for details.
