using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace PVMonitor
{
    class SerialCom
    {
		private SerialDevice UartPort;
		private DataReader DataReaderObject = null;
		private DataWriter DataWriterObject;
		private CancellationTokenSource ReadCancellationTokenSource;

		public async Task Initialise(uint BaudRate)
		{
			try
			{
				string aqs = SerialDevice.GetDeviceSelector("UART0");
				var dis = await DeviceInformation.FindAllAsync(aqs);
				UartPort = await SerialDevice.FromIdAsync(dis[0].Id);

				//Configure serial settings
				UartPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);    //mS before a time-out occurs when a write operation does not finish (default=InfiniteTimeout).
				UartPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);     //mS before a time-out occurs when a read operation does not finish (default=InfiniteTimeout).
				UartPort.BaudRate = 9600;
				UartPort.Parity = SerialParity.None;
				UartPort.StopBits = SerialStopBitCount.One;
				UartPort.DataBits = 8;

				DataReaderObject = new DataReader(UartPort.InputStream);
				DataReaderObject.InputStreamOptions = InputStreamOptions.Partial;
				DataWriterObject = new DataWriter(UartPort.OutputStream);

				StartReceive();
			}
			catch (Exception ex)
			{
				throw new Exception("Uart Initialise Error", ex);
			}
		}

		public async void StartReceive()
		{

			ReadCancellationTokenSource = new CancellationTokenSource();

			while (true)
			{
				await Listen();
				if ((ReadCancellationTokenSource.Token.IsCancellationRequested) || (UartPort == null))
					break;
			}
		}

		//LISTEN FOR NEXT RECEIVE
		private async Task Listen()
		{
			const int NUMBER_OF_BYTES_TO_RECEIVE = 1;           //<<<<<SET THE NUMBER OF BYTES YOU WANT TO WAIT FOR

			byte[] ReceiveData;
			UInt32 bytesRead;

			try
			{
				if (UartPort != null)
				{
					while (true)
					{
						//###### WINDOWS IoT MEMORY LEAK BUG 2017-03 - USING CancellationToken WITH LoadAsync() CAUSES A BAD MEMORY LEAK.  WORKAROUND IS
						//TO BUILD RELEASE WITHOUT USING THE .NET NATIVE TOOLCHAIN OR TO NOT USE A CancellationToken IN THE CALL #####
						bytesRead = await DataReaderObject.LoadAsync(NUMBER_OF_BYTES_TO_RECEIVE).AsTask();  //Wait until buffer is full

						if ((ReadCancellationTokenSource.Token.IsCancellationRequested) || (UartPort == null))
							break;

						if (bytesRead > 0)
						{
							ReceiveData = new byte[NUMBER_OF_BYTES_TO_RECEIVE];
							DataReaderObject.ReadBytes(ReceiveData);

							foreach (byte Data in ReceiveData)
							{
								//Do something with it

							}
						}

					}
				}
			}
			catch (Exception ex)
			{
				//We will get here often if the USB serial cable is removed so reset ready for a new connection (otherwise a never ending error occurs)
				if (ReadCancellationTokenSource != null)
					ReadCancellationTokenSource.Cancel();
				System.Diagnostics.Debug.WriteLine("UART ReadAsync Exception: {0}", ex.Message);
			}
		}
	}
}
