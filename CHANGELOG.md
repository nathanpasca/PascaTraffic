# Changelog

All notable changes to PascaTraffic are documented in this file.

## 0.1.1 - 2026-07-26

- Fixed a carjacking race condition that could place the original driver back
  into the vehicle while the player was entering.
- PascaTraffic now detects when the player targets an owned vehicle and
  releases the driver and vehicle from script control.
- Prevented the stuck-driver watchdog from recovering a driver during a
  player-entry or carjacking animation.
- Added `releasedForPlayer` to diagnostic summaries.
- Expanded the README configuration guide for non-technical users.

## 0.1.0 - 2026-07-26

Initial public test release.

- Added zone-aware GTA Online vehicle traffic.
- Added dynamic parked-vehicle replacement.
- Added traffic-light-aware stuck-driver recovery.
- Added mission, cutscene, loading, and character-switch safeguards.
- Added bounded entity ownership and automatic distance cleanup.
- Added interval-based scheduling and allocation-conscious spawn checks.
- Added configurable vehicle catalog and runtime diagnostic summaries.
