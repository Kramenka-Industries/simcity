using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;

// Contains the BepInEx plugin and VL-49 cargo integration.
namespace Simcity
{
    /// <summary>Installs the game hooks used by the VL-49 cargo options.</summary>
    [BepInPlugin("ki.simcity", "KI Simcity VL-49 Cargo", "0.6.0")]
    public sealed class SimcityPlugin : BaseUnityPlugin
    {
        /// <summary>Current plugin instance used by static Harmony callbacks.</summary>
        private static SimcityPlugin instance;
        /// <summary>Owns the Harmony patches installed by this plugin.</summary>
        private Harmony harmony;
        /// <summary>Registers cargo mounts and assigns their deployable vehicles.</summary>
        private CargoRegistry cargoRegistry;

        /// <summary>Install the encyclopedia and weapon registration hooks.</summary>
        private void Awake()
        {
            instance = this;
            cargoRegistry = new CargoRegistry(Logger);
            var afterLoad = typeof(Encyclopedia).GetMethod("AfterLoad", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (afterLoad == null)
            {
                Logger.LogError("Encyclopedia.AfterLoad() was not found; cargo options were not installed.");
                return;
            }

            harmony = new Harmony("ki.simcity");
            // Re-register cargo after the game rebuilds its encyclopedia lookups.
            harmony.Patch(afterLoad, postfix: new HarmonyMethod(typeof(SimcityPlugin), nameof(OnEncyclopediaLoaded)));
            // Replace the cloned HLT payload before the weapon joins the aircraft.
            harmony.Patch(AccessTools.Method(typeof(WeaponManager), nameof(WeaponManager.RegisterWeapon)),
                prefix: new HarmonyMethod(typeof(SimcityPlugin), nameof(BeforeRegisterWeapon)));
            PatchOptionalBlueprinterLoad();
            Logger.LogInfo("VL-49 cargo hooks installed.");
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
        }

        /// <summary>Register optional vehicles once Blueprinter has applied its patches.</summary>
        private static void OnBlueprinterDefinitionsLoaded(Encyclopedia __0)
        {
            OnEncyclopediaLoaded(__0);
        }

        /// <summary>Assign the selected mount's deployable vehicle before registration.</summary>
        private static void BeforeRegisterWeapon(Weapon weapon, WeaponMount weaponMount)
        {
            if (instance != null) instance.cargoRegistry.AttachCargo(weapon, weaponMount);
        }
    }
}
