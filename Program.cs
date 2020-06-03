
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace PVMonitor
{
    class Program
    {
        private const string LinuxUSBSerialPort = "/dev/ttyUSB0"; 
        
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
            // print a list of all available serial ports for convenience
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                Console.WriteLine("Serial port available: " + port);
            }

            // start processing smart meter messages
            SmartMeterLanguage sml = new SmartMeterLanguage(LinuxUSBSerialPort);
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
                        BaseAddress = "http://192.168.178.31/"
                    };
                    var json = webClient.DownloadString("solar_api/v1/GetInverterRealtimeData.cgi?Scope=Device&DeviceID=1&DataCollection=CommonInverterData");
                    DCACConverter converter = JsonConvert.DeserializeObject<DCACConverter>(json);
                    if (converter != null)
                    {
                        if (converter.Body.Data.PAC != null)
                        {
                            telemetryData.PVOutputPower = ((double)converter.Body.Data.PAC.Value) / 1000.0;
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
                        telemetryData.MeterEnergyConsumed = 0.0;

                        // calculate energy consumed from the other telemetry, if available
                        if ((telemetryData.MeterEnergyPurchased != 0.0)
                         && (telemetryData.MeterEnergySold != 0.0)
                         && (telemetryData.PVOutputEnergyTotal != 0.0))
                        {
                            telemetryData.MeterEnergyConsumed = telemetryData.PVOutputEnergyTotal + sml.Meter.EnergyPurchased - sml.Meter.EnergySold;
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
