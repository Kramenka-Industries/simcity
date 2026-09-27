// Contains the selectable logistics vehicle family for generated cargo options.
namespace Simcity
{
    /// <summary>Which logistics vehicle family is offered as generated cargo.</summary>
    internal enum VehicleFamily
    {
        /// <summary>Only the HLT series; matching MSV equivalents are hidden.</summary>
        HLT,
        /// <summary>Only the MSV series; matching HLT equivalents are hidden.</summary>
        MSV,
        /// <summary>Both families are offered.</summary>
        Both,
    }
}
