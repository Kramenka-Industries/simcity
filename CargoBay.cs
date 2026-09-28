// Contains one placeable cargo bay group for the dynamic vehicle registries.
namespace Simcity
{
    /// <summary>Describes one cargo bay (or symmetric bay pair) and its mass range.</summary>
    internal sealed class CargoBay
    {
        /// <summary>Bays or bay pairs that receive this cargo's cloned mount.</summary>
        public readonly HardpointSet[] Sets;
        /// <summary>Stock mount cloned to build the in-bay model and encyclopedia entry.</summary>
        public readonly WeaponMount Template;
        /// <summary>Player-facing bay name used in descriptions and diagnostics.</summary>
        public readonly string Name;
        /// <summary>Loadout description appended to generated vehicle names.</summary>
        public readonly string Description;
        /// <summary>Minimum payload mass in kilograms (inclusive).</summary>
        public readonly float MinMass;
        /// <summary>Maximum payload mass in kilograms.</summary>
        public readonly float MaxMass;
        /// <summary>Whether the maximum mass is inclusive.</summary>
        public readonly bool MaxInclusive;

        /// <summary>Describe one cargo bay group and its supported mass range.</summary>
        public CargoBay(string name, HardpointSet[] sets, WeaponMount template, float minMass, float maxMass, bool maxInclusive, string description)
        {
            Name = name;
            Sets = sets;
            Template = template;
            MinMass = minMass;
            MaxMass = maxMass;
            MaxInclusive = maxInclusive;
            Description = description;
        }

        /// <summary>Check whether a payload mass belongs in this bay.</summary>
        public bool Matches(float mass)
        {
            if (mass < MinMass) return false;
            return MaxInclusive ? mass <= MaxMass : mass < MaxMass;
        }

        /// <summary>Check whether a payload mass exceeds this bay's upper limit.</summary>
        public bool ExceedsMax(float mass)
        {
            return MaxInclusive ? mass > MaxMass : mass >= MaxMass;
        }
    }
}
