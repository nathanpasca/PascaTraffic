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
- Releases script ownership during player entry and carjacking so vanilla
  animations can complete normally.
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

PascaTraffic v0.1.1 was developed and tested with:

- GTA V Enhanced 1.0.1158.13
- ScriptHookVDotNet Enhanced 1.1.0.6
- ScriptHookVDotNet API 3.9.0

## Installation

1. Download `PascaTraffic-v0.1.1.zip` from the Releases page.
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

All settings are stored in `PascaTraffic.ini`. The supplied defaults are
recommended for most players. You do not need to change anything before using
the mod.

You can edit the file with Notepad while the game is running. Save the file,
open the ScriptHookVDotNet console, and enter `Reload()` to apply the new
values.

### Understanding the Values

- Settings ending in `Ms` use milliseconds. `1000` milliseconds equals one
  second.
- Distance settings use in-game meters.
- Driver speed settings use meters per second. Multiply a value by `3.6` to
  estimate kilometers per hour.
- A value of `true` means enabled. A value of `false` means disabled.
- Decimal chances use a range from `0.0` to `1.0`. For example, `0.50` means a
  50 percent chance.

### Main Settings

| Setting | Default | Plain-language explanation |
| --- | ---: | --- |
| `Enabled` | `true` | Master switch for the entire mod. Set this to `false` to keep the DLL installed but stop all PascaTraffic activity. |
| `TrafficEnabled` | `true` | Enables moving GTA Online vehicles in normal road traffic. |
| `ParkingEnabled` | `true` | Enables GTA Online vehicles in suitable parking locations. |
| `VerboseLogging` | `false` | Writes extra technical details to the log. Leave this off for normal play and turn it on only when diagnosing a problem. |
| `TickIntervalMs` | `100` | How often the lightweight scheduler wakes up. `100` means ten times per second. Higher values use slightly less CPU but make timers less precise. |
| `StartupGraceMs` | `8000` | Waits eight seconds after the script starts before adding vehicles. This gives Story Mode time to finish loading. |
| `MissionGraceMs` | `8000` | Waits eight seconds after a mission, cutscene, or character switch before traffic generation resumes. |
| `SummaryIntervalMs` | `30000` | Writes one diagnostic summary every 30 seconds. This does not display anything on screen. |

### Moving Traffic

PascaTraffic adds vehicles alongside Rockstar's traffic. `MaxVehicles` is not
the total number of cars in the world; it is only the maximum number owned by
this mod.

| Setting | Default | Plain-language explanation |
| --- | ---: | --- |
| `MaxVehicles` | `8` | Maximum additional moving vehicles. Use `6` for a lighter setup, `8` for the recommended balance, or `10` for more variety. |
| `SpawnIntervalMs` | `3500` | Time between spawn attempts. A lower value fills traffic faster but performs road searches more often. |
| `MinSpawnDistance` | `95.0` | The closest distance at which a vehicle may appear. Lower values make new cars easier to notice popping in. |
| `MaxSpawnDistance` | `180.0` | The farthest distance used when searching for a road spawn point. |
| `DespawnDistance` | `420.0` | Removes a PascaTraffic vehicle after it becomes this far from the player. This prevents vehicles accumulating across the map. |
| `SpawnClearRadius` | `9.0` | Required empty space around a road node. Raising this reduces overlap risk but may reduce successful spawns in dense traffic. |
| `NodeAttempts` | `14` | Number of road positions tried during one spawn attempt. Leave this at the default unless the log repeatedly reports `noNode`. |
| `ModelAttempts` | `5` | Number of vehicle models tried if the first selected model cannot be loaded. |
| `ModelRequestTimeoutMs` | `1200` | Maximum time allowed for one model-loading request. Increasing it may help very slow storage but can delay a spawn attempt. |

Recommended limits:

- `6` to `8` moving vehicles for integrated graphics or older CPUs.
- `8` to `10` moving vehicles for a typical gaming PC.
- Values above `12` are not recommended without extended testing.

### Driver Behavior

Speed values are targets, not guaranteed speeds. Drivers still slow down for
traffic, pedestrians, intersections, corners, and road conditions.

| Setting | Default | Plain-language explanation |
| --- | ---: | --- |
| `DrivingStyle` | `786603` | Controls GTA's internal driving rules. The default follows roads, avoids traffic and pedestrians, and stops normally. Changing this number can produce unsafe or broken driving. |
| `Ability` | `0.85` | General driver skill from `0.0` to `1.0`. Higher values usually produce smoother control. |
| `Aggressiveness` | `0.10` | Driver impatience from `0.0` to `1.0`. Higher values can cause risky overtaking and collisions. |
| `CityMinSpeed` | `10.0` | Lowest city cruise target, approximately 36 km/h. |
| `CityMaxSpeed` | `14.0` | Highest city cruise target, approximately 50 km/h. |
| `HighwayMinSpeed` | `18.0` | Lowest highway cruise target, approximately 65 km/h. |
| `HighwayMaxSpeed` | `23.0` | Highest highway cruise target, approximately 83 km/h. |
| `FreeRoadInitialSpeed` | `8.0` | Initial speed when the spawn point has no nearby moving traffic. |
| `BlockedRoadInitialSpeed` | `2.5` | Gentle initial speed used near slow traffic to reduce rear-end collisions. |

For natural Story Mode traffic, leave `DrivingStyle`, `Ability`, and
`Aggressiveness` at their defaults.

### Stuck-driver Watchdog

The watchdog does not constantly control every driver. It checks occasionally
and intervenes only when a vehicle has made no meaningful progress. A driver
waiting at a traffic light or behind another vehicle is treated as normal
traffic and is not reset.

| Setting | Default | Plain-language explanation |
| --- | ---: | --- |
| `IntervalMs` | `2000` | Checks owned traffic every two seconds. |
| `StuckTimeoutMs` | `20000` | A vehicle must remain stuck for 20 seconds before recovery is considered. |
| `ProgressDistance` | `2.0` | Moving at least two meters counts as progress and resets the stuck timer. |
| `ObstructionRadius` | `16.0` | Vehicles within this distance are treated as a legitimate queue or traffic jam. |
| `MaxRecoveries` | `2` | Maximum number of recovery attempts before a repeatedly broken vehicle is safely removed. |

Do not lower `StuckTimeoutMs` aggressively. Short values can mistake red lights
and normal congestion for broken AI.

### Parked Vehicles

The parking system does not blindly place cars at random coordinates. It looks
for suitable empty vanilla vehicles and replaces them outside the player's
immediate view. Because of this safety rule, some areas may show fewer parked
MP vehicles than others.

| Setting | Default | Plain-language explanation |
| --- | ---: | --- |
| `MaxVehicles` | `10` | Maximum parked vehicles owned by PascaTraffic. This is a limit, not a guaranteed amount. |
| `ScanIntervalMs` | `3500` | Time between parking searches. Increasing it reduces search frequency but makes parking populate more slowly. |
| `ScanRadius` | `120.0` | Searches for parking candidates within 120 meters of the player. |
| `MinSwapDistance` | `35.0` | Prevents a nearby vehicle from changing while the player is standing close to it. |
| `DespawnDistance` | `260.0` | Cleans a parked PascaTraffic vehicle after the player travels far away. |
| `SwapChance` | `0.50` | Gives each scheduled parking search a 50 percent chance to run. |
| `MaxCandidateSpeed` | `0.60` | Allows only empty vehicles that are stopped or experiencing tiny physics movement. |
| `MaxViewDot` | `0.35` | Controls how far outside the player's forward view a replacement must be. Lower values hide replacements more strictly. |

`MaxViewDot` is an advanced setting. Leave it at the default if you are unsure.

### Simple Presets

Only change the listed settings. Leave every other value at its default.

#### Lower overhead

```ini
[MAIN]
TickIntervalMs = 150

[TRAFFIC]
MaxVehicles = 6
SpawnIntervalMs = 4500

[PARKING]
MaxVehicles = 6
ScanIntervalMs = 5000
```

#### Recommended balance

Use the supplied `PascaTraffic.ini` without changes.

#### More vehicle variety

```ini
[TRAFFIC]
MaxVehicles = 10
SpawnIntervalMs = 3000

[PARKING]
MaxVehicles = 12
```

The higher-variety preset adds more persistent entities. Test it for a while
before increasing the values further.

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

Version 0.1.1 is the current public test release. It fixes driver recovery
interfering with player-entry and carjacking animations. Reports should include
both `PascaTraffic.log` and `ScriptHookVDotNet.log`, the GTA V build number, and
the ScriptHookVDotNet Enhanced version.
