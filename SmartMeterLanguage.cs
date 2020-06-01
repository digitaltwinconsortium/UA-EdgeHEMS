
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
                // read a byte at a time until we find the next escape sequence
                while (!ReadEscapeSequence())
                {
                    _reader.ReadByte();
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

        private bool ReadEscapeSequence()
        {
            // look for the escape sequence
            bool isEscapeSequence = (_reader.ReadUInt32() == Constants.EscapeSequence);
            if (!isEscapeSequence)
            {
                // if we didn't find it, we wind back to the original byte
                _reader.BaseStream.Seek(-4, SeekOrigin.Current);
            }

            return isEscapeSequence;
        }

        private void ProcessSMLMessages()
        {
            while (!ReadEscapeSequence())
            {
                SMLType type = SMLType.Unknown;
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
        }

        private void ProcessSMLMessageBody()
        {
            SMLType type = SMLType.Unknown;
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
                case Constants.PublicOpenReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.PublicOpenRes:
                    {
                        ProcessOpenResponse();
                    }
                    break;
                case Constants.PublicCloseReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.PublicCloseRes:
                    {
                        ProcessCloseResponse();
                    }
                    break;
                case Constants.GetProfilePackReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetProfilePackRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetProfileListReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetProfileListRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetProcParameterReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetProcParameterRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.SetProcParameterRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetListReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetListRes:
                    {
                        ProcessGetListResponse();
                    }
                    break;
                case Constants.GetCosemReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.GetCosemRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.SetCosemReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.SetCosemRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.ActionCosemReq:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.ActionCosemRes:
                    {
                        throw new NotImplementedException();
                    }
                case Constants.AttentionRes:
                    {
                        throw new NotImplementedException();
                    }
                default:
                {
                    throw new InvalidDataException("Unknown command received: " + command.ToString());
                }
            }
        }

        private void ProcessOpenResponse()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 6)
            {
                throw new InvalidDataException("Expected list length of 6");
            }

            // process codepage (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string codePage = BitConverter.ToString(_reader.ReadBytes(length));
            }

            // process client ID (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string clientID = BitConverter.ToString(_reader.ReadBytes(length));
            }

            // process file ID
            ProcessNextType(out type, out length);
            if (type != SMLType.OctetString)
            {
                throw new InvalidDataException("Expected string");
            }
            string fileID = BitConverter.ToString(_reader.ReadBytes(length));

            // process server ID
            ProcessNextType(out type, out length);
            if (type != SMLType.OctetString)
            {
                throw new InvalidDataException("Expected string");
            }
            string serverID = BitConverter.ToString(_reader.ReadBytes(length));

            // process ref time (optional)
            ProcessTime();

            // process SML version (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.Unsigned)
                {
                    throw new InvalidDataException("Expected unsigned");
                }
                if (length != 1)
                {
                    throw new InvalidDataException("Expected unsigned length of 1");
                }
                byte smlVersion = _reader.ReadByte();
            }
        }

        private void ProcessTime()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.List)
                {
                    throw new InvalidDataException("Expected list");
                }
                if (length != 2)
                {
                    throw new InvalidDataException("Expected list length of 2");
                }

                ProcessNextType(out type, out length);
                if (type != SMLType.Unsigned)
                {
                    throw new InvalidDataException("Expected unsigned");
                }
                if (length != 1)
                {
                    throw new InvalidDataException("Expected unsigned length of 1");
                }
                byte secIndex = _reader.ReadByte();

                ProcessNextType(out type, out length);
                if (type != SMLType.Unsigned)
                {
                    throw new InvalidDataException("Expected unsigned");
                }
                if (length != 4)
                {
                    throw new InvalidDataException("Expected unsigned length of 4");
                }
                uint timeStamp = _reader.ReadUInt32();
            }
        }

        private void ProcessGetListResponse()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 7)
            {
                throw new InvalidDataException("Expected list length of 7");
            }

            // process client ID (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string clientID = BitConverter.ToString(_reader.ReadBytes(length));
            }

            // process server ID
            ProcessNextType(out type, out length);
            if (type != SMLType.OctetString)
            {
                throw new InvalidDataException("Expected string");
            }
            string serverID = BitConverter.ToString(_reader.ReadBytes(length));

            // process list name (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string listName = BitConverter.ToString(_reader.ReadBytes(length));
            }

            // process act sensor time (optional)
            ProcessTime();

            // process val list
            ProcessValList();

            // process list signature (optinal)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string clientID = BitConverter.ToString(_reader.ReadBytes(length));
            }

            // process gateway time (optional)
            ProcessTime();
        }

        private void ProcessValList()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            for (int i = 0; i < length; i++)
            {
                ProcessListEntry();
            }
        }

        private void ProcessListEntry()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 7)
            {
                throw new InvalidDataException("Expected list length of 7");
            }

            // process object name
            ProcessNextType(out type, out length);
            if (type != SMLType.OctetString)
            {
                throw new InvalidDataException("Expected string");
            }
            string objectName = BitConverter.ToString(_reader.ReadBytes(length));

            // process status (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.Unsigned)
                {
                    throw new InvalidDataException("Expected unsigned");
                }
                byte[] status = _reader.ReadBytes(length);
            }

            // process valTime (optional)
            ProcessTime();

            // process unit (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.Unsigned)
                {
                    throw new InvalidDataException("Expected unsigned");
                }
                byte unit = _reader.ReadByte();
            }

            // process scalar (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.Integer)
                {
                    throw new InvalidDataException("Expected integer");
                }
                byte scalar = _reader.ReadByte();
            }

            // process value
            ProcessNextType(out type, out length);
            if (type != SMLType.Integer)
            {
                throw new InvalidDataException("Expected integer");
            }
            if (length != 8)
            {
                throw new InvalidDataException("Expected integer length of 8");
            }
            long value = _reader.ReadInt64();

            // process value signature (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string clientID = BitConverter.ToString(_reader.ReadBytes(length));
            }
        }

        private void ProcessCloseResponse()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            ProcessNextType(out type, out length);
            if (type != SMLType.List)
            {
                throw new InvalidDataException("Expected list");
            }
            if (length != 1)
            {
                throw new InvalidDataException("Expected list length of 1");
            }

            // process SML signature (optional)
            ProcessNextType(out type, out length);
            if (type != SMLType.Empty)
            {
                if (type != SMLType.OctetString)
                {
                    throw new InvalidDataException("Expected string");
                }
                string clientID = BitConverter.ToString(_reader.ReadBytes(length));
            }
        }

        private void ProcessNextType(out SMLType type, out int length)
        {
            byte byteRead = _reader.ReadByte();
            
            if ((byteRead & Constants.ExtraByteMask) != 0)
            {
                throw new NotImplementedException("Multi-byte length is not supported");
            }

            // special case: an empty optional field is encoded with 0x01
            if (byteRead == 0x01)
            {
                type = SMLType.Empty;
                length = 0;
                return;
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
