
namespace Models
{
    using System.Collections.Generic;

    // JSON shapes for the Gen1 Shelly EM (type "SHEM") /status REST endpoint.
    // See https://shelly-api-docs.shelly.cloud/gen1/#shelly-em-status

    /// <summary>
    /// Response shape for the Gen1 Shelly EM /status endpoint. Only the
    /// fields actually consumed by this server are mapped; the device
    /// returns considerably more state (Wi-Fi, cloud, relays, etc.).
    /// </summary>
    public sealed class ShellyEMStatus
    {
        // One entry per CT clamp. The Shelly EM (SHEM) reports two entries.
        public List<ShellyEMeter> emeters { get; set; }
    }

    /// <summary>
    /// Per-clamp instantaneous measurements and lifetime energy counters
    /// as returned in the "emeters" array of the Gen1 Shelly EM /status
    /// response.
    /// </summary>
    public sealed class ShellyEMeter
    {
        // Active power on the clamp in Watts. Positive means power is being
        // drawn from the grid; negative means power is being exported.
        public double? power { get; set; }

        // Power factor (-1.0 .. 1.0).
        public double? pf { get; set; }

        // RMS voltage in Volts.
        public double? voltage { get; set; }

        // Whether the clamp has a valid reading. False after a cold start
        // until the device has produced its first sample.
        public bool? is_valid { get; set; }

        // Lifetime active energy consumed (imported) in Watt-hours.
        public double? total { get; set; }

        // Lifetime active energy returned (exported) in Watt-hours.
        public double? total_returned { get; set; }
    }
}
