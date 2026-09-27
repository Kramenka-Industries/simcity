using System;
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
        /// <summary>Maximum front or rear MC-260 cargo payload in kilograms.</summary>
        private const float MaximumCargoMass = 45000f;

        /// <summary>Plugin log used for registration errors and diagnostics.</summary>
        private readonly ManualLogSource logger;
        /// <summary>Configured vehicle choices and their resolved runtime state.</summary>
        private readonly CargoOption[] options =
        {
            new CargoOption("SPAAG1", "simcity_chimera_aerosentry_cargo", "AeroSentry SPAAG",
                "AeroSentry SPAAG", "AeroSentry",
                "Deploys one AeroSentry SPAAG from an MC-260 front or rear cargo bay. Twin 30 mm guns engage nearby aircraft."),
        };

        /// <summary>Create a Chimera cargo registry that writes to the plugin log.</summary>
        public ChimeraCargoRegistry(ManualLogSource logger)
        {
            this.logger = logger;
        }

        /// <summary>Add known cargo options to the MC-260 front and rear cargo bays.</summary>
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

            foreach (var option in options)
            {
                try { RegisterOption(encyclopedia, cargoTemplate, cargoSets, option); }
                catch (Exception error) { logger.LogError("Could not register " + option.VehicleKey + " MC-260 cargo: " + error); }
            }
        }

        /// <summary>Create or restore one cargo mount and its encyclopedia entries.</summary>
        private void RegisterOption(Encyclopedia encyclopedia, WeaponMount template, HardpointSet[] cargoSets, CargoOption option)
        {
            option.Vehicle = encyclopedia.vehicles.FirstOrDefault(definition => definition != null && definition.jsonKey == option.VehicleKey);
            if (option.Vehicle == null)
            {
                logger.LogError("Expected game vehicle " + option.VehicleKey + " was not found.");
                return;
            }

            var unit = option.Vehicle.unitPrefab == null ? null : option.Vehicle.unitPrefab.GetComponent<Unit>();
            if (unit == null)
            {
                logger.LogWarning("Skipping " + option.Name + ": vehicle prefab has no Unit component.");
                return;
            }

            var prefabMass = unit.GetPrefabMass();
            if (option.Vehicle.mass > MaximumCargoMass || prefabMass > MaximumCargoMass ||
                option.Vehicle.mass <= 0f || prefabMass <= 0f ||
                float.IsNaN(option.Vehicle.mass) || float.IsNaN(prefabMass))
            {
                logger.LogWarning("Skipping " + option.Name + ": definition mass=" + option.Vehicle.mass +
                    " kg, prefab mass=" + prefabMass + " kg; MC-260 front/rear cargo limit=" + MaximumCargoMass + " kg.");
                return;
            }

            var mount = encyclopedia.weaponMounts.FirstOrDefault(candidate => candidate != null && candidate.jsonKey == option.MountKey);
            if (mount != null && mount != option.Mount)
            {
                logger.LogError("Another cargo mount already uses key " + option.MountKey + ".");
                return;
            }

            if (mount == null)
            {
                mount = CreateMount(template, option);
                encyclopedia.weaponMounts.Add(mount);
                option.Mount = mount;
                logger.LogInfo("Added " + option.Name + " to MC-260 front and rear cargo bays (definition mass=" +
                    option.Vehicle.mass + " kg, prefab mass=" + prefabMass + " kg).");
            }

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
                    return;
                }
            }
            else Encyclopedia.WeaponLookup.Add(option.MountKey, mount);

            if (!encyclopedia.IndexLookup.Contains(mount))
            {
                ((INetworkDefinition)mount).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(mount);
            }

            foreach (var cargoSet in cargoSets)
            {
                if (!cargoSet.weaponOptions.Contains(mount)) cargoSet.weaponOptions.Add(mount);
            }
        }

        /// <summary>Replace the cloned cargo payload with the selected vehicle.</summary>
        public void AttachCargo(Weapon weapon, WeaponMount mount)
        {
            var cargo = weapon as MountedCargo;
            if (cargo == null) return;

            foreach (var option in options)
            {
                if (option.Mount == null || mount != option.Mount) continue;
                cargo.cargo = option.Vehicle;
                cargo.info = option.Info;
                if (!option.LoggedMass)
                {
                    option.LoggedMass = true;
                    logger.LogInfo(option.Name + " attached: vehicle definition mass=" + option.Vehicle.mass +
                        ", vehicle prefab mass=" + option.Vehicle.unitPrefab.GetComponent<Unit>().GetPrefabMass() +
                        ", MC-260 cargo empty mass=" + option.Mount.emptyMass + ".");
                }
                return;
            }
        }

        /// <summary>Check that a mount carries one deployable cargo component.</summary>
        private static bool IsSingleCargo(WeaponMount mount)
        {
            return mount.Cargo && mount.prefab != null &&
                mount.prefab.GetComponentsInChildren<MountedCargo>(true).Length == 1;
        }

        /// <summary>Clone the MC-260 cargo mount and give it vehicle-specific loadout metadata.</summary>
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
