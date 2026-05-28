namespace Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// SIA DC-03 / DC-09 protocol constants and event code lookup.
    /// See SIA Digital Communication Standard - Event Codes (ANSI/SIA DC-05).
    /// </summary>
    static class SIAConstants
    {
        // framing markers used by SIA DC-03 on a serial line
        public const byte LineFeedMarker = 0x0A;     // LF - start of an event block / message
        public const byte CarriageReturnMarker = 0x0D; // CR - end of an event block / message

        // selection of the most common SIA event codes mapped to human readable descriptions
        public static readonly IReadOnlyDictionary<string, string> EventCodes = new Dictionary<string, string>
        {
            // burglary
            { "BA", "Burglary Alarm" },
            { "BB", "Burglary Bypass" },
            { "BC", "Burglary Cancel" },
            { "BR", "Burglary Restore" },
            { "BT", "Burglary Trouble" },
            { "BV", "Burglary Verified" },
            { "BU", "Burglary Unbypass" },

            // fire
            { "FA", "Fire Alarm" },
            { "FB", "Fire Bypass" },
            { "FR", "Fire Restore" },
            { "FT", "Fire Trouble" },
            { "FU", "Fire Unbypass" },

            // hold-up / panic / medical
            { "HA", "Hold-Up Alarm" },
            { "HR", "Hold-Up Restore" },
            { "PA", "Panic Alarm" },
            { "PR", "Panic Restore" },
            { "MA", "Medical Alarm" },
            { "MR", "Medical Restore" },

            // gas / water / environmental
            { "GA", "Gas Alarm" },
            { "GR", "Gas Restore" },
            { "WA", "Water Alarm" },
            { "WR", "Water Restore" },
            { "KA", "Heat Alarm" },
            { "KR", "Heat Restore" },
            { "ZA", "Freeze Alarm" },
            { "ZR", "Freeze Restore" },

            // tamper / supervisory
            { "TA", "Tamper Alarm" },
            { "TR", "Tamper Restore" },
            { "UA", "Untyped Zone Alarm" },
            { "UR", "Untyped Zone Restore" },

            // power
            { "AT", "AC Power Trouble" },
            { "AR", "AC Power Restore" },
            { "YT", "Battery Trouble" },
            { "YR", "Battery Restore" },
            { "YP", "Power Supply Trouble" },
            { "YQ", "Power Supply Restore" },

            // system / communication
            { "RR", "Power Up" },
            { "RX", "Manual Test" },
            { "RP", "Automatic Test" },
            { "LT", "Phone Line Trouble" },
            { "LR", "Phone Line Restore" },
            { "YC", "Communications Trouble" },
            { "YK", "Communications Restore" },

            // arm / disarm
            { "CL", "Closing (Armed)" },
            { "CA", "Automatic Closing" },
            { "CF", "Forced Closing" },
            { "CR", "Recent Closing" },
            { "OP", "Opening (Disarmed)" },
            { "OA", "Automatic Opening" },
            { "OR", "Disarm From Alarm" },
            { "OK", "Early Opening" },
        };

        /// <summary>
        /// SIA event codes that represent an active alarm condition (sets AlarmActive=true).
        /// </summary>
        public static readonly HashSet<string> ActiveAlarmCodes = new HashSet<string>
        {
            "BA", "FA", "HA", "PA", "MA", "GA", "WA", "KA", "ZA", "TA", "UA", "BV"
        };

        /// <summary>
        /// SIA event codes that clear an alarm condition (sets AlarmActive=false).
        /// </summary>
        public static readonly HashSet<string> RestoreCodes = new HashSet<string>
        {
            "BR", "FR", "HR", "PR", "MR", "GR", "WR", "KR", "ZR", "TR", "UR", "OR"
        };
    }

    public sealed class HouseAlarm
    {
        public HouseAlarm()
        {
            LastEventCode = string.Empty;
            LastEventDescription = string.Empty;
            LastEventZone = string.Empty;
            LastEventArea = string.Empty;
            LastEventTimestamp = DateTime.MinValue;
            AccountNumber = string.Empty;
            AlarmActive = false;
        }

        public string LastEventCode { get; set; }

        public string LastEventDescription { get; set; }

        public string LastEventZone { get; set; }

        public string LastEventArea { get; set; }

        public DateTime LastEventTimestamp { get; set; }

        public string AccountNumber { get; set; }

        public bool AlarmActive { get; set; }
    }
}
