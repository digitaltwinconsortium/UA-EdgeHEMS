/*
    Original code Copyright 2011 Stefan Schake (https://github.com/stschake)

    Original code licensed under the terms of the GNU Lesser General
    Public License as published by the Free Software Foundation,
    either version 3 of the license, or any later version,
    see http://www.gnu.org/licenses.
*/

using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PVMonitor
{
    internal static class Constants
    {
        public const byte ExtraByteMask = 0x80;
        public const byte TypeMask = 0x70;
        public const byte LengthMask = 0x0F;

        public const uint EscapeSequence = 0x1b1b1b1b;
        public const uint Version1Marker = 0x01010101;

        public const uint PublicOpenReq = 0x100;
        public const uint PublicOpenRes = 0x101;

        public const uint PublicCloseReq = 0x200;
        public const uint PublicCloseRes = 0x201;

        public const uint GetProfilePackReq = 0x300;
        public const uint GetProfilePackRes = 0x301;

        public const uint GetProfileListReq = 0x400;
        public const uint GetProfileListRes = 0x401;

        public const uint GetProcParameterReq = 0x500;
        public const uint GetProcParameterRes = 0x501;

        public const uint SetProcParameterRes = 0x600;

        public const uint GetListReq = 0x700;
        public const uint GetListRes = 0x701;

        public const uint GetCosemReq = 0x800;
        public const uint GetCosemRes = 0x801;

        public const uint SetCosemReq = 0x900;
        public const uint SetCosemRes = 0x901;

        public const uint ActionCosemReq = 0xA00;
        public const uint ActionCosemRes = 0xA01;

        public const uint AttentionRes = 0xFF01;
        
        public const byte EndOfSmlMessage = 0x0;
    }

    public enum AbortOnError : byte
    {
        Continue = 0x00,
        ContinueNextGroup = 0x01,
        ContinueCurrentGroup = 0x02,
        AbortImmediately = 0xFF
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GetListRes
    {
        public byte[] ClientId;

        public byte[] ServerId;

        public byte[] ListName;

        public Time? ActSensorTime;

        public List<ListEntry> ValList;

        public byte[] ListSignature;

        public Time? ActGatewayTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GetProfilePackReq
    {
        public byte[] ServerId;

        public byte[] Username;

        public byte[] Password;

        public bool WithRawdata;

        public Time? BeginTime;

        public Time? EndTime;

        public List<byte[]> ParameterTreePath;

        public List<byte[]> ObjectList;

        public Tree DasDetails;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GetProfilePackRes
    {
        public byte[] ServerId;

        public Time ActTime;

        public uint RegPeriod;

        public List<byte[]> ParameterTreePath;

        public List<ProfObjHeaderEntry> HeaderList;

        public List<ProfObjPeriodEntry> PeriodList;

        public byte[] Rawdata;

        public byte[] ProfileSignature;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SMLMessage
    {
        public byte[] TransactionId;

        public byte GroupNo;

        public AbortOnError AbortOnError;

        public byte[] MessageBody;

        public ushort CRC16;

        public byte EndOfSmlMessage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PeriodEntry
    {
        public byte[] ObjName;

        public byte Unit;

        public sbyte Scaler;

        public object Value;

        public byte[] ValueSignature;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcParValue
    {
        public object SMLValue;

        public PeriodEntry? SMLPeriodEntry;

        public TupleEntry? SMLTupleEntry;

        public Time SMLTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProfObjHeaderEntry
    {
        public byte[] ObjName;

        public byte Unit;

        public sbyte Scaler;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProfObjPeriodEntry
    {
        public Time ValTime;

        public ulong Status;

        public List<ValueEntry> ValueList;

        public byte[] PeriodSignature;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PublicOpenReq
    {
        public byte[] Codepage;

        public byte[] ClientId;

        public byte[] ReqFileId;

        public byte[] ServerId;

        public byte[] Username;

        public byte[] Password;

        public byte? SMLVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PublicOpenRes
    {
        public byte[] Codepage;

        public byte[] ClientId;

        public byte[] ReqFileId;

        public byte[] ServerId;

        public Time? RefTime;

        public byte? SMLVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PublicClose
    {
        public byte[] GlobalSignature;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Tree
    {
        public byte[] ParameterName;


        public ProcParValue? ParameterValue;


        public List<Tree> ChildList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ValueEntry
    {
        public object Value;

        public byte[] ValueSignature;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TupleEntry
    {
        public byte[] ServerId;
        public Time SecIndex;
        public ulong Status;

        public byte UnitPA;
        public sbyte ScalerPA;
        public ulong ValuePA;
        public byte UnitR1;
        public sbyte ScalerR1;
        public ulong ValueR1;
        public byte UnitR4;
        public sbyte ScalerR4;
        public ulong ValueR4;
        public byte[] SignaturePAR1R4;

        public byte UnitMA;
        public sbyte ScalerMA;
        public ulong ValueMA;
        public byte UnitR2;
        public sbyte ScalerR2;
        public ulong ValueR2;
        public byte UnitR3;
        public sbyte ScalerR3;
        public ulong ValueR3;
        public byte[] SignatureMAR2R3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Time
    {
        public uint? SecIndex;

        public uint? Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ListEntry
    {
        public byte[] ObjName;

        public ulong Status;
         
        public Time? ValTime;
         
        public byte? Unit;

        public sbyte Scaler;

        public object Value;

        public byte[] ValueSignature;
    }

    public enum SMLType : byte
    {
        OctetString = 0,
        Boolean = 4,
        Integer = 5,
        Unsigned = 6,
        List = 7
    }

    public sealed class SmartMeter
    {
        public double EnergyPurchased { get; set; }

        public double EnergySold { get; set; }
    }
}