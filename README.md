# Home Energy Management System with an OPC UA Server Interface

This Home Energy Management System (HEMS) is an OPC UA Server capturing energy telemetry data from a photovoltaic system, a smart energy meter and sub-metering clamps, monitoring a house alarm panel, and controlling an EV wallbox and heat pump with the surplus energy available from local production. This is running in Docker containers (pre-built Intel x64 and ARMx64 containers are available).

## Running UA-EdgeHEMS

Simply run the pre-built containers on a Docker-enabled computer, e.g. for RaspberryPi4:

`docker run -itd -p 4840:4840 --device=/dev/ttyUSB0 --device=/dev/ttyUSB1 --restart=always ghcr.io/digitaltwinconsortium/ua-edgehems-arm64:latest`

This will expose the OPC UA server on the default OPC UA port of 4840 and also make any USB serial devices available in the container. Two serial devices are used:

* `/dev/ttyUSB0` - the smart meter optical reader
* `/dev/ttyUSB1` - the house alarm panel serial connection

The application targets .NET 10 and is built from the included `Dockerfile`.

### Optional environment variables

| Variable | Purpose | Default |
| --- | --- | --- |
| `LOG_FILE_PATH` | Path of the rolling Serilog log file. | Application directory |
| `APP_NAME` | OPC UA application name and hostname used in the endpoint URL. | Machine name |

### Device addresses

Device IP addresses, the OpenWeatherMap location, the electricity tariff (cost and feed-in profit per kWh) and the grid export power limit are currently compile-time constants at the top of `UANodeManager.cs`. Adjust them there to match your installation before building.

## Telemetry Data Captured

All telemetry is polled on background threads (every 15 seconds, except the house alarm at 5 seconds) and exposed via an OPC UA server interface.

1. **Weather data** from [www.openweathermap.org](http://www.openweathermap.org), as the weather impacts PV performance. Both current conditions and the cloudiness forecast are read.
2. **PV inverter telemetry** read from a Fronius inverter via the [Fronius Solar API](https://www.fronius.com/en/photovoltaics/products/all-products/system-monitoring/open-interfaces/fronius-solar-api-json-) (JSON over HTTP). A [SunSpec](https://sunspec.org)-compliant version using ModbusTCP is in PoC stage; it is already used at startup to set the inverter's active power limit to the configured grid export limit.
3. **Smart meter telemetry** leveraging its [IEC 62056-21 standard](https://en.wikipedia.org/wiki/IEC_62056) optical interface using [a USB reader](https://shop.weidmann-elektronik.de/index.php?page=product&info=24). IEC 62056-21 uses the Smart Message Language - [SML](https://wiki.wireshark.org/SML). Energy cost and feed-in profit are calculated from the meter readings.
4. **Wallbe wallbox** control via ModbusTCP, using surplus energy from the PV to charge an Electric Vehicle.
5. **IDM heat pump** integration via ModbusTCP, including reading temperatures and writing the available PV surplus.
6. **House alarm panel** monitoring, reading events in [SIA](https://en.wikipedia.org/wiki/Security_Industry_Association) DC-03 format from a serial port.
7. **Shelly EM sub-metering**, reading two CT clamps from a Gen1 Shelly EM (device type `SHEM`) over its REST API.

## OPC UA Address Space

The server exposes the following folders, each in its own namespace:

### `PVInverter`
`PVOutputPower` (W), `PVOutputEnergyDay`, `PVOutputEnergyYear`, `PVOutputEnergyTotal` (all kWh).

### `SmartMeter`
`MeterEnergyPurchased`, `MeterEnergySold`, `MeterEnergyConsumed` (kWh), `EnergyCost`, `EnergyProfit`, `CurrentPower` (W, negative means surplus is being exported), `CurrentPowerConsumed` (W).

### `Wallbox`
`EVChargingInProgress`, `WallboxCurrent` (A), `ChargeNow`, `NumChargingPhases`.

### `HeatPump`
`HeatPumpCurrentPowerConsumption`, `HeatPumpOutsideTemp`, `HeatPumpTapWaterTemp`, `HeatPumpHeatingWaterATemp`, `HeatPumpHeatingWaterBTemp`, `HeatPumpHeatingWaterCTemp`, `HeatPumpMode`.

### `Weather`
`Temperature` (°C), `WindSpeed`, `CloudCover`, `CloudinessForecast`.

### `HouseAlarm`
`HouseAlarmActive` plus the details of the most recent event: `HouseAlarmLastEventCode`, `HouseAlarmLastEventDescription`, `HouseAlarmLastEventZone`, `HouseAlarmLastEventArea`, `HouseAlarmLastEventTimestamp` and `HouseAlarmAccountNumber`.

### `ShellyEM`
Two physical clamps and one derived load. Clamp 0 measures the cottage feeder, clamp 1 measures the whole-house feeder (which physically includes the cottage), so the loft consumption is derived as house minus cottage:

* `ShellyEMCottagePower` (W), `ShellyEMCottageCurrent` (A), `ShellyEMCottageVoltage` (V), `ShellyEMCottagePowerFactor`, `ShellyEMCottageEnergyImported`, `ShellyEMCottageEnergyExported` (kWh)
* `ShellyEMHousePower`, `ShellyEMHouseCurrent`, `ShellyEMHouseVoltage`, `ShellyEMHousePowerFactor`, `ShellyEMHouseEnergyImported`, `ShellyEMHouseEnergyExported`
* `ShellyEMLoftPower` (W), `ShellyEMLoftEnergyImported`, `ShellyEMLoftEnergyExported` (kWh)

The Gen1 Shelly EM does not report current directly, so the current values are derived from power and voltage.

### `Control`
Two OPC UA methods are available:

* `ToggleChargeNow` - toggles the `ChargeNow` flag. When active, the wallbox bypasses surplus-based optimization and charges at the maximum current (16 A, or whatever lower maximum the wallbox reports).
* `IncrementChargingPhases` - cycles the number of charging phases used in the surplus calculation.

## Control Logic

### EV charging
The wallbox is controlled over ModbusTCP. Charging starts automatically when the exported surplus is large enough to supply at least 6 A per charging phase. While charging, the current is ramped in 1 A steps: up when more than 500 W is being exported, down when more than 500 W is being imported. A 1 kW deadzone between -500 W and +500 W avoids oscillation. When the current would drop below the 6 A minimum, charging stops. Activating `ChargeNow` overrides all of this and charges at the maximum current regardless of surplus.

### Heat pump
The currently available PV surplus is written to the IDM heat pump's PV surplus register in kW, allowing the heat pump to use excess solar energy for heating and hot water.

### Inverter power limit
At startup the inverter's active power limit is set via SunSpec ModbusTCP so that grid export stays within the configured limit.

## Future Extensions

As a next step, the connected EV battery will be used as an additional energy source during the night via Vehicle-to-Home (V2H) leveraging EEBUS (see [separate EEBus.Net repo](https://github.com/digitaltwinconsortium/EEBUS.Net) for a reference implementation).
