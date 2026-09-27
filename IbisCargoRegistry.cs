using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

// Contains registration and payload selection for UH-90 Ibis cargo mounts.
namespace Simcity
{
    /// <summary>Registers deployable vehicles as UH-90 Ibis cargo choices.</summary>
    internal sealed class IbisCargoRegistry : VehicleCargoRegistry
    {
        /// <summary>Game definition key of the UH-90 Ibis helicopter.</summary>
        private const string AircraftKeyName = "UtilityHelo1";
        /// <summary>Configured name of the full cargo bay.</summary>
        private const string FullSetName = "Cargo Bay";
        /// <summary>Configured name of the front small cargo bay.</summary>
        private const string FrontSetName = "Cargo Bay (Front)";
        /// <summary>Configured name of the rear small cargo bay.</summary>
        private const string RearSetName = "Cargo Bay (Rear)";
        /// <summary>Maximum front or rear cargo payload in kilograms.</summary>
        private const float MaximumFrontRearMass = 4000f;
        /// <summary>Maximum full cargo bay payload in kilograms.</summary>
        private const float MaximumFullMass = 8000f;

        /// <summary>Create a UH-90 Ibis registry that writes to the plugin log.</summary>
        public IbisCargoRegistry(ManualLogSource logger) : base(logger, "simcity_ibis_cargo_")
        {
        }

        /// <summary>Game definition key of the UH-90 Ibis.</summary>
        protected override string AircraftKey { get { return AircraftKeyName; } }
        /// <summary>Short name used in registration logs.</summary>
        public override string DisplayName { get { return "UH-90 Ibis"; } }

        /// <summary>Locate the Ibis full cargo bay and the two small front/rear bays.</summary>
        protected override CargoBay[] BuildBays(Encyclopedia encyclopedia, Aircraft aircraft)
        {
            var fullSet = FindSetByName(aircraft, FullSetName);
            if (fullSet == null)
            {
                logger.LogError("UH-90 Ibis cargo bay " + FullSetName + " was not found.");
                return null;
            }

            var fullTemplate = FirstSingleCargo(new[] { fullSet }) ?? FindAnyCargoTemplate(encyclopedia);
            if (fullTemplate == null)
            {
                logger.LogError("UH-90 Ibis cargo bay has no single-cargo mount to clone.");
                return null;
            }

            var frontSet = FindSetByName(aircraft, FrontSetName);
            var rearSet = FindSetByName(aircraft, RearSetName);
            var smallSets = new[] { frontSet, rearSet }.Where(set => set != null).ToArray();
            if (smallSets.Length == 0)
            {
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
                    0f, MaximumFrontRearMass, true, "from the UH-90 Ibis front or rear cargo bay"));
            }
            bays.Add(new CargoBay(fullSet.name, new[] { fullSet }, fullTemplate,
                MaximumFrontRearMass, MaximumFullMass, true, "from the UH-90 Ibis cargo bay"));
            return bays.ToArray();
        }
    }
}
