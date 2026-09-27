using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

// Contains the BepInEx plugin and cargo integration.
namespace Simcity
{
    /// <summary>Installs the game hooks used by the cargo options.</summary>
    [BepInPlugin("ki.simcity", "KI Simcity Cargo", "0.8.0")]
    public sealed class SimcityPlugin : BaseUnityPlugin
    {
        /// <summary>Current plugin instance used by static Harmony callbacks.</summary>
        private static SimcityPlugin instance;
        /// <summary>Owns the Harmony patches installed by this plugin.</summary>
        private Harmony harmony;
        /// <summary>Per-aircraft registries that add cargo mounts and assign deployable vehicles.</summary>
        private VehicleCargoRegistry[] registries;
        /// <summary>Cargo taller than this many meters is spawned clear of the aircraft.</summary>
        private ConfigEntry<float> tallCargoHeightThreshold;
        /// <summary>Distance in meters ahead of the aircraft at which tall cargo is spawned.</summary>
        private ConfigEntry<float> tallCargoClearanceDistance;
        /// <summary>Which logistics vehicle family (HLT, MSV, or Both) is offered as cargo.</summary>
        private ConfigEntry<string> vehicleSet;
        /// <summary>Comma-separated words that exclude a vehicle from generated cargo.</summary>
        private ConfigEntry<string> vehicleNameDenylist;
        /// <summary>Burst cargo waiting for its first unit to spawn, keyed by owning unit.</summary>
        private static readonly Dictionary<Unit, PendingBurst> pendingBursts = new Dictionary<Unit, PendingBurst>();
        /// <summary>Distance in meters behind the aircraft at which the first burst unit is spawned.</summary>
        private const float BurstDropBehindDistance = 20f;
        /// <summary>Extra meters of separation added for each later burst unit.</summary>
        private const float BurstDropSpacing = 2f;

        /// <summary>One burst cargo waiting for the game to spawn its first unit.</summary>
        private sealed class PendingBurst
        {
            /// <summary>The burst cargo option being deployed.</summary>
            public CargoOption Option;
            /// <summary>When this pending burst stops being valid.</summary>
            public float Expires;
        }

        /// <summary>Install the encyclopedia and weapon registration hooks.</summary>
        private void Awake()
        {
            instance = this;
            tallCargoHeightThreshold = Config.Bind("Cargo deployment", "TallCargoHeightThreshold", 3.6f,
                "Cargo whose unit is taller than this many meters is spawned clear of the aircraft to avoid clipping into the bay.");
            tallCargoClearanceDistance = Config.Bind("Cargo deployment", "TallCargoClearanceDistance", 20f,
                "Distance in meters ahead of the aircraft at which tall cargo is spawned. Set to 0 to disable the clearance.");
            vehicleSet = Config.Bind("Cargo", "VehicleSet", "HLT",
                new ConfigDescription("Which logistics vehicle family is offered as generated cargo. HLT and MSV hide each other's matching vehicles.",
                    new AcceptableValueList<string>("HLT", "MSV", "Both")));
            vehicleNameDenylist = Config.Bind("Cargo", "VehicleNameDenylist", "hypersonic,ballistic,nuclear",
                "Comma-separated words that exclude a vehicle from generated cargo when its name contains one (case-insensitive).");

            registries = new VehicleCargoRegistry[]
            {
                new CargoRegistry(Logger),
                new ChimeraCargoRegistry(Logger),
                new IbisCargoRegistry(Logger),
            };
            var afterLoad = typeof(Encyclopedia).GetMethod("AfterLoad", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (afterLoad == null)
            {
                Logger.LogError("Encyclopedia.AfterLoad() was not found; cargo options were not installed.");
                return;
            }

            harmony = new Harmony("ki.simcity");
            // Re-register cargo after the game rebuilds its encyclopedia lookups.
            harmony.Patch(afterLoad, postfix: new HarmonyMethod(typeof(SimcityPlugin), nameof(OnEncyclopediaLoaded)));
            // Replace the cloned cargo payload before the weapon joins the aircraft.
            harmony.Patch(AccessTools.Method(typeof(WeaponManager), nameof(WeaponManager.RegisterWeapon)),
                prefix: new HarmonyMethod(typeof(SimcityPlugin), nameof(BeforeRegisterWeapon)));
            PatchCargoRailLaunch();
            PatchCargoSpawn();
            PatchOptionalBlueprinterLoad();
            Logger.LogInfo("Cargo hooks installed.");
        }

        /// <summary>Install the tall-cargo deployment clearance on the cargo rail launch.</summary>
        private void PatchCargoRailLaunch()
        {
            var railLaunch = AccessTools.Method(typeof(MountedCargo), "RailLaunch");
            if (railLaunch == null)
            {
                Logger.LogWarning("MountedCargo.RailLaunch() was not found; tall cargo deployment clearance is unavailable.");
                return;
            }

            harmony.Patch(railLaunch, prefix: new HarmonyMethod(typeof(SimcityPlugin), nameof(BeforeCargoRailLaunch)));
        }

        /// <summary>Install the burst cargo hook on the server unit spawner.</summary>
        private void PatchCargoSpawn()
        {
            var spawnUnit = AccessTools.Method(typeof(Spawner), "SpawnUnit");
            if (spawnUnit == null)
            {
                Logger.LogWarning("Spawner.SpawnUnit() was not found; burst cargo loadouts are unavailable.");
                return;
            }

            harmony.Patch(spawnUnit, postfix: new HarmonyMethod(typeof(SimcityPlugin), nameof(AfterSpawnUnit)));
        }

        /// <summary>Register optional cargo after Blueprinter finishes applying mod assets.</summary>
        private void PatchOptionalBlueprinterLoad()
        {
            // Scan loaded assemblies so the base plugin has no Blueprinter reference.
            var runnerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Blueprinter.PatchRunner", false))
                .FirstOrDefault(type => type != null);
            if (runnerType == null) return;

            var applyAll = AccessTools.Method(runnerType, "ApplyAllOps");
            if (applyAll == null)
            {
                Logger.LogWarning("Blueprinter patch runner was found, but ApplyAllOps() was not; optional cargo may be unavailable.");
                return;
            }

            harmony.Patch(applyAll, postfix: new HarmonyMethod(typeof(SimcityPlugin), nameof(OnBlueprinterDefinitionsLoaded)));
            Logger.LogInfo("Optional Blueprinter cargo hook installed.");
        }

        /// <summary>Remove this plugin's Harmony patches when Unity destroys it.</summary>
        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            instance = null;
        }

        /// <summary>Register cargo after the game rebuilds its definition lookups.</summary>
        private static void OnEncyclopediaLoaded(Encyclopedia __instance)
        {
            if (instance == null) return;
            var family = SelectedFamily();
            var denylist = ParseDenylist(instance.vehicleNameDenylist.Value);
            foreach (var registry in instance.registries)
            {
                try { registry.Register(__instance, family, denylist); }
                catch (Exception error) { instance.Logger.LogError("Could not register " + registry.DisplayName + " cargo: " + error); }
            }
        }

        /// <summary>Read the configured logistics vehicle family.</summary>
        private static VehicleFamily SelectedFamily()
        {
            switch (instance.vehicleSet.Value)
            {
                case "MSV": return VehicleFamily.MSV;
                case "Both": return VehicleFamily.Both;
                default: return VehicleFamily.HLT;
            }
        }

        /// <summary>Split the configured denylist into trimmed words.</summary>
        private static string[] ParseDenylist(string value)
        {
            if (string.IsNullOrEmpty(value)) return new string[0];

            var parts = value.Split(',');
            var words = new List<string>();
            for (var i = 0; i < parts.Length; i++)
            {
                var word = parts[i].Trim();
                if (word.Length > 0) words.Add(word);
            }
            return words.ToArray();
        }

        /// <summary>Register optional vehicles once Blueprinter has applied its patches.</summary>
        private static void OnBlueprinterDefinitionsLoaded(Encyclopedia __0)
        {
            OnEncyclopediaLoaded(__0);
        }

        /// <summary>Assign the selected mount's deployable vehicle before registration.</summary>
        private static void BeforeRegisterWeapon(Weapon weapon, WeaponMount weaponMount)
        {
            if (instance == null) return;
            foreach (var registry in instance.registries) registry.AttachCargo(weapon, weaponMount);
        }

        /// <summary>Spawn tall cargo clear of the aircraft and arm burst loadouts before they deploy.</summary>
        private static void BeforeCargoRailLaunch(MountedCargo __instance)
        {
            if (instance == null || __instance == null || __instance.info == null) return;

            var burst = FindBurstOption(__instance.info);
            if (burst != null && __instance.attachedUnit != null && __instance.attachedUnit.IsServer)
            {
                pendingBursts[__instance.attachedUnit] = new PendingBurst { Option = burst, Expires = Time.time + 60f };
            }

            var threshold = instance.tallCargoHeightThreshold.Value;
            var tall = false;
            foreach (var registry in instance.registries)
            {
                if (registry.IsTallCargo(__instance.info, threshold))
                {
                    tall = true;
                    break;
                }
            }
            if (!tall) return;

            var distance = instance.tallCargoClearanceDistance.Value;
            if (distance <= 0f) return;

            var rail = Traverse.Create(__instance).Field("railVector").GetValue<Vector3>();
            if (rail.sqrMagnitude < 0.0001f) return;

            var mountedPosition = Traverse.Create(__instance).Field("mountedPosition").GetValue<Vector3>();
            __instance.transform.localPosition = mountedPosition + rail.normalized * distance;
        }

        /// <summary>Start the remaining burst spawns once the game spawns the first unit.</summary>
        private static void AfterSpawnUnit(Unit __result, UnitDefinition unit, Vector3 velocity, Unit owner, Player player)
        {
            if (instance == null || owner == null || unit == null) return;

            PendingBurst pending;
            if (!pendingBursts.TryGetValue(owner, out pending)) return;
            if (pending.Expires < Time.time)
            {
                pendingBursts.Remove(owner);
                return;
            }
            if (pending.Option.Vehicle == null || unit.jsonKey != pending.Option.Vehicle.jsonKey) return;

            pendingBursts.Remove(owner);
            instance.StartCoroutine(instance.SpawnBurst(pending.Option, velocity, owner, player));
        }

        /// <summary>Spawn the rest of a burst cargo behind the aircraft after a short delay between each unit.</summary>
        private IEnumerator SpawnBurst(CargoOption option, Vector3 velocity, Unit owner, Player player)
        {
            var vehicles = option.BurstVehicles;
            if (vehicles == null) yield break;

            for (var i = 0; i < vehicles.Length; i++)
            {
                if (option.BurstInterval > 0f) yield return new WaitForSeconds(option.BurstInterval);
                if (owner == null || NetworkSceneSingleton<Spawner>.i == null) yield break;

                // Drop relative to the aircraft's current heading so the stream always falls behind it.
                var dropRotation = owner.transform.rotation;
                var dropPosition = owner.transform.position - owner.transform.forward * (BurstDropBehindDistance + i * BurstDropSpacing);
                try
                {
                    NetworkSceneSingleton<Spawner>.i.SpawnUnit(vehicles[i], dropPosition, dropRotation, velocity, owner, player);
                }
                catch (Exception error)
                {
                    Logger.LogError("Could not spawn burst cargo: " + error);
                    yield break;
                }
            }
        }

        /// <summary>Find the burst cargo option behind a weapon information, if any.</summary>
        private static CargoOption FindBurstOption(WeaponInfo info)
        {
            foreach (var registry in instance.registries)
            {
                var option = registry.FindByInfo(info);
                if (option != null && option.BurstVehicles != null && option.BurstVehicles.Length > 0) return option;
            }
            return null;
        }
    }
}
