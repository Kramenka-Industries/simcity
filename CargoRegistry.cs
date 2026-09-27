using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

// Contains registration and payload selection for VL-49 cargo mounts.
namespace Simcity
{
    /// <summary>Registers deployable vehicles as VL-49 cargo choices.</summary>
    internal sealed class CargoRegistry : VehicleCargoRegistry
    {
        /// <summary>Game definition key of the VL-49 Tarantula.</summary>
        private const string AircraftKeyName = "QuadVTOL1";
        /// <summary>Stock full cargo mount used to locate and clone the cargo bay.</summary>
        private const string FullTemplateKey = "HLT-Rx1";
        /// <summary>Configured name of the front small cargo bay.</summary>
        private const string FrontSetName = "Cargo Bay (Front)";
        /// <summary>Configured name of the rear small cargo bay.</summary>
        private const string RearSetName = "Cargo Bay (Rear)";
        /// <summary>Maximum front or rear cargo payload in kilograms.</summary>
        private const float MaximumFrontRearMass = 12000f;
        /// <summary>Maximum full cargo bay payload in kilograms.</summary>
        private const float MaximumFullMass = 24000f;

        /// <summary>Create a VL-49 registry that writes to the plugin log.</summary>
        public CargoRegistry(ManualLogSource logger) : base(logger, "simcity_vl49_cargo_")
        {
        }

        /// <summary>Game definition key of the VL-49 Tarantula.</summary>
        protected override string AircraftKey { get { return AircraftKeyName; } }
        /// <summary>Short name used in registration logs.</summary>
        protected override string DisplayName { get { return "VL-49"; } }

        /// <summary>Locate the VL-49 full cargo bay and the two small front/rear bays.</summary>
        protected override CargoBay[] BuildBays(Encyclopedia encyclopedia, Aircraft aircraft)
        {
            var fullTemplate = encyclopedia.weaponMounts.FirstOrDefault(mount =>
                mount != null && mount.jsonKey == FullTemplateKey && IsSingleCargo(mount));
            if (fullTemplate == null)
            {
                logger.LogError("VL-49 cargo mount " + FullTemplateKey + " was not found.");
                return null;
            }

            var fullSet = aircraft.weaponManager.hardpointSets.FirstOrDefault(set =>
                set != null && set.weaponOptions != null && set.weaponOptions.Contains(fullTemplate));
            if (fullSet == null)
            {
                logger.LogError("VL-49 cargo bay containing " + FullTemplateKey + " was not found.");
                return null;
            }

            var frontSet = FindSetByName(aircraft, FrontSetName);
            var rearSet = FindSetByName(aircraft, RearSetName);
            var smallSets = new[] { frontSet, rearSet }.Where(set => set != null).ToArray();
            if (smallSets.Length == 0)
            {
                // Fall back to any other cargo set so at least the full bay keeps working.
                smallSets = aircraft.weaponManager.hardpointSets
                    .Where(set => set != null && set.weaponOptions != null && set != fullSet &&
                        set.weaponOptions.Any(option => option != null && IsSingleCargo(option)))
                    .ToArray();
            }

            var bays = new List<CargoBay>();
            if (smallSets.Length > 0)
            {
                var smallTemplate = FirstSingleCargo(smallSets) ?? fullTemplate;
                bays.Add(new CargoBay("Cargo Bay (Front) and Cargo Bay (Rear)", smallSets, smallTemplate,
                    0f, MaximumFrontRearMass, true, "from the VL-49 front or rear cargo bay"));
            }
            bays.Add(new CargoBay(fullSet.name, new[] { fullSet }, fullTemplate,
                MaximumFrontRearMass, MaximumFullMass, true, "from the VL-49 cargo bay"));
            return bays.ToArray();
        }
    }
}
