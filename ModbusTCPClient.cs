
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace PVMonitor
{
    public class ApplicationDataUnit
    {
        public const uint maxADU = 260;
        public const int headerLength = 8;

        public ushort TransactionID;

        // protocol is always 0 for Modbus
        public ushort ProtocolID = 0;

        public ushort Length;

        public byte UnitID;

        public byte FunctionCode;

        public byte[] Data = new byte[maxADU - headerLength];

        public void CopyADUToNetworkBuffer(byte[] buffer)
        {
            if (buffer.Length < maxADU)
            {
                throw new ArgumentException("buffer must be at least " + maxADU.ToString() + " bytes long");
            }

            buffer[0] = (byte) (TransactionID >> 8);
            buffer[1] = (byte) (TransactionID & 0x00FF);

            buffer[2] = (byte) (ProtocolID >> 8);
            buffer[3] = (byte) (ProtocolID & 0x00FF);

            buffer[4] = (byte) (Length >> 8);
            buffer[5] = (byte) (Length & 0x00FF);

            buffer[6] = UnitID;
            
            buffer[7] = FunctionCode;
            
            Data.CopyTo(buffer, 8);
        }

        public void CopyHeaderFromNetworkBuffer(byte[] buffer)
        {
            if (buffer.Length < headerLength)
            {
                throw new ArgumentException("buffer must be at least " + headerLength.ToString() + " bytes long");
            }

            TransactionID |= (ushort)(buffer[0] << 8);
            TransactionID = buffer[1];

            ProtocolID |= (ushort)(buffer[2] << 8);
            ProtocolID = buffer[3];

            Length = (ushort)(buffer[4] << 8);
            Length = buffer[5];

            UnitID = buffer[6];

            FunctionCode = buffer[7];
        }
    }

    class ModbusTCPClient
    {
        private TcpClient tcpClient = null;

        // Modbus uses long timeouts (10 seconds minimum)
        private const int timeout = 10000;

        private ushort transactionID = 0;

        private const byte errorFlag = 0x80;

        private void HandlerError(byte errorCode)
        {
            switch (errorCode)
            {
                case 1: throw new Exception("Illegal function");
                case 2: throw new Exception("Illegal data address");
                case 3: throw new Exception("Illegal data value");
                case 4: throw new Exception("Server failure");
                case 5: throw new Exception("Acknowledged");
                case 6: throw new Exception("Busy");
                case 11: throw new Exception("Target unit failed to respond");
                default: throw new Exception("Unknown error");
            }
        }

        public void Connect(string ipAddress, int port)
        {
            try
            {
                tcpClient = new TcpClient(ipAddress, port);
                tcpClient.GetStream().ReadTimeout = timeout;
                tcpClient.GetStream().WriteTimeout = timeout;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        public void Disconnect()
        {
            tcpClient.Close();
            tcpClient = null;
        }

        public byte[] ReadHoldingRegisters(byte unitID, ushort registerBaseAddress, ushort count)
        {
            ApplicationDataUnit aduRequest = new ApplicationDataUnit();
            aduRequest.TransactionID = transactionID++;
            aduRequest.Length = 6;
            aduRequest.UnitID = unitID;
            aduRequest.FunctionCode = 3;

            aduRequest.Data[0] = (byte) (registerBaseAddress >> 8);
            aduRequest.Data[1] = (byte) (registerBaseAddress & 0x00FF);
            aduRequest.Data[2] = (byte) (count >> 8);
            aduRequest.Data[3] = (byte) (count & 0x00FF);
            
            byte[] buffer = new byte[ApplicationDataUnit.maxADU];
            aduRequest.CopyADUToNetworkBuffer(buffer);
            
            // send request to Modbus server
            tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

            // read response header from Modbus server
            int numBytesRead = tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength);
            if (numBytesRead != ApplicationDataUnit.headerLength)
            {
                throw new EndOfStreamException();
            }

            ApplicationDataUnit aduResponse = new ApplicationDataUnit();
            aduResponse.CopyHeaderFromNetworkBuffer(buffer);

            // check for error
            if ((aduResponse.FunctionCode & errorFlag) > 0)
            {
                // read error
                int errorCode = tcpClient.GetStream().ReadByte();
                if (errorCode == -1)
                {
                    throw new EndOfStreamException();
                }
                else
                {
                    HandlerError((byte) errorCode);
                }
            }

            // read length of response
            int length = tcpClient.GetStream().ReadByte();
            if (length == -1)
            {
                throw new EndOfStreamException();
            }

            // read response
            byte[] responseBuffer = new byte[length];
            numBytesRead = tcpClient.GetStream().Read(responseBuffer, 0, length);
            if (numBytesRead != length)
            {
                throw new EndOfStreamException();
            }

            return responseBuffer;
        }

        public void WriteRegister(byte unitID, ushort registerBaseAddress, ushort value)
        {
            throw new NotImplementedException();
        }

        public void WriteRegisters(byte unitID, ushort registerBaseAddress, ushort[] values)
        {
            throw new NotImplementedException();
        }
    }
}
