
using System;
using System.Diagnostics;
using System.IO;

namespace PVMonitor
{
    class SmartMeterLanguage
    {
        private BinaryReader _reader;

        public SmartMeterLanguage(Stream source)
        {
            _reader = new BinaryReader(source);
        }

        public void ProcessStream()
        {
            try
            {
                // look for the escape sequence
                while (_reader.ReadUInt32() != Constants.EscapeSequence)
                {
                    // wind back to the original byte + 1
                    _reader.BaseStream.Seek(-3, SeekOrigin.Current);
                }

                // next, we should see the version info
                if (_reader.ReadUInt32() != Constants.Version1Marker)
                {
                    throw new InvalidDataException("Expected version marker");
                }

                // next, we should see some SML messages
                ProcessSMLMessages();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void ProcessSMLMessages()
        {
            SMLType type = SMLType.Unkown;
            int length = 0;

            // an SML message has 6 list elements
            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 6)
            {
                throw new InvalidDataException("Expected 6 elements in SML message");
            }

            // process transaction ID
            ProcessNextType(out type, out length);
            if (type != SMLType.OctetString)
            {
                throw new InvalidDataException("Expected string");
            }
            string transactionId = BitConverter.ToString(_reader.ReadBytes(length));

            // process group ID
            ProcessNextType(out type, out length);
            if (type != SMLType.Unsigned)
            {
                throw new InvalidDataException("Expected group ID as unsigned");
            }
            if (length != 1)
            {
                throw new InvalidDataException("Expected group ID length of 1");
            }
            byte groupNo = _reader.ReadByte();

            // process abort flag
            ProcessNextType(out type, out length);
            if (type != SMLType.Unsigned)
            {
                throw new InvalidDataException("Expected abort flag as unsigned");
            }
            if (length != 1)
            {
                throw new InvalidDataException("Expected abort flag length of 1");
            }
            AbortOnError abortFlag = (AbortOnError)_reader.ReadByte();

            // process message body
            ProcessSMLMessageBody();

            // process CRC
            ProcessNextType(out type, out length);
            if (type != SMLType.Unsigned)
            {
                throw new InvalidDataException("Expected CRC16 as unsigned");
            }
            if (length != 2)
            {
                throw new InvalidDataException("Expected CRC16 length of 2");
            }
            ushort CRC16 = _reader.ReadUInt16();

            // process end of message
            ProcessNextType(out type, out length);
            if (_reader.ReadByte() != Constants.EndOfSmlMessage)
            {
                throw new InvalidDataException("Expected end of message flag");
            }
        }

        private void ProcessSMLMessageBody()
        {
            SMLType type = SMLType.Unkown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 2)
            {
                throw new InvalidDataException("Expected list length of 2");
            }

            // process command
            ProcessNextType(out type, out length);
            if (type != SMLType.Unsigned)
            {
                throw new InvalidDataException("Expected command as unsigned");
            }
            if (length != 2)
            {
                throw new InvalidDataException("Expected command length of 2");
            }
            ushort command = _reader.ReadUInt16();
            switch (command)
            {
                case Constants.PublicOpenReq: break;
                case Constants.PublicOpenRes:
                    {
                        ProcessOpenResponse();
                    }
                    break;
                case Constants.PublicCloseReq: break;
                case Constants.PublicCloseRes:
                    {
                        ProcessCloseResponse();
                    }
                    break;
                case Constants.GetProfilePackReq: break;
                case Constants.GetProfilePackRes: break;
                case Constants.GetProfileListReq:break;
                case Constants.GetProfileListRes:break;
                case Constants.GetProcParameterReq:break;
                case Constants.GetProcParameterRes:break;
                case Constants.SetProcParameterRes:break;
                case Constants.GetListReq:break;
                case Constants.GetListRes:
                    {
                        ProcessGetListResponse();
                    }
                    break;
                case Constants.GetCosemReq:break;
                case Constants.GetCosemRes:break;
                case Constants.SetCosemReq:break;
                case Constants.SetCosemRes:break;
                case Constants.ActionCosemReq:break;
                case Constants.ActionCosemRes:break;
                case Constants.AttentionRes:break;
                default:
                {
                    throw new InvalidDataException("Unknown command received: " + command.ToString());
                }
            }
        }

        private void ProcessOpenResponse()
        {
            SMLType type = SMLType.Unkown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 2)
            {
                throw new InvalidDataException("Expected list length of 6");
            }


        }

        private void ProcessGetListResponse()
        {
            throw new NotImplementedException();
        }

        private void ProcessCloseResponse()
        {
            throw new NotImplementedException();
        }

        private void ProcessNextType(out SMLType type, out int length)
        {
            byte byteRead = _reader.ReadByte();
            
            if ((byteRead & Constants.ExtraByteMask) != 0)
            {
                throw new NotImplementedException("Multi-byte length is not supported");
            }
            
            type = (SMLType)((byteRead & Constants.TypeMask) >> 4);
            length = byteRead & Constants.LengthMask;

            // list types don't count the type byte as part of the length, while all others do
            if (type != SMLType.List)
            {
                length--;
            }
        }

        public static short Reverse(short value)
        {
            return (short)Reverse((ushort)value);
        }

        public static int Reverse(int value)
        {
            return (int)Reverse((uint)value);
        }

        public static long Reverse(long value)
        {
            return (long)Reverse((ulong)value);
        }

        public static ushort Reverse(ushort value)
        {
            return (ushort)(((value & 0x00FF) << 8) |
                            ((value & 0xFF00) >> 8));
        }

        public static uint Reverse(uint value)
        {
            return ((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) <<  8) |
                   ((value & 0x00FF0000) >>  8) |
                   ((value & 0xFF000000) >> 24);
        }

        public static ulong Reverse(ulong value)
        {
            return ((value & 0x00000000000000FFUL) << 56) |
                   ((value & 0x000000000000FF00UL) << 40) |
                   ((value & 0x0000000000FF0000UL) << 24) |
                   ((value & 0x00000000FF000000UL) <<  8) |
                   ((value & 0x000000FF00000000UL) >>  8) |
                   ((value & 0x0000FF0000000000UL) >> 24) |
                   ((value & 0x00FF000000000000UL) >> 40) |
                   ((value & 0xFF00000000000000UL) >> 56);
        }
    }
}
