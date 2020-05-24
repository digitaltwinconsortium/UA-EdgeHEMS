using System;
using System.Collections.Generic;

namespace PVMonitor
{
    public sealed class Coord
    {
        public double lon { get; set; }

        public double lat { get; set; }
    }

    public sealed class Weather
    {
        public int id { get; set; }

        public string main { get; set; }

        public string description { get; set; }

        public string icon { get; set; }
    }

    public sealed class Main
    {
        public double temp { get; set; }

        public double feels_like { get; set; }

        public double temp_min { get; set; }

        public double temp_max { get; set; }

        public int pressure { get; set; }

        public int humidity { get; set; }
    }

    public sealed class Wind
    {
        public double speed { get; set; }

        public int deg { get; set; }
    }

    public sealed class Clouds
    {
        public int all { get; set; }
    }

    public sealed class Sys
    {
        public int type { get; set; }

        public int id { get; set; }

        public string country { get; set; }

        public int sunrise { get; set; }

        public int sunset { get; set; }
    }

    public sealed class WeatherInfo
    {
        public Coord coord { get; set; }

        public IList<Weather> weather { get; set; }

        public string Base { get; set; }

        public Main main { get; set; }

        public Wind wind { get; set; }

        public Clouds clouds { get; set; }

        public int dt { get; set; }

        public Sys sys { get; set; }

        public int timezone { get; set; }

        public int id { get; set; }

        public string name { get; set; }

        public int cod { get; set; }
    }

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

    public sealed class SmartMeter
    {
        public double EnergyPurchased { get; set; }

        public double EnergySold { get; set; }
    }

    public sealed class TelemetryData
    {
        public TelemetryData()
        {
            // init data
            Temperature = -100.0;
            CloudCover = "not available";
            WindSpeed = "not available";
            PVOutputPower = 0.0;
            PVOutputEnergyDay = 0.0;
            PVOutputEnergyYear = 0.0;
            PVOutputEnergyTotal = 0.0;
            MeterEnergyPurchased = 0.0;
            MeterEnergySold = 0.0;
        }

        public double Temperature { get; set; }

        public string CloudCover { get; set; }

        public string WindSpeed { get; set; }

        public double PVOutputPower { get; set; }

        public double PVOutputEnergyDay { get; set; }

        public double PVOutputEnergyYear { get; set; }

        public double PVOutputEnergyTotal { get; set; }

        public double MeterEnergyPurchased { get; set; }

        public double MeterEnergySold { get; set; }
    }
}
