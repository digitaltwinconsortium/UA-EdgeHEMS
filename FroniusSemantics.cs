namespace PVMonitor
{
    public sealed class DAYENERGY
    {
        public string Unit { get; set; }

        public int Value { get; set; }
    }

    public sealed class DeviceStatus
    {
        public int ErrorCode { get; set; }

        public int LEDColor { get; set; }

        public int LEDState { get; set; }

        public int MgmtTimerRemainingTime { get; set; }

        public bool StateToReset { get; set; }

        public int StatusCode { get; set; }
    }

    public sealed class FAC
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class IAC
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class IDC
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class PAC
    {
        public string Unit { get; set; }

        public int Value { get; set; }
    }

    public sealed class TOTALENERGY
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class UAC
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class UDC
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class YEARENERGY
    {
        public string Unit { get; set; }

        public double Value { get; set; }
    }

    public sealed class Data
    {
        public DAYENERGY DAY_ENERGY { get; set; }

        public DeviceStatus DeviceStatus { get; set; }

        public FAC FAC { get; set; }

        public IAC IAC { get; set; }

        public IDC IDC { get; set; }

        public PAC PAC { get; set; }

        public TOTALENERGY TOTAL_ENERGY { get; set; }

        public UAC UAC { get; set; }

        public UDC UDC { get; set; }

        public YEARENERGY YEAR_ENERGY { get; set; }
    }

    public sealed class Body
    {
        public Data Data { get; set; }
    }

    public sealed class RequestArguments
    {
        public string DataCollection { get; set; }

        public string DeviceClass { get; set; }

        public string DeviceId { get; set; }

        public string Scope { get; set; }
    }

    public sealed class Status
    {
        public int Code { get; set; }

        public string Reason { get; set; }

        public string UserMessage { get; set; }
    }

    public sealed class Head
    {
        public RequestArguments RequestArguments { get; set; }

        public Status Status { get; set; }

        public string Timestamp { get; set; }
    }

    public sealed class DCACConverter
    {
        public Body Body { get; set; }

        public Head Head { get; set; }
    }
}
  
