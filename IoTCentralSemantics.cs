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
    }
}
