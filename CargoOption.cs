// Contains the data for each VL-49 cargo loadout choice.
namespace Simcity
{
    /// <summary>Describes one cargo choice and caches its game objects.</summary>
    internal sealed class CargoOption
    {
        /// <summary>Game definition key of the vehicle to deploy.</summary>
        public readonly string VehicleKey;
        /// <summary>Unique encyclopedia key for this cargo mount.</summary>
        public readonly string MountKey;
        /// <summary>Short label shown in the VL-49 cargo bay.</summary>
        public readonly string Label;
        /// <summary>Full name stored in the weapon information.</summary>
        public readonly string Name;
        /// <summary>Abbreviated weapon name for compact displays.</summary>
        public readonly string ShortName;
        /// <summary>Loadout description shown to the player.</summary>
        public readonly string Description;
        /// <summary>Whether the choice depends on another mod registering its vehicle.</summary>
        public readonly bool Optional;
        /// <summary>Stock mount whose loadout icon this choice should display, if any.</summary>
        public readonly string IconSourceMountKey;
        /// <summary>Vehicle keys spawned one after another when this is a burst cargo, or null.</summary>
        public readonly string[] BurstVehicleKeys;
        /// <summary>Seconds between each burst spawn.</summary>
        public readonly float BurstInterval;
        /// <summary>Vehicle definition resolved from the encyclopedia.</summary>
        public VehicleDefinition Vehicle;
        /// <summary>Resolved burst vehicle definitions, in spawn order.</summary>
        public UnitDefinition[] BurstVehicles;
        /// <summary>Cloned cargo mount added to the VL-49.</summary>
        public WeaponMount Mount;
        /// <summary>Weapon information assigned to the cloned mount.</summary>
        public WeaponInfo Info;
        /// <summary>Tracks whether the mass diagnostic was logged.</summary>
        public bool LoggedMass;

        /// <summary>Describe one vehicle and its cargo loadout entry.</summary>
        public CargoOption(string vehicleKey, string mountKey, string label, string name, string shortName, string description,
            bool optional = false, string iconSourceMountKey = null, string[] burstVehicleKeys = null, float burstInterval = 0f)
        {
            VehicleKey = vehicleKey;
            MountKey = mountKey;
            Label = label;
            Name = name;
            ShortName = shortName;
            Description = description;
            Optional = optional;
            IconSourceMountKey = iconSourceMountKey;
            BurstVehicleKeys = burstVehicleKeys;
            BurstInterval = burstInterval;
        }
    }
}
