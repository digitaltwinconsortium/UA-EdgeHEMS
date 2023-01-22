
namespace UAEdgeHEMS
{
    using Models;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
         static async Task Main(string[] args)
        {
            // setup logging
            string pathToLogFile = Directory.GetCurrentDirectory();
            if (Environment.GetEnvironmentVariable("LOG_FILE_PATH") != null)
            {
                pathToLogFile = Environment.GetEnvironmentVariable("LOG_FILE_PATH");
            }
            InitLogging(pathToLogFile);

            // create OPC UA client app
            string appName = "UAEdgeHEMS";
            if (Environment.GetEnvironmentVariable("APP_NAME") != null)
            {
                appName = Environment.GetEnvironmentVariable("APP_NAME");
            }

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance app = new ApplicationInstance
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "UA.Edge.HEMS"
            };

            await app.LoadApplicationConfiguration(false).ConfigureAwait(false);
            await app.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);

            // create OPC UA cert validator
            app.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAClientCertificateValidationCallback);

            // start the server
            await app.Start(new UAServer()).ConfigureAwait(false);

            Log.Logger.Information("UA Edge HEMS is running.");
            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }

        private static void OPCUAClientCertificateValidationCallback(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            // always trust the OPC UA client certificate
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = true;
            }
        }

        private static void InitLogging(string pathToLogFile)
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

#if DEBUG
            loggerConfiguration.MinimumLevel.Debug();
#else
            loggerConfiguration.MinimumLevel.Information();
#endif
            if (!Directory.Exists(pathToLogFile))
            {
                Directory.CreateDirectory(pathToLogFile);
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console();
            loggerConfiguration.WriteTo.File(Path.Combine(pathToLogFile, "uaedgehems.logfile.txt"), fileSizeLimitBytes: 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 10);

            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Logger.Information($"Log file is: {Path.Combine(pathToLogFile, "uaedgehems.logfile.txt")}");
        }
                
        private static void ActivateTPKasaSmartDevice(string deviceName, string username, string password, bool activate)
        {
            try
            {
                // login
                HttpClient webClient = new();
                string queryBodyJson = "{\"method\":\"login\",\"params\":{\"appType\":\"Kasa_Android\",\"cloudPassword\":\"" + password + "\",\"cloudUserName\":\"" + username + "\",\"terminalUUID\":\"" + Guid.NewGuid().ToString() + "\"}}";
                HttpResponseMessage response = webClient.Send(new HttpRequestMessage(HttpMethod.Post, "https://wap.tplinkcloud.com")
                {
                    Content = new StringContent(queryBodyJson, Encoding.UTF8, "application/json"),
                });

                string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                LoginResponse tpLinkLoginResponse = JsonConvert.DeserializeObject<LoginResponse>(responseString);

                // get device list
                queryBodyJson = "{\"method\":\"getDeviceList\"}";
                response = webClient.Send(new HttpRequestMessage(HttpMethod.Post, "https://wap.tplinkcloud.com/?token=" + tpLinkLoginResponse.result.token)
                {
                    Content = new StringContent(queryBodyJson, Encoding.UTF8, "application/json"),
                });

                responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                GetDeviceListResponse getDeviceListResponse = JsonConvert.DeserializeObject<GetDeviceListResponse>(responseString);

                // find our device
                string deviceID = string.Empty;
                foreach (DeviceListItem item in getDeviceListResponse.result.deviceList)
                {
                    if (item.alias == deviceName)
                    {
                        deviceID = item.deviceId;
                        break;
                    }
                }

                // activate/deactivate it if we found it
                if (!string.IsNullOrEmpty(deviceID))
                {
                    int active = 0;
                    if (activate)
                    {
                        active = 1;
                    }

                    queryBodyJson = "{\"method\":\"passthrough\",\"params\":{\"deviceId\":\"" + deviceID + "\",\"requestData\":'{\"system\":{\"set_relay_state\":{ \"state\":" + active.ToString() + "}}}'}}";
                    response = webClient.Send(new HttpRequestMessage(HttpMethod.Post, "https://wap.tplinkcloud.com/?token=" + tpLinkLoginResponse.result.token)
                    {
                        Content = new StringContent(queryBodyJson, Encoding.UTF8, "application/json"),
                    });

                    responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    PassThroughResponse passthroughResponse = JsonConvert.DeserializeObject<PassThroughResponse>(responseString);
                }

                webClient.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TPLink Kasa device activation/deactivation failed!");
            }
        }
    }
}
