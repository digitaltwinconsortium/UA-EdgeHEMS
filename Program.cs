
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PVMonitor
{
    class Program
    {
        private const string LinuxUSBSerialPort = "/dev/ttyUSB0";

        private const string FroniusInverterBaseAddress = "192.168.178.31";
        private const int FroniusInverterModbusTCPPort = 502;
        private const int FroniusInverterModbusUnitID = 1;

        private const float FroniusSymoMaxPower = 8200f;

        private const string WallbeWallboxBaseAddress = "192.168.178.21";
        private const int WallbeWallboxModbusTCPPort = 502;
        private const int WallbeWallboxModbusUnitID = 255;

        private const int WallbeWallboxMinChargingCurrent = 6; // EVs don't charge with less than 6 Amps
        private const int WallbeWallboxEVStatusAddress = 100;
        private const int WallbeWallboxMaxCurrentSettingAddress = 101;
        private const int WallbeWallboxCurrentCurrentSettingAddress = 300;
        private const int WallbeWallboxDesiredCurrentSettingAddress = 528;
        private const int WallbeWallboxEnableChargingFlagAddress = 400;

        private const float KWhCost = 0.3090f;
        private const float KWhProfit = 0.0944f;
        private const float GridExportPowerLimit = 7000f;

        private static bool _chargeNow = false;
        private static int _chargingPhases = 2;

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
            // init log file
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(
                        "logfile.txt",
                        fileSizeLimitBytes: 1024 * 1024,
                        flushToDiskInterval: TimeSpan.FromSeconds(30),
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: 2)
                .MinimumLevel.Debug()
                .CreateLogger();
            Log.Information($"{Assembly.GetExecutingAssembly()} V{FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion}");

            // init Modbus TCP client for wallbox
            ModbusTCPClient wallbox = new ModbusTCPClient();
            wallbox.Connect(WallbeWallboxBaseAddress, WallbeWallboxModbusTCPPort);

            // init Modbus TCP client for inverter
            ModbusTCPClient inverter = new ModbusTCPClient();
            inverter.Connect(FroniusInverterBaseAddress, FroniusInverterModbusTCPPort);

            // read current inverter power limit (percentage)
            byte[] WMaxLimit = inverter.Read(
                FroniusInverterModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength);

            int existingLimitPercent = Utils.ByteSwap(BitConverter.ToUInt16(WMaxLimit)) / 100;

            // go to the maximum grid export power limit with immediate effect without timeout
            ushort InverterPowerOutputPercent = (ushort) ((GridExportPowerLimit / FroniusSymoMaxPower) * 100);
            inverter.WriteHoldingRegisters(
                FroniusInverterModbusUnitID,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                new ushort[] { (ushort) (InverterPowerOutputPercent * 100), 0, 0, 0, 1});

            // check new setting
            WMaxLimit = inverter.Read(
                FroniusInverterModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength);

            int newLimitPercent = Utils.ByteSwap(BitConverter.ToUInt16(WMaxLimit)) / 100;

            // print a list of all available serial ports for convenience
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                Log.Information("Serial port available: " + port);
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

                // register our methods
                await deviceClient.SetMethodHandlerAsync("ChargeNowToggle", ChargeNowHandler, null);
                await deviceClient.SetMethodHandlerAsync("ChargingPhases", ChargingPhasesHandler, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Registering device failed!");
            }

            TelemetryData telemetryData = new TelemetryData();
            while (true)
            {
                telemetryData.ChargeNow = _chargeNow;
                telemetryData.NumChargingPhases = _chargingPhases;

                try
                {
                    // read the current weather data from web service
                    WebClient webClient = new WebClient
                    {
                        BaseAddress = "https://api.openweathermap.org/"
                    };

                    string json = webClient.DownloadString("data/2.5/weather?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b");
                    WeatherInfo weather = JsonConvert.DeserializeObject<WeatherInfo>(json);
                    if (weather != null)
                    {
                        telemetryData.Temperature = weather.main.temp;
                        telemetryData.WindSpeed = weather.wind.speed;
                        telemetryData.CloudCover = weather.weather[0].description;
                    }

                    webClient.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Getting weather data failed!");
                }

                try
                {
                    // read the current forecast data from web service
                    WebClient webClient = new WebClient
                    {
                        BaseAddress = "https://api.openweathermap.org/"
                    };

                    string json = webClient.DownloadString("data/2.5/forecast?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b");
                    Forecast forecast = JsonConvert.DeserializeObject<Forecast>(json);
                    if (forecast != null && forecast.list != null && forecast.list.Count == 40)
                    {
                        telemetryData.CloudinessForecast = string.Empty;
                        for (int i = 0; i < 40; i++)
                        {
                            telemetryData.CloudinessForecast += "Cloudiness on " + forecast.list[i].dt_txt + ": " + forecast.list[i].clouds.all + "%\r\n";
                        }
                    }

                    webClient.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Getting weather forecast failed!");
                }

                try
                {
                    // read the current converter data from web service
                    WebClient webClient = new WebClient
                    {
                        BaseAddress = "http://" + FroniusInverterBaseAddress
                    };

                    string json = webClient.DownloadString("solar_api/v1/GetInverterRealtimeData.cgi?Scope=Device&DeviceID=1&DataCollection=CommonInverterData");
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

                    webClient.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Getting converter data failed!");
                }

                try
                {
                    // read the current smart meter data
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
                catch (Exception ex)
                {
                    Log.Error(ex, "Getting smart meter data failed!");
                }

                try
                {
                    // ramp up or down EV charging, based on surplus
                    bool chargingInProgress = IsEVChargingInProgress(wallbox);
                    telemetryData.EVChargingInProgress = chargingInProgress? 1 : 0;
                    if (chargingInProgress)
                    {
                        // read current current (in Amps)
                        ushort wallbeWallboxCurrentCurrentSetting = Utils.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                            WallbeWallboxModbusUnitID,
                            ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                            WallbeWallboxCurrentCurrentSettingAddress,
                            1)));
                        telemetryData.WallboxCurrent = wallbeWallboxCurrentCurrentSetting;

                        OptimizeEVCharging(wallbox, sml.Meter.CurrentPower);
                    }
                    else
                    {
                        telemetryData.WallboxCurrent = 0;

                        // check if we should start charging our EV with the surplus power, but we need at least 6A of current per charing phase
                        // or the user set the "charge now" flag via direct method
                        if (((sml.Meter.CurrentPower / 230) < (_chargingPhases * -6.0f)) || _chargeNow)
                        {
                            StartEVCharging(wallbox);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "EV charing control failed!");
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
                    Log.Error(ex, "Sending telemetry failed!");
                }

                // wait 5 seconds and go again
                await Task.Delay(5000).ConfigureAwait(false);
            }
        }

        private static Task<MethodResponse> ChargingPhasesHandler(MethodRequest methodRequest, object userContext)
        {
            // increase charing pahses. They can be 1, 2 or 3. Most hybrids only charge on a single phase, most EVs with 2 or even 3 phases
            _chargingPhases += 1;

            if (_chargingPhases > 3)
            {
                _chargingPhases = 1;
            }

            return Task.FromResult(new MethodResponse((int)HttpStatusCode.OK));
        }

        private static Task<MethodResponse> ChargeNowHandler(MethodRequest methodRequest, object userContext)
        {
            // toggle charge now
            _chargeNow = !_chargeNow;

            return Task.FromResult(new MethodResponse((int)HttpStatusCode.OK));
        }

        private static void StopEVCharging(ModbusTCPClient wallbox)
        {
            wallbox.WriteCoil(WallbeWallboxModbusUnitID, WallbeWallboxEnableChargingFlagAddress, false);
        }

        private static void StartEVCharging(ModbusTCPClient wallbox)
        {
            if (IsEVConnected(wallbox))
            {
                // check if we already set our charging enabled flag
                bool chargingEnabled = BitConverter.ToBoolean(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadCoilStatus,
                WallbeWallboxEnableChargingFlagAddress,
                1));

                if (!chargingEnabled)
                {
                    // start charging
                    wallbox.WriteCoil(WallbeWallboxModbusUnitID, WallbeWallboxEnableChargingFlagAddress, true);
                }
            }
        }

        private static bool IsEVConnected(ModbusTCPClient wallbox)
        {
            // read EV status
            char EVStatus = (char)Utils.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxEVStatusAddress,
                1)));

            switch (EVStatus)
            {
                case 'A': return false; // no vehicle connected
                case 'B': return true;  // vehicle connected, not charging
                case 'C': return true;  // vehicle connected, charging, no ventilation required
                case 'D': return true;  // vehicle connected, charging, ventilation required
                case 'E': return false; // wallbox has no power
                case 'F': return false; // wallbox not available
                default: return false;
            }
        }

        private static void OptimizeEVCharging(ModbusTCPClient wallbox, double currentPower)
        {
            // we ramp up and down our charging current in 1 Amp increments/decrements
            // we increase our charging current until a) we have reached the maximum the wallbox can handle or
            // b) we are just below consuming power from the grid (indicated by currentPower becoming positive), we are setting this to -200 Watts
            // we decrease our charging current when currentPower is above 0 (again indicated we are comsuming pwoer from the grid)

            // read maximum current rating
            ushort maxCurrent = Utils.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxMaxCurrentSettingAddress,
                1)));

            // read current current (in Amps)
            ushort wallbeWallboxCurrentCurrentSetting = Utils.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                WallbeWallboxCurrentCurrentSettingAddress,
                1)));

            // check if we have reached our limits (we define a 1KW "deadzone" from -500W to 500W where we keep things the way they are to cater for jitter)
            // "charge now" overwrites this and always charges, but as slowly as possible
            if ((wallbeWallboxCurrentCurrentSetting < maxCurrent) && (currentPower < -500) && !_chargeNow)
            {
                // increse desired current by 1 Amp
                wallbox.WriteHoldingRegisters(
                    WallbeWallboxModbusUnitID,
                    WallbeWallboxDesiredCurrentSettingAddress,
                    new ushort[] { (ushort)(wallbeWallboxCurrentCurrentSetting + 1) });
            }
            else if (currentPower > 500)
            {
                // need to decrease our charging current
                if (wallbeWallboxCurrentCurrentSetting == WallbeWallboxMinChargingCurrent)
                {
                    // we are already at the minimum, so stop, unless charge now is active
                    if (!_chargeNow)
                    {
                        StopEVCharging(wallbox);
                    }
                }
                else
                {
                    // decrease desired current by 1 Amp
                    wallbox.WriteHoldingRegisters(
                        WallbeWallboxModbusUnitID,
                        WallbeWallboxDesiredCurrentSettingAddress,
                        new ushort[] { (ushort)(wallbeWallboxCurrentCurrentSetting - 1) });
                }
            }
        }

        private static bool IsEVChargingInProgress(ModbusTCPClient wallbox)
        {
            // read EV status
            char EVStatus = (char)Utils.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxEVStatusAddress,
                1)));

            switch (EVStatus)
            {
                case 'A': return false; // no vehicle connected
                case 'B': return false; // vehicle connected, not charging
                case 'C': return true;  // vehicle connected, charging, no ventilation required
                case 'D': return true;  // vehicle connected, charging, ventilation required
                case 'E': return false; // wallbox has no power
                case 'F': return false; // wallbox not available
                default: return false;
            }
        }

        private static void ActivateTPKasaSmartDevice(string deviceName, string username, string password, bool activate)
        {
            try
            {
                // login
                WebClient webClient = new WebClient();
                webClient.Headers[HttpRequestHeader.ContentType] = "application/json";
                string response = webClient.UploadString("https://wap.tplinkcloud.com", "{\"method\":\"login\",\"params\":{\"appType\":\"Kasa_Android\",\"cloudPassword\":\"" + password + "\",\"cloudUserName\":\"" + username + "\",\"terminalUUID\":\"" + Guid.NewGuid().ToString() + "\"}}");
                TPLinkKasa.LoginResponse tpLinkLoginResponse = JsonConvert.DeserializeObject<TPLinkKasa.LoginResponse>(response);

                // get device list
                response = webClient.UploadString("https://wap.tplinkcloud.com/?token=" + tpLinkLoginResponse.result.token, "{\"method\":\"getDeviceList\"}");
                TPLinkKasa.GetDeviceListResponse getDeviceListResponse = JsonConvert.DeserializeObject<TPLinkKasa.GetDeviceListResponse>(response);

                // find our device
                string deviceID = string.Empty;
                foreach (TPLinkKasa.DeviceListItem item in getDeviceListResponse.result.deviceList)
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

                    response = webClient.UploadString("https://wap.tplinkcloud.com/?token=" + tpLinkLoginResponse.result.token, "{\"method\":\"passthrough\",\"params\":{\"deviceId\":\"" + deviceID + "\",\"requestData\":'{\"system\":{\"set_relay_state\":{ \"state\":" + active.ToString() + "}}}'}}");
                    TPLinkKasa.PassThroughResponse passthroughResponse = JsonConvert.DeserializeObject<TPLinkKasa.PassThroughResponse>(response);
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
