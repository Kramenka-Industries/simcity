using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

// Contains shared registration and payload selection for aircraft cargo mounts.
namespace Simcity
{
    /// <summary>Registers encyclopedia vehicles across an aircraft's cargo bays by mass.</summary>
    internal abstract class VehicleCargoRegistry
    {
        /// <summary>Plugin log used for registration errors and diagnostics.</summary>
        protected readonly ManualLogSource logger;
        /// <summary>Prefix applied to every encyclopedia key created by this registry.</summary>
        private readonly string mountKeyPrefix;
        /// <summary>Cached vehicle choices and their resolved runtime state, keyed by vehicle definition key.</summary>
        private readonly Dictionary<string, CargoOption> options = new Dictionary<string, CargoOption>();
        /// <summary>Options indexed by their cloned mount for fast cargo attachment.</summary>
        private readonly Dictionary<WeaponMount, CargoOption> mounts = new Dictionary<WeaponMount, CargoOption>();

        /// <summary>Create a registry that writes to the plugin log and tags its mounts.</summary>
        protected VehicleCargoRegistry(ManualLogSource logger, string mountKeyPrefix)
        {
            this.logger = logger;
            this.mountKeyPrefix = mountKeyPrefix;
        }

        /// <summary>Game definition key of the aircraft this registry serves.</summary>
        protected abstract string AircraftKey { get; }
        /// <summary>Short name used in registration logs.</summary>
        protected abstract string DisplayName { get; }

        /// <summary>Discover the cargo bay groups for this aircraft, or null when the setup is invalid.</summary>
        protected abstract CargoBay[] BuildBays(Encyclopedia encyclopedia, Aircraft aircraft);

        /// <summary>Add every eligible encyclopedia vehicle to the matching cargo bay.</summary>
        public void Register(Encyclopedia encyclopedia)
        {
            var aircraftDefinition = encyclopedia.aircraft.FirstOrDefault(definition => definition != null && definition.jsonKey == AircraftKey);
            if (aircraftDefinition == null) return;

            var aircraft = aircraftDefinition.unitPrefab.GetComponent<Aircraft>();
            if (aircraft == null || aircraft.weaponManager == null)
            {
                logger.LogError(DisplayName + " prefab has no Aircraft/WeaponManager component.");
                return;
            }

            var bays = BuildBays(encyclopedia, aircraft);
            if (bays == null || bays.Length == 0) return;

            // Base game and other mods may already offer some of these vehicles; do not add duplicates.
            var existing = new Dictionary<CargoBay, HashSet<string>>();
            foreach (var bay in bays) existing[bay] = ExistingCargoKeys(bay.Sets);

            mounts.Clear();
            var registered = 0;
            var duplicates = 0;
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

                var bay = bays.FirstOrDefault(candidate => candidate.Matches(mass));
                if (bay == null)
                {
                    skipped++;
                    continue;
                }

                if (existing[bay].Contains(vehicle.jsonKey))
                {
                    duplicates++;
                    continue;
                }

                try
                {
                    var option = GetOrCreateOption(vehicle, bay.Description);
                    if (RegisterOption(encyclopedia, bay, option)) registered++;
                    else skipped++;
                }
                catch (Exception error)
                {
                    logger.LogError("Could not register " + vehicle.jsonKey + " " + DisplayName + " cargo: " + error);
                }
            }

            logger.LogInfo(DisplayName + " cargo: " + registered + " vehicle(s) added across " + bays.Length +
                " bay(s), " + duplicates + " already offered by another mount, " + skipped + " skipped.");
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
                    ", cargo empty mass=" + option.Mount.emptyMass + ".");
            }
        }

        /// <summary>Check whether a registered weapon carries cargo tall enough to need deployment clearance.</summary>
        public bool IsTallCargo(WeaponInfo info, float threshold)
        {
            if (info == null) return false;

            foreach (var option in options.Values)
            {
                if (option.Info == info && option.Vehicle != null && option.Vehicle.height > threshold) return true;
            }
            return false;
        }

        /// <summary>Return the cached cargo choice for a vehicle, creating it on first use.</summary>
        private CargoOption GetOrCreateOption(VehicleDefinition vehicle, string bayDescription)
        {
            CargoOption option;
            if (options.TryGetValue(vehicle.jsonKey, out option)) return option;

            var label = string.IsNullOrEmpty(vehicle.unitName) ? vehicle.jsonKey : vehicle.unitName;
            var shortName = string.IsNullOrEmpty(vehicle.code) ? label : vehicle.code;
            option = new CargoOption(vehicle.jsonKey, mountKeyPrefix + Sanitize(vehicle.jsonKey),
                label, label, shortName, "Deploys one " + label + " " + bayDescription + ".");
            options.Add(vehicle.jsonKey, option);
            return option;
        }

        /// <summary>Create or restore one cargo mount and its encyclopedia entries.</summary>
        private bool RegisterOption(Encyclopedia encyclopedia, CargoBay bay, CargoOption option)
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
            if (bay.ExceedsMax(option.Vehicle.mass) || bay.ExceedsMax(prefabMass) ||
                option.Vehicle.mass <= 0f || prefabMass <= 0f ||
                float.IsNaN(option.Vehicle.mass) || float.IsNaN(prefabMass))
            {
                logger.LogWarning("Skipping " + option.Name + ": definition mass=" + option.Vehicle.mass +
                    " kg, prefab mass=" + prefabMass + " kg; " + bay.Name + " limit=" + bay.MaxMass + " kg.");
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
                mount = CreateMount(bay.Template, option);
                encyclopedia.weaponMounts.Add(mount);
                logger.LogInfo("Added " + option.Name + " to " + DisplayName + " " + bay.Name +
                    " (definition mass=" + option.Vehicle.mass + " kg, prefab mass=" + prefabMass + " kg).");
            }
            option.Mount = mount;

            mount.info = option.Info;
            mount.mountName = option.Label;
            mount.emptyCost = bay.Template.emptyCost;
            mount.emptyMass = bay.Template.emptyMass;
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

            foreach (var target in bay.Sets)
            {
                if (target != null && target.weaponOptions != null && !target.weaponOptions.Contains(mount)) target.weaponOptions.Add(mount);
            }

            mounts[mount] = option;
            return true;
        }

        /// <summary>Collect the vehicle keys that existing (non-registry) mounts in the given sets already deploy.</summary>
        protected HashSet<string> ExistingCargoKeys(IEnumerable<HardpointSet> sets)
        {
            var existing = new HashSet<string>();
            if (sets == null) return existing;

            foreach (var set in sets)
            {
                if (set == null || set.weaponOptions == null) continue;
                foreach (var mount in set.weaponOptions)
                {
                    if (mount == null || IsOurMount(mount) || !mount.Cargo || mount.prefab == null) continue;

                    foreach (var cargo in mount.prefab.GetComponentsInChildren<MountedCargo>(true))
                    {
                        if (cargo == null || cargo.cargo == null || string.IsNullOrEmpty(cargo.cargo.jsonKey)) continue;
                        existing.Add(cargo.cargo.jsonKey);
                    }
                }
            }
            return existing;
        }

        /// <summary>Check whether a mount was created by this registry.</summary>
        protected bool IsOurMount(WeaponMount mount)
        {
            return mount.jsonKey != null && mount.jsonKey.StartsWith(mountKeyPrefix, StringComparison.Ordinal);
        }

        /// <summary>Find the first single-cargo mount not created by this registry among the given sets.</summary>
        protected WeaponMount FirstSingleCargo(IEnumerable<HardpointSet> sets)
        {
            if (sets == null) return null;

            foreach (var set in sets)
            {
                if (set == null || set.weaponOptions == null) continue;
                var template = set.weaponOptions.FirstOrDefault(option => option != null && !IsOurMount(option) && IsSingleCargo(option));
                if (template != null) return template;
            }
            return null;
        }

        /// <summary>Find a hardpoint set by its configured name.</summary>
        protected static HardpointSet FindSetByName(Aircraft aircraft, string name)
        {
            return aircraft.weaponManager.hardpointSets.FirstOrDefault(set =>
                set != null && set.weaponOptions != null && set.name == name);
        }

        /// <summary>Check that a mount carries one deployable cargo component.</summary>
        protected static bool IsSingleCargo(WeaponMount mount)
        {
            return mount.Cargo && mount.prefab != null &&
                mount.prefab.GetComponentsInChildren<MountedCargo>(true).Length == 1;
        }

        /// <summary>Build a stable encyclopedia key from a vehicle definition key.</summary>
        protected static string Sanitize(string key)
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
