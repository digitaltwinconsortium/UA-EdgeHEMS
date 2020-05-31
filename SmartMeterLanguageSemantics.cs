
namespace PVMonitor
{
    static class Constants
    {
        public const byte ExtraByteMask = 0x80;
        public const byte TypeMask = 0x70;
        public const byte LengthMask = 0x0F;

        public const uint EscapeSequence = 0x1B1B1B1B;
        public const uint Version1Marker = 0x01010101;

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
        
        public const byte EndOfSmlMessage = 0x0;
    }

    public enum SMLType : byte
    {
        OctetString = 0,
        Boolean = 4,
        Integer = 5,
        Unsigned = 6,
        List = 7,
        Unkown = 0xFF
    }

    public enum AbortOnError : byte
    {
        Continue = 0x00,
        ContinueNextGroup = 0x01,
        ContinueCurrentGroup = 0x02,
        AbortImmediately = 0xFF
    }

    public sealed class SmartMeter
    {
        public double EnergyPurchased { get; set; }

        public double EnergySold { get; set; }
    }
}