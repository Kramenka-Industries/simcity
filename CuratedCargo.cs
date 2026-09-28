// Contains one manually curated cargo loadout that is added alongside the generated ones.
namespace Simcity
{
    /// <summary>Describes a hand-authored cargo entry and the bay it belongs to.</summary>
    internal sealed class CuratedCargo
    {
        /// <summary>Name of the cargo bay this entry is added to.</summary>
        public readonly string BayName;
        /// <summary>Unique encyclopedia key for the cloned mount.</summary>
        public readonly string MountKey;
        /// <summary>Game definition key of the vehicle that is spawned first.</summary>
        public readonly string VehicleKey;
        /// <summary>Short label shown in the cargo bay.</summary>
        public readonly string Label;
        /// <summary>Full name stored in the weapon information.</summary>
        public readonly string Name;
        /// <summary>Abbreviated weapon name for compact displays.</summary>
        public readonly string ShortName;
        /// <summary>Loadout description shown to the player.</summary>
        public readonly string Description;
        /// <summary>Vehicle keys spawned one after another when deployed, or null for a single spawn.</summary>
        public readonly string[] BurstVehicleKeys;
        /// <summary>Seconds between each burst spawn.</summary>
        public readonly float BurstInterval;
        /// <summary>Cached option so weapon information survives encyclopedia reloads.</summary>
        public CargoOption Option;

        /// <summary>Describe one curated cargo entry.</summary>
        public CuratedCargo(string bayName, string mountKey, string vehicleKey, string label, string name, string shortName,
            string description, string[] burstVehicleKeys = null, float burstInterval = 0f)
        {
            BayName = bayName;
            MountKey = mountKey;
            VehicleKey = vehicleKey;
            Label = label;
            Name = name;
            ShortName = shortName;
            Description = description;
            BurstVehicleKeys = burstVehicleKeys;
            BurstInterval = burstInterval;
        }
    }
}
