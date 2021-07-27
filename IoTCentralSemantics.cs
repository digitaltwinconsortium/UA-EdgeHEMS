
namespace PVMonitor
{
    public sealed class TelemetryData
    {
        public TelemetryData()
        {
            // init data
            Temperature = -100.0;
            CloudCover = "not available";
            WindSpeed = 0.0;
            PVOutputPower = 0.0;
            PVOutputEnergyDay = 0.0;
            PVOutputEnergyYear = 0.0;
            PVOutputEnergyTotal = 0.0;
            MeterEnergyPurchased = 0.0;
            MeterEnergySold = 0.0;
            MeterEnergyConsumed = 0.0;
            EnergyCost = 0.0;
            EnergyProfit = 0.0;
            CurrentPower = 0.0;
            CurrentPowerConsumed = 0.0;
            EVChargingInProgress = 0;
            WallboxCurrent = 0;
            CloudinessForecast = "not available";
            ChargeNow = false;
            NumChargingPhases = 2;
        }

        public double Temperature { get; set; }

        public string CloudCover { get; set; }

        public double WindSpeed { get; set; }

        public double PVOutputPower { get; set; }

        public double PVOutputEnergyDay { get; set; }

        public double PVOutputEnergyYear { get; set; }

        public double PVOutputEnergyTotal { get; set; }

        public double MeterEnergyPurchased { get; set; }

        public double MeterEnergySold { get; set; }

        public double MeterEnergyConsumed { get; set; }

        public double EnergyCost { get; set; }

        public double EnergyProfit { get; set; }

        public double CurrentPower { get; set; }

        public double CurrentPowerConsumed { get; set; }

        public int EVChargingInProgress { get; set; }

        public int WallboxCurrent { get; set; }

        public string CloudinessForecast { get; set; }

        public bool ChargeNow { get; set; }

        public int NumChargingPhases { get; set; }
    }
}
