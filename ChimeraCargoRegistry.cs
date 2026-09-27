using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

// Contains registration and payload selection for MC-260 cargo mounts.
namespace Simcity
{
    /// <summary>Registers deployable vehicles as MC-260 cargo choices.</summary>
    internal sealed class ChimeraCargoRegistry
    {
        /// <summary>Game definition key of the MC-260 Chimera.</summary>
        private const string AircraftKey = "Aryx_CargoPlane1";
        /// <summary>Chimera cargo mount used to locate and clone the front and rear bays.</summary>
        private const string CargoTemplateKey = "Aryx_ADLT_01x1";
        /// <summary>Name of the Chimera hardpoint set that carries heavy vehicles.</summary>
        private const string MissionBaySetName = "Mission Bay";
        /// <summary>Maximum front or rear MC-260 cargo payload in kilograms.</summary>
        private const float MaximumCargoMass = 45000f;
        /// <summary>Maximum mission bay MC-260 cargo payload in kilograms.</summary>
        private const float MaximumMissionMass = 90000f;

        /// <summary>Plugin log used for registration errors and diagnostics.</summary>
        private readonly ManualLogSource logger;
        /// <summary>Cached vehicle choices and their resolved runtime state, keyed by vehicle definition key.</summary>
        private readonly Dictionary<string, CargoOption> options = new Dictionary<string, CargoOption>();
        /// <summary>Options indexed by their cloned mount for fast cargo attachment.</summary>
        private readonly Dictionary<WeaponMount, CargoOption> mounts = new Dictionary<WeaponMount, CargoOption>();

        /// <summary>Create a Chimera cargo registry that writes to the plugin log.</summary>
        public ChimeraCargoRegistry(ManualLogSource logger)
        {
            this.logger = logger;
        }

        /// <summary>Add every eligible encyclopedia vehicle to the matching MC-260 cargo bay.</summary>
        public void Register(Encyclopedia encyclopedia)
        {
            var aircraftDefinition = encyclopedia.aircraft.FirstOrDefault(definition => definition != null && definition.jsonKey == AircraftKey);
            if (aircraftDefinition == null) return;

            var aircraft = aircraftDefinition.unitPrefab.GetComponent<Aircraft>();
            if (aircraft == null || aircraft.weaponManager == null)
            {
                logger.LogError("MC-260 prefab has no Aircraft/WeaponManager component.");
                return;
            }

            var cargoTemplate = encyclopedia.weaponMounts.FirstOrDefault(mount =>
                mount != null && mount.jsonKey == CargoTemplateKey && IsSingleCargo(mount));
            if (cargoTemplate == null)
            {
                logger.LogError("MC-260 cargo mount " + CargoTemplateKey + " was not found.");
                return;
            }

            var cargoSets = aircraft.weaponManager.hardpointSets
                .Where(set => set != null && set.weaponOptions != null && set.weaponOptions.Contains(cargoTemplate))
                .ToArray();
            if (cargoSets.Length != 2)
            {
                logger.LogError("Expected MC-260 front and rear cargo sets containing " + CargoTemplateKey +
                    ", found " + cargoSets.Length + ". Sets: " + string.Join(", ", cargoSets.Select(set => set.name).ToArray()));
                return;
            }

            var missionSet = FindMissionSet(aircraft.weaponManager.hardpointSets, cargoSets);
            var missionTemplate = missionSet == null ? null : missionSet.weaponOptions
                .Where(option => option != null && IsSingleCargo(option))
                .OrderByDescending(option => option.mass)
                .FirstOrDefault();
            if (missionSet == null || missionTemplate == null)
            {
                logger.LogWarning("MC-260 mission bay was not found; vehicles at or above " + MaximumCargoMass + " kg were skipped.");
            }

            mounts.Clear();
            var cargoCount = 0;
            var missionCount = 0;
            var skipped = 0;
            foreach (var vehicle in encyclopedia.vehicles)
            {
                if (vehicle == null || vehicle.unitPrefab == null) continue;

                var unit = vehicle.unitPrefab.GetComponent<Unit>();
                if (unit == null) continue;

                var prefabMass = unit.GetPrefabMass();
                var mass = Mathf.Max(vehicle.mass, prefabMass);
                if (mass <= 0f || float.IsNaN(mass) || float.IsInfinity(mass))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    if (mass < MaximumCargoMass)
                    {
                        var option = GetOrCreateOption(vehicle, "from an MC-260 front or rear cargo bay");
                        if (RegisterOption(encyclopedia, cargoTemplate, cargoSets, option, MaximumCargoMass)) cargoCount++;
                        else skipped++;
                    }
                    else if (mass <= MaximumMissionMass && missionTemplate != null)
                    {
                        var option = GetOrCreateOption(vehicle, "from the MC-260 mission bay");
                        if (RegisterOption(encyclopedia, missionTemplate, new[] { missionSet }, option, MaximumMissionMass)) missionCount++;
                        else skipped++;
                    }
                    else skipped++;
                }
                catch (Exception error)
                {
                    logger.LogError("Could not register " + vehicle.jsonKey + " MC-260 cargo: " + error);
                }
            }

            logger.LogInfo("MC-260 cargo: " + cargoCount + " vehicle(s) in the front and rear bays, " +
                missionCount + " in the mission bay, " + skipped + " skipped.");
        }

        /// <summary>Locate the mission bay hardpoint set by name, falling back to the remaining single-cargo set.</summary>
        private static HardpointSet FindMissionSet(HardpointSet[] hardpointSets, HardpointSet[] cargoSets)
        {
            var missionSet = hardpointSets.FirstOrDefault(set => set != null && set.weaponOptions != null && set.name == MissionBaySetName);
            if (missionSet != null) return missionSet;

            return hardpointSets.FirstOrDefault(set => set != null && set.weaponOptions != null &&
                !cargoSets.Contains(set) && set.weaponOptions.Any(option => option != null && IsSingleCargo(option)));
        }

        /// <summary>Return the cached cargo choice for a vehicle, creating it on first use.</summary>
        private CargoOption GetOrCreateOption(VehicleDefinition vehicle, string bayDescription)
        {
            CargoOption option;
            if (options.TryGetValue(vehicle.jsonKey, out option)) return option;

            var label = string.IsNullOrEmpty(vehicle.unitName) ? vehicle.jsonKey : vehicle.unitName;
            var shortName = string.IsNullOrEmpty(vehicle.code) ? label : vehicle.code;
            option = new CargoOption(vehicle.jsonKey, "simcity_chimera_cargo_" + Sanitize(vehicle.jsonKey),
                label, label, shortName, "Deploys one " + label + " " + bayDescription + ".");
            options.Add(vehicle.jsonKey, option);
            return option;
        }

        /// <summary>Create or restore one cargo mount and its encyclopedia entries.</summary>
        private bool RegisterOption(Encyclopedia encyclopedia, WeaponMount template, HardpointSet[] targets, CargoOption option, float maxMass)
        {
            option.Vehicle = encyclopedia.vehicles.FirstOrDefault(definition => definition != null && definition.jsonKey == option.VehicleKey);
            if (option.Vehicle == null)
            {
                logger.LogError("Expected game vehicle " + option.VehicleKey + " was not found.");
                return false;
            }

            var unit = option.Vehicle.unitPrefab == null ? null : option.Vehicle.unitPrefab.GetComponent<Unit>();
            if (unit == null)
            {
                logger.LogWarning("Skipping " + option.Name + ": vehicle prefab has no Unit component.");
                return false;
            }

            var prefabMass = unit.GetPrefabMass();
            if (option.Vehicle.mass > maxMass || prefabMass > maxMass ||
                option.Vehicle.mass <= 0f || prefabMass <= 0f ||
                float.IsNaN(option.Vehicle.mass) || float.IsNaN(prefabMass))
            {
                logger.LogWarning("Skipping " + option.Name + ": definition mass=" + option.Vehicle.mass +
                    " kg, prefab mass=" + prefabMass + " kg; MC-260 cargo limit=" + maxMass + " kg.");
                return false;
            }

            var mount = encyclopedia.weaponMounts.FirstOrDefault(candidate => candidate != null && candidate.jsonKey == option.MountKey);
            if (mount != null && mount != option.Mount)
            {
                logger.LogError("Another cargo mount already uses key " + option.MountKey + ".");
                return false;
            }

            if (mount == null)
            {
                mount = CreateMount(template, option);
                encyclopedia.weaponMounts.Add(mount);
                logger.LogInfo("Added " + option.Name + " to MC-260 " + string.Join(" and ", targets.Select(set => set.name).ToArray()) +
                    " (definition mass=" + option.Vehicle.mass + " kg, prefab mass=" + prefabMass + " kg).");
            }
            option.Mount = mount;

            mount.info = option.Info;
            mount.mountName = option.Label;
            mount.emptyCost = template.emptyCost;
            mount.emptyMass = template.emptyMass;
            mount.mass = option.Vehicle.mass;
            option.Info.SetCostPerRound(option.Vehicle.value);
            option.Info.SetMassPerRound(option.Vehicle.mass);

            WeaponMount registered;
            if (Encyclopedia.WeaponLookup.TryGetValue(option.MountKey, out registered))
            {
                if (registered != mount)
                {
                    logger.LogError("Cargo key is already in use: " + option.MountKey);
                    return false;
                }
            }
            else Encyclopedia.WeaponLookup.Add(option.MountKey, mount);

            if (!encyclopedia.IndexLookup.Contains(mount))
            {
                ((INetworkDefinition)mount).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(mount);
            }

            foreach (var target in targets)
            {
                if (!target.weaponOptions.Contains(mount)) target.weaponOptions.Add(mount);
            }

            mounts[mount] = option;
            return true;
        }

        /// <summary>Replace the cloned cargo payload with the selected vehicle.</summary>
        public void AttachCargo(Weapon weapon, WeaponMount mount)
        {
            var cargo = weapon as MountedCargo;
            if (cargo == null || mount == null) return;

            CargoOption option;
            if (!mounts.TryGetValue(mount, out option)) return;

            cargo.cargo = option.Vehicle;
            cargo.info = option.Info;
            if (!option.LoggedMass)
            {
                option.LoggedMass = true;
                logger.LogInfo(option.Name + " attached: vehicle definition mass=" + option.Vehicle.mass +
                    ", vehicle prefab mass=" + option.Vehicle.unitPrefab.GetComponent<Unit>().GetPrefabMass() +
                    ", MC-260 cargo empty mass=" + option.Mount.emptyMass + ".");
            }
        }

        /// <summary>Check that a mount carries one deployable cargo component.</summary>
        private static bool IsSingleCargo(WeaponMount mount)
        {
            return mount.Cargo && mount.prefab != null &&
                mount.prefab.GetComponentsInChildren<MountedCargo>(true).Length == 1;
        }

        /// <summary>Build a stable encyclopedia key from a vehicle definition key.</summary>
        private static string Sanitize(string key)
        {
            if (string.IsNullOrEmpty(key)) return "vehicle";

            var characters = key.ToCharArray();
            for (var i = 0; i < characters.Length; i++)
            {
                if (!char.IsLetterOrDigit(characters[i])) characters[i] = '_';
            }
            return new string(characters);
        }

        /// <summary>Clone the template mount and give it vehicle-specific loadout metadata.</summary>
        private static WeaponMount CreateMount(WeaponMount template, CargoOption option)
        {
            var mount = UnityEngine.Object.Instantiate(template);
            mount.name = option.MountKey;
            mount.jsonKey = option.MountKey;
            mount.Initialize();

            var info = UnityEngine.Object.Instantiate(template.info);
            info.name = option.MountKey + "_info";
            info.weaponName = option.Name;
            info.shortName = option.ShortName;
            info.description = option.Description;
            info.cargo = true;
            info.SetMassPerRound(option.Vehicle.mass);
            info.SetCostPerRound(option.Vehicle.value);
            option.Info = info;
            mount.info = info;
            mount.mountName = option.Label;
            mount.emptyCost = template.emptyCost;
            mount.mass = option.Vehicle.mass;
            return mount;
        }
    }
}
