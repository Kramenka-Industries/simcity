using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

// Contains registration and payload selection for MC-260 cargo mounts.
namespace Simcity
{
    /// <summary>Registers deployable vehicles as MC-260 cargo choices.</summary>
    internal sealed class ChimeraCargoRegistry : VehicleCargoRegistry
    {
        /// <summary>Game definition key of the MC-260 Chimera.</summary>
        private const string AircraftKeyName = "Aryx_CargoPlane1";
        /// <summary>Chimera cargo mount used to locate and clone the front and rear bays.</summary>
        private const string CargoTemplateKey = "Aryx_ADLT_01x1";
        /// <summary>Name of the Chimera hardpoint set that carries heavy vehicles.</summary>
        private const string MissionBaySetName = "Mission Bay";
        /// <summary>Maximum front or rear MC-260 cargo payload in kilograms.</summary>
        private const float MaximumCargoMass = 45000f;
        /// <summary>Maximum mission bay MC-260 cargo payload in kilograms.</summary>
        private const float MaximumMissionMass = 90000f;
        /// <summary>Hand-authored mission bay loadout that drops a Hexhound swarm.</summary>
        private static readonly CuratedCargo[] curated =
        {
            new CuratedCargo("Mission Bay", "simcity_chimera_hexhounds", "UGV1_grenade",
                "16 Hexhounds and a Dream", "16 Hexhounds and a Dream", "16 Hexhounds",
                "Deploys 8 Hexhound GMGs and 8 Hexhound SAMs in quick succession from the MC-260 mission bay.",
                new[]
                {
                    "UGV1_SAM", "UGV1_grenade", "UGV1_SAM", "UGV1_grenade", "UGV1_SAM",
                    "UGV1_grenade", "UGV1_SAM", "UGV1_grenade", "UGV1_SAM", "UGV1_grenade",
                    "UGV1_SAM", "UGV1_grenade", "UGV1_SAM", "UGV1_grenade", "UGV1_SAM",
                }, 0.3f),
        };

        /// <summary>Create a Chimera cargo registry that writes to the plugin log.</summary>
        public ChimeraCargoRegistry(ManualLogSource logger) : base(logger, "simcity_chimera_cargo_")
        {
        }

        /// <summary>Game definition key of the MC-260 Chimera.</summary>
        protected override string AircraftKey { get { return AircraftKeyName; } }
        /// <summary>Short name used in registration logs.</summary>
        public override string DisplayName { get { return "MC-260"; } }

        /// <summary>Locate the Chimera front and rear cargo bays and its mission bay.</summary>
        protected override CargoBay[] BuildBays(Encyclopedia encyclopedia, Aircraft aircraft)
        {
            var cargoTemplate = encyclopedia.weaponMounts.FirstOrDefault(mount =>
                mount != null && mount.jsonKey == CargoTemplateKey && IsSingleCargo(mount));
            if (cargoTemplate == null)
            {
                logger.LogError("MC-260 cargo mount " + CargoTemplateKey + " was not found.");
                return null;
            }

            var cargoSets = aircraft.weaponManager.hardpointSets
                .Where(set => set != null && set.weaponOptions != null && set.weaponOptions.Contains(cargoTemplate))
                .ToArray();
            if (cargoSets.Length != 2)
            {
                logger.LogError("Expected MC-260 front and rear cargo sets containing " + CargoTemplateKey +
                    ", found " + cargoSets.Length + ". Sets: " + string.Join(", ", cargoSets.Select(set => set.name).ToArray()));
                return null;
            }

            var missionSet = FindMissionSet(aircraft, cargoSets);
            var missionTemplate = missionSet == null ? null : missionSet.weaponOptions
                .Where(option => option != null && IsSingleCargo(option))
                .OrderByDescending(option => option.mass)
                .FirstOrDefault();

            var bays = new List<CargoBay>();
            bays.Add(new CargoBay("Cargo Bay (Front) and Cargo Bay (Rear)", cargoSets, cargoTemplate,
                0f, MaximumCargoMass, false, "from an MC-260 front or rear cargo bay"));
            if (missionSet != null && missionTemplate != null)
            {
                bays.Add(new CargoBay("Mission Bay", new[] { missionSet }, missionTemplate,
                    MaximumCargoMass, MaximumMissionMass, true, "from the MC-260 mission bay"));
            }
            else
            {
                logger.LogWarning("MC-260 mission bay was not found; vehicles at or above " + MaximumCargoMass + " kg were skipped.");
            }
            return bays.ToArray();
        }

        /// <summary>Add the hand-authored Hexhound swarm to the mission bay.</summary>
        protected override CuratedCargo[] BuildCurated(Encyclopedia encyclopedia, Aircraft aircraft)
        {
            return curated;
        }

        /// <summary>Locate the mission bay hardpoint set by name, falling back to the remaining single-cargo set.</summary>
        private static HardpointSet FindMissionSet(Aircraft aircraft, HardpointSet[] cargoSets)
        {
            var missionSet = FindSetByName(aircraft, MissionBaySetName);
            if (missionSet != null) return missionSet;

            return aircraft.weaponManager.hardpointSets.FirstOrDefault(set => set != null && set.weaponOptions != null &&
                !cargoSets.Contains(set) && set.weaponOptions.Any(option => option != null && IsSingleCargo(option)));
        }
    }
}
