
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace PVMonitor
{
    class SmartMessageLanguage
    {
        private BinaryReader _reader = null;
        private SerialPort _serialPort = null;

        public SmartMeter Meter = new SmartMeter();

        public SmartMessageLanguage(string serialPortName)
        {
            try
            {
                // open the serial port
                _serialPort = new SerialPort(serialPortName, 9600, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 2000
                };
                _serialPort.Open();

                _reader = new BinaryReader(_serialPort.BaseStream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        public void ProcessStream()
        {
            new Thread(() =>
            {
                // loop forever on a background thread
                Thread.CurrentThread.IsBackground = true;
                while (true)
                {
                    try
                    {
                        // find the next escape sequence
                        FindEscapeSequence();

                        // process the SML messages
                        ProcessSMLMessages();

                        // read the fill bytes
                        byte byteRead = _reader.ReadByte();
                        while (byteRead == Constants.FillByteMarker)
                        {
                            byteRead = _reader.ReadByte();
                        }

                        // read the escape sequence at the end
                        while (byteRead == Constants.EscapeMarker)
                        {
                            byteRead = _reader.ReadByte();
                        }

                        // check the file end marker
                        if (byteRead != Constants.FileEndMarker)
                        {
                            throw new InvalidDataException("Expected file end marker");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }
                }
            }).Start();
        }

        private void FindEscapeSequence()
        {
            // look for the escape sequence
            while (true)
            {
                // find first part of escape marker
                while (_reader.ReadByte() != Constants.EscapeMarker);
                
                // the next 3 bytes must also be the escape marker, but we need to read them a byte at a time
                if (_reader.ReadByte() == Constants.EscapeMarker)
                {
                    if (_reader.ReadByte() == Constants.EscapeMarker)
                    {
                        if (_reader.ReadByte() == Constants.EscapeMarker)
                        {
                            // we found it! Next, read the file begin sequence
                            if (_reader.ReadUInt32() == Constants.FileBeginMarker)
                            {
                                // we are really at the begining of a message (and not at the end!)
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void ProcessSMLMessages()
        {
            SMLType type = SMLType.Unknown;
            int length = 0;

            while (true)
            {
                // an SML message has 6 list elements
                if (!ProcessNextType(out type, out length))
                {
                    // no more SML messages
                    break;
                }

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
                byte abortFlag = _reader.ReadByte();
                if (abortFlag != 0x00)
                {
                    Debug.WriteLine("Abort received");
                    return;
                }

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
                ushort CRC16 = Utils.ByteSwap(_reader.ReadUInt16());

                // process end of message
                if (_reader.ReadByte() != Constants.EndOfMessageMarker)
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
            ushort command = Utils.ByteSwap(_reader.ReadUInt16());
            switch (command)
            {
                case Constants.PublicOpenReq:
                    throw new NotImplementedException();
                
                case Constants.PublicOpenRes:
                    ProcessOpenResponse();
                    break;
                
                case Constants.PublicCloseReq:
                    throw new NotImplementedException();
                
                case Constants.PublicCloseRes:
                    ProcessCloseResponse();
                    break;
                
                case Constants.GetProfilePackReq:
                    throw new NotImplementedException();
                
                case Constants.GetProfilePackRes:
                    throw new NotImplementedException();
                
                case Constants.GetProfileListReq:
                    throw new NotImplementedException();
                
                case Constants.GetProfileListRes:
                    throw new NotImplementedException();
                
                case Constants.GetProcParameterReq:
                    throw new NotImplementedException();
                
                case Constants.GetProcParameterRes:
                    throw new NotImplementedException();
                
                case Constants.SetProcParameterRes:
                    throw new NotImplementedException();
                
                case Constants.GetListReq:
                    throw new NotImplementedException();
                
                case Constants.GetListRes:
                    ProcessGetListResponse();
                    break;
                
                case Constants.GetCosemReq:
                    throw new NotImplementedException();
                
                case Constants.GetCosemRes:
                    throw new NotImplementedException();
                
                case Constants.SetCosemReq:
                    throw new NotImplementedException();
                
                case Constants.SetCosemRes:
                    throw new NotImplementedException();
                
                case Constants.ActionCosemReq:
                    throw new NotImplementedException();
                
                case Constants.ActionCosemRes:
                    throw new NotImplementedException();
                
                case Constants.AttentionRes:
                    throw new NotImplementedException();
                
                default:
                   throw new InvalidDataException("Unknown command received: " + command.ToString());
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
                uint timeStamp = Utils.ByteSwap(_reader.ReadUInt32());
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
            string OBISID = BitConverter.ToString(_reader.ReadBytes(length));

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
            byte unit = 0;
            if(type != SMLType.Empty)
            {
                if (type != SMLType.Unsigned)
                {
                    throw new InvalidDataException("Expected unsigned");
                }
                if (length != 1)
                {
                    throw new InvalidDataException("Expected length of 1");
                }

                unit = _reader.ReadByte();
                if (((OBISID == Constants.PositiveActiveEnergyTotal)
                  || (OBISID == Constants.NegativeActiveEnergyTotal))
                  && (unit != Constants.WattHours))
                {
                    throw new InvalidDataException("Expected watt-hours for units");
                }
            }

            // process scaler (optional)
            ProcessNextType(out type, out length);
            sbyte scaler = 0;
            if (type != SMLType.Empty)
            {
                if (type != SMLType.Integer)
                {
                    throw new InvalidDataException("Expected integer");
                }
                if (length != 1)
                {
                    throw new InvalidDataException("Expected length of 1");
                }

                scaler = (sbyte)_reader.ReadByte();
            }

            // process value
            ProcessNextType(out type, out length);
            switch (type)
            {
                case SMLType.OctetString:
                    string value = BitConverter.ToString(_reader.ReadBytes(length));
                    break;

                case SMLType.Integer:
                    long int64 = 0;

                    if (length == 8)
                    {
                        int64 = (long)Utils.ByteSwap((ulong) _reader.ReadInt64());
                    }
                    if (length == 4)
                    {
                        int64 = Utils.ByteSwap((uint) _reader.ReadInt32());
                    }
                    if (length == 2)
                    {
                        int64 = Utils.ByteSwap((ushort) _reader.ReadInt16());
                    }
                    if (length == 1)
                    {
                        int64 = _reader.ReadByte();
                    }

                    if (OBISID == Constants.PositiveActiveEnergyTotal)
                    {
                        Meter.EnergyPurchased = int64 * Math.Pow(10, scaler) / 1000; // in kWh
                    }
                    if (OBISID == Constants.NegativeActiveEnergyTotal)
                    {
                        Meter.EnergySold = int64 * Math.Pow(10, scaler) / 1000; // in kWh
                    }
                    if (OBISID == Constants.ActivePowerTotal)
                    {
                        Meter.CurrentPower = int64 * Math.Pow(10, scaler); // in Watts
                    }

                    break;

                case SMLType.Unsigned:
                    ulong uint64 = 0;

                    if (length == 8)
                    {
                        uint64 = Utils.ByteSwap(_reader.ReadUInt64());
                    }
                    if (length == 4)
                    {
                        uint64 = Utils.ByteSwap(_reader.ReadUInt32());
                    }
                    if (length == 2)
                    {
                        uint64 = Utils.ByteSwap(_reader.ReadUInt16());
                    }
                    if (length == 1)
                    {
                        uint64 = _reader.ReadByte();
                    }

                    if (OBISID == Constants.PositiveActiveEnergyTotal)
                    {
                        Meter.EnergyPurchased = uint64 * Math.Pow(10, scaler) / 1000; // in kWh
                    }
                    if (OBISID == Constants.NegativeActiveEnergyTotal)
                    {
                        Meter.EnergySold = uint64 * Math.Pow(10, scaler) / 1000; // in kWh
                    }
                    if (OBISID == Constants.ActivePowerTotal)
                    {
                        Meter.CurrentPower = uint64 * Math.Pow(10, scaler); // in Watts
                    }

                    break;

                default:
                    throw new NotImplementedException();
            }

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

        private bool ProcessNextType(out SMLType type, out int length)
        {
            byte byteRead = _reader.ReadByte();

            // check if we came across an EoM marker
            if (byteRead == Constants.EndOfMessageMarker)
            {
                type = SMLType.Unknown;
                length = 0;
                return false;
            }
            
            type = (SMLType)((byteRead & Constants.TypeMask) >> 4);

            length = byteRead & Constants.LengthMask;
            if ((byteRead & Constants.ExtraByteMask) != 0)
            {
                // read the extra length byte
                byte extaLength = _reader.ReadByte();
                
                length = (length << 4) | extaLength;
            }
            
            // list types don't count the type byte as part of the length, while all others do
            if (type != SMLType.List)
            {
                length--;
            }

            // special case: an empty optional field is encoded as an empty string
            if ((type == SMLType.OctetString) && (length == 0))
            {
                type = SMLType.Empty;
            }

            return true;
        }
    }
}
