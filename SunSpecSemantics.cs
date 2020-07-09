
namespace PVMonitor
{
    class SunSpecInverterModbusRegisterMapFloat
    {
        public const ushort InverterBaseAddress = 40000;

        // limit power output (% * 100, i.e. 50% is 5000)
        public const ushort WMaxLimPctOffset = 242;
        public const ushort WMaxLimPctLength = 1;
    }
}
