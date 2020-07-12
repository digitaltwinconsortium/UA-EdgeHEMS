
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PVMonitor
{
    class Program
    {
        private const string LinuxUSBSerialPort = "/dev/ttyUSB0";
        private const string FroniusInverterBaseAddress = "192.168.178.31";
        private const int FroniusInverterModbusTCPPort = 502;
        private const float FroniusSymoMaxPower = 8200f;

        private const float KWhCost = 0.2850f;
        private const float KWhProfit = 0.1018f;
        private const float GridExportPowerLimit = 7000f;

        static async Task Main(string[] args)
        {
#if DEBUG
            // Attach remote debugger
            while (true)
            {

                Console.WriteLine("Waiting for remote debugger to attach...");
                
                if (Debugger.IsAttached)
                {
                    break;
                }

                System.Threading.Thread.Sleep(1000);
            }
#endif
            // init Modbus TCP client
            ModbusTCPClient client = new ModbusTCPClient();
            client.Connect(FroniusInverterBaseAddress, FroniusInverterModbusTCPPort);

            // initial check
            byte[] WMaxLimit = client.ReadHoldingRegisters(
                1, // inverter unit#
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength);

            int existingLimitPercent = Utils.ByteSwap(BitConverter.ToUInt16(WMaxLimit)) / 100;

            // go to the maximum grid export power limit with immediate effect without timeout
            ushort InverterPowerOutputPercent = (ushort) ((GridExportPowerLimit / FroniusSymoMaxPower) * 100);
            client.WriteHoldingRegisters(
                1, // inverter unit#
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                new ushort[] { (ushort) (InverterPowerOutputPercent * 100), 0, 0, 0, 1});

            // check new setting
            WMaxLimit = client.ReadHoldingRegisters(
                1, // inverter unit#
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength);

            int newLimitPercent = Utils.ByteSwap(BitConverter.ToUInt16(WMaxLimit)) / 100;

            // print a list of all available serial ports for convenience
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                Console.WriteLine("Serial port available: " + port);
            }

            // start processing smart meter messages
            SmartMessageLanguage sml = new SmartMessageLanguage(LinuxUSBSerialPort);
            sml.ProcessStream();

            DeviceClient deviceClient = null;
            try
            {
                // register the device
                string scopeId = "0ne0010B637";
                string deviceId = "RasPi2B";
                string primaryKey = "";
                string secondaryKey = "";

                var security = new SecurityProviderSymmetricKey(deviceId, primaryKey, secondaryKey);
                var transport = new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpWithWebSocketFallback);

                var provisioningClient = ProvisioningDeviceClient.Create("global.azure-devices-provisioning.net", scopeId, security, transport);
                var result = await provisioningClient.RegisterAsync();

                var connectionString = "HostName=" + result.AssignedHub + ";DeviceId=" + result.DeviceId + ";SharedAccessKey=" + primaryKey;
                deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            while (true)
            {
                TelemetryData telemetryData = new TelemetryData();

                try
                {
                    // read the current weather data from web service
                    using WebClient webClient = new WebClient
                    {
                        BaseAddress = "https://api.openweathermap.org/"
                    };
                    var json = webClient.DownloadString("data/2.5/weather?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b");
                    WeatherInfo weather = JsonConvert.DeserializeObject<WeatherInfo>(json);
                    if (weather != null)
                    {
                        telemetryData.Temperature = weather.main.temp;
                        telemetryData.WindSpeed = weather.wind.speed;
                        telemetryData.CloudCover = weather.weather[0].description;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                try
                {
                    // read the current converter data from web service
                    using WebClient webClient = new WebClient
                    {
                        BaseAddress = FroniusInverterBaseAddress
                    };
                    var json = webClient.DownloadString("solar_api/v1/GetInverterRealtimeData.cgi?Scope=Device&DeviceID=1&DataCollection=CommonInverterData");
                    DCACConverter converter = JsonConvert.DeserializeObject<DCACConverter>(json);
                    if (converter != null)
                    {
                        if (converter.Body.Data.PAC != null)
                        {
                            telemetryData.PVOutputPower = converter.Body.Data.PAC.Value;
                        }
                        if (converter.Body.Data.DAY_ENERGY != null)
                        {
                            telemetryData.PVOutputEnergyDay = ((double)converter.Body.Data.DAY_ENERGY.Value) / 1000.0;
                        }
                        if (converter.Body.Data.YEAR_ENERGY != null)
                        {
                            telemetryData.PVOutputEnergyYear = ((double)converter.Body.Data.YEAR_ENERGY.Value) / 1000.0;
                        }
                        if (converter.Body.Data.TOTAL_ENERGY != null)
                        {
                            telemetryData.PVOutputEnergyTotal = ((double)converter.Body.Data.TOTAL_ENERGY.Value) / 1000.0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                try
                {
                    // read the current smart meter data
                    if (sml != null)
                    {
                        telemetryData.MeterEnergyPurchased = sml.Meter.EnergyPurchased;
                        telemetryData.MeterEnergySold = sml.Meter.EnergySold;
                        telemetryData.CurrentPower = sml.Meter.CurrentPower;

                        telemetryData.EnergyCost = telemetryData.MeterEnergyPurchased * KWhCost;
                        telemetryData.EnergyProfit = telemetryData.MeterEnergySold * KWhProfit;

                        // calculate energy consumed from the other telemetry, if available
                        telemetryData.MeterEnergyConsumed = 0.0;
                        if ((telemetryData.MeterEnergyPurchased != 0.0)
                         && (telemetryData.MeterEnergySold != 0.0)
                         && (telemetryData.PVOutputEnergyTotal != 0.0))
                        {
                            telemetryData.MeterEnergyConsumed = telemetryData.PVOutputEnergyTotal + sml.Meter.EnergyPurchased - sml.Meter.EnergySold;
                            telemetryData.CurrentPowerConsumed = telemetryData.PVOutputPower + sml.Meter.CurrentPower;
                        }
                    }

                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }


                try
                {
                    string messageString = JsonConvert.SerializeObject(telemetryData);
                    Message cloudMessage = new Message(Encoding.UTF8.GetBytes(messageString));

                    await deviceClient.SendEventAsync(cloudMessage);
                    Debug.WriteLine("{0}: {1}", DateTime.Now, messageString);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                await Task.Delay(5000);
            }
        }
    }
}
