using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
        /// <summary>Registers VL-49 cargo mounts and assigns their deployable vehicles.</summary>
        private CargoRegistry cargoRegistry;
        /// <summary>Registers MC-260 cargo mounts and assigns their deployable vehicles.</summary>
        private ChimeraCargoRegistry chimeraCargoRegistry;
        /// <summary>Cargo taller than this many meters is spawned clear of the aircraft.</summary>
        private ConfigEntry<float> tallCargoHeightThreshold;
        /// <summary>Distance in meters ahead of the aircraft at which tall cargo is spawned.</summary>
        private ConfigEntry<float> tallCargoClearanceDistance;

        /// <summary>Install the encyclopedia and weapon registration hooks.</summary>
        private void Awake()
        {
            instance = this;
            tallCargoHeightThreshold = Config.Bind("Cargo deployment", "TallCargoHeightThreshold", 3.6f,
                "Cargo whose unit is taller than this many meters is spawned clear of the aircraft to avoid clipping into the bay.");
            tallCargoClearanceDistance = Config.Bind("Cargo deployment", "TallCargoClearanceDistance", 20f,
                "Distance in meters ahead of the aircraft at which tall cargo is spawned. Set to 0 to disable the clearance.");

            cargoRegistry = new CargoRegistry(Logger);
            chimeraCargoRegistry = new ChimeraCargoRegistry(Logger);
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
            try { instance.cargoRegistry.Register(__instance); }
            catch (Exception error) { instance.Logger.LogError("Could not register VL-49 cargo: " + error); }
            try { instance.chimeraCargoRegistry.Register(__instance); }
            catch (Exception error) { instance.Logger.LogError("Could not register MC-260 cargo: " + error); }
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
            instance.cargoRegistry.AttachCargo(weapon, weaponMount);
            instance.chimeraCargoRegistry.AttachCargo(weapon, weaponMount);
        }

        /// <summary>Spawn tall cargo clear of the aircraft so it does not clip and get stuck.</summary>
        private static void BeforeCargoRailLaunch(MountedCargo __instance)
        {
            if (instance == null || __instance == null || __instance.info == null) return;

            var threshold = instance.tallCargoHeightThreshold.Value;
            if (!instance.cargoRegistry.IsTallCargo(__instance.info, threshold) &&
                !instance.chimeraCargoRegistry.IsTallCargo(__instance.info, threshold)) return;

            var distance = instance.tallCargoClearanceDistance.Value;
            if (distance <= 0f) return;

            var rail = Traverse.Create(__instance).Field("railVector").GetValue<Vector3>();
            if (rail.sqrMagnitude < 0.0001f) return;

            var mountedPosition = Traverse.Create(__instance).Field("mountedPosition").GetValue<Vector3>();
            __instance.transform.localPosition = mountedPosition + rail.normalized * distance;
        }
    }
}
