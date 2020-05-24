using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace PVMonitor
{
    public sealed class StartupTask : IBackgroundTask
    {
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
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

                var connectionString = "HostName=" + result.AssignedHub + ";DeviceId=" + result.DeviceId;
                deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            WeatherInfo weather = null;
            DCACConverter converter = null;
            SmartMeter meter = null;
            while (true)
            {
                try
                {
                    // read the current weather data from web service
                    weather = null;
                    using (WebClient webClient = new WebClient())
                    {
                        webClient.BaseAddress = "https://api.openweathermap.org/";
                        var json = webClient.DownloadString("data/2.5/weather?q=Munich,de&units=metric&lang=de&appid=2898258e654f7f321ef3589c4fa58a9b");
                        weather = JsonConvert.DeserializeObject<WeatherInfo>(json);
                    }
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                }

                try
                {
                    // read the current converter data from web service
                    converter = null;
                    using (WebClient webClient = new WebClient())
                    {
                        webClient.BaseAddress = "http://192.168.178.31/";
                        var json = webClient.DownloadString("solar_api/v1/GetInverterRealtimeData.cgi?Scope=Device&DeviceID=1&DataCollection=CommonInverterData");
                        converter = JsonConvert.DeserializeObject<DCACConverter>(json);
                    }
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                }

                try
                {
                    // read the currnt smart meter data from serial port
                    meter = null;
                    // TODO
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex.Message);
                }

                TelemetryData telemetryData = new TelemetryData();
                if (weather != null)
                {
                    telemetryData.Temperature = weather.main.temp;
                    telemetryData.WindSpeed = weather.wind.speed.ToString();
                    telemetryData.CloudCover = weather.weather[0].description;
                }
                if (converter != null)
                {
                    telemetryData.PVOutputPower = ((double)converter.Body.Data.PAC.Value) / 1000.0;
                    telemetryData.PVOutputEnergyDay = ((double)converter.Body.Data.DAY_ENERGY.Value) / 1000.0;
                    telemetryData.PVOutputEnergyYear = ((double)converter.Body.Data.YEAR_ENERGY.Value) / 1000.0;
                    telemetryData.PVOutputEnergyTotal = ((double)converter.Body.Data.TOTAL_ENERGY.Value) / 1000.0;
                }
                if (meter != null)
                {
                    telemetryData.MeterEnergyPurchased = meter.EnergyPurchased;
                    telemetryData.MeterEnergySold = meter.EnergySold;
                }

                try
                {
                    string messageString = JsonConvert.SerializeObject(telemetryData);
                    Message message = new Message(Encoding.UTF8.GetBytes(messageString));

                    await deviceClient.SendEventAsync(message);
                    Console.WriteLine("{0}: {1}", DateTime.Now, messageString);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                await Task.Delay(5000);
            }
        }
    }
}
