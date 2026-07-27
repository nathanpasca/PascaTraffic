# Changelog

All notable changes to PascaTraffic are documented in this file.

## 0.2.0 - 2026-07-27

- Enforced `MaxSpawnDistance` as a real road-node acceptance limit instead of
  allowing sparse nodes almost as far away as the cleanup radius.
- Added a separate, bounded rural node distance and support for unpaved roads.
- Expanded rural zone classification for the northern half of the map.
- Required new traffic to spawn in the player's forward road corridor.
- Added early cleanup for generated vehicles that fall well behind the player.
- Increased the recommended moving-traffic pool from 8 to 16 vehicles.
- Added a configurable recent-model cooldown to reduce visible repetition.
- Added an optional free-roam vanilla traffic-density rebalance so MP vehicles
  are more noticeable without relying only on additional entities.
- Added a relaxed fallback after forward-preferred city node attempts so
  winding or unusual road layouts cannot starve the traffic pool.
- Added recent generated model names to diagnostic summaries.
- Added region and visibility counters to runtime diagnostic summaries.
- Expanded the supplied GTA Online vehicle catalog from 155 to 339 models.
- Added a separate zone-aware sports-classic category.
- Included drift variants while excluding Arena War, weaponized, police and
  emergency, dedicated race, and mission-specific vehicles.

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
