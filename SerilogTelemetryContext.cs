
namespace UAEdgeHEMS
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Serilog;
    using Serilog.Events;
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Threading.Tasks;

    /// <summary>
    /// Implements the OPC UA <see cref="ITelemetryContext"/> by routing
    /// log messages from the OPC UA stack into a Serilog pipeline and
    /// exposing a single shared <see cref="Meter"/> with hot-path counters
    /// used across the application.
    /// </summary>
    internal sealed class SerilogTelemetryContext : ITelemetryContext, IDisposable
    {
        private const string MeterName = "UAEdgeHEMS";
        private const string MeterVersion = "1.0.0";

        // Cap individual log files at 50 MB. Combined with rollOnFileSizeLimit
        // below, the sink rolls to a new numbered segment when the cap is hit,
        // so log entries are not dropped between daily rollovers.
        private const long LogFileSizeLimitBytes = 50L * 1024 * 1024;

        // Single shared Meter instance — creating a new Meter per call would
        // leak meters and cause registered instruments to be orphaned after
        // the first GC, breaking any external metrics consumer.
        private readonly Meter _meter = new(MeterName, MeterVersion);

        private bool _disposed;

        public SerilogTelemetryContext(string pathToLogFile, Action<ILoggingBuilder> configure = null)
        {
            if (string.IsNullOrWhiteSpace(pathToLogFile))
            {
                pathToLogFile = Directory.GetCurrentDirectory();
            }

            if (!Directory.Exists(pathToLogFile))
            {
                Directory.CreateDirectory(pathToLogFile);
            }

            string logFilePath = Path.Combine(pathToLogFile, "uaedgehems.logfile.txt");

            LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: LogFileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Logger.Information("Log file is: {LogFilePath}", logFilePath);

            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
#if DEBUG
                builder.SetMinimumLevel(LogLevel.Debug);
#else
                builder.SetMinimumLevel(LogLevel.Information);
#endif
                configure?.Invoke(builder);
            }).AddSerilog(Log.Logger);

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;
        }

        public ILoggerFactory LoggerFactory { get; }

        public ActivitySource ActivitySource { get; } = new ActivitySource(MeterName, MeterVersion);

        public Meter CreateMeter() => _meter;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= Unobserved_TaskException;

            _meter.Dispose();
            ActivitySource.Dispose();
            LoggerFactory?.Dispose();
            Log.CloseAndFlush();
        }

        private static void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs args)
        {
            Log.Logger.Error(
                args.ExceptionObject as Exception,
                "Unhandled Exception: (IsTerminating: {IsTerminating})",
                args.IsTerminating);
        }

        private static void Unobserved_TaskException(
            object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            Log.Logger.Error(
                args.Exception,
                "Unobserved Task Exception (Observed: {Observed})",
                args.Observed);
        }
    }
}
