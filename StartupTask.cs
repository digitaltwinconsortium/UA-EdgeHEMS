using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
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
            string scopeId = "";
            string registrationId = "";
            string primaryKey = "[]";
            string secondaryKey = "[]";

            var security = new SecurityProviderSymmetricKey(registrationId, primaryKey, secondaryKey);
            var transport = new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpWithWebSocketFallback);
            
            var client = ProvisioningDeviceClient.Create("global.azure-devices-provisioning.net", scopeId, security, transport);
            var result = await client.RegisterAsync();
        
            var connectionString = "HostName=" + result.AssignedHub + ";DeviceId=" + result.DeviceId;
            var deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);

            Random rnd = new Random();
            while (true)
            {
                double currentTemperature = 20 + rnd.NextDouble() * 15;
                double currentHumidity = 60 + rnd.NextDouble() * 20;

                var telemetryDataPoint = new
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity
                };

                try
                {
                    string messageString = JsonConvert.SerializeObject(telemetryDataPoint);
                    Message message = new Message(Encoding.ASCII.GetBytes(messageString));

                    await deviceClient.SendEventAsync(message);
                    Console.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);
                }
                catch (Exception)
                {
                    // do nothing
                }

                await Task.Delay(5000);
            }
        }
    }
}
