
namespace UAEdgeHEMS
{
    using Models;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO.Ports;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;

    public class UANodeManager : CustomNodeManager2
    {
        private const string LinuxUSBSerialPort = "/dev/ttyUSB0";

        private const string FroniusInverterBaseAddress = "192.168.178.31";
        private const int FroniusInverterModbusTCPPort = 502;
        private const int FroniusInverterModbusUnitID = 1;

        private const string IDMHeatPumpBaseAddress = "192.168.178.91";
        private const int IDMHeatPumpModbusTCPPort = 502;
        private const int IDMHeatPumpModbusUnitID = 1;

        private const int IDMHeatPumpPVSurplus = 74;
        private const int IDMHeatPumpPVProduction = 78;
        private const int IDMHeatPumpCurrentPowerConsumption = 4122;
        private const int IDMHeatPumpTapWaterTemp = 1030;
        private const int IDMHeatPumpHeatingWaterTemp = 1350;
        private const int IDMHeatPumpStatus = 1090;

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

        private const float KWhCost = 0.48671f;
        private const float KWhProfit = 0.0944f;
        private const float GridExportPowerLimit = 7000f;

        private long _lastUsedId = 0;

        private Timer m_timer;

        private SmartMessageLanguage _sml;

        private ModbusTCPClient _wallbox = new ModbusTCPClient();

        private Dictionary<string, BaseDataVariableState> _uaVariables = new();

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            List<string> namespaceUris = new List<string>
            {                                                   // namespace indicies (0 and 1 are used by the default UA namespaces):
                "http://opcfoundation.org/UA/EdgeHEMS/",        // 2
                "http://opcfoundation.org/UA/SunSpecInverter/", // 3
                "http://opcfoundation.org/UA/SmartMeter/",      // 4
                "http://opcfoundation.org/UA/Wallbox/",         // 5
                "http://opcfoundation.org/UA/Heatpump/",        // 6
                "http://opcfoundation.org/UA/OpenWeatherMap/"   // 7
            };

            NamespaceUris = namespaceUris;

            // init Modbus TCP client for _wallbox
            _wallbox.Connect(WallbeWallboxBaseAddress, WallbeWallboxModbusTCPPort);

            SetPVInverterToFullPower();

            // print a list of all available serial ports for convenience
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                Log.Information("Serial port available: " + port);
            }

            // start processing smart meter messages
            try
            {
                _sml = new SmartMessageLanguage(LinuxUSBSerialPort);
                _sml.ProcessStream();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Connecting to smart meter failed!");
            }

            // kick off our variable update timer
            m_timer = new Timer(UpdateNodeValues, null, 15000, 15000);
        }

        private void SetPVInverterToFullPower()
        {
            // init Modbus TCP client for inverter
            ModbusTCPClient inverter = new ModbusTCPClient();
            inverter.Connect(FroniusInverterBaseAddress, FroniusInverterModbusTCPPort);

            // read current inverter power limit (percentage)
            byte[] WMaxLimit = inverter.Read(
                FroniusInverterModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength).GetAwaiter().GetResult();

            int existingLimitPercent = ByteSwapper.ByteSwap(BitConverter.ToUInt16(WMaxLimit)) / 100;

            // go to the maximum grid export power limit with immediate effect without timeout
            ushort InverterPowerOutputPercent = (ushort)((GridExportPowerLimit / FroniusSymoMaxPower) * 100);
            inverter.WriteHoldingRegisters(
                FroniusInverterModbusUnitID,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                new ushort[] { (ushort)(InverterPowerOutputPercent * 100), 0, 0, 0, 1 }).GetAwaiter().GetResult();

            // check new setting
            WMaxLimit = inverter.Read(
                FroniusInverterModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength).GetAwaiter().GetResult();

            int newLimitPercent = ByteSwapper.ByteSwap(BitConverter.ToUInt16(WMaxLimit)) / 100;
            Log.Information($"PV Inverter Power set to {newLimitPercent}%");
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), NamespaceIndex);
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                IList<IReference> references = null;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                // create our top-level control folder
                FolderState controlFolder = CreateFolder(null, "Control", NamespaceIndex);
                controlFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, controlFolder.NodeId));
                controlFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddRootNotifier(controlFolder);

                // create our methods
                MethodState configureAssetMethod = CreateMethod(controlFolder, "IncrementChargingPhases", NamespaceIndex);
                configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(IncrementChargingPhases);

                MethodState getAssetsMethod = CreateMethod(controlFolder, "ToggleChargeNow", NamespaceIndex);
                getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(ToggleChargeNow);

                // create our top-level telemetry folder
                FolderState telemetryFolder = CreateFolder(null, "Telemetry", NamespaceIndex);
                telemetryFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, controlFolder.NodeId));
                telemetryFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddRootNotifier(telemetryFolder);

                // create our variables
                _uaVariables.Add("Temperature", CreateVariable(telemetryFolder, "Temperature", NamespaceIndex));
                _uaVariables.Add("CloudCover", CreateVariable(telemetryFolder, "CloudCover", NamespaceIndex, true));
                _uaVariables.Add("WindSpeed", CreateVariable(telemetryFolder, "WindSpeed", NamespaceIndex));
                _uaVariables.Add("CloudinessForecast", CreateVariable(telemetryFolder, "CloudinessForecast", NamespaceIndex, true));
                _uaVariables.Add("PVOutputPower", CreateVariable(telemetryFolder, "PVOutputPower", NamespaceIndex));
                _uaVariables.Add("PVOutputEnergyDay", CreateVariable(telemetryFolder, "PVOutputEnergyDay", NamespaceIndex));
                _uaVariables.Add("PVOutputEnergyYear", CreateVariable(telemetryFolder, "PVOutputEnergyYear", NamespaceIndex));
                _uaVariables.Add("PVOutputEnergyTotal", CreateVariable(telemetryFolder, "PVOutputEnergyTotal", NamespaceIndex));
                _uaVariables.Add("MeterEnergyPurchased", CreateVariable(telemetryFolder, "MeterEnergyPurchased", NamespaceIndex));
                _uaVariables.Add("MeterEnergySold", CreateVariable(telemetryFolder, "MeterEnergySold", NamespaceIndex));
                _uaVariables.Add("MeterEnergyConsumed", CreateVariable(telemetryFolder, "MeterEnergyConsumed", NamespaceIndex));
                _uaVariables.Add("EnergyCost", CreateVariable(telemetryFolder, "EnergyCost", NamespaceIndex));
                _uaVariables.Add("EnergyProfit", CreateVariable(telemetryFolder, "EnergyProfit", NamespaceIndex));
                _uaVariables.Add("CurrentPower", CreateVariable(telemetryFolder, "CurrentPower", NamespaceIndex));
                _uaVariables.Add("CurrentPowerConsumed", CreateVariable(telemetryFolder, "CurrentPowerConsumed", NamespaceIndex));
                _uaVariables.Add("EVChargingInProgress", CreateVariable(telemetryFolder, "EVChargingInProgress", NamespaceIndex));
                _uaVariables.Add("WallboxCurrent", CreateVariable(telemetryFolder, "WallboxCurrent", NamespaceIndex));
                _uaVariables.Add("ChargeNow", CreateVariable(telemetryFolder, "ChargeNow", NamespaceIndex));
                _uaVariables.Add("NumChargingPhases", CreateVariable(telemetryFolder, "NumChargingPhases", NamespaceIndex));

                // set inital values
                _uaVariables["ChargeNow"].Value = 0.0f;
                _uaVariables["NumChargingPhases"].Value = 2.0f;

                // add everyting to our nodeset
                AddPredefinedNode(SystemContext, controlFolder);
                AddPredefinedNode(SystemContext, telemetryFolder);
                AddReverseReferences(externalReferences);
            }
        }

        private PropertyState<Argument[]> CreateInputArguments(NodeState parent, string name, string description)
        {
            PropertyState<Argument[]> arguments = new PropertyState<Argument[]>(parent)
            {
                NodeId = new NodeId(parent.BrowseName.Name + "InArgs", NamespaceIndex),
                BrowseName = BrowseNames.InputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = new Argument[]
                {
                    new Argument { Name = name, Description = description, DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
                }
            };

            arguments.DisplayName = arguments.BrowseName.Name;

            return arguments;
        }

        private FolderState CreateFolder(NodeState parent, string name, ushort namespaceIndex)
        {
            FolderState folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            parent?.AddChild(folder);

            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string name, ushort namespaceIndex, bool isString = false)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = isString? DataTypes.String : DataTypes.Float
            };
            parent?.AddChild(variable);

            return variable;
        }

        private MethodState CreateMethod(NodeState parent, string name, ushort namespaceIndex)
        {
            MethodState method = new MethodState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };

            parent?.AddChild(method);

            return method;
        }

        private ServiceResult IncrementChargingPhases(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            // increase charing phases. They can be 1, 2 or 3. Most hybrids only charge on a single phase, most EVs with 2 or even 3 phases
            _uaVariables["NumChargingPhases"].Value = (float)_uaVariables["NumChargingPhases"].Value + 1.0f;

            if ((float)_uaVariables["NumChargingPhases"].Value > 3.0f)
            {
                _uaVariables["NumChargingPhases"].Value = 1.0f;
            }

            _uaVariables["NumChargingPhases"].Timestamp = DateTime.UtcNow;
            _uaVariables["NumChargingPhases"].ClearChangeMasks(SystemContext, false);

            return ServiceResult.Good;
        }

        private ServiceResult ToggleChargeNow(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if ((float)_uaVariables["ChargeNow"].Value == 0.0f)
            {
                _uaVariables["ChargeNow"].Value = 1.0f;
            }
            else
            {
                _uaVariables["ChargeNow"].Value = 0.0f;
            }

            _uaVariables["ChargeNow"].Timestamp = DateTime.UtcNow;
            _uaVariables["ChargeNow"].ClearChangeMasks(SystemContext, false);

            return ServiceResult.Good;
        }

        private void UpdateNodeValues(object state)
        {
            HttpClient webClient = new();

            try
            {
                // read the current weather data from web service
                string address = "https://api.openweathermap.org/data/2.5/weather?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b";
                HttpResponseMessage response = webClient.Send(new HttpRequestMessage(HttpMethod.Get, address));
                string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                WeatherInfo weather = JsonConvert.DeserializeObject<WeatherInfo>(responseString);
                if (weather != null)
                {
                    _uaVariables["Temperature"].Value = (float)weather.main.temp;
                    _uaVariables["WindSpeed"].Value = (float)weather.wind.speed;
                    _uaVariables["CloudCover"].Value = weather.weather[0].description;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting weather data failed!");
            }

            try
            {
                // read the current forecast data from web service
                string address = "https://api.openweathermap.org/data/2.5/forecast?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b";
                HttpResponseMessage response = webClient.Send(new HttpRequestMessage(HttpMethod.Get, address));
                string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Forecast forecast = JsonConvert.DeserializeObject<Forecast>(responseString);
                if (forecast != null && forecast.list != null && forecast.list.Count == 40)
                {
                    _uaVariables["CloudinessForecast"].Value = string.Empty;
                    for (int i = 0; i < 40; i++)
                    {
                        _uaVariables["CloudinessForecast"].Value = (string)_uaVariables["CloudinessForecast"].Value + "Cloudiness on " + forecast.list[i].dt_txt + ": " + forecast.list[i].clouds.all + "%\r\n";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting weather forecast failed!");
            }

            try
            {
                // read the current converter data from web service
                string address = "http://" + FroniusInverterBaseAddress + "/solar_api/v1/GetInverterRealtimeData.cgi?Scope=Device&DeviceID=1&DataCollection=CommonInverterData";
                HttpResponseMessage response = webClient.Send(new HttpRequestMessage(HttpMethod.Get, address));
                string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                DCACConverter converter = JsonConvert.DeserializeObject<DCACConverter>(responseString);
                if (converter != null)
                {
                    if (converter.Body.Data.PAC != null)
                    {
                        _uaVariables["PVOutputPower"].Value = (float)converter.Body.Data.PAC.Value;
                    }
                    if (converter.Body.Data.DAY_ENERGY != null)
                    {
                        _uaVariables["PVOutputEnergyDay"].Value = ((float)converter.Body.Data.DAY_ENERGY.Value) / 1000.0f;
                    }
                    if (converter.Body.Data.YEAR_ENERGY != null)
                    {
                        _uaVariables["PVOutputEnergyYear"].Value = ((float)converter.Body.Data.YEAR_ENERGY.Value) / 1000.0f;
                    }
                    if (converter.Body.Data.TOTAL_ENERGY != null)
                    {
                        _uaVariables["PVOutputEnergyTotal"].Value = ((float)converter.Body.Data.TOTAL_ENERGY.Value) / 1000.0f;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting converter data failed!");
            }

            webClient.Dispose();

            try
            {
                if (_sml != null)
                {
                    // read the current smart meter data
                    _uaVariables["MeterEnergyPurchased"].Value = (float)_sml.Meter.EnergyPurchased;
                    _uaVariables["MeterEnergySold"].Value = (float)_sml.Meter.EnergySold;
                    _uaVariables["CurrentPower"].Value = (float)_sml.Meter.CurrentPower;

                    _uaVariables["EnergyCost"].Value = (float)_uaVariables["MeterEnergyPurchased"].Value * KWhCost;
                    _uaVariables["EnergyProfit"].Value = (float)_uaVariables["MeterEnergySold"].Value * KWhProfit;

                    // calculate energy consumed from the other telemetry, if available
                    _uaVariables["MeterEnergyConsumed"].Value = 0.0f;
                    if (((float)_uaVariables["MeterEnergyPurchased"].Value != 0.0f)
                        && ((float)_uaVariables["MeterEnergySold"].Value != 0.0f)
                        && ((float)_uaVariables["PVOutputEnergyTotal"].Value != 0.0f))
                    {
                        _uaVariables["MeterEnergyConsumed"].Value = (float)_uaVariables["PVOutputEnergyTotal"].Value + (float)_sml.Meter.EnergyPurchased - (float)_sml.Meter.EnergySold;
                        _uaVariables["CurrentPowerConsumed"].Value = (float)_uaVariables["PVOutputPower"].Value + (float)_sml.Meter.CurrentPower;
                    }
                }
                else
                {
                    _uaVariables["MeterEnergyPurchased"].Value = 0.0f;
                    _uaVariables["MeterEnergySold"].Value = 0.0f;
                    _uaVariables["CurrentPower"].Value = 0.0f;
                    _uaVariables["EnergyCost"].Value = 0.0f;
                    _uaVariables["EnergyProfit"].Value = 0.0f;
                    _uaVariables["MeterEnergyConsumed"].Value = 0.0f;
                    _uaVariables["CurrentPowerConsumed"].Value = 0.0f;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting smart meter data failed!");
            }

            try
            {
                lock (_wallbox)
                {
                    // ramp up or down EV charging, based on surplus
                    bool chargingInProgress = IsEVChargingInProgress(_wallbox);
                    _uaVariables["EVChargingInProgress"].Value = chargingInProgress ? 1.0f : 0.0f;
                    if (chargingInProgress)
                    {
                        // read current current (in Amps)
                        ushort wallbeWallboxCurrentCurrentSetting = ByteSwapper.ByteSwap(BitConverter.ToUInt16(_wallbox.Read(
                            WallbeWallboxModbusUnitID,
                            ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                            WallbeWallboxCurrentCurrentSettingAddress,
                            1).GetAwaiter().GetResult()));
                        _uaVariables["WallboxCurrent"].Value = (float)wallbeWallboxCurrentCurrentSetting;

                        OptimizeEVCharging(_wallbox, (float)_uaVariables["CurrentPower"].Value);
                    }
                    else
                    {
                        _uaVariables["WallboxCurrent"].Value = 0.0f;

                        // check if we should start charging our EV with the surplus power, but we need at least 6A of current per charing phase
                        // or the user set the "charge now" flag via direct method
                        if ((((float)_uaVariables["CurrentPower"].Value / 230.0f) < ((float)_uaVariables["NumChargingPhases"].Value * -6.0f)) || ((float)_uaVariables["ChargeNow"].Value == 1.0f))
                        {
                            StartEVCharging(_wallbox);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EV charging control failed!");

                // reconnect
                lock (_wallbox)
                {
                    _wallbox.Disconnect();
                    _wallbox.Connect(WallbeWallboxBaseAddress, WallbeWallboxModbusTCPPort);
                }
            }

            foreach (BaseDataVariableState variable in _uaVariables.Values.ToList())
            {
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }

        private void StopEVCharging(ModbusTCPClient wallbox)
        {
            wallbox.WriteCoil(WallbeWallboxModbusUnitID, WallbeWallboxEnableChargingFlagAddress, false).GetAwaiter().GetResult();
        }

        private void StartEVCharging(ModbusTCPClient wallbox)
        {
            if (IsEVConnected(wallbox))
            {
                // check if we already set our charging enabled flag
                bool chargingEnabled = BitConverter.ToBoolean(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadCoilStatus,
                WallbeWallboxEnableChargingFlagAddress,
                1).GetAwaiter().GetResult());

                if (!chargingEnabled)
                {
                    // start charging
                    wallbox.WriteCoil(WallbeWallboxModbusUnitID, WallbeWallboxEnableChargingFlagAddress, true).GetAwaiter().GetResult();
                }
            }
        }

        private bool IsEVConnected(ModbusTCPClient wallbox)
        {
            // read EV status
            char EVStatus = (char)ByteSwapper.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxEVStatusAddress,
                1).GetAwaiter().GetResult()));

            switch (EVStatus)
            {
                case 'A': return false; // no vehicle connected
                case 'B': return true;  // vehicle connected, not charging
                case 'C': return true;  // vehicle connected, charging, no ventilation required
                case 'D': return true;  // vehicle connected, charging, ventilation required
                case 'E': return false; // _wallbox has no power
                case 'F': return false; // _wallbox not available
                default: return false;
            }
        }

        private void OptimizeEVCharging(ModbusTCPClient wallbox, double currentPower)
        {
            // we ramp up and down our charging current in 1 Amp increments/decrements
            // we increase our charging current until a) we have reached the maximum the _wallbox can handle or
            // b) we are just below consuming power from the grid (indicated by currentPower becoming positive), we are setting this to -200 Watts
            // we decrease our charging current when currentPower is above 0 (again indicated we are comsuming pwoer from the grid)

            // read maximum current rating
            ushort maxCurrent = ByteSwapper.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxMaxCurrentSettingAddress,
                1).GetAwaiter().GetResult()));

            // read current current (in Amps)
            ushort wallbeWallboxCurrentCurrentSetting = ByteSwapper.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                WallbeWallboxCurrentCurrentSettingAddress,
                1).GetAwaiter().GetResult()));

            // check if we have reached our limits (we define a 1KW "deadzone" from -500W to 500W where we keep things the way they are to cater for jitter)
            // "charge now" overwrites this and always charges, but as slowly as possible
            if ((wallbeWallboxCurrentCurrentSetting < maxCurrent) && (currentPower < -500) && ((float)_uaVariables["ChargeNow"].Value == 0.0f))
            {
                // increse desired current by 1 Amp
                wallbox.WriteHoldingRegisters(
                    WallbeWallboxModbusUnitID,
                    WallbeWallboxDesiredCurrentSettingAddress,
                    new ushort[] { (ushort)(wallbeWallboxCurrentCurrentSetting + 1) }).GetAwaiter().GetResult();
            }
            else if (currentPower > 500)
            {
                // need to decrease our charging current
                if (wallbeWallboxCurrentCurrentSetting == WallbeWallboxMinChargingCurrent)
                {
                    // we are already at the minimum, so stop, unless charge now is active
                    if ((float)_uaVariables["ChargeNow"].Value == 0.0f)
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
                        new ushort[] { (ushort)(wallbeWallboxCurrentCurrentSetting - 1) }).GetAwaiter().GetResult();
                }
            }
        }

        private bool IsEVChargingInProgress(ModbusTCPClient wallbox)
        {
            // read EV status
            char EVStatus = (char)ByteSwapper.ByteSwap(BitConverter.ToUInt16(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxEVStatusAddress,
                1).GetAwaiter().GetResult()));

            switch (EVStatus)
            {
                case 'A': return false; // no vehicle connected
                case 'B': return false; // vehicle connected, not charging
                case 'C': return true;  // vehicle connected, charging, no ventilation required
                case 'D': return true;  // vehicle connected, charging, ventilation required
                case 'E': return false; // _wallbox has no power
                case 'F': return false; // _wallbox not available
                default: return false;
            }
        }
    }
}
