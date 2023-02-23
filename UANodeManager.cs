
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
        // addresses
        private const string LinuxUSBSerialPort = "/dev/ttyUSB0";

        private const string FroniusInverterBaseAddress = "192.168.178.31";
        private const int FroniusInverterModbusTCPPort = 502;
        private const int FroniusInverterModbusUnitID = 1;

        private const string IDMHeatPumpBaseAddress = "192.168.178.23";
        private const int IDMHeatPumpModbusTCPPort = 502;
        private const int IDMHeatPumpModbusUnitID = 1;

        private const string WallbeWallboxBaseAddress = "192.168.178.21";
        private const int WallbeWallboxModbusTCPPort = 502;
        private const int WallbeWallboxModbusUnitID = 255;

        // tags
        private const float FroniusSymoMaxPower = 8200f;

        private const int IDMHeatPumpPVSurplus = 74;
        private const int IDMHeatPumpCurrentPowerConsumption = 4122;
        private const int IDMHeatPumpOutsideTemp = 1000;
        private const int IDMHeatPumpTapWaterTemp = 1014;
        private const int IDMHeatPumpMode = 1090;
        private const int IDMHeatPumpHeatingWaterATemp = 1350;
        private const int IDMHeatPumpHeatingWaterBTemp = 1352;
        private const int IDMHeatPumpHeatingWaterCTemp = 1354;

        private const int WallbeWallboxMinChargingCurrent = 6; // EVs don't charge with less than 6 Amps
        private const int WallbeWallboxEVStatusAddress = 100;
        private const int WallbeWallboxMaxCurrentSettingAddress = 101;
        private const int WallbeWallboxCurrentCurrentSettingAddress = 300;
        private const int WallbeWallboxDesiredCurrentSettingAddress = 528;
        private const int WallbeWallboxEnableChargingFlagAddress = 400;

        // constants
        private const float KWhCost = 0.48671f;
        private const float KWhProfit = 0.0944f;
        private const float GridExportPowerLimit = 7000f;

        // variables
        private long _lastUsedId = 0;

        private SmartMessageLanguage _sml;

        private ModbusTCPClient _wallbox = new ModbusTCPClient();

        private ModbusTCPClient _heatPump = new ModbusTCPClient();

        private ModbusTCPClient _inverter = new ModbusTCPClient();

        private Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private Timer _timerWeather;
        private Timer _timerInverter;
        private Timer _timerHeatPump;
        private Timer _timerSmartMeter;
        private Timer _timerSurplus;
        private Timer _timerEVCharging;

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

            // init Modbus TCP client for wallbox
            _wallbox.Connect(WallbeWallboxBaseAddress, WallbeWallboxModbusTCPPort);

            // init Modbus TCP client for inverter
            _inverter.Connect(FroniusInverterBaseAddress, FroniusInverterModbusTCPPort);

            // init Modbus TCP client for heat pump
            _heatPump.Connect(IDMHeatPumpBaseAddress, IDMHeatPumpModbusTCPPort);

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
        }

        private void SetPVInverterToFullPower()
        {
            // read current inverter power limit (percentage)
            byte[] WMaxLimit = _inverter.Read(
                FroniusInverterModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength).GetAwaiter().GetResult();

            int existingLimitPercent = BitConverter.ToUInt16(ByteSwapper.Swap(WMaxLimit)) / 100;

            // go to the maximum grid export power limit with immediate effect without timeout
            ushort InverterPowerOutputPercent = (ushort)((GridExportPowerLimit / FroniusSymoMaxPower) * 100);
            _inverter.WriteHoldingRegisters(
                FroniusInverterModbusUnitID,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                new ushort[] { (ushort)(InverterPowerOutputPercent * 100), 0, 0, 0, 1 }).GetAwaiter().GetResult();

            // check new setting
            WMaxLimit = _inverter.Read(
                FroniusInverterModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength).GetAwaiter().GetResult();

            int newLimitPercent = BitConverter.ToUInt16(ByteSwapper.Swap(WMaxLimit)) / 100;
            Log.Information($"PV Inverter Power set to {newLimitPercent}%");
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeHEMS/"));
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

                CreateUANodes(references);

                AddReverseReferences(externalReferences);

                // set inital values
                _uaVariables["ChargeNow"].Value = 0.0f;
                _uaVariables["NumChargingPhases"].Value = 2.0f;
                _uaVariables["CurrentPower"].Value = 0.0f;
                _uaVariables["PVOutputEnergyTotal"].Value = 0.0f;
                _uaVariables["PVOutputPower"].Value = 0.0f;
                _uaVariables["HeatPumpCurrentPowerConsumption"].Value = 0.0f;
                _uaVariables["HeatPumpMode"].Value = 0.0f;
            }

            // kick off our timers
            _timerWeather = new Timer(ReadWeatherData, null, 15000, 15000);
            _timerInverter = new Timer(ReadInverterTags, null, 5000, 5000);
            _timerHeatPump = new Timer(ReadHeatPumpTags, null, 5000, 5000);
            _timerSmartMeter = new Timer(ReadSmartMeterTags, null, 5000, 5000);
            _timerSurplus = new Timer(ControlSurplusEnergyForHeatPump, null, 5000, 5000);
            _timerEVCharging = new Timer(ControlSmartEVCharging, null, 5000, 5000);
        }

        private void CreateUANodes(IList<IReference> references)
        {
            // create our top-level control folder
            FolderState controlFolder = CreateFolder(null, "Control", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeHEMS/"));
            controlFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, controlFolder.NodeId));
            controlFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(controlFolder);

            // create our methods
            MethodState configureAssetMethod = CreateMethod(controlFolder, "IncrementChargingPhases", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeHEMS/"));
            configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(IncrementChargingPhases);

            MethodState getAssetsMethod = CreateMethod(controlFolder, "ToggleChargeNow", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeHEMS/"));
            getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(ToggleChargeNow);

            // create our top-level PV Inverter folder
            FolderState inverterFolder = CreateFolder(null, "PVInverter", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SunSpecInverter/"));
            inverterFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, inverterFolder.NodeId));
            inverterFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(inverterFolder);

            // create our top-level Smart Meter folder
            FolderState smartMeterFolder = CreateFolder(null, "SmartMeter", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/"));
            smartMeterFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, smartMeterFolder.NodeId));
            smartMeterFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(smartMeterFolder);

            // create our top-level Wallbox folder
            FolderState wallboxFolder = CreateFolder(null, "Wallbox", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Wallbox/"));
            wallboxFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, wallboxFolder.NodeId));
            wallboxFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(wallboxFolder);

            // create our top-level Heat Pump folder
            FolderState heatPumpFolder = CreateFolder(null, "HeatPump", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/"));
            heatPumpFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, heatPumpFolder.NodeId));
            heatPumpFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(heatPumpFolder);

            // create our top-level Weather folder
            FolderState weatherFolder = CreateFolder(null, "Weather", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenWeatherMap/"));
            weatherFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, weatherFolder.NodeId));
            weatherFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(weatherFolder);

            // create our variables
            _uaVariables.Add("PVOutputPower", CreateVariable(inverterFolder, "PVOutputPower", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SunSpecInverter/")));
            _uaVariables.Add("PVOutputEnergyDay", CreateVariable(inverterFolder, "PVOutputEnergyDay", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SunSpecInverter/")));
            _uaVariables.Add("PVOutputEnergyYear", CreateVariable(inverterFolder, "PVOutputEnergyYear", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SunSpecInverter/")));
            _uaVariables.Add("PVOutputEnergyTotal", CreateVariable(inverterFolder, "PVOutputEnergyTotal", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SunSpecInverter/")));

            _uaVariables.Add("MeterEnergyPurchased", CreateVariable(smartMeterFolder, "MeterEnergyPurchased", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));
            _uaVariables.Add("MeterEnergySold", CreateVariable(smartMeterFolder, "MeterEnergySold", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));
            _uaVariables.Add("MeterEnergyConsumed", CreateVariable(smartMeterFolder, "MeterEnergyConsumed", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));
            _uaVariables.Add("EnergyCost", CreateVariable(smartMeterFolder, "EnergyCost", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));
            _uaVariables.Add("EnergyProfit", CreateVariable(smartMeterFolder, "EnergyProfit", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));
            _uaVariables.Add("CurrentPower", CreateVariable(smartMeterFolder, "CurrentPower", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));
            _uaVariables.Add("CurrentPowerConsumed", CreateVariable(smartMeterFolder, "CurrentPowerConsumed", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/SmartMeter/")));

            _uaVariables.Add("EVChargingInProgress", CreateVariable(wallboxFolder, "EVChargingInProgress", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Wallbox/")));
            _uaVariables.Add("WallboxCurrent", CreateVariable(wallboxFolder, "WallboxCurrent", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Wallbox/")));
            _uaVariables.Add("ChargeNow", CreateVariable(wallboxFolder, "ChargeNow", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Wallbox/")));
            _uaVariables.Add("NumChargingPhases", CreateVariable(wallboxFolder, "NumChargingPhases", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Wallbox/")));

            _uaVariables.Add("HeatPumpCurrentPowerConsumption", CreateVariable(heatPumpFolder, "HeatPumpCurrentPowerConsumption", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));
            _uaVariables.Add("HeatPumpOutsideTemp", CreateVariable(heatPumpFolder, "HeatPumpOutsideTemp", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));
            _uaVariables.Add("HeatPumpTapWaterTemp", CreateVariable(heatPumpFolder, "HeatPumpTapWaterTemp", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));
            _uaVariables.Add("HeatPumpHeatingWaterATemp", CreateVariable(heatPumpFolder, "HeatPumpHeatingWaterATemp", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));
            _uaVariables.Add("HeatPumpHeatingWaterBTemp", CreateVariable(heatPumpFolder, "HeatPumpHeatingWaterBTemp", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));
            _uaVariables.Add("HeatPumpHeatingWaterCTemp", CreateVariable(heatPumpFolder, "HeatPumpHeatingWaterCTemp", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));
            _uaVariables.Add("HeatPumpMode", CreateVariable(heatPumpFolder, "HeatPumpMode", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Heatpump/")));

            _uaVariables.Add("Temperature", CreateVariable(weatherFolder, "Temperature", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenWeatherMap/")));
            _uaVariables.Add("CloudCover", CreateVariable(weatherFolder, "CloudCover", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenWeatherMap/"), true));
            _uaVariables.Add("WindSpeed", CreateVariable(weatherFolder, "WindSpeed", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenWeatherMap/")));
            _uaVariables.Add("CloudinessForecast", CreateVariable(weatherFolder, "CloudinessForecast", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenWeatherMap/"), true));

            // add everyting to our nodeset
            AddPredefinedNode(SystemContext, controlFolder);
            AddPredefinedNode(SystemContext, inverterFolder);
            AddPredefinedNode(SystemContext, smartMeterFolder);
            AddPredefinedNode(SystemContext, wallboxFolder);
            AddPredefinedNode(SystemContext, heatPumpFolder);
            AddPredefinedNode(SystemContext, weatherFolder);
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

        private void ControlSmartEVCharging(object state)
        {
            try
            {
                lock (_wallbox)
                {
                    // ramp up or down EV charging, based on surplus
                    bool chargingInProgress = IsEVChargingInProgress(_wallbox);
                    _uaVariables["EVChargingInProgress"].Value = chargingInProgress ? 1.0f : 0.0f;
                    _uaVariables["EVChargingInProgress"].Timestamp = DateTime.UtcNow;
                    _uaVariables["EVChargingInProgress"].ClearChangeMasks(SystemContext, false);

                    if (chargingInProgress)
                    {
                        // read current current (in Amps)
                        ushort wallbeWallboxCurrentCurrentSetting = BitConverter.ToUInt16(ByteSwapper.Swap(_wallbox.Read(
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

                    _uaVariables["WallboxCurrent"].Timestamp = DateTime.UtcNow;
                    _uaVariables["WallboxCurrent"].ClearChangeMasks(SystemContext, false);
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
        }

        private void ReadSmartMeterTags(object state)
        {
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

                _uaVariables["MeterEnergyPurchased"].Timestamp = DateTime.UtcNow;
                _uaVariables["MeterEnergyPurchased"].ClearChangeMasks(SystemContext, false);
                _uaVariables["MeterEnergySold"].Timestamp = DateTime.UtcNow;
                _uaVariables["MeterEnergySold"].ClearChangeMasks(SystemContext, false);
                _uaVariables["CurrentPower"].Timestamp = DateTime.UtcNow;
                _uaVariables["CurrentPower"].ClearChangeMasks(SystemContext, false);
                _uaVariables["EnergyCost"].Timestamp = DateTime.UtcNow;
                _uaVariables["EnergyCost"].ClearChangeMasks(SystemContext, false);
                _uaVariables["EnergyProfit"].Timestamp = DateTime.UtcNow;
                _uaVariables["EnergyProfit"].ClearChangeMasks(SystemContext, false);
                _uaVariables["MeterEnergyConsumed"].Timestamp = DateTime.UtcNow;
                _uaVariables["MeterEnergyConsumed"].ClearChangeMasks(SystemContext, false);
                _uaVariables["CurrentPowerConsumed"].Timestamp = DateTime.UtcNow;
                _uaVariables["CurrentPowerConsumed"].ClearChangeMasks(SystemContext, false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting smart meter data failed!");
            }
        }

        private void ControlSurplusEnergyForHeatPump(object state)
        {
            try
            {
                // set the surplus for our heatpump in kW
                float surplusPowerKW = -((float)_uaVariables["CurrentPower"].Value / 1000.0f);
                float heatPumpPowerRequirementKW = (float)_uaVariables["HeatPumpCurrentPowerConsumption"].Value;
                if (surplusPowerKW > heatPumpPowerRequirementKW)
                {
                    byte[] buffer = new byte[4];
                    BitConverter.TryWriteBytes(buffer, surplusPowerKW);
                    ushort[] registers = new ushort[2];
                    registers[0] = (ushort)(buffer[1] << 8 | buffer[0]);
                    registers[1] = (ushort)(buffer[3] << 8 | buffer[2]);

                    _heatPump.WriteHoldingRegisters(
                        IDMHeatPumpModbusUnitID,
                        IDMHeatPumpPVSurplus,
                        registers).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Surplus energy control for heat pump failed!");
            }
        }

        private void ReadInverterTags(object state)
        {
            try
            {
                using (HttpClient webClient = new())
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
                            _uaVariables["PVOutputPower"].Timestamp = DateTime.UtcNow;
                            _uaVariables["PVOutputPower"].ClearChangeMasks(SystemContext, false);
                        }
                        if (converter.Body.Data.DAY_ENERGY != null)
                        {
                            _uaVariables["PVOutputEnergyDay"].Value = ((float)converter.Body.Data.DAY_ENERGY.Value) / 1000.0f;
                            _uaVariables["PVOutputEnergyDay"].Timestamp = DateTime.UtcNow;
                            _uaVariables["PVOutputEnergyDay"].ClearChangeMasks(SystemContext, false);
                        }
                        if (converter.Body.Data.YEAR_ENERGY != null)
                        {
                            _uaVariables["PVOutputEnergyYear"].Value = ((float)converter.Body.Data.YEAR_ENERGY.Value) / 1000.0f;
                            _uaVariables["PVOutputEnergyYear"].Timestamp = DateTime.UtcNow;
                            _uaVariables["PVOutputEnergyYear"].ClearChangeMasks(SystemContext, false);
                        }
                        if (converter.Body.Data.TOTAL_ENERGY != null)
                        {
                            _uaVariables["PVOutputEnergyTotal"].Value = ((float)converter.Body.Data.TOTAL_ENERGY.Value) / 1000.0f;
                            _uaVariables["PVOutputEnergyTotal"].Timestamp = DateTime.UtcNow;
                            _uaVariables["PVOutputEnergyTotal"].ClearChangeMasks(SystemContext, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting converter data failed!");
            }
        }

        private void ReadWeatherData(object state)
        {
            try
            {
                using (HttpClient webClient = new())
                {
                    // read the current weather data from web service
                    string address = "https://api.openweathermap.org/data/2.5/weather?q=Munich,de&units=metric&appid=2898258e654f7f321ef3589c4fa58a9b";
                    HttpResponseMessage response = webClient.Send(new HttpRequestMessage(HttpMethod.Get, address));
                    string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    WeatherInfo weather = JsonConvert.DeserializeObject<WeatherInfo>(responseString);
                    if (weather != null)
                    {
                        _uaVariables["Temperature"].Value = (float)weather.main.temp;
                        _uaVariables["Temperature"].Timestamp = DateTime.UtcNow;
                        _uaVariables["Temperature"].ClearChangeMasks(SystemContext, false);

                        _uaVariables["WindSpeed"].Value = (float)weather.wind.speed;
                        _uaVariables["WindSpeed"].Timestamp = DateTime.UtcNow;
                        _uaVariables["WindSpeed"].ClearChangeMasks(SystemContext, false);

                        _uaVariables["CloudCover"].Value = weather.weather[0].description;
                        _uaVariables["CloudCover"].Timestamp = DateTime.UtcNow;
                        _uaVariables["CloudCover"].ClearChangeMasks(SystemContext, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting weather data failed!");
            }

            try
            {
                using (HttpClient webClient = new())
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

                        _uaVariables["CloudinessForecast"].Timestamp = DateTime.UtcNow;
                        _uaVariables["CloudinessForecast"].ClearChangeMasks(SystemContext, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Getting weather forecast failed!");
            }
        }

        private void ReadHeatPumpTags(object state)
        {
            try
            {
                // read the heat pump registers
                _uaVariables["HeatPumpOutsideTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(_heatPump.Read(
                    IDMHeatPumpModbusUnitID,
                    ModbusTCPClient.FunctionCode.ReadInputRegisters,
                    IDMHeatPumpOutsideTemp,
                    2).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpOutsideTemp"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpOutsideTemp"].ClearChangeMasks(SystemContext, false);

                _uaVariables["HeatPumpHeatingWaterATemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(_heatPump.Read(
                    IDMHeatPumpModbusUnitID,
                    ModbusTCPClient.FunctionCode.ReadInputRegisters,
                    IDMHeatPumpHeatingWaterATemp,
                    2).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpHeatingWaterATemp"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpHeatingWaterATemp"].ClearChangeMasks(SystemContext, false);

                _uaVariables["HeatPumpHeatingWaterBTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(_heatPump.Read(
                    IDMHeatPumpModbusUnitID,
                    ModbusTCPClient.FunctionCode.ReadInputRegisters,
                    IDMHeatPumpHeatingWaterBTemp,
                    2).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpHeatingWaterBTemp"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpHeatingWaterBTemp"].ClearChangeMasks(SystemContext, false);

                _uaVariables["HeatPumpHeatingWaterCTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(_heatPump.Read(
                   IDMHeatPumpModbusUnitID,
                   ModbusTCPClient.FunctionCode.ReadInputRegisters,
                   IDMHeatPumpHeatingWaterCTemp,
                   2).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpHeatingWaterCTemp"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpHeatingWaterCTemp"].ClearChangeMasks(SystemContext, false);

                _uaVariables["HeatPumpTapWaterTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(_heatPump.Read(
                   IDMHeatPumpModbusUnitID,
                   ModbusTCPClient.FunctionCode.ReadInputRegisters,
                   IDMHeatPumpTapWaterTemp,
                   2).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpTapWaterTemp"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpTapWaterTemp"].ClearChangeMasks(SystemContext, false);

                _uaVariables["HeatPumpCurrentPowerConsumption"].Value = BitConverter.ToSingle(ByteSwapper.Swap(_heatPump.Read(
                    IDMHeatPumpModbusUnitID,
                    ModbusTCPClient.FunctionCode.ReadInputRegisters,
                    IDMHeatPumpCurrentPowerConsumption,
                    2).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpCurrentPowerConsumption"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpCurrentPowerConsumption"].ClearChangeMasks(SystemContext, false);

                _uaVariables["HeatPumpMode"].Value = (float)BitConverter.ToUInt16(ByteSwapper.Swap(_heatPump.Read(
                   IDMHeatPumpModbusUnitID,
                   ModbusTCPClient.FunctionCode.ReadInputRegisters,
                   IDMHeatPumpMode,
                   1).GetAwaiter().GetResult(), true));

                _uaVariables["HeatPumpMode"].Timestamp = DateTime.UtcNow;
                _uaVariables["HeatPumpMode"].ClearChangeMasks(SystemContext, false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Communicating with heat pump failed!");

                // reconnect
                lock (_heatPump)
                {
                    _heatPump.Disconnect();
                    _heatPump.Connect(IDMHeatPumpBaseAddress, IDMHeatPumpModbusTCPPort);
                }
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
            char EVStatus = (char)BitConverter.ToUInt16(ByteSwapper.Swap(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxEVStatusAddress, 1).GetAwaiter().GetResult()));

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
            ushort maxCurrent = BitConverter.ToUInt16(ByteSwapper.Swap(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadInputRegisters,
                WallbeWallboxMaxCurrentSettingAddress,
                1).GetAwaiter().GetResult()));

            // read current current (in Amps)
            ushort wallbeWallboxCurrentCurrentSetting = BitConverter.ToUInt16(ByteSwapper.Swap(wallbox.Read(
                WallbeWallboxModbusUnitID,
                ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                WallbeWallboxCurrentCurrentSettingAddress, 1).GetAwaiter().GetResult()));

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
            char EVStatus = (char)BitConverter.ToUInt16(ByteSwapper.Swap(wallbox.Read(
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
