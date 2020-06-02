
namespace PVMonitor
{
    static class Constants
    {
        // masks
        public const byte ExtraByteMask = 0x80;
        public const byte TypeMask = 0x70;
        public const byte LengthMask = 0x0F;

        // markers
        public const uint EscapeMarker = 0x1B;
        public const uint FileBeginMarker = 0x01010101;
        public const ushort FileEndMarker = 0x1A;
        public const byte EndOfMessageMarker = 0x00;
        public const byte FillByteMarker = 0x00;

        // messages
        public const ushort PublicOpenReq = 0x100;
        public const ushort PublicOpenRes = 0x101;

        public const ushort PublicCloseReq = 0x200;
        public const ushort PublicCloseRes = 0x201;

        public const ushort GetProfilePackReq = 0x300;
        public const ushort GetProfilePackRes = 0x301;

        public const ushort GetProfileListReq = 0x400;
        public const ushort GetProfileListRes = 0x401;

        public const ushort GetProcParameterReq = 0x500;
        public const ushort GetProcParameterRes = 0x501;

        public const ushort SetProcParameterRes = 0x600;

        public const ushort GetListReq = 0x700;
        public const ushort GetListRes = 0x701;

        public const ushort GetCosemReq = 0x800;
        public const ushort GetCosemRes = 0x801;

        public const ushort SetCosemReq = 0x900;
        public const ushort SetCosemRes = 0x901;

        public const ushort ActionCosemReq = 0xA00;
        public const ushort ActionCosemRes = 0xA01;

        public const ushort AttentionRes = 0xFF01;

        // OBIS identifiers
        public const string PositivActiveEnergyTotal = "01-00-01-08-00-FF";  // energy purchased from the grid
        public const string NegativeActiveEnergyTotal = "01-00-02-08-00-FF"; // energy sold to the grid

        // DLMS units
        public const byte WattHours = 0x1E;
    }

    public enum SMLType : byte
    {
        OctetString = 0x0,
        Boolean = 0x4,
        Integer = 0x5,
        Unsigned = 0x6,
        List = 0x7,
        Empty = 0x01,
        Unknown = 0xFF
    }

    public sealed class SmartMeter
    {
        public SmartMeter()
        {
            // init members
            EnergyPurchased = 0.0;
            EnergySold = 0.0;
        }

        public double EnergyPurchased { get; set; }

        public double EnergySold { get; set; }
    }
}