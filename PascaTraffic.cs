using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GTA;
using GTA.Math;
using GTA.Native;

[assembly: AssemblyTitle("PascaTraffic")]
[assembly: AssemblyDescription("Lightweight GTA V Enhanced MP traffic and parking generator")]
[assembly: AssemblyCompany("nathanpasca")]
[assembly: AssemblyProduct("PascaTraffic")]
[assembly: AssemblyCopyright("Copyright 2026 nathanpasca")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

namespace PascaTraffic
{
    public sealed class PascaTrafficScript : Script
    {
        private sealed class TrafficSlot
        {
            public Vehicle Vehicle;
            public Ped Driver;
            public Vector3 LastPosition;
            public int LastProgressTime;
            public int Recoveries;
            public string SpawnZone;
        }

        private sealed class ParkedSlot
        {
            public Vehicle Vehicle;
        }

        private readonly Random _random = new Random();
        private readonly List<TrafficSlot> _traffic = new List<TrafficSlot>();
        private readonly List<ParkedSlot> _parked = new List<ParkedSlot>();
        private readonly Queue<string> _recentModels = new Queue<string>();
        private readonly HashSet<string> _recentModelSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ModConfig _config;
        private ModelCatalog _catalog;
        private ModLog _log;

        private int _nextTrafficSpawn;
        private int _nextParkingScan;
        private int _nextWatchdog;
        private int _nextSummary;
        private int _resumeTime;
        private bool _wasRestricted;
        private bool _wasMissionActive;

        private int _attempts;
        private int _noNode;
        private int _occupied;
        private int _finalOccupied;
        private int _invalidModel;
        private int _spawnedTraffic;
        private int _spawnedParking;
        private int _retasked;
        private int _cleaned;
        private int _parkingScans;
        private int _parkingVehiclesSeen;
        private int _parkingCandidatesSeen;
        private int _releasedForPlayer;
        private int _candidateTooFar;
        private int _candidateBehind;
        private int _behindCleaned;
        private int _unpavedSearches;
        private int _recentModelRerolls;
        private string _lastSpawnZone = "NONE";

        internal static readonly HashSet<string> RichZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RICHM", "RGLEN", "ROCKF", "VINE", "DTVINE", "WVINE",
            "CHIL", "PBLUFF", "GOLF", "MORN", "OBSERV"
        };

        internal static readonly HashSet<string> RuralZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ALAMO", "BANHAMCA", "BAYTRE", "BHAMCA", "BRADP", "BRADT",
            "CALAFB", "CANNY", "CCREAK", "CHU", "CMSW", "DESRT", "ELGORL",
            "GALFISH", "GRAPES", "GREATC", "HARMO", "HUMLAB", "LACT", "LAGO",
            "LDAM", "MTCHIL", "MTGORDO", "MTJOSE", "NCHU", "PALCOV",
            "PALETO", "PALFOR", "PALHIGH", "PALMPOW", "PROCOB", "RTRAK",
            "SANCHIA", "SANDY", "SLAB", "TATAMO", "TONGVAH", "TONGVAV",
            "WINDF", "ZANCUDO", "ZQ_UAR"
        };

        private static readonly HashSet<string> MixedRoadZones =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CHIL", "RGLEN", "OBSERV", "GALLI"
        };

        internal static readonly HashSet<string> PoorZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DAVIS", "CHAMH", "EBURO", "HAWICK", "MIRR", "RANCHO", "STRAW"
        };

        internal static readonly HashSet<string> IndustrialZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CYPRE", "ELYSIAN", "TERMINA", "BANNING",
            "MURRI", "LMESA", "ZP_ORT", "PORT"
        };

        private static readonly HashSet<string> BlockedZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AIRP", "ARMYB", "JAIL"
        };

        public PascaTrafficScript()
        {
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 100;

            try
            {
                string scriptsPath = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(scriptsPath))
                    throw new InvalidOperationException("Could not resolve the scripts directory.");
                _log = new ModLog(Path.Combine(scriptsPath, "PascaTraffic.log"));
                _log.Reset();
                _config = ModConfig.Load(Path.Combine(scriptsPath, "PascaTraffic.ini"));
                _catalog = ModelCatalog.Load(Path.Combine(scriptsPath, "PascaTraffic_Vehicles.ini"), _log);
                Interval = _config.TickIntervalMs;

                _log.Info("PascaTraffic started.");
                _log.Info("Traffic enabled=" + _config.TrafficEnabled +
                          ", parking enabled=" + _config.ParkingEnabled +
                          ", allow during missions=" + _config.AllowDuringMissions +
                          ", models=" + _catalog.Count + ".");

                _nextTrafficSpawn = Game.GameTime + _config.StartupGraceMs;
                _nextParkingScan = Game.GameTime + _config.StartupGraceMs;
                _nextWatchdog = Game.GameTime + _config.WatchdogIntervalMs;
                _nextSummary = Game.GameTime + _config.SummaryIntervalMs;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Error("Initialization failed", ex);
                Abort();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (_config == null || !_config.Enabled) return;

                bool missionActive = Function.Call<bool>(Hash.GET_MISSION_FLAG);
                if (IsRestrictedState(missionActive))
                {
                    if (!_wasRestricted)
                    {
                        _wasRestricted = true;
                        CleanupForMission();
                        _log.Info("Generators suspended for mission/cutscene/loading.");
                    }
                    return;
                }

                if (_wasRestricted)
                {
                    _wasRestricted = false;
                    _resumeTime = Game.GameTime + _config.MissionGraceMs;
                    _log.Info("Free roam restored; generators resume after grace period.");
                }

                if (missionActive && !_wasMissionActive)
                {
                    _wasMissionActive = true;
                    CleanupParkingForMission();
                    _log.Info("Mission traffic enabled; parking replacement and density adjustment remain suspended.");
                }
                else if (!missionActive && _wasMissionActive)
                {
                    _wasMissionActive = false;
                    _resumeTime = Game.GameTime + _config.MissionGraceMs;
                    _log.Info("Mission ended; all generators resume after grace period.");
                }

                if (Game.GameTime < _resumeTime) return;

                Ped player = Game.Player.Character;
                if (player == null || !player.Exists()) return;

                HandlePlayerVehicleEntry(player);

                if (Game.GameTime >= _nextWatchdog)
                {
                    MaintainTraffic(player);
                    if (!missionActive)
                        MaintainParking(player);
                    _nextWatchdog = Game.GameTime + _config.WatchdogIntervalMs;
                }

                if (_config.TrafficEnabled &&
                    _traffic.Count < _config.MaxTrafficVehicles &&
                    Game.GameTime >= _nextTrafficSpawn)
                {
                    AttemptTrafficSpawn(player);
                    _nextTrafficSpawn = Game.GameTime + _config.TrafficSpawnIntervalMs;
                }

                if (!missionActive &&
                    _config.ParkingEnabled &&
                    _parked.Count < _config.MaxParkedVehicles &&
                    Game.GameTime >= _nextParkingScan)
                {
                    AttemptParkingSwap(player);
                    _nextParkingScan = Game.GameTime + _config.ParkingScanIntervalMs;
                }

                if (Game.GameTime >= _nextSummary)
                {
                    WriteSummary();
                    _nextSummary = Game.GameTime + _config.SummaryIntervalMs;
                }
            }
            catch (Exception ex)
            {
                _log.Error("Tick failed", ex);
            }
        }

        private bool IsRestrictedState(bool missionActive)
        {
            if (missionActive && !_config.AllowDuringMissions) return true;
            if (Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING)) return true;
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return true;

            Ped player = Game.Player.Character;
            return player == null || !player.Exists() || player.IsDead;
        }

        private void AttemptTrafficSpawn(Ped player)
        {
            _attempts++;

            RoadCandidate candidate;
            if (!TryFindRoadCandidate(player, out candidate))
            {
                _noNode++;
                return;
            }

            string zone = GetZone(candidate.Position);
            if (BlockedZones.Contains(zone))
            {
                _noNode++;
                return;
            }

            string modelName = PickFreshModel(zone);
            Vehicle vehicle;
            string actualModelName;
            if (!TryCreateVehicle(modelName, zone, candidate.Position, candidate.Heading, out vehicle, out actualModelName))
            {
                _invalidModel++;
                return;
            }

            Ped driver = null;
            try
            {
                driver = vehicle.CreateRandomPedOnSeat(VehicleSeat.Driver);
            }
            catch (Exception ex)
            {
                _log.Error("Driver creation failed for " + modelName, ex);
            }

            if (driver == null || !driver.Exists())
            {
                SafeDelete(vehicle);
                return;
            }

            ConfigureAmbientDriver(driver);

            float cruiseSpeed = GetCruiseSpeed(candidate.IsHighway);
            float initialSpeed = GetInitialSpeed(candidate.Position, cruiseSpeed);
            ConfigureDriveTask(driver, vehicle, cruiseSpeed);
            Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, vehicle, initialSpeed);

            TrafficSlot slot = new TrafficSlot();
            slot.Vehicle = vehicle;
            slot.Driver = driver;
            slot.LastPosition = vehicle.Position;
            slot.LastProgressTime = Game.GameTime;
            slot.Recoveries = 0;
            slot.SpawnZone = zone;
            _traffic.Add(slot);
            RememberModel(actualModelName);
            _spawnedTraffic++;
            _lastSpawnZone = zone;

            if (_config.VerboseLogging)
            {
                _log.Info("Traffic spawned: " + actualModelName +
                          " zone=" + zone +
                          " distance=" + player.Position.DistanceTo(vehicle.Position).ToString("0", CultureInfo.InvariantCulture) +
                          " speed=" + cruiseSpeed.ToString("0.0", CultureInfo.InvariantCulture) + ".");
            }
        }

        private bool TryFindRoadCandidate(Ped player, out RoadCandidate result)
        {
            result = new RoadCandidate();

            Vector3 origin = player.Position;
            Vector3 forward = player.IsInVehicle() ? player.CurrentVehicle.ForwardVector : player.ForwardVector;
            forward.Z = 0.0f;
            if (forward.LengthSquared() < 0.01f) forward = Vector3.RelativeFront;
            forward.Normalize();

            string originZone = GetZone(origin);
            bool allowUnpaved = _config.AllowUnpavedInRural &&
                (RuralZones.Contains(originZone) || MixedRoadZones.Contains(originZone));
            int nodeFlags = allowUnpaved ? 1 : 0;
            if (allowUnpaved) _unpavedSearches++;

            for (int attempt = 0; attempt < _config.NodeAttempts; attempt++)
            {
                float angle;
                if (attempt == 0)
                    angle = 0.0f;
                else
                    angle = (float)(_random.NextDouble() * 100.0 - 50.0);

                Vector3 direction = RotateFlat(forward, angle);
                float distance = Lerp(_config.MinSpawnDistance, _config.MaxSpawnDistance, (float)_random.NextDouble());
                Vector3 search = origin + direction * distance;
                Vector3 desired = search + direction * 80.0f;

                OutputArgument outPosition = new OutputArgument();
                OutputArgument outHeading = new OutputArgument();
                int nth = 1 + _random.Next(4);

                bool found = Function.Call<bool>(
                    Hash.GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION,
                    search.X, search.Y, search.Z,
                    desired.X, desired.Y, desired.Z,
                    nth, outPosition, outHeading,
                    nodeFlags, 3.0f, 0.0f);

                if (!found) continue;

                Vector3 node = outPosition.GetResult<Vector3>();
                if (node == Vector3.Zero) continue;

                float actualDistance = origin.DistanceTo(node);
                if (actualDistance < _config.MinSpawnDistance)
                    continue;

                float allowedMaxDistance = RuralZones.Contains(originZone)
                    ? _config.RuralMaxSpawnDistance
                    : _config.MaxSpawnDistance;
                if (actualDistance > allowedMaxDistance)
                {
                    _candidateTooFar++;
                    continue;
                }

                Vector3 toNode = node - origin;
                toNode.Z = 0.0f;
                if (toNode.LengthSquared() < 0.01f) continue;
                toNode.Normalize();

                // Curving rural roads and hill hairpins can place a valid node
                // geometrically beside or behind the player's current heading.
                // The hard distance limit keeps these nodes nearby, so apply the
                // forward-cone rule only on the denser paved-road search.
                int forwardPreferenceAttempts = Math.Max(1, (_config.NodeAttempts * 2) / 3);
                if (!allowUnpaved &&
                    attempt < forwardPreferenceAttempts &&
                    Vector3.Dot(forward, toNode) < _config.MinSpawnForwardDot)
                {
                    _candidateBehind++;
                    continue;
                }

                OutputArgument outDensity = new OutputArgument();
                OutputArgument outFlags = new OutputArgument();
                Function.Call<bool>(
                    Hash.GET_VEHICLE_NODE_PROPERTIES,
                    node.X, node.Y, node.Z,
                    outDensity, outFlags);

                int flags = outFlags.GetResult<int>();
                const int alwaysRejectedFlags = 8 | 16 | 32 | 1024;
                int rejectedFlags = alwaysRejectedFlags;
                if (!allowUnpaved)
                    rejectedFlags |= 1;

                if ((flags & rejectedFlags) != 0) continue;

                if (IsSpawnPointOccupied(node))
                {
                    _occupied++;
                    continue;
                }

                result.Position = node;
                result.Heading = outHeading.GetResult<float>();
                result.IsHighway = (flags & 64) != 0;
                return true;
            }

            return false;
        }

        private bool TryRequestVehicleModel(
            string preferredModelName,
            string zone,
            out Model requestedModel,
            out string actualModelName)
        {
            requestedModel = default(Model);
            actualModelName = preferredModelName;

            for (int modelAttempt = 0; modelAttempt < _config.ModelAttempts; modelAttempt++)
            {
                string candidateName = modelAttempt == 0
                    ? preferredModelName
                    : PickFreshModel(zone);

                Model model = new Model(candidateName);
                if (!model.IsValid || !model.IsInCdImage) continue;
                if (!model.Request(_config.ModelRequestTimeoutMs))
                {
                    model.MarkAsNoLongerNeeded();
                    continue;
                }

                requestedModel = model;
                actualModelName = candidateName;
                return true;
            }

            return false;
        }

        private bool TryCreateVehicle(
            string modelName,
            string zone,
            Vector3 position,
            float heading,
            out Vehicle vehicle,
            out string actualModelName)
        {
            vehicle = null;

            Model model;
            if (!TryRequestVehicleModel(modelName, zone, out model, out actualModelName))
                return false;

            try
            {
                // Model streaming can yield long enough for Rockstar traffic to
                // enter a node that was clear during the road search. Recheck
                // immediately before creation to close that overlap window.
                if (IsSpawnPointOccupied(position))
                {
                    _occupied++;
                    _finalOccupied++;
                    return false;
                }

                vehicle = World.CreateVehicle(model, position, heading);
            }
            finally
            {
                model.MarkAsNoLongerNeeded();
            }

            if (vehicle == null || !vehicle.Exists()) return false;

            vehicle.IsPersistent = true;
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle, 5.0f);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle, true, true, false);
            return true;
        }

        private bool IsSpawnPointOccupied(Vector3 position)
        {
            return Function.Call<bool>(
                Hash.IS_ANY_VEHICLE_NEAR_POINT,
                position.X, position.Y, position.Z,
                _config.SpawnClearRadius);
        }

        private void ConfigureAmbientDriver(Ped driver)
        {
            driver.IsPersistent = true;
            driver.BlockPermanentEvents = true;
            driver.CanRagdoll = true;
            driver.CanBeDraggedOutOfVehicle = true;
            driver.IsInvincible = false;
            Function.Call(Hash.SET_DRIVER_ABILITY, driver, _config.DriverAbility);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver, _config.DriverAggressiveness);
            Function.Call(Hash.SET_PED_KEEP_TASK, driver, true);
        }

        private void ConfigureDriveTask(Ped driver, Vehicle vehicle, float speed)
        {
            Function.Call(
                Hash.TASK_VEHICLE_DRIVE_WANDER,
                driver, vehicle, speed, _config.DrivingStyle);
            Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, driver, _config.DrivingStyle);
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, driver, speed);
            Function.Call(Hash.SET_PED_KEEP_TASK, driver, true);
        }

        private float GetCruiseSpeed(bool highway)
        {
            if (highway)
                return Lerp(_config.HighwayMinSpeed, _config.HighwayMaxSpeed, (float)_random.NextDouble());
            return Lerp(_config.CityMinSpeed, _config.CityMaxSpeed, (float)_random.NextDouble());
        }

        private float GetInitialSpeed(Vector3 position, float cruiseSpeed)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(position, 32.0f);
            if (nearby == null || nearby.Length == 0)
                return Math.Min(cruiseSpeed, _config.FreeRoadInitialSpeed);

            float total = 0.0f;
            int count = 0;
            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle v = nearby[i];
                if (v == null || !v.Exists()) continue;
                float speed = v.Velocity.Length();
                if (speed < 0.1f || speed > 45.0f) continue;
                total += speed;
                count++;
            }

            if (count == 0) return _config.BlockedRoadInitialSpeed;
            return Math.Max(_config.BlockedRoadInitialSpeed, Math.Min(cruiseSpeed, total / count));
        }

        private void HandlePlayerVehicleEntry(Ped player)
        {
            Vehicle target = player.VehicleTryingToEnter;
            if (target == null || !target.Exists()) return;

            for (int i = _traffic.Count - 1; i >= 0; i--)
            {
                TrafficSlot slot = _traffic[i];
                Vehicle vehicle = slot.Vehicle;
                if (vehicle == null || !vehicle.Exists() || vehicle.Handle != target.Handle)
                    continue;

                Ped driver = slot.Driver;
                if (driver != null && driver.Exists())
                {
                    driver.BlockPermanentEvents = false;
                    driver.CanBeDraggedOutOfVehicle = true;
                    Function.Call(Hash.SET_PED_KEEP_TASK, driver, false);
                }

                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle, 1);
                ReleaseTrafficSlot(slot);
                _traffic.RemoveAt(i);
                _releasedForPlayer++;
                _log.Info("Released an MP traffic vehicle for player entry/carjacking.");
                return;
            }

            for (int i = _parked.Count - 1; i >= 0; i--)
            {
                Vehicle vehicle = _parked[i].Vehicle;
                if (vehicle == null || !vehicle.Exists() || vehicle.Handle != target.Handle)
                    continue;

                Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, vehicle, 1);
                vehicle.MarkAsNoLongerNeeded();
                _parked.RemoveAt(i);
                _releasedForPlayer++;
                _log.Info("Released a parked MP vehicle for player entry.");
                return;
            }
        }

        private void MaintainTraffic(Ped player)
        {
            for (int i = _traffic.Count - 1; i >= 0; i--)
            {
                TrafficSlot slot = _traffic[i];
                Vehicle vehicle = slot.Vehicle;
                Ped driver = slot.Driver;

                if (vehicle == null || !vehicle.Exists() || vehicle.IsDead ||
                    driver == null || !driver.Exists() || driver.IsDead)
                {
                    DeleteTrafficSlot(slot);
                    _traffic.RemoveAt(i);
                    continue;
                }

                if (player.IsInVehicle() && player.CurrentVehicle.Handle == vehicle.Handle)
                {
                    ReleaseTrafficSlot(slot);
                    _traffic.RemoveAt(i);
                    _log.Info("Player took an MP traffic vehicle; ownership released.");
                    continue;
                }

                float distance = player.Position.DistanceTo(vehicle.Position);
                if (distance > _config.DespawnDistance ||
                    Function.Call<bool>(Hash.IS_ENTITY_IN_WATER, vehicle) ||
                    Function.Call<bool>(Hash.IS_VEHICLE_STUCK_ON_ROOF, vehicle))
                {
                    DeleteTrafficSlot(slot);
                    _traffic.RemoveAt(i);
                    continue;
                }

                if (distance > _config.BehindDespawnDistance &&
                    IsBehindPlayer(player, vehicle.Position, _config.BehindDespawnDot))
                {
                    DeleteTrafficSlot(slot);
                    _traffic.RemoveAt(i);
                    _behindCleaned++;
                    continue;
                }

                Ped seatedDriver = vehicle.GetPedOnSeat(VehicleSeat.Driver);
                if (seatedDriver == null || !seatedDriver.Exists() || seatedDriver.Handle != driver.Handle)
                {
                    if (!TryRecoverDriver(slot))
                    {
                        DeleteTrafficSlot(slot);
                        _traffic.RemoveAt(i);
                    }
                    continue;
                }

                float progress = slot.LastPosition.DistanceTo(vehicle.Position);
                if (progress >= _config.StuckProgressDistance)
                {
                    slot.LastPosition = vehicle.Position;
                    slot.LastProgressTime = Game.GameTime;
                    slot.Recoveries = 0;
                    continue;
                }

                if (Game.GameTime - slot.LastProgressTime < _config.StuckTimeoutMs) continue;

                if (vehicle.IsStoppedAtTrafficLights)
                {
                    slot.LastPosition = vehicle.Position;
                    slot.LastProgressTime = Game.GameTime;
                    slot.Recoveries = 0;
                    continue;
                }

                if (HasTrafficObstruction(vehicle.Position, vehicle.Handle))
                {
                    slot.LastPosition = vehicle.Position;
                    slot.LastProgressTime = Game.GameTime;
                    slot.Recoveries = 0;
                    continue;
                }

                if (slot.Recoveries >= _config.MaxDriverRecoveries)
                {
                    _log.Info("Cleaning repeatedly stuck traffic vehicle.");
                    DeleteTrafficSlot(slot);
                    _traffic.RemoveAt(i);
                    continue;
                }

                slot.Recoveries++;
                slot.LastPosition = vehicle.Position;
                slot.LastProgressTime = Game.GameTime;
                ConfigureDriveTask(driver, vehicle, GetCruiseSpeed(false));
                _retasked++;
                if (_config.VerboseLogging)
                    _log.Info("Retasked a stuck MP traffic driver (recovery " + slot.Recoveries + ").");
            }
        }

        private bool TryRecoverDriver(TrafficSlot slot)
        {
            if (slot.Recoveries >= _config.MaxDriverRecoveries) return false;

            slot.Recoveries++;
            Function.Call(Hash.SET_PED_INTO_VEHICLE, slot.Driver, slot.Vehicle, -1);
            ConfigureDriveTask(slot.Driver, slot.Vehicle, GetCruiseSpeed(false));
            slot.LastPosition = slot.Vehicle.Position;
            slot.LastProgressTime = Game.GameTime;
            _retasked++;
            if (_config.VerboseLogging)
                _log.Info("Recovered a driver that left its MP traffic vehicle.");
            return true;
        }

        private bool HasTrafficObstruction(Vector3 position, int ownHandle)
        {
            Vehicle[] nearby = World.GetNearbyVehicles(position, _config.ObstructionRadius);
            if (nearby == null) return false;

            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle vehicle = nearby[i];
                if (vehicle == null || !vehicle.Exists() || vehicle.Handle == ownHandle) continue;
                return true;
            }

            return false;
        }

        private void AttemptParkingSwap(Ped player)
        {
            if (_random.NextDouble() > _config.ParkingSwapChance) return;

            _parkingScans++;
            Vehicle[] nearby = World.GetNearbyVehicles(player.Position, _config.ParkingScanRadius);
            if (nearby == null || nearby.Length == 0) return;
            _parkingVehiclesSeen += nearby.Length;

            Vehicle selected = null;
            int candidateCount = 0;
            for (int i = 0; i < nearby.Length; i++)
            {
                Vehicle vehicle = nearby[i];
                if (!IsSafeParkingCandidate(player, vehicle)) continue;
                _parkingCandidatesSeen++;
                candidateCount++;
                if (_random.Next(candidateCount) == 0)
                    selected = vehicle;
            }

            if (selected == null) return;

            Vehicle original = selected;
            Vector3 position = original.Position;
            Vector3 rotation = original.Rotation;
            float heading = original.Heading;
            string zone = GetZone(position);
            string modelName = PickFreshModel(zone);

            Model requestedModel;
            string actualModelName;
            if (!TryRequestVehicleModel(modelName, zone, out requestedModel, out actualModelName))
            {
                _invalidModel++;
                return;
            }

            Vehicle replacement = null;
            try
            {
                original.Delete();
                replacement = World.CreateVehicle(requestedModel, position, heading);
            }
            finally
            {
                requestedModel.MarkAsNoLongerNeeded();
            }

            if (replacement == null || !replacement.Exists())
            {
                _invalidModel++;
                return;
            }

            replacement.IsPersistent = true;
            replacement.Rotation = rotation;
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, replacement, false, true, false);
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, replacement, true);
            Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, replacement, 1);

            ParkedSlot slot = new ParkedSlot();
            slot.Vehicle = replacement;
            _parked.Add(slot);
            RememberModel(actualModelName);
            _spawnedParking++;
            _lastSpawnZone = zone;

            if (_config.VerboseLogging)
                _log.Info("Parking swapped: " + actualModelName + " zone=" + zone + ".");
        }

        private bool IsSafeParkingCandidate(Ped player, Vehicle vehicle)
        {
            if (vehicle == null || !vehicle.Exists() || vehicle.IsDead) return false;
            if (vehicle.IsPersistent) return false;
            if (Function.Call<bool>(Hash.IS_ENTITY_A_MISSION_ENTITY, vehicle)) return false;

            Ped driver = vehicle.GetPedOnSeat(VehicleSeat.Driver);
            if (driver != null && driver.Exists()) return false;

            float speed = vehicle.Velocity.Length();
            if (speed > _config.ParkedMaxSpeed) return false;

            float distance = player.Position.DistanceTo(vehicle.Position);
            if (distance < _config.ParkingMinSwapDistance ||
                distance > _config.ParkingScanRadius)
                return false;

            Vector3 toVehicle = vehicle.Position - player.Position;
            toVehicle.Z = 0.0f;
            if (toVehicle.LengthSquared() < 0.01f) return false;
            toVehicle.Normalize();

            Vector3 view = player.IsInVehicle() ? player.CurrentVehicle.ForwardVector : player.ForwardVector;
            view.Z = 0.0f;
            if (view.LengthSquared() < 0.01f) return false;
            view.Normalize();

            return Vector3.Dot(view, toVehicle) < _config.ParkingMaxViewDot;
        }

        private void MaintainParking(Ped player)
        {
            for (int i = _parked.Count - 1; i >= 0; i--)
            {
                ParkedSlot slot = _parked[i];
                Vehicle vehicle = slot.Vehicle;

                if (vehicle == null || !vehicle.Exists() || vehicle.IsDead)
                {
                    _parked.RemoveAt(i);
                    continue;
                }

                if (player.IsInVehicle() && player.CurrentVehicle.Handle == vehicle.Handle)
                {
                    vehicle.MarkAsNoLongerNeeded();
                    _parked.RemoveAt(i);
                    _log.Info("Player took a parked MP vehicle; ownership released.");
                    continue;
                }

                if (player.Position.DistanceTo(vehicle.Position) > _config.ParkingDespawnDistance)
                {
                    SafeDelete(vehicle);
                    _parked.RemoveAt(i);
                    _cleaned++;
                }
            }
        }

        private void CleanupForMission()
        {
            Ped player = Game.Player.Character;
            int playerVehicleHandle = 0;
            if (player != null && player.Exists() && player.IsInVehicle())
                playerVehicleHandle = player.CurrentVehicle.Handle;

            for (int i = _traffic.Count - 1; i >= 0; i--)
            {
                TrafficSlot slot = _traffic[i];
                if (slot.Vehicle != null && slot.Vehicle.Exists() &&
                    slot.Vehicle.Handle == playerVehicleHandle)
                    ReleaseTrafficSlot(slot);
                else
                    DeleteTrafficSlot(slot);
            }
            _traffic.Clear();

            CleanupParkingForMission();
        }

        private void CleanupParkingForMission()
        {
            Ped player = Game.Player.Character;
            int playerVehicleHandle = 0;
            if (player != null && player.Exists() && player.IsInVehicle())
                playerVehicleHandle = player.CurrentVehicle.Handle;

            for (int i = _parked.Count - 1; i >= 0; i--)
            {
                Vehicle vehicle = _parked[i].Vehicle;
                if (vehicle != null && vehicle.Exists() && vehicle.Handle == playerVehicleHandle)
                    vehicle.MarkAsNoLongerNeeded();
                else
                    SafeDelete(vehicle);
            }
            _parked.Clear();
        }

        private void DeleteTrafficSlot(TrafficSlot slot)
        {
            if (slot == null) return;
            SafeDelete(slot.Driver);
            SafeDelete(slot.Vehicle);
            _cleaned++;
        }

        private void ReleaseTrafficSlot(TrafficSlot slot)
        {
            if (slot == null) return;
            if (slot.Driver != null && slot.Driver.Exists()) slot.Driver.MarkAsNoLongerNeeded();
            if (slot.Vehicle != null && slot.Vehicle.Exists()) slot.Vehicle.MarkAsNoLongerNeeded();
        }

        private static void SafeDelete(Entity entity)
        {
            if (entity == null || !entity.Exists()) return;
            try
            {
                entity.Delete();
            }
            catch
            {
                try { entity.MarkAsNoLongerNeeded(); }
                catch { }
            }
        }

        private static bool IsBehindPlayer(
            Ped player,
            Vector3 position,
            float behindDot)
        {
            Vector3 toVehicle = position - player.Position;
            toVehicle.Z = 0.0f;
            if (toVehicle.LengthSquared() < 0.01f) return false;
            toVehicle.Normalize();

            Vector3 view = player.IsInVehicle()
                ? player.CurrentVehicle.ForwardVector
                : player.ForwardVector;
            view.Z = 0.0f;
            if (view.LengthSquared() < 0.01f) return false;
            view.Normalize();

            return Vector3.Dot(view, toVehicle) < behindDot;
        }

        private string PickFreshModel(string zone)
        {
            string selected = _catalog.Pick(zone, _random);
            if (_config.RecentModelWindow <= 0) return selected;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (!_recentModelSet.Contains(selected))
                    return selected;

                _recentModelRerolls++;
                selected = _catalog.Pick(zone, _random);
            }

            return selected;
        }

        private void RememberModel(string modelName)
        {
            if (_config.RecentModelWindow <= 0 || string.IsNullOrEmpty(modelName))
                return;

            if (_recentModelSet.Add(modelName))
                _recentModels.Enqueue(modelName);

            while (_recentModels.Count > _config.RecentModelWindow)
            {
                string oldest = _recentModels.Dequeue();
                _recentModelSet.Remove(oldest);
            }
        }

        private string GetRecentModelSummary(int maximumCount)
        {
            if (_recentModels.Count == 0 || maximumCount <= 0)
                return "NONE";

            string[] models = _recentModels.ToArray();
            int start = Math.Max(0, models.Length - maximumCount);
            string result = string.Empty;
            for (int i = start; i < models.Length; i++)
            {
                if (result.Length > 0) result += "|";
                result += models[i];
            }
            return result;
        }

        private string GetZone(Vector3 position)
        {
            string zone = Function.Call<string>(Hash.GET_NAME_OF_ZONE, position.X, position.Y, position.Z);
            return string.IsNullOrEmpty(zone) ? "MIDDLE" : zone;
        }

        private void WriteSummary()
        {
            _log.Info(
                "Summary: activeTraffic=" + _traffic.Count +
                ", activeParking=" + _parked.Count +
                ", attempts=" + _attempts +
                ", spawnedTraffic=" + _spawnedTraffic +
                ", spawnedParking=" + _spawnedParking +
                ", noNode=" + _noNode +
                ", occupied=" + _occupied +
                ", finalOccupied=" + _finalOccupied +
                ", invalidModel=" + _invalidModel +
                ", retasked=" + _retasked +
                ", cleaned=" + _cleaned +
                ", parkingScans=" + _parkingScans +
                ", parkingVehiclesSeen=" + _parkingVehiclesSeen +
                ", parkingCandidatesSeen=" + _parkingCandidatesSeen +
                ", releasedForPlayer=" + _releasedForPlayer +
                ", currentZone=" + GetZone(Game.Player.Character.Position) +
                ", lastSpawnZone=" + _lastSpawnZone +
                ", candidateTooFar=" + _candidateTooFar +
                ", candidateBehind=" + _candidateBehind +
                ", behindCleaned=" + _behindCleaned +
                ", unpavedSearches=" + _unpavedSearches +
                ", recentModelRerolls=" + _recentModelRerolls +
                ", recentModels=" + GetRecentModelSummary(8) + ".");
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try
            {
                CleanupForMission();
                if (_log != null) _log.Info("PascaTraffic aborted and owned entities cleaned.");
            }
            catch { }
        }

        private static Vector3 RotateFlat(Vector3 vector, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new Vector3(
                vector.X * cos - vector.Y * sin,
                vector.X * sin + vector.Y * cos,
                0.0f);
        }

        private static float Lerp(float min, float max, float amount)
        {
            return min + (max - min) * amount;
        }

        private sealed class RoadCandidate
        {
            public Vector3 Position;
            public float Heading;
            public bool IsHighway;
        }
    }

    public sealed class PascaTrafficDensityScript : Script
    {
        private ModConfig _config;

        public PascaTrafficDensityScript()
        {
            Tick += OnTick;
            Interval = 0;

            try
            {
                string scriptsPath = AppDomain.CurrentDomain.BaseDirectory;
                _config = ModConfig.Load(Path.Combine(scriptsPath, "PascaTraffic.ini"));
            }
            catch
            {
                Abort();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_config == null ||
                !_config.Enabled ||
                !_config.TrafficEnabled ||
                !_config.AdjustVanillaTraffic ||
                _config.VanillaTrafficDensity >= 0.999f)
                return;

            if (Function.Call<bool>(Hash.GET_MISSION_FLAG) ||
                Function.Call<bool>(Hash.IS_CUTSCENE_PLAYING) ||
                Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS))
                return;

            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || player.IsDead)
                return;

            Function.Call(
                Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME,
                _config.VanillaTrafficDensity);
            Function.Call(
                Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME,
                _config.VanillaTrafficDensity);
        }
    }

    internal sealed class ModConfig
    {
        public bool Enabled;
        public bool TrafficEnabled;
        public bool ParkingEnabled;
        public bool AllowDuringMissions;
        public bool VerboseLogging;

        public int TickIntervalMs;
        public int StartupGraceMs;
        public int MissionGraceMs;
        public int SummaryIntervalMs;

        public int MaxTrafficVehicles;
        public int TrafficSpawnIntervalMs;
        public float MinSpawnDistance;
        public float MaxSpawnDistance;
        public float DespawnDistance;
        public float SpawnClearRadius;
        public int NodeAttempts;
        public int ModelAttempts;
        public int ModelRequestTimeoutMs;
        public bool AllowUnpavedInRural;
        public float RuralMaxSpawnDistance;
        public float MinSpawnForwardDot;
        public float BehindDespawnDistance;
        public float BehindDespawnDot;
        public int RecentModelWindow;
        public bool AdjustVanillaTraffic;
        public float VanillaTrafficDensity;

        public float CityMinSpeed;
        public float CityMaxSpeed;
        public float HighwayMinSpeed;
        public float HighwayMaxSpeed;
        public float FreeRoadInitialSpeed;
        public float BlockedRoadInitialSpeed;
        public int DrivingStyle;
        public float DriverAbility;
        public float DriverAggressiveness;

        public int WatchdogIntervalMs;
        public int StuckTimeoutMs;
        public float StuckProgressDistance;
        public float ObstructionRadius;
        public int MaxDriverRecoveries;

        public int MaxParkedVehicles;
        public int ParkingScanIntervalMs;
        public float ParkingScanRadius;
        public float ParkingMinSwapDistance;
        public float ParkingDespawnDistance;
        public double ParkingSwapChance;
        public float ParkedMaxSpeed;
        public float ParkingMaxViewDot;

        public static ModConfig Load(string path)
        {
            ScriptSettings settings = ScriptSettings.Load(path);
            ModConfig c = new ModConfig();

            c.Enabled = settings.GetValue<bool>("MAIN", "Enabled", true);
            c.TrafficEnabled = settings.GetValue<bool>("MAIN", "TrafficEnabled", true);
            c.ParkingEnabled = settings.GetValue<bool>("MAIN", "ParkingEnabled", true);
            c.AllowDuringMissions = settings.GetValue<bool>("MAIN", "AllowDuringMissions", false);
            c.VerboseLogging = settings.GetValue<bool>("MAIN", "VerboseLogging", false);
            c.TickIntervalMs = Clamp(settings.GetValue<int>("MAIN", "TickIntervalMs", 100), 25, 1000);
            c.StartupGraceMs = Clamp(settings.GetValue<int>("MAIN", "StartupGraceMs", 8000), 1000, 60000);
            c.MissionGraceMs = Clamp(settings.GetValue<int>("MAIN", "MissionGraceMs", 8000), 1000, 60000);
            c.SummaryIntervalMs = Clamp(settings.GetValue<int>("MAIN", "SummaryIntervalMs", 30000), 5000, 300000);

            c.MaxTrafficVehicles = Clamp(settings.GetValue<int>("TRAFFIC", "MaxVehicles", 8), 0, 20);
            c.TrafficSpawnIntervalMs = Clamp(settings.GetValue<int>("TRAFFIC", "SpawnIntervalMs", 3500), 1000, 60000);
            c.MinSpawnDistance = Clamp(settings.GetValue<float>("TRAFFIC", "MinSpawnDistance", 95.0f), 60.0f, 400.0f);
            c.MaxSpawnDistance = Clamp(settings.GetValue<float>("TRAFFIC", "MaxSpawnDistance", 180.0f), c.MinSpawnDistance + 10.0f, 500.0f);
            c.DespawnDistance = Clamp(settings.GetValue<float>("TRAFFIC", "DespawnDistance", 420.0f), c.MaxSpawnDistance + 50.0f, 1000.0f);
            c.SpawnClearRadius = Clamp(settings.GetValue<float>("TRAFFIC", "SpawnClearRadius", 9.0f), 5.0f, 30.0f);
            c.NodeAttempts = Clamp(settings.GetValue<int>("TRAFFIC", "NodeAttempts", 14), 1, 40);
            c.ModelAttempts = Clamp(settings.GetValue<int>("TRAFFIC", "ModelAttempts", 5), 1, 20);
            c.ModelRequestTimeoutMs = Clamp(settings.GetValue<int>("TRAFFIC", "ModelRequestTimeoutMs", 1200), 100, 5000);
            c.AllowUnpavedInRural = settings.GetValue<bool>("TRAFFIC", "AllowUnpavedInRural", true);
            c.RuralMaxSpawnDistance = Clamp(
                settings.GetValue<float>("TRAFFIC", "RuralMaxSpawnDistance", 220.0f),
                c.MaxSpawnDistance,
                Math.Min(500.0f, c.DespawnDistance - 50.0f));
            c.MinSpawnForwardDot = Clamp(
                settings.GetValue<float>("TRAFFIC", "MinSpawnForwardDot", 0.05f),
                -1.0f,
                1.0f);
            c.BehindDespawnDistance = Clamp(
                settings.GetValue<float>("TRAFFIC", "BehindDespawnDistance", 170.0f),
                c.MinSpawnDistance,
                c.DespawnDistance - 20.0f);
            c.BehindDespawnDot = Clamp(
                settings.GetValue<float>("TRAFFIC", "BehindDespawnDot", -0.15f),
                -1.0f,
                1.0f);

            c.RecentModelWindow = Clamp(
                settings.GetValue<int>("VARIETY", "RecentModelWindow", 24),
                0,
                100);
            c.AdjustVanillaTraffic = settings.GetValue<bool>(
                "VARIETY",
                "AdjustVanillaTraffic",
                true);
            c.VanillaTrafficDensity = Clamp(
                settings.GetValue<float>("VARIETY", "VanillaTrafficDensity", 0.55f),
                0.10f,
                1.0f);

            c.CityMinSpeed = Clamp(settings.GetValue<float>("DRIVER", "CityMinSpeed", 10.0f), 2.0f, 30.0f);
            c.CityMaxSpeed = Clamp(settings.GetValue<float>("DRIVER", "CityMaxSpeed", 14.0f), c.CityMinSpeed, 40.0f);
            c.HighwayMinSpeed = Clamp(settings.GetValue<float>("DRIVER", "HighwayMinSpeed", 18.0f), 5.0f, 50.0f);
            c.HighwayMaxSpeed = Clamp(settings.GetValue<float>("DRIVER", "HighwayMaxSpeed", 23.0f), c.HighwayMinSpeed, 60.0f);
            c.FreeRoadInitialSpeed = Clamp(settings.GetValue<float>("DRIVER", "FreeRoadInitialSpeed", 8.0f), 0.0f, 30.0f);
            c.BlockedRoadInitialSpeed = Clamp(settings.GetValue<float>("DRIVER", "BlockedRoadInitialSpeed", 2.5f), 0.0f, 15.0f);
            c.DrivingStyle = settings.GetValue<int>("DRIVER", "DrivingStyle", 786603);
            c.DriverAbility = Clamp(settings.GetValue<float>("DRIVER", "Ability", 0.85f), 0.0f, 1.0f);
            c.DriverAggressiveness = Clamp(settings.GetValue<float>("DRIVER", "Aggressiveness", 0.10f), 0.0f, 1.0f);

            c.WatchdogIntervalMs = Clamp(settings.GetValue<int>("WATCHDOG", "IntervalMs", 2000), 500, 10000);
            c.StuckTimeoutMs = Clamp(settings.GetValue<int>("WATCHDOG", "StuckTimeoutMs", 14000), 5000, 120000);
            c.StuckProgressDistance = Clamp(settings.GetValue<float>("WATCHDOG", "ProgressDistance", 2.0f), 0.5f, 20.0f);
            c.ObstructionRadius = Clamp(settings.GetValue<float>("WATCHDOG", "ObstructionRadius", 11.0f), 3.0f, 30.0f);
            c.MaxDriverRecoveries = Clamp(settings.GetValue<int>("WATCHDOG", "MaxRecoveries", 2), 0, 10);

            c.MaxParkedVehicles = Clamp(settings.GetValue<int>("PARKING", "MaxVehicles", 10), 0, 30);
            c.ParkingScanIntervalMs = Clamp(settings.GetValue<int>("PARKING", "ScanIntervalMs", 3500), 1000, 60000);
            c.ParkingScanRadius = Clamp(settings.GetValue<float>("PARKING", "ScanRadius", 220.0f), 80.0f, 500.0f);
            c.ParkingMinSwapDistance = Clamp(settings.GetValue<float>("PARKING", "MinSwapDistance", 100.0f), 25.0f, c.ParkingScanRadius - 10.0f);
            c.ParkingDespawnDistance = Clamp(settings.GetValue<float>("PARKING", "DespawnDistance", 380.0f), c.ParkingScanRadius + 20.0f, 1000.0f);
            c.ParkingSwapChance = Clamp(settings.GetValue<double>("PARKING", "SwapChance", 0.30), 0.0, 1.0);
            c.ParkedMaxSpeed = Clamp(settings.GetValue<float>("PARKING", "MaxCandidateSpeed", 0.15f), 0.0f, 2.0f);
            c.ParkingMaxViewDot = Clamp(settings.GetValue<float>("PARKING", "MaxViewDot", 0.20f), -1.0f, 1.0f);

            return c;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }

    internal sealed class ModelCatalog
    {
        private readonly Dictionary<string, List<string>> _groups =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] RichSelection =
        {
            "Luxury", "Luxury", "Sports", "Sports", "Classic", "SUV", "Sedan", "Coupe"
        };

        private static readonly string[] MiddleSelection =
        {
            "Sedan", "Sedan", "SUV", "Sports", "Classic", "Muscle", "Compact", "Coupe", "Van"
        };

        private static readonly string[] PoorSelection =
        {
            "Compact", "Compact", "Sedan", "Muscle", "Muscle", "Lowrider", "Classic", "Van", "SUV"
        };

        private static readonly string[] RuralSelection =
        {
            "Pickup", "Pickup", "Offroad", "Offroad", "SUV", "Muscle", "Classic", "Van", "Sedan"
        };

        private static readonly string[] IndustrialSelection =
        {
            "Van", "Van", "Pickup", "Industrial", "Sedan", "Muscle", "Offroad", "Classic"
        };

        public int Count { get; private set; }

        public static ModelCatalog Load(string path, ModLog log)
        {
            ModelCatalog catalog = new ModelCatalog();
            string currentGroup = null;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentGroup = line.Substring(1, line.Length - 2).Trim();
                    if (!catalog._groups.ContainsKey(currentGroup))
                        catalog._groups[currentGroup] = new List<string>();
                    continue;
                }

                if (string.IsNullOrEmpty(currentGroup)) continue;

                int comment = line.IndexOf(';');
                if (comment >= 0) line = line.Substring(0, comment).Trim();
                int equals = line.IndexOf('=');
                if (equals >= 0) line = line.Substring(0, equals).Trim();
                if (line.Length == 0) continue;

                catalog._groups[currentGroup].Add(line);
                catalog.Count++;
            }

            if (catalog.Count == 0)
                throw new InvalidOperationException("PascaTraffic_Vehicles.ini contains no models.");

            log.Info("Loaded " + catalog.Count + " vehicle model entries in " + catalog._groups.Count + " groups.");
            return catalog;
        }

        public string Pick(string zone, Random random)
        {
            string[] selection;
            if (RichZones.Contains(zone))
                selection = RichSelection;
            else if (RuralZones.Contains(zone))
                selection = RuralSelection;
            else if (PoorZones.Contains(zone))
                selection = PoorSelection;
            else if (IndustrialZones.Contains(zone))
                selection = IndustrialSelection;
            else
                selection = MiddleSelection;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                string groupName = selection[random.Next(selection.Length)];
                List<string> group;
                if (!_groups.TryGetValue(groupName, out group) || group.Count == 0) continue;
                return group[random.Next(group.Count)];
            }

            foreach (KeyValuePair<string, List<string>> pair in _groups)
            {
                if (pair.Value.Count > 0)
                    return pair.Value[random.Next(pair.Value.Count)];
            }

            return "sultan";
        }

        private static readonly HashSet<string> RichZones = PascaTrafficScript.RichZones;
        private static readonly HashSet<string> RuralZones = PascaTrafficScript.RuralZones;
        private static readonly HashSet<string> PoorZones = PascaTrafficScript.PoorZones;
        private static readonly HashSet<string> IndustrialZones = PascaTrafficScript.IndustrialZones;
    }

    internal sealed class ModLog
    {
        private readonly string _path;

        public ModLog(string path)
        {
            _path = path;
        }

        public void Reset()
        {
            File.WriteAllText(_path, string.Empty);
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Error(string message, Exception ex)
        {
            Write("ERROR", message + ": " + ex);
        }

        private void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                          " [" + level + "] " + message + Environment.NewLine;
            File.AppendAllText(_path, line);
        }
    }
}
