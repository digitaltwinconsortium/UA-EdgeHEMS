# Home Energy Management System with an OPC UA Server Interface

This Home Energy Management System (HEMS) is an OPC UA Server capturing energy telemetry data from a photovoltaik system and a smart meter and controlling an EV wallbox and heat pump with the surplus energy available from local production. This is running in Docker containers (pre-built Intel x64 and ARMx64 containers are available). 

## Running UA-EdgeHEMS

Simply run the pre-built containers on a Docker-enabled computer, e.g. for RaspberryPi4:

`docker run -itd -p 4840:4840 --device=/dev/ttyUSB0 --restart=always ghcr.io/barnstee/ua-edgehems-arm64:latest`

This will expose the OPC UA server on the default OPC UA port of 4840 and also make any USB serial devices available in the container (for reading out smart meters, etc.).

## Future Extensions
As a next step, the connected EV battery will be used as an additional energy source during the night via Vechile-to-Home (V2H) leveraging EEBUS (see [seperate EEBus.Net repo](https://github.com/digitaltwinconsortium/EEBUS.Net) for a reference implementation).

## Telemetry Data Captured
1. Weather data from [www.openweathermap.org](http://www.openweathermap.org), as the weather impacts PV performance.
2. Telemetry data from the PV’s inverter (The inverter uses the [Fronius API](https://www.fronius.com/en/photovoltaics/products/all-products/system-monitoring/open-interfaces/fronius-solar-api-json-)). A [SunSpec](https://sunspec.org)-compliant version using ModbusTCP is in PoC stage.
3. Smart meter telemetry data leveraging its [IEC 62056-21 standard](https://en.wikipedia.org/wiki/IEC_62056) optical interface using [a USB reader](https://shop.weidmann-elektronik.de/index.php?page=product&info=24). IEC 62056-21 uses the Smart Message Language - [SML](https://wiki.wireshark.org/SML).
4. Wallbe wallbox configuration using surplus energy from the PV to charge an Electric Vehicle.
5. IDM heat pump integration.

Telemetry is made available via an OPC UA server interface.
