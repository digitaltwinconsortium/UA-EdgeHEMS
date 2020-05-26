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
            // print a list of serial ports
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                Console.WriteLine("Serial port available: " + port);
            }

            // open the serial port
            SerialPort serialPort = new SerialPort("/dev/ttyUSB0", 9600, Parity.None, 8, StopBits.One);
            serialPort.ReadTimeout = 2000;
            serialPort.Open();

            string message = string.Empty;

            while (true)
            {
                
                try
                {
                    byte[] array = new byte[256];
                    int bytesRead = serialPort.Read(array, 0, 256);
                    string bytesReadString = BitConverter.ToString(array).Replace('-', ',').Substring(0, bytesRead * 3);
                    Console.WriteLine(bytesReadString);
                }
                catch (TimeoutException)
                {
                    // do nothing
                }
            }

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
                    using (WebClient webClient = new WebClient())
                    {
                        webClient.BaseAddress = "https://api.openweathermap.org/";
                        var json = webClient.DownloadString("data/2.5/weather?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b");
                        WeatherInfo weather = JsonConvert.DeserializeObject<WeatherInfo>(json);
                        if (weather != null)
                        {
                            telemetryData.Temperature = weather.main.temp;
                            telemetryData.WindSpeed = weather.wind.speed;
                            telemetryData.CloudCover = weather.weather[0].description;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                try
                {
                    // read the current converter data from web service
                    using (WebClient webClient = new WebClient())
                    {
                        webClient.BaseAddress = "http://192.168.178.31/";
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
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                try
                {
                    // read the current smart meter data from serial port
                    SmartMeter meter = null;
                    
                    // TODO: parse SML message
             
                    if (meter != null)
                    {
                        telemetryData.MeterEnergyPurchased = meter.EnergyPurchased;
                        telemetryData.MeterEnergySold = meter.EnergySold;
                        telemetryData.MeterEnergyConsumed = telemetryData.PVOutputEnergyTotal + meter.EnergyPurchased - meter.EnergySold;
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
