
namespace UAEdgeHEMS
{
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static ApplicationInstance App { get; set; }

         static async Task Main(string[] args)
        {
            // setup logging
            string pathToLogFile = Directory.GetCurrentDirectory();
            if (Environment.GetEnvironmentVariable("LOG_FILE_PATH") != null)
            {
                pathToLogFile = Environment.GetEnvironmentVariable("LOG_FILE_PATH");
            }

            // route OPC UA stack diagnostics through our Serilog-backed telemetry context
            using SerilogTelemetryContext telemetry = new SerilogTelemetryContext(pathToLogFile);

            // create OPC UA client app
            string appName = "UAEdgeHEMS";
            if (Environment.GetEnvironmentVariable("APP_NAME") != null)
            {
                appName = Environment.GetEnvironmentVariable("APP_NAME");
            }

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            App = new ApplicationInstance(telemetry)
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Ua.Edge.HEMS"
            };

            await App.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            await App.CheckApplicationInstanceCertificatesAsync(false, 0).ConfigureAwait(false);

            // create OPC UA cert validator
            App.ApplicationConfiguration.CertificateValidator = new CertificateValidator(telemetry);
            App.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAClientCertificateValidationCallback);
            App.ApplicationConfiguration.CertificateValidator.UpdateAsync(App.ApplicationConfiguration).GetAwaiter().GetResult();

            // start the server
            await App.StartAsync(new UAServer()).ConfigureAwait(false);

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
    }
}
