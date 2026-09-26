using System;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

// Contains registration and payload selection for VL-49 cargo mounts.
namespace Simcity
{
    /// <summary>Registers deployable vehicles as VL-49 cargo choices.</summary>
    internal sealed class CargoRegistry
    {
        /// <summary>Game definition key of the VL-49 Tarantula.</summary>
        private const string AircraftKey = "QuadVTOL1";
        /// <summary>Stock cargo mount used to locate the full bay and clone its prefab.</summary>
        private const string RadarCargoKey = "HLT-Rx1";

        /// <summary>Plugin log used for registration errors and diagnostics.</summary>
        private readonly ManualLogSource logger;
        /// <summary>Configured vehicle choices and their resolved runtime state.</summary>
        private readonly CargoOption[] options =
        {
            new CargoOption("Truck2-RSAM", "simcity_msv_rsam_cargo", "MSV R9 Stratolance Launcher",
                "MSV R9 Stratolance Launcher", "MSV R9",
                "Deploys one mobile R9 launcher from the VL-49 cargo bay. Requires friendly radar support."),
            new CargoOption("Truck2-FC", "simcity_msv_fire_control_cargo", "MSV Fire Control",
                "MSV Fire Control Truck", "MSV FC",
                "Deploys one MSV launch control truck from the VL-49 cargo bay. Distributes radar targets to nearby SAM launchers."),
            new CargoOption("Truck2-M", "simcity_msv_munitions_cargo", "MSV Munitions",
                "MSV Munitions Truck", "MSV M",
                "Deploys one MSV munitions truck from the VL-49 cargo bay. Rearms friendly units within 300 m."),
            new CargoOption("Truck2-CRAM", "simcity_msv_cram_cargo", "MSV CRAM",
                "MSV CRAM", "MSV CRAM",
                "Deploys one MSV CRAM from the VL-49 cargo bay. Provides point defense against nearby aerial threats."),
            new CargoOption("Truck2-LADS", "simcity_msv_lads_cargo", "MSV LADS",
                "MSV LADS", "MSV LADS",
                "Deploys one MSV LADS from the VL-49 cargo bay. Its laser engages small aerial munitions."),
            new CargoOption("SPAAG1", "simcity_aerosentry_cargo", "AeroSentry SPAAG",
                "AeroSentry SPAAG", "AeroSentry",
                "Deploys one AeroSentry SPAAG from the VL-49 cargo bay. Twin 30 mm guns engage nearby aircraft."),
            new CargoOption("SPAAG2", "simcity_anvil_cargo", "FGA-57 Anvil",
                "FGA-57 Anvil", "Anvil",
                "Deploys one FGA-57 Anvil from the VL-49 cargo bay. Its 57 mm gun engages air and ground targets."),
            new CargoOption("MC260_AAGunContainer_35", "simcity_mc260_sky_sentry_cargo", "Sky Sentry AAA",
                "Sky Sentry AAA", "Sky Sentry",
                "Deploys one Sky Sentry AAA container from the VL-49 cargo bay. Requires the MC-260 Chimera mod.",
                true, "MunitionsContainerx1"),
        };

        /// <summary>Create a cargo registry that writes to the plugin log.</summary>
        public CargoRegistry(ManualLogSource logger)
        {
            this.logger = logger;
        }

        /// <summary>Add all known cargo options to the VL-49 full cargo bay.</summary>
        public void Register(Encyclopedia encyclopedia)
        {
            var aircraftDefinition = encyclopedia.aircraft.FirstOrDefault(definition => definition != null && definition.jsonKey == AircraftKey);
            if (aircraftDefinition == null)
            {
                logger.LogError("Expected game aircraft " + AircraftKey + " was not found.");
                return;
            }

            var aircraft = aircraftDefinition.unitPrefab.GetComponent<Aircraft>();
            if (aircraft == null || aircraft.weaponManager == null)
            {
                logger.LogError("VL-49 prefab has no Aircraft/WeaponManager component.");
                return;
            }

            // HLT-Rx1 identifies the full bay and supplies a compatible cargo prefab.
            var radarMount = encyclopedia.weaponMounts.FirstOrDefault(mount => mount != null && mount.jsonKey == RadarCargoKey && IsSingleCargo(mount));
            if (radarMount == null)
            {
                logger.LogError("HLT-Rx1 cargo mount was not found; cannot identify the full cargo bay safely.");
                return;
            }

            var cargoSets = aircraft.weaponManager.hardpointSets
                .Where(set => set != null && set.weaponOptions != null && set.weaponOptions.Contains(radarMount))
                .ToList();
            if (cargoSets.Count != 1)
            {
                logger.LogError("Expected one VL-49 full cargo set containing HLT-Rx1, found " + cargoSets.Count + ". Sets: " +
                    string.Join(", ", cargoSets.Select(set => set.name).ToArray()));
                return;
            }

            foreach (var option in options)
            {
                try { RegisterOption(encyclopedia, radarMount, cargoSets[0], option); }
                catch (Exception error) { logger.LogError("Could not register " + option.VehicleKey + " cargo: " + error); }
            }
        }

        /// <summary>Create or restore one cargo mount and its encyclopedia entries.</summary>
        private void RegisterOption(Encyclopedia encyclopedia, WeaponMount template, HardpointSet cargoSet, CargoOption option)
        {
            option.Vehicle = encyclopedia.vehicles.FirstOrDefault(definition => definition != null && definition.jsonKey == option.VehicleKey);
            if (option.Vehicle == null)
            {
                if (!option.Optional) logger.LogError("Expected game vehicle " + option.VehicleKey + " was not found.");
                return;
            }

            // Reuse our mount when another mod reloads the encyclopedia.
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
                logger.LogInfo("Added " + option.Name + " to VL-49 " + cargoSet.name +
                    " (using HLT-R cargo mount as a temporary in-bay model).");
            }

            // Other mods rerun AfterLoad, which reinitializes and resets our cloned mount.
            mount.info = option.Info;
            mount.mountName = option.Label;
            mount.emptyCost = template.emptyCost;
            mount.emptyMass = template.emptyMass;
            mount.mass = option.Vehicle.mass;
            option.Info.SetCostPerRound(option.Vehicle.value);
            option.Info.SetMassPerRound(option.Vehicle.mass);
            ApplyIcon(encyclopedia, option);

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

            // The first registration happens after the game's network index pass.
            if (!encyclopedia.IndexLookup.Contains(mount))
            {
                ((INetworkDefinition)mount).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(mount);
            }

            if (!cargoSet.weaponOptions.Contains(mount)) cargoSet.weaponOptions.Add(mount);
        }

        /// <summary>Use a stock container symbol for cargo shaped like a container.</summary>
        private void ApplyIcon(Encyclopedia encyclopedia, CargoOption option)
        {
            if (option.IconSourceMountKey == null) return;

            var iconMount = encyclopedia.weaponMounts.FirstOrDefault(candidate =>
                candidate != null && candidate.jsonKey == option.IconSourceMountKey);
            if (iconMount == null || iconMount.info == null || iconMount.info.weaponIcon == null)
            {
                logger.LogWarning("Container icon source " + option.IconSourceMountKey + " was not found for " + option.Name + ".");
                return;
            }

            option.Info.weaponIcon = iconMount.info.weaponIcon;
        }

        /// <summary>Replace the cloned HLT cargo payload with the selected vehicle.</summary>
        public void AttachCargo(Weapon weapon, WeaponMount mount)
        {
            var cargo = weapon as MountedCargo;
            if (cargo == null) return;

            foreach (var option in options)
            {
                if (option.Mount == null || mount != option.Mount) continue;
                // The cloned HLT prefab still points at HLT-R until this hook runs.
                cargo.cargo = option.Vehicle;
                cargo.info = option.Info;
                if (!option.LoggedMass)
                {
                    option.LoggedMass = true;
                    logger.LogInfo(option.Name + " attached: vehicle definition mass=" + option.Vehicle.mass +
                        ", vehicle prefab mass=" + option.Vehicle.unitPrefab.GetComponent<Unit>().GetPrefabMass() +
                        ", HLT cargo empty mass=" + option.Mount.emptyMass + ".");
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

        /// <summary>Clone the HLT mount and give it vehicle-specific loadout metadata.</summary>
        private static WeaponMount CreateMount(WeaponMount template, CargoOption option)
        {
            // Keep the HLT cargo prefab while giving each vehicle its own loadout entry.
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
