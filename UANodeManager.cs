
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
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    public class UANodeManager : CustomNodeManager2
    {
        // addresses
        private const string LinuxUSBSerialPort = "/dev/ttyUSB0";
        private const string LinuxHouseAlarmSerialPort = "/dev/ttyUSB1";
        private const int HouseAlarmBaudRate = 9600;

        private const string FroniusInverterBaseAddress = "192.168.178.31";
        private const int FroniusInverterModbusTCPPort = 502;
        private const int FroniusInverterModbusUnitID = 1;

        private const string IDMHeatPumpBaseAddress = "192.168.178.23";
        private const int IDMHeatPumpModbusTCPPort = 502;
        private const int IDMHeatPumpModbusUnitID = 1;

        private const string WallbeWallboxBaseAddress = "192.168.178.21";
        private const int WallbeWallboxModbusTCPPort = 502;
        private const int WallbeWallboxModbusUnitID = 255;

        private const string ShellyEMBaseAddress = "192.168.1.20";
        // The Gen1 Shelly EM (type "SHEM") exposes both CT clamps in the
        // single /status response under the "emeters" array (indices 0 and 1).
        // Clamp 0 is around the Cottage feeder, clamp 1 is around the whole-house feeder
        // (which physically includes the cottage). The Loft load is therefore derived as
        // House - Cottage.
        private const int ShellyEMCottageChannelId = 0;
        private const int ShellyEMHouseChannelId = 1;

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
        private const int WallbeWallboxMaxChargingCurrent = 16; // the maximum current a single charging phase can deliver
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
        private uint _lastUsedId = 0;

        private SmartMessageLanguage _sml;

        private SIAProtocol _sia;

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
                "http://opcfoundation.org/UA/OpenWeatherMap/",  // 7
                "http://opcfoundation.org/UA/HouseAlarm/",      // 8
                "http://opcfoundation.org/UA/ShellyEM/"         // 9
            };

            NamespaceUris = namespaceUris;

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

            // start processing SIA-format house alarm messages from the serial port
            try
            {
                _sia = new SIAProtocol(LinuxHouseAlarmSerialPort, HouseAlarmBaudRate);
                _sia.ProcessStream();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Connecting to house alarm panel failed!");
            }
        }

        private void SetPVInverterToFullPower()
        {
            ModbusTCPClient inverter = new();

            try
            {
                inverter.Connect(FroniusInverterBaseAddress, FroniusInverterModbusTCPPort);

                // read current inverter power limit (percentage)
                byte[] WMaxLimit = inverter.Read(
                    FroniusInverterModbusUnitID,
                    ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                    SunSpecInverterModbusRegisterMapFloat.InverterBaseAddress + SunSpecInverterModbusRegisterMapFloat.WMaxLimPctOffset,
                    SunSpecInverterModbusRegisterMapFloat.WMaxLimPctLength).GetAwaiter().GetResult();

                int existingLimitPercent = BitConverter.ToUInt16(ByteSwapper.Swap(WMaxLimit)) / 100;

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

                int newLimitPercent = BitConverter.ToUInt16(ByteSwapper.Swap(WMaxLimit)) / 100;
                Log.Information($"PV InverterUpdate Power set to {newLimitPercent}%");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Set PV InverterUpdate Full Power failed!");
            }
            finally
            {
                if (inverter.IsConnected())
                {
                    inverter.Disconnect();
                }
            }
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
            }

            // set inital values
            _uaVariables["ChargeNow"].Value = 0.0f;
            _uaVariables["NumChargingPhases"].Value = 2.0f;
            _uaVariables["CurrentPower"].Value = 0.0f;
            _uaVariables["PVOutputEnergyTotal"].Value = 0.0f;
            _uaVariables["PVOutputPower"].Value = 0.0f;
            _uaVariables["HeatPumpCurrentPowerConsumption"].Value = 0.0f;
            _uaVariables["HeatPumpMode"].Value = 0.0f;

            _uaVariables["HouseAlarmActive"].Value = 0.0f;
            _uaVariables["HouseAlarmLastEventCode"].Value = string.Empty;
            _uaVariables["HouseAlarmLastEventDescription"].Value = string.Empty;
            _uaVariables["HouseAlarmLastEventZone"].Value = string.Empty;
            _uaVariables["HouseAlarmLastEventArea"].Value = string.Empty;
            _uaVariables["HouseAlarmLastEventTimestamp"].Value = string.Empty;
            _uaVariables["HouseAlarmAccountNumber"].Value = string.Empty;

            _uaVariables["ShellyEMCottagePower"].Value = 0.0f;
            _uaVariables["ShellyEMCottageCurrent"].Value = 0.0f;
            _uaVariables["ShellyEMCottageVoltage"].Value = 0.0f;
            _uaVariables["ShellyEMCottagePowerFactor"].Value = 0.0f;
            _uaVariables["ShellyEMCottageEnergyImported"].Value = 0.0f;
            _uaVariables["ShellyEMCottageEnergyExported"].Value = 0.0f;
            _uaVariables["ShellyEMHousePower"].Value = 0.0f;
            _uaVariables["ShellyEMHouseCurrent"].Value = 0.0f;
            _uaVariables["ShellyEMHouseVoltage"].Value = 0.0f;
            _uaVariables["ShellyEMHousePowerFactor"].Value = 0.0f;
            _uaVariables["ShellyEMHouseEnergyImported"].Value = 0.0f;
            _uaVariables["ShellyEMHouseEnergyExported"].Value = 0.0f;
            _uaVariables["ShellyEMLoftPower"].Value = 0.0f;
            _uaVariables["ShellyEMLoftEnergyImported"].Value = 0.0f;
            _uaVariables["ShellyEMLoftEnergyExported"].Value = 0.0f;

            // kick off our asset update background tasks
            _ = Task.Factory.StartNew(WeatherDataUpdate, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(InverterUpdate, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(HeatPumpUpdate, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(SmartMeterUpdate, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(EVChargingUpdate, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(HouseAlarmUpdate, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(ShellyEMUpdate, TaskCreationOptions.LongRunning);
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

            // create our top-level PV InverterUpdate folder
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

            // create our top-level House Alarm folder
            FolderState houseAlarmFolder = CreateFolder(null, "HouseAlarm", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"));
            houseAlarmFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, houseAlarmFolder.NodeId));
            houseAlarmFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(houseAlarmFolder);

            // create our top-level Shelly EM folder
            FolderState shellyEMFolder = CreateFolder(null, "ShellyEM", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/ShellyEM/"));
            shellyEMFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, shellyEMFolder.NodeId));
            shellyEMFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(shellyEMFolder);

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

            _uaVariables.Add("HouseAlarmActive", CreateVariable(houseAlarmFolder, "HouseAlarmActive", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/")));
            _uaVariables.Add("HouseAlarmLastEventCode", CreateVariable(houseAlarmFolder, "HouseAlarmLastEventCode", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"), true));
            _uaVariables.Add("HouseAlarmLastEventDescription", CreateVariable(houseAlarmFolder, "HouseAlarmLastEventDescription", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"), true));
            _uaVariables.Add("HouseAlarmLastEventZone", CreateVariable(houseAlarmFolder, "HouseAlarmLastEventZone", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"), true));
            _uaVariables.Add("HouseAlarmLastEventArea", CreateVariable(houseAlarmFolder, "HouseAlarmLastEventArea", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"), true));
            _uaVariables.Add("HouseAlarmLastEventTimestamp", CreateVariable(houseAlarmFolder, "HouseAlarmLastEventTimestamp", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"), true));
            _uaVariables.Add("HouseAlarmAccountNumber", CreateVariable(houseAlarmFolder, "HouseAlarmAccountNumber", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/HouseAlarm/"), true));

            ushort shellyEMNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/ShellyEM/");
            _uaVariables.Add("ShellyEMCottagePower", CreateVariable(shellyEMFolder, "ShellyEMCottagePower", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMCottageCurrent", CreateVariable(shellyEMFolder, "ShellyEMCottageCurrent", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMCottageVoltage", CreateVariable(shellyEMFolder, "ShellyEMCottageVoltage", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMCottagePowerFactor", CreateVariable(shellyEMFolder, "ShellyEMCottagePowerFactor", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMCottageEnergyImported", CreateVariable(shellyEMFolder, "ShellyEMCottageEnergyImported", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMCottageEnergyExported", CreateVariable(shellyEMFolder, "ShellyEMCottageEnergyExported", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMHousePower", CreateVariable(shellyEMFolder, "ShellyEMHousePower", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMHouseCurrent", CreateVariable(shellyEMFolder, "ShellyEMHouseCurrent", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMHouseVoltage", CreateVariable(shellyEMFolder, "ShellyEMHouseVoltage", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMHousePowerFactor", CreateVariable(shellyEMFolder, "ShellyEMHousePowerFactor", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMHouseEnergyImported", CreateVariable(shellyEMFolder, "ShellyEMHouseEnergyImported", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMHouseEnergyExported", CreateVariable(shellyEMFolder, "ShellyEMHouseEnergyExported", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMLoftPower", CreateVariable(shellyEMFolder, "ShellyEMLoftPower", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMLoftEnergyImported", CreateVariable(shellyEMFolder, "ShellyEMLoftEnergyImported", shellyEMNamespaceIndex));
            _uaVariables.Add("ShellyEMLoftEnergyExported", CreateVariable(shellyEMFolder, "ShellyEMLoftEnergyExported", shellyEMNamespaceIndex));

            // add everything to our nodeset
            AddPredefinedNode(SystemContext, controlFolder);
            AddPredefinedNode(SystemContext, inverterFolder);
            AddPredefinedNode(SystemContext, smartMeterFolder);
            AddPredefinedNode(SystemContext, wallboxFolder);
            AddPredefinedNode(SystemContext, heatPumpFolder);
            AddPredefinedNode(SystemContext, weatherFolder);
            AddPredefinedNode(SystemContext, houseAlarmFolder);
            AddPredefinedNode(SystemContext, shellyEMFolder);
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

        private void EVChargingUpdate()
        {
            Log.Information("Started Control Smart EV Charging Thread.");

            while (true)
            {
                Thread.Sleep(15000);

                ModbusTCPClient wallbox = new();

                try
                {
                    wallbox.Connect(WallbeWallboxBaseAddress, WallbeWallboxModbusTCPPort);

                    // ramp up or down EV charging, based on surplus
                    bool chargingInProgress = IsEVChargingInProgress(wallbox);
                    _uaVariables["EVChargingInProgress"].Value = chargingInProgress ? 1.0f : 0.0f;
                    _uaVariables["EVChargingInProgress"].Timestamp = DateTime.UtcNow;
                    _uaVariables["EVChargingInProgress"].ClearChangeMasks(SystemContext, false);

                    if (chargingInProgress)
                    {
                        // read current current (in Amps)
                        ushort wallbeWallboxCurrentCurrentSetting = BitConverter.ToUInt16(ByteSwapper.Swap(wallbox.Read(
                            WallbeWallboxModbusUnitID,
                            ModbusTCPClient.FunctionCode.ReadHoldingRegisters,
                            WallbeWallboxCurrentCurrentSettingAddress,
                            1).GetAwaiter().GetResult()));
                        _uaVariables["WallboxCurrent"].Value = (float)wallbeWallboxCurrentCurrentSetting;

                        OptimizeEVCharging(wallbox, (float)_uaVariables["CurrentPower"].Value);
                    }
                    else
                    {
                        _uaVariables["WallboxCurrent"].Value = 0.0f;

                        // check if we should start charging our EV with the surplus power, but we need at least 6A of current per charing phase
                        // or the user set the "charge now" flag via direct method
                        if ((((float)_uaVariables["CurrentPower"].Value / 230.0f) < ((float)_uaVariables["NumChargingPhases"].Value * -6.0f)) || ((float)_uaVariables["ChargeNow"].Value == 1.0f))
                        {
                            StartEVCharging(wallbox);
                        }
                    }

                    _uaVariables["WallboxCurrent"].Timestamp = DateTime.UtcNow;
                    _uaVariables["WallboxCurrent"].ClearChangeMasks(SystemContext, false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "EV charging control failed!");
                }
                finally
                {
                    if (wallbox.IsConnected())
                    {
                        wallbox.Disconnect();
                    }
                }
            }
        }

        private void HouseAlarmUpdate()
        {
            Log.Information("Started Read House Alarm Tags Thread.");

            while (true)
            {
                Thread.Sleep(5000);

                try
                {
                    if (_sia != null)
                    {
                        _uaVariables["HouseAlarmActive"].Value = _sia.Alarm.AlarmActive ? 1.0f : 0.0f;
                        _uaVariables["HouseAlarmLastEventCode"].Value = _sia.Alarm.LastEventCode ?? string.Empty;
                        _uaVariables["HouseAlarmLastEventDescription"].Value = _sia.Alarm.LastEventDescription ?? string.Empty;
                        _uaVariables["HouseAlarmLastEventZone"].Value = _sia.Alarm.LastEventZone ?? string.Empty;
                        _uaVariables["HouseAlarmLastEventArea"].Value = _sia.Alarm.LastEventArea ?? string.Empty;
                        _uaVariables["HouseAlarmLastEventTimestamp"].Value = _sia.Alarm.LastEventTimestamp == DateTime.MinValue
                            ? string.Empty
                            : _sia.Alarm.LastEventTimestamp.ToString("o");
                        _uaVariables["HouseAlarmAccountNumber"].Value = _sia.Alarm.AccountNumber ?? string.Empty;
                    }

                    DateTime now = DateTime.UtcNow;
                    _uaVariables["HouseAlarmActive"].Timestamp = now;
                    _uaVariables["HouseAlarmActive"].ClearChangeMasks(SystemContext, false);
                    _uaVariables["HouseAlarmLastEventCode"].Timestamp = now;
                    _uaVariables["HouseAlarmLastEventCode"].ClearChangeMasks(SystemContext, false);
                    _uaVariables["HouseAlarmLastEventDescription"].Timestamp = now;
                    _uaVariables["HouseAlarmLastEventDescription"].ClearChangeMasks(SystemContext, false);
                    _uaVariables["HouseAlarmLastEventZone"].Timestamp = now;
                    _uaVariables["HouseAlarmLastEventZone"].ClearChangeMasks(SystemContext, false);
                    _uaVariables["HouseAlarmLastEventArea"].Timestamp = now;
                    _uaVariables["HouseAlarmLastEventArea"].ClearChangeMasks(SystemContext, false);
                    _uaVariables["HouseAlarmLastEventTimestamp"].Timestamp = now;
                    _uaVariables["HouseAlarmLastEventTimestamp"].ClearChangeMasks(SystemContext, false);
                    _uaVariables["HouseAlarmAccountNumber"].Timestamp = now;
                    _uaVariables["HouseAlarmAccountNumber"].ClearChangeMasks(SystemContext, false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Updating house alarm data failed!");
                }
            }
        }

        private void ShellyEMUpdate()
        {
            Log.Information("Started Read Shelly EM Tags Thread.");

            while (true)
            {
                Thread.Sleep(15000);

                try
                {
                    using (HttpClient webClient = new())
                    {
                        webClient.Timeout = TimeSpan.FromSeconds(5);

                        // The Gen1 Shelly EM returns both clamps' instantaneous values
                        // and lifetime energy counters in a single /status response.
                        ShellyEMStatus status = ReadShellyEMStatus(webClient);
                        ShellyEMeter cottage = GetShellyEMeter(status, ShellyEMCottageChannelId);
                        ShellyEMeter house = GetShellyEMeter(status, ShellyEMHouseChannelId);

                        DateTime now = DateTime.UtcNow;

                        UpdateShellyChannelVariables("ShellyEMCottage", cottage, now);
                        UpdateShellyChannelVariables("ShellyEMHouse", house, now);

                        // Loft = whole-house clamp minus cottage clamp, because the house clamp
                        // physically includes the cottage feeder. Units match the per-clamp variables:
                        // power stays in Watts, energy is converted from Wh to kWh.
                        float loftPowerW = ToFloat(house?.power) - ToFloat(cottage?.power);
                        float loftImportedKWh = (ToFloat(house?.total) - ToFloat(cottage?.total)) / 1000.0f;
                        float loftExportedKWh = (ToFloat(house?.total_returned) - ToFloat(cottage?.total_returned)) / 1000.0f;

                        SetFloatVariable("ShellyEMLoftPower", loftPowerW, now);
                        SetFloatVariable("ShellyEMLoftEnergyImported", loftImportedKWh, now);
                        SetFloatVariable("ShellyEMLoftEnergyExported", loftExportedKWh, now);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Getting Shelly EM data failed!");
                }
            }
        }

        private static ShellyEMStatus ReadShellyEMStatus(HttpClient webClient)
        {
            string address = "http://" + ShellyEMBaseAddress + "/status";
            HttpResponseMessage response = webClient.Send(new HttpRequestMessage(HttpMethod.Get, address));
            response.EnsureSuccessStatusCode();
            string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonConvert.DeserializeObject<ShellyEMStatus>(responseString);
        }

        private static ShellyEMeter GetShellyEMeter(ShellyEMStatus status, int index)
        {
            if (status?.emeters == null || index < 0 || index >= status.emeters.Count)
            {
                return null;
            }

            return status.emeters[index];
        }

        private void UpdateShellyChannelVariables(string prefix, ShellyEMeter clamp, DateTime timestamp)
        {
            // expose raw values: power in Watts, current in Amps (derived, the Gen1 SHEM
            // does not report current directly), voltage in Volts, energy in kWh.
            float power = ToFloat(clamp?.power);
            float voltage = ToFloat(clamp?.voltage);
            float current = voltage > 0.0f ? Math.Abs(power) / voltage : 0.0f;

            SetFloatVariable(prefix + "Power", power, timestamp);
            SetFloatVariable(prefix + "Current", current, timestamp);
            SetFloatVariable(prefix + "Voltage", voltage, timestamp);
            SetFloatVariable(prefix + "PowerFactor", ToFloat(clamp?.pf), timestamp);
            SetFloatVariable(prefix + "EnergyImported", ToFloat(clamp?.total) / 1000.0f, timestamp);
            SetFloatVariable(prefix + "EnergyExported", ToFloat(clamp?.total_returned) / 1000.0f, timestamp);
        }

        private void SetFloatVariable(string name, float value, DateTime timestamp)
        {
            BaseDataVariableState variable = _uaVariables[name];
            variable.Value = value;
            variable.Timestamp = timestamp;
            variable.ClearChangeMasks(SystemContext, false);
        }

        private static float ToFloat(double? value)
        {
            return value.HasValue ? (float)value.Value : 0.0f;
        }

        private void SmartMeterUpdate()
        {
            Log.Information("Started Read Smart Meter Tags Thread.");

            while (true)
            {
                Thread.Sleep(15000);

                try
                {
                    if (_sml != null)
                    {
                        // read the current smart meter data
                        _uaVariables["MeterEnergyPurchased"].Value = (float)_sml.Meter.EnergyPurchased;
                        _uaVariables["MeterEnergySold"].Value = (float)_sml.Meter.EnergySold;
                        _uaVariables["CurrentPower"].Value = (float)_sml.Meter.CurrentPower;
                        _uaVariables["CurrentPowerConsumed"].Value = (float)_uaVariables["PVOutputPower"].Value + (float)_sml.Meter.CurrentPower;

                        _uaVariables["EnergyCost"].Value = (float)_uaVariables["MeterEnergyPurchased"].Value * KWhCost;
                        _uaVariables["EnergyProfit"].Value = (float)_uaVariables["MeterEnergySold"].Value * KWhProfit;

                        // calculate energy consumed from the other telemetry, if available
                        _uaVariables["MeterEnergyConsumed"].Value = 0.0f;
                        if (((float)_uaVariables["MeterEnergyPurchased"].Value != 0.0f)
                            && ((float)_uaVariables["MeterEnergySold"].Value != 0.0f)
                            && ((float)_uaVariables["PVOutputEnergyTotal"].Value != 0.0f))
                        {
                            _uaVariables["MeterEnergyConsumed"].Value = (float)_uaVariables["PVOutputEnergyTotal"].Value + (float)_sml.Meter.EnergyPurchased - (float)_sml.Meter.EnergySold;
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
        }

        private void InverterUpdate()
        {
            Log.Information("Started Read InverterUpdate Tags Thread.");

            while (true)
            {
                Thread.Sleep(15000);

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
        }

        private void WeatherDataUpdate()
        {
            Log.Information("Started Read Weather Data Thread.");

            while (true)
            {
                Thread.Sleep(15000);

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
        }

        private void HeatPumpUpdate()
        {
            Log.Information("Started Read Heat Pump Tags Thread.");

            while (true)
            {
                Thread.Sleep(15000);

                ModbusTCPClient heatPump = new();

                try
                {
                    // init Modbus TCP client for heat pump
                    heatPump.Connect(IDMHeatPumpBaseAddress, IDMHeatPumpModbusTCPPort);

                    // read the heat pump registers
                    _uaVariables["HeatPumpOutsideTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpOutsideTemp,
                        2).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpOutsideTemp"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpOutsideTemp"].ClearChangeMasks(SystemContext, false);

                    Thread.Sleep(250);

                    _uaVariables["HeatPumpHeatingWaterATemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpHeatingWaterATemp,
                        2).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpHeatingWaterATemp"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpHeatingWaterATemp"].ClearChangeMasks(SystemContext, false);

                    Thread.Sleep(250);

                    _uaVariables["HeatPumpHeatingWaterBTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpHeatingWaterBTemp,
                        2).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpHeatingWaterBTemp"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpHeatingWaterBTemp"].ClearChangeMasks(SystemContext, false);

                    Thread.Sleep(250);

                    _uaVariables["HeatPumpHeatingWaterCTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpHeatingWaterCTemp,
                        2).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpHeatingWaterCTemp"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpHeatingWaterCTemp"].ClearChangeMasks(SystemContext, false);

                    Thread.Sleep(250);

                    _uaVariables["HeatPumpTapWaterTemp"].Value = BitConverter.ToSingle(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpTapWaterTemp,
                        2).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpTapWaterTemp"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpTapWaterTemp"].ClearChangeMasks(SystemContext, false);

                    Thread.Sleep(250);

                    _uaVariables["HeatPumpCurrentPowerConsumption"].Value = BitConverter.ToSingle(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpCurrentPowerConsumption,
                        2).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpCurrentPowerConsumption"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpCurrentPowerConsumption"].ClearChangeMasks(SystemContext, false);

                    Thread.Sleep(250);

                    _uaVariables["HeatPumpMode"].Value = (float)BitConverter.ToUInt16(ByteSwapper.Swap(heatPump.Read(
                        IDMHeatPumpModbusUnitID,
                        ModbusTCPClient.FunctionCode.ReadInputRegisters,
                        IDMHeatPumpMode,
                        1).GetAwaiter().GetResult(), true));

                    _uaVariables["HeatPumpMode"].Timestamp = DateTime.UtcNow;
                    _uaVariables["HeatPumpMode"].ClearChangeMasks(SystemContext, false);

                    // set the surplus for our heatpump in kW
                    float surplusPowerKW = -((float)_uaVariables["CurrentPower"].Value / 1000.0f);
                    float heatPumpPowerRequirementKW = (float)_uaVariables["HeatPumpCurrentPowerConsumption"].Value;
                    //if (surplusPowerKW > heatPumpPowerRequirementKW)
                    {
                        byte[] buffer = new byte[4];
                        BitConverter.TryWriteBytes(buffer, surplusPowerKW);
                        ushort[] registers = new ushort[2];
                        registers[0] = (ushort)(buffer[1] << 8 | buffer[0]);
                        registers[1] = (ushort)(buffer[3] << 8 | buffer[2]);

                        heatPump.Connect(IDMHeatPumpBaseAddress, IDMHeatPumpModbusTCPPort);

                        heatPump.WriteHoldingRegisters(
                            IDMHeatPumpModbusUnitID,
                            IDMHeatPumpPVSurplus,
                            registers).GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Communicating with heat pump failed!");
                }
                finally
                {
                    if (heatPump.IsConnected())
                    {
                        heatPump.Disconnect();
                    }
                }

            }
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

                    Log.Information("EV charging started.");
                }
            }
        }

        private void StopEVCharging(ModbusTCPClient wallbox)
        {
            wallbox.WriteCoil(WallbeWallboxModbusUnitID, WallbeWallboxEnableChargingFlagAddress, false).GetAwaiter().GetResult();

            Log.Information("EV charging stopped.");
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
            // we increase our charging current until a) we have reached the maximum the wallbox can handle or
            // b) we are just below consuming power from the grid (indicated by currentPower becoming positive), we are setting this to -200 Watts
            // we decrease our charging current when currentPower is above 0 (again indicated we are comsuming power from the grid)
            // when "charge now" is active we bypass this entirely and charge at the maximum current

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

            // never exceed what the wallbox itself reports it can deliver
            ushort upperCurrentLimit = Math.Min(maxCurrent, (ushort)WallbeWallboxMaxChargingCurrent);

            // "charge now" overrides the surplus-based optimization and always charges as fast as possible,
            // so we go straight to the maximum current instead of ramping in 1 Amp steps
            if ((float)_uaVariables["ChargeNow"].Value == 1.0f)
            {
                if (wallbeWallboxCurrentCurrentSetting != upperCurrentLimit)
                {
                    wallbox.WriteHoldingRegisters(
                        WallbeWallboxModbusUnitID,
                        WallbeWallboxDesiredCurrentSettingAddress,
                        new ushort[] { upperCurrentLimit }).GetAwaiter().GetResult();

                    Log.Information($"Charge now active, EV charging current set to {upperCurrentLimit}A.");
                }

                return;
            }

            // check if we have reached our limits (we define a 1KW "deadzone" from -500W to 500W where we keep things the way they are to cater for jitter)
            if ((wallbeWallboxCurrentCurrentSetting < upperCurrentLimit) && (currentPower < -500))
            {
                // increase desired current by 1 Amp
                wallbox.WriteHoldingRegisters(
                    WallbeWallboxModbusUnitID,
                    WallbeWallboxDesiredCurrentSettingAddress,
                    new ushort[] { (ushort)(wallbeWallboxCurrentCurrentSetting + 1) }).GetAwaiter().GetResult();
            }
            else if (currentPower > 500)
            {
                // need to decrease our charging current
                if (wallbeWallboxCurrentCurrentSetting <= WallbeWallboxMinChargingCurrent)
                {
                    // we are already at the minimum, so stop
                    StopEVCharging(wallbox);
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
                case 'E': return false; // wallbox has no power
                case 'F': return false; // wallbox not available
                default: return false;
            }
        }
    }
}
