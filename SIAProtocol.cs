namespace UAEdgeHEMS
{
    using System;
    using System.IO.Ports;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Models;
    using Serilog;

    /// <summary>
    /// Reads house alarm messages in SIA (Security Industry Association) format
    /// from a UART/serial port. Supports the most common SIA DC-03 block layout
    /// used by residential alarm panels reporting events over a serial connection.
    ///
    /// A typical SIA event block looks like:
    ///   [LF] N ri01/BA00123 [CR]
    /// or with an account prefix:
    ///   [LF] #1234 N ri01/BA00123 [CR]
    /// where BA = "Burglary Alarm" and 00123 = zone number.
    /// </summary>
    class SIAProtocol
    {
        private SerialPort _serialPort = null;

        // SIA event token: 2 letter event code optionally followed by a numeric argument (zone / user).
        // Optional "ri<area>/" prefix identifies the reporting area.
        private static readonly Regex EventRegex = new Regex(
            @"(?:ri(?<area>\d+)/)?(?<code>[A-Z]{2})(?<arg>\d*)",
            RegexOptions.Compiled);

        // Optional account number prefix "#1234"
        private static readonly Regex AccountRegex = new Regex(
            @"#(?<acct>[0-9A-Fa-f]+)",
            RegexOptions.Compiled);

        public HouseAlarm Alarm { get; } = new HouseAlarm();

        public SIAProtocol(string serialPortName, int baudRate = 9600)
        {
            // most residential SIA-capable panels speak 9600 8N1 over RS-232 / TTL UART
            _serialPort = new SerialPort(serialPortName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = SerialPort.InfiniteTimeout,
                NewLine = "\r"
            };

            _serialPort.Open();
        }

        public void ProcessStream()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;

                StringBuilder block = new StringBuilder();
                bool inBlock = false;

                while (true)
                {
                    try
                    {
                        int read = _serialPort.ReadByte();
                        if (read < 0)
                        {
                            continue;
                        }

                        byte b = (byte)read;

                        if (b == SIAConstants.LineFeedMarker)
                        {
                            // start of a new SIA event block
                            block.Clear();
                            inBlock = true;
                            continue;
                        }

                        if (b == SIAConstants.CarriageReturnMarker)
                        {
                            if (inBlock && block.Length > 0)
                            {
                                ProcessEventBlock(block.ToString());
                            }

                            block.Clear();
                            inBlock = false;
                            continue;
                        }

                        if (inBlock)
                        {
                            block.Append((char)b);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Reading SIA alarm message from serial port failed!");

                        // reset accumulator and keep the thread alive
                        block.Clear();
                        inBlock = false;
                        Thread.Sleep(1000);
                    }
                }
            }).Start();
        }

        private void ProcessEventBlock(string block)
        {
            Log.Debug("SIA event block received: {block}", block);

            // optional account number prefix
            Match accountMatch = AccountRegex.Match(block);
            if (accountMatch.Success)
            {
                Alarm.AccountNumber = accountMatch.Groups["acct"].Value;
            }

            // first valid event token wins (panels typically send one event per block)
            Match eventMatch = EventRegex.Match(block);
            if (!eventMatch.Success)
            {
                Log.Warning("SIA event block contained no recognizable event code: {block}", block);
                return;
            }

            string code = eventMatch.Groups["code"].Value;
            string area = eventMatch.Groups["area"].Value;
            string arg  = eventMatch.Groups["arg"].Value;

            string description = SIAConstants.EventCodes.TryGetValue(code, out string desc)
                ? desc
                : "Unknown SIA Event";

            Alarm.LastEventCode = code;
            Alarm.LastEventDescription = description;
            Alarm.LastEventZone = arg;
            Alarm.LastEventArea = area;
            Alarm.LastEventTimestamp = DateTime.UtcNow;

            if (SIAConstants.ActiveAlarmCodes.Contains(code))
            {
                Alarm.AlarmActive = true;
            }
            else if (SIAConstants.RestoreCodes.Contains(code))
            {
                Alarm.AlarmActive = false;
            }

            Log.Information("House alarm event: {code} ({description}) area={area} zone/user={arg}",
                code, description, string.IsNullOrEmpty(area) ? "-" : area, string.IsNullOrEmpty(arg) ? "-" : arg);
        }
    }
}
