# PascaTraffic

PascaTraffic is a lightweight GTA V Enhanced single-player traffic and parking
generator built for ScriptHookVDotNet Enhanced. It adds a curated selection of
GTA Online road vehicles to Story Mode without replacing the game's entire
traffic population.

The mod is designed for a natural appearance, predictable performance, and
safe use while completing the Story Mode campaign. It has no menu, blips, or
spawn notifications.

## Features

- Adds 155 curated GTA Online road vehicles.
- Selects appropriate vehicle classes for rich, middle-income, poor,
  industrial, and rural areas.
- Adds moving MP vehicles without deleting occupied vanilla traffic.
- Dynamically replaces suitable empty parked vehicles outside the player's
  immediate view.
- Matches a spawned vehicle's initial speed to nearby traffic.
- Uses normal road navigation, traffic-light behavior, and conservative driver
  aggression.
- Detects traffic lights and vehicle queues before treating a driver as stuck.
- Recovers genuinely stuck drivers and removes repeatedly broken entities.
- Suspends generation during missions, cutscenes, loading, and character
  switches.
- Cleans all owned entities when the script is reloaded or aborted.
- Keeps entity counts bounded and performs expensive scans on timed intervals.
- Writes a compact diagnostic summary to `scripts/PascaTraffic.log`.

## Requirements

- Grand Theft Auto V Enhanced
- Script Hook V compatible with the installed GTA V build
- [ScriptHookVDotNet Enhanced](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced)
- Microsoft .NET Framework 4.8

PascaTraffic v0.1.0 was developed and tested with:

- GTA V Enhanced 1.0.1158.13
- ScriptHookVDotNet Enhanced 1.1.0.6
- ScriptHookVDotNet API 3.9.0

## Installation

1. Download `PascaTraffic-v0.1.0.zip` from the Releases page.
2. Extract these files into the GTA V Enhanced `scripts` directory:
   - `PascaTraffic.dll`
   - `PascaTraffic.ini`
   - `PascaTraffic_Vehicles.ini`
3. Start Story Mode.

Do not run PascaTraffic alongside another script that generates MP traffic or
replaces parked vehicles. ScriptHookVDotNet scans subdirectories recursively,
so disabled DLL files and backups must be stored outside the `scripts`
directory.

No GameConfig, Heap Adjuster, Packfile Limit Adjuster, or DLC list change is
required by PascaTraffic.

## Usage

PascaTraffic starts automatically in Story Mode. It has no user interface or
in-game notification.

To reload the script during testing:

1. Open the ScriptHookVDotNet console.
2. Enter `Reload()`.
3. Press Enter.

The default ScriptHookVDotNet console key is F4, although it can be changed
with `ConsoleKeyBinding` in `ScriptHookVDotNet.ini`.

## Configuration

The supplied defaults are intentionally conservative.

| Section | Setting | Default | Description |
| --- | --- | ---: | --- |
| `MAIN` | `TickIntervalMs` | `100` | Lightweight scheduler interval |
| `TRAFFIC` | `MaxVehicles` | `8` | Maximum owned moving vehicles |
| `TRAFFIC` | `SpawnIntervalMs` | `3500` | Delay between spawn attempts |
| `TRAFFIC` | `MinSpawnDistance` | `95` | Minimum traffic spawn distance |
| `TRAFFIC` | `DespawnDistance` | `420` | Moving vehicle cleanup distance |
| `DRIVER` | `DrivingStyle` | `786603` | Conservative road-driving flags |
| `WATCHDOG` | `StuckTimeoutMs` | `20000` | Time before genuine stuck recovery |
| `PARKING` | `MaxVehicles` | `10` | Maximum owned parked vehicles |
| `PARKING` | `ScanRadius` | `120` | Parking candidate search radius |
| `PARKING` | `MinSwapDistance` | `35` | Minimum safe replacement distance |

Vehicle model names can be edited in `PascaTraffic_Vehicles.ini`. Weaponized,
aircraft, boats, emergency vehicles, and special-purpose vehicles are excluded
from the supplied catalog.

## Performance

PascaTraffic does not perform vehicle searches every frame. Its lightweight
scheduler runs at 10 Hz, while road, parking, and watchdog work is gated by
multi-second timers. Road occupancy uses a native boolean query instead of
allocating nearby-vehicle arrays, and parking selection uses a single-pass
candidate algorithm.

The default configuration owns at most eight moving vehicles and ten parked
vehicles. Distant entities are cleaned automatically.

## Building

Compile `PascaTraffic.cs` as an optimized x64 .NET Framework class library and
reference `ScriptHookVDotNet3.dll` from the GTA V Enhanced installation.

Example using the .NET Framework compiler:

```powershell
csc.exe /target:library /platform:x64 /optimize+ `
  /reference:"C:\Path\To\GTA V Enhanced\ScriptHookVDotNet3.dll" `
  /out:PascaTraffic.dll PascaTraffic.cs
```

## Diagnostics

Runtime statistics are written to `scripts/PascaTraffic.log` every 30 seconds.
The log includes active and total spawn counts, road-node failures, occupied
spawn points, invalid models, AI recoveries, cleanup counts, and parking
candidate statistics.

Script loading errors are written to `ScriptHookVDotNet.log` in the GTA V
Enhanced root directory.

## Project Status

Version 0.1.0 is the initial public test release. Reports should include both
`PascaTraffic.log` and `ScriptHookVDotNet.log`, the GTA V build number, and the
ScriptHookVDotNet Enhanced version.
