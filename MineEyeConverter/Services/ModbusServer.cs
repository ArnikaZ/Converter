/*
Copyright (c) 2018-2020 Rossmann-Engineering
Permission is hereby granted, free of charge, 
to any person obtaining a copy of this software
and associated documentation files (the "Software"),
to deal in the Software without restriction, 
including without limitation the rights to use, 
copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit 
persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission 
notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, 
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE 
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System.Net.Sockets;
using System.Net;
using System.IO.Ports;
using EasyModbus;
using System.Linq;

namespace MineEyeConverter
{
    #region class ModbusProtocol
    /// <summary>
    /// Modbus Protocol informations.
    /// </summary>
    public class ModbusProtocol
    {
    	public enum ProtocolType { ModbusTCP = 0, ModbusUDP = 1, ModbusRTU = 2};
        public DateTime TimeStamp { get; set; }
        public bool Request { get; set; }
        public bool Response { get; set; }
        public UInt16 TransactionIdentifier { get; set; }
        public UInt16 ProtocolIdentifier { get; set; }
        public UInt16 Length { get; set; }
        public byte UnitIdentifier { get; set; }
        public byte FunctionCode { get; set; }
        public UInt16 StartingAdress { get; set; }
        public UInt16 StartingAddressRead { get; set; }
        public UInt16 StartingAddressWrite { get; set; }
        public UInt16 Quantity { get; set; }
        public UInt16 QuantityRead { get; set; }
        public UInt16 QuantityWrite { get; set; }
        public byte ByteCount { get; set; }
        public byte ExceptionCode { get; set; }
        public byte ErrorCode { get; set; }
        public UInt16[]? ReceiveCoilValues { get; set; }
        public UInt16[]? ReceiveRegisterValues { get; set; }
        public Int16[]? SendRegisterValues { get; set; }
        public bool[]? SendCoilValues { get; set; }    
        public UInt16 Crc { get; set; }
    }
#endregion

#region structs
    struct NetworkConnectionParameter
    {
        public NetworkStream stream;        //For TCP-Connection only
        public Byte[] bytes;
        public int portIn;                  //For UDP-Connection only
        public IPAddress ipAddressIn;       //For UDP-Connection only
        public TcpClient tcpClient;
    }
#endregion

#region TCPHandler class
    public class TcpHandler
    {
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(nameof(TcpHandler));
        public ModbusTcpServer? TcpServer { get; set; }
        public delegate void DataChanged(object networkConnectionParameter);
        public event DataChanged? dataChanged;

        public delegate void NumberOfClientsChanged();
        public event NumberOfClientsChanged? numberOfClientsChanged;

        readonly TcpListener? server = null;


        private readonly List<Client> tcpClientLastRequestList = new List<Client>();

        public int NumberOfConnectedClients { get; set; }

        public string? ipAddress { get; set; }

        /// When making a server TCP listen socket, will listen to this IP address.
        public IPAddress LocalIPAddress {
            get { return localIPAddress; }
        }
        private readonly IPAddress localIPAddress = IPAddress.Any;

        /// <summary>
        /// Listen to all network interfaces.
        /// </summary>
        /// <param name="port">TCP port to listen</param>
        public TcpHandler(int port)
        {
            server = new TcpListener(LocalIPAddress, port);
            server.Start();
            server.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
        }

        /// <summary>
        /// Listen to a specific network interface.
        /// </summary>
        /// <param name="localIPAddress">IP address of network interface to listen</param>
        /// <param name="port">TCP port to listen</param>
        public TcpHandler(IPAddress localIPAddress, int port)
        {
            this.localIPAddress = localIPAddress;
            server = new TcpListener(LocalIPAddress, port);
            server.Start();
            server.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
        }


        private void AcceptTcpClientCallback(IAsyncResult asyncResult)
        {
            TcpClient? tcpClient = null;
            try
            {
                if (server == null)
                    return;
                tcpClient = server.EndAcceptTcpClient(asyncResult);
                tcpClient.ReceiveTimeout = 4000;
                if (ipAddress != null)
                {
                    string? ipEndpoint = tcpClient.Client?.RemoteEndPoint?.ToString();
                    if (ipEndpoint != null)
                    {
                        ipEndpoint = ipEndpoint.Split(':')[0];
                        if (ipEndpoint != ipAddress)
                        {
                            tcpClient.Client?.Disconnect(false);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("Exception in AcceptTcpClientCallback", ex);
            }
            try
            {
                server?.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
                if (tcpClient != null)
                {
                    Client client = new Client(tcpClient);
                    NetworkStream networkStream = client.NetworkStream;
                    networkStream.ReadTimeout = 4000;
                    networkStream.BeginRead(client.Buffer, 0, client.Buffer.Length, ReadCallback, client);
                }
            }
            catch (Exception ex)
            {
                _log.Error("Exception in AcceptTcpClientCallback", ex);
            }
        }

        private readonly object _lockObject = new object();

        private int GetAndCleanNumberOfConnectedClients(Client client)
        {
            lock (_lockObject)
            {
                bool objetExists = false;
                foreach (var _ in tcpClientLastRequestList.Where(clientLoop => client.Equals(clientLoop)).Select(clientLoop => new { }))
                {
                    objetExists = true;
                }

                try
                {
                    tcpClientLastRequestList.RemoveAll(delegate (Client c)
                        {
                            return ((DateTime.Now.Ticks - c.Ticks) > 40000000);
                        }

                        );
                }
                catch (Exception ex)
                {
                    _log.ErrorFormat("Error in GetAndCleanNumberOfConnectedClients: {0}", ex.Message);
                }
                if (!objetExists)
                    tcpClientLastRequestList.Add(client);

                return tcpClientLastRequestList.Count;
            }
        }

        private void ReadCallback(IAsyncResult asyncResult)
        {
            NetworkConnectionParameter networkConnectionParameter = new NetworkConnectionParameter();
            Client? client = asyncResult.AsyncState as Client;
            if (client == null)
            {
                return;
            }
            client.Ticks = DateTime.Now.Ticks;
            NumberOfConnectedClients = GetAndCleanNumberOfConnectedClients(client);
            if (numberOfClientsChanged != null)
                numberOfClientsChanged();
            int read;
            NetworkStream? networkStream = null;
            try
            {
                networkStream = client.NetworkStream;
                read = networkStream.EndRead(asyncResult);
            }
            catch (Exception ex)
            {
                _log.Error("Exception in ReadCallback", ex);
                return;
            }

            if (read == 0)
            {
                return;
            }
            byte[] data = new byte[read];
            Buffer.BlockCopy(client.Buffer, 0, data, 0, read);
            networkConnectionParameter.bytes = data;
            networkConnectionParameter.stream = networkStream;
            networkConnectionParameter.tcpClient = client.TcpClient;
            if (dataChanged != null)
                dataChanged(networkConnectionParameter);
            try
            {
                networkStream.BeginRead(client.Buffer, 0, client.Buffer.Length, ReadCallback, client);
            }
            catch (Exception ex)
            {
                // Log the exception or handle it as needed
                Console.WriteLine($"Exception in ReadCallback: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                foreach (Client clientLoop in tcpClientLastRequestList)
                {
                    clientLoop.NetworkStream.Close(00);
                }
            }
            catch (Exception ex)
            {
                // Log the exception or handle it as needed
                Console.WriteLine($"Exception in Disconnect: {ex.Message}");
            }
            server?.Stop();
        }


        internal class Client
        {
            private readonly TcpClient tcpClient;
            private readonly byte[] buffer;
            public long Ticks { get; set; }

            public Client(TcpClient tcpClient)
            {
                this.tcpClient = tcpClient;
                int bufferSize = tcpClient.ReceiveBufferSize;
                buffer = new byte[bufferSize];
            }

            public TcpClient TcpClient
            {
                get { return tcpClient; }
            }

            public byte[] Buffer
            {
                get { return buffer; }
            }

            public NetworkStream NetworkStream
            {
                get {
                    
                        return tcpClient.GetStream();

                }
            }
        }
    }
#endregion
    
    /// <summary>
    /// Modbus TCP Server.
    /// </summary>
    public class ModbusServer
    {
        public bool UseWhiteList { get; set; }
        public List<MineEyeConverter.Client>? WhiteList { get; set; }
        public IPAddress? ClientIp { get; set; }
        public IOperationModeHandler? OperationModeHandler { get; set; }

        
        private bool debug = false;
        public int Port { get; set; }
        public ModbusProtocol SendData { get; set; } = new ModbusProtocol();

        public HoldingRegisters holdingRegisters { get; set; }      
        public InputRegisters inputRegisters { get; set; }
        public Coils coils { get; set; }
        public DiscreteInputs discreteInputs { get; set; }
        private int numberOfConnections = 0;
        private string serialPort = "COM1";
        private SerialPort? serialport;
        private ushort _lastStartingAddress;
        private ushort _lastQuantity;
        private int _functionCode;

        public byte UnitIdentifierValue => UnitIdentifier;
        public byte CurrentUnitIdentifier { get; set; }
        public ushort LastStartingAddress => _lastStartingAddress;
        public ushort LastQuantity => _lastQuantity;
        public int FunctionCode => _functionCode;


        public IPAddress? IpAddressIn { get; set; }
        private UdpClient? udpClient;
        private IPEndPoint? iPEndPoint;
        private TcpHandler? tcpHandler;
        Thread? listenerThread;
        Thread? clientConnectionThread;
        private readonly ModbusProtocol[] modbusLogData = new ModbusProtocol[100];
        public bool FunctionCode1Disabled {get; set;}
        public bool FunctionCode2Disabled { get; set; }
        public bool FunctionCode3Disabled { get; set; }
        public bool FunctionCode4Disabled { get; set; }
        public bool FunctionCode5Disabled { get; set; }
        public bool FunctionCode6Disabled { get; set; }
        public bool FunctionCode15Disabled { get; set; }
        public bool FunctionCode16Disabled { get; set; }
        public bool FunctionCode23Disabled { get; set; }
        public bool PortChanged { get; set; }
        readonly object lockCoils = new object();
        readonly object lockHoldingRegisters = new object();
        private volatile bool shouldStop;

        private IPAddress localIPAddress = IPAddress.Any;
        private readonly log4net.ILog _log=log4net.LogManager.GetLogger(typeof(ModbusServer));

        /// <summary>
        /// When creating a TCP or UDP socket, the local IP address to attach to.
        /// </summary>
        public IPAddress LocalIPAddress
        {
            get { return localIPAddress; }
            set { if (listenerThread == null) localIPAddress = value; }
        }

        public ModbusServer()
        {
            holdingRegisters = new HoldingRegisters(this);
            inputRegisters = new InputRegisters(this);
            coils = new Coils(this);
            discreteInputs = new DiscreteInputs(this);

        }

        #region events
        public delegate void CoilsChangedHandler(byte slaveId, int coil, int numberOfCoils);
        public event CoilsChangedHandler? CoilsChanged;

        public delegate void HoldingRegistersChangedHandler(byte slaveId, int register, int numberOfRegisters);
        public event HoldingRegistersChangedHandler? HoldingRegistersChanged;

        public delegate void NumberOfConnectedClientsChangedHandler();
        public event NumberOfConnectedClientsChangedHandler? NumberOfConnectedClientsChanged;

        public delegate void LogDataChangedHandler();
        public event LogDataChangedHandler? LogDataChanged;
        #endregion

        public void Listen()
        {
            
            listenerThread = new Thread(ListenerThread);
            listenerThread.Start();
        }

        public void StopListening()
        {
        	if (SerialFlag && (serialport != null))
        	{
        		if (serialport.IsOpen)
        			serialport.Close();
                shouldStop = true;
            }
            try
            {
                tcpHandler?.Disconnect();
                
                shouldStop = true;
                if (listenerThread != null && listenerThread.IsAlive && !listenerThread.Join(TimeSpan.FromSeconds(3)))
                {
                    _log.Warn("Warning: ListenerThread did not exit gracefully");
                }


            }
           
            catch (Exception ex)
            {
                _log.Error("Exception in ListenerThread", ex);
            }
           

            try
            {
                if (clientConnectionThread != null && clientConnectionThread.IsAlive && !clientConnectionThread.Join(TimeSpan.FromSeconds(3)))
                {
                    _log.Warn("Warning: ClientConnectionThread did not exit gracefully");
                }
            }
            catch (Exception ex) 
            {
                _log.Error("Exception in ClientConnectionThread", ex);
            }
        }
        
void ListenerThread()
        {
            Byte[] bytes;
            int portIn;
            if (!UDPFlag && !SerialFlag)
            {
                if (udpClient != null)
                {
                    try
                    {
                        udpClient.Close();
                    }
                    catch (Exception ex)
                    {
    
                        Console.WriteLine($"Exception in ListenerThread: {ex.Message}");
                    }
                }
                tcpHandler = new TcpHandler(LocalIPAddress, Port);
                if (debug) StoreLogData.Instance.Store($"EasyModbus Server listing for incomming data at Port {Port}, local IP {LocalIPAddress}", System.DateTime.Now);
                tcpHandler.dataChanged += new TcpHandler.DataChanged(ProcessReceivedData);
                tcpHandler.numberOfClientsChanged += new TcpHandler.NumberOfClientsChanged(numberOfClientsChanged);
            }
            else if (SerialFlag)
            {
                if (serialport == null)
                {
                    if (debug) StoreLogData.Instance.Store("EasyModbus RTU-Server listing for incomming data at Serial Port " + serialPort, System.DateTime.Now);
                    serialport = new SerialPort();
                    serialport.PortName = serialPort;
                    serialport.BaudRate = this.Baudrate;
                    serialport.Parity = this.Parity;
                    serialport.StopBits = StopBits;
                    serialport.WriteTimeout = 10000;
                    serialport.ReadTimeout = 1000;
                    serialport.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                    serialport.Open();
                }
            }
            else
                while (!shouldStop)
                {
                    if (UDPFlag)
                    {
                        if (udpClient == null || PortChanged)
                        {
                            IPEndPoint localEndoint = new IPEndPoint(LocalIPAddress, Port);
                            udpClient = new UdpClient(localEndoint);
                            if (debug) StoreLogData.Instance.Store($"EasyModbus Server listing for incomming data at Port {Port}, local IP {LocalIPAddress}", System.DateTime.Now);
                            udpClient.Client.ReceiveTimeout = 1000;
                            iPEndPoint = new IPEndPoint(IPAddress.Any, Port);
                            PortChanged = false;
                        }
                        if (tcpHandler != null)
                            tcpHandler.Disconnect();
                        try
                        {
                            bytes = udpClient.Receive(ref iPEndPoint);
                            portIn = iPEndPoint.Port;
                            NetworkConnectionParameter networkConnectionParameter = new NetworkConnectionParameter();
                            networkConnectionParameter.bytes = bytes;
                            IpAddressIn = iPEndPoint.Address;
                            networkConnectionParameter.portIn = portIn;
                            networkConnectionParameter.ipAddressIn = IpAddressIn;
                            ParameterizedThreadStart pts = new ParameterizedThreadStart(this.ProcessReceivedData);
                            Thread processDataThread = new Thread(pts);
                            processDataThread.Start(networkConnectionParameter);
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Exception in ListenerThread: ", ex);
                        }
                    }

                }
        }
    
		#region SerialHandler
      
        private readonly byte[] readBuffer = new byte[2094];
        private DateTime lastReceive;
        private int nextSign = 0;
        private void DataReceivedHandler(object sender,
                        SerialDataReceivedEventArgs e)
        {
            int silence = 4000 / Baudrate;
            if ((DateTime.Now.Ticks - lastReceive.Ticks) > TimeSpan.TicksPerMillisecond*silence)
                nextSign = 0;


            SerialPort sp = (SerialPort)sender;

            int numbytes = sp.BytesToRead;
            byte[] rxbytearray = new byte[numbytes];

            sp.Read(rxbytearray, 0, numbytes);
            
            Array.Copy(rxbytearray, 0,  readBuffer, nextSign, rxbytearray.Length);
            lastReceive= DateTime.Now;
            nextSign = numbytes+ nextSign;
            if (EasyModbus.ModbusClient.DetectValidModbusFrame(readBuffer, nextSign))
            {
     
                nextSign= 0;

                    NetworkConnectionParameter networkConnectionParameter = new NetworkConnectionParameter();
                    networkConnectionParameter.bytes = readBuffer;
                    ParameterizedThreadStart pts = new ParameterizedThreadStart(this.ProcessReceivedData);
                    Thread processDataThread = new Thread(pts);
                    processDataThread.Start(networkConnectionParameter);
                    
                
            }
            
        }
		#endregion
 
		#region Method numberOfClientsChanged
        private void numberOfClientsChanged()
        {
            if (tcpHandler != null)
            {
                numberOfConnections = tcpHandler.NumberOfConnectedClients;
            }
            
            if (NumberOfConnectedClientsChanged != null)
                NumberOfConnectedClientsChanged();
        }
        #endregion


        readonly object lockProcessReceivedData = new object();
        #region Method ProcessReceivedData
        public void ProcessReceivedData(object? networkConnectionParameter)
        {
            if (networkConnectionParameter == null)
                return;
            lock (lockProcessReceivedData)
            {
                NetworkConnectionParameter netParam = (NetworkConnectionParameter)networkConnectionParameter;
                if (netParam.tcpClient.Client.RemoteEndPoint != null)
                {
                    ClientIp = ((IPEndPoint)netParam.tcpClient.Client.RemoteEndPoint).Address;
                }

                Byte[] bytes = new byte[((NetworkConnectionParameter)networkConnectionParameter).bytes.Length];
                if (debug) StoreLogData.Instance.Store("Received Data: " + BitConverter.ToString(bytes), System.DateTime.Now);
                NetworkStream stream = ((NetworkConnectionParameter)networkConnectionParameter).stream;
                int portIn = ((NetworkConnectionParameter)networkConnectionParameter).portIn;
                
                IPAddress ipAddressIn = ((NetworkConnectionParameter)networkConnectionParameter).ipAddressIn;
                

                Array.Copy(((NetworkConnectionParameter)networkConnectionParameter).bytes, 0, bytes, 0, ((NetworkConnectionParameter)networkConnectionParameter).bytes.Length);

                ModbusProtocol receiveDataThread = new ModbusProtocol();
                ModbusProtocol sendDataThread = new ModbusProtocol();

                try
                {
                    UInt16[] wordData = new UInt16[1];
                    byte[] byteData = new byte[2];
                    receiveDataThread.TimeStamp = DateTime.Now;
                    receiveDataThread.Request = true;
                    if (!SerialFlag)
                    {
                        //Lese Transaction identifier
                        byteData[1] = bytes[0];
                        byteData[0] = bytes[1];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.TransactionIdentifier = wordData[0];

                        //Lese Protocol identifier
                        byteData[1] = bytes[2];
                        byteData[0] = bytes[3];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.ProtocolIdentifier = wordData[0];

                        //Lese length
                        byteData[1] = bytes[4];
                        byteData[0] = bytes[5];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.Length = wordData[0];
                    }

                    //Lese unit identifier

                    receiveDataThread.UnitIdentifier = bytes[6 - 6 * Convert.ToInt32(SerialFlag)];
                    CurrentUnitIdentifier = receiveDataThread.UnitIdentifier;

                    //Check UnitIdentifier
                    if (receiveDataThread.UnitIdentifier == 0)
                        return;

                    // Lese function code
                    receiveDataThread.FunctionCode = bytes[7 - 6 * Convert.ToInt32(SerialFlag)];
                    _functionCode = receiveDataThread.FunctionCode;

                    // Lese starting address 
                    byteData[1] = bytes[8 - 6 * Convert.ToInt32(SerialFlag)];
                    byteData[0] = bytes[9 - 6 * Convert.ToInt32(SerialFlag)];
                    Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                    receiveDataThread.StartingAdress = wordData[0];

                    _lastStartingAddress = receiveDataThread.StartingAdress;

                    if (receiveDataThread.FunctionCode <= 4)
                    {
                        // Lese quantity
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.Quantity = wordData[0];
                        _lastQuantity = receiveDataThread.Quantity;
                    }

                    if (receiveDataThread.FunctionCode == 5)
                    {
                        receiveDataThread.ReceiveCoilValues = new ushort[1];
                        // Lese Value
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, receiveDataThread.ReceiveCoilValues, 0, 2);
                    }
                    if (receiveDataThread.FunctionCode == 6)
                    {
                        receiveDataThread.ReceiveRegisterValues = new ushort[1];
                        // Lese Value
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, receiveDataThread.ReceiveRegisterValues, 0, 2);
                    }
                    if (receiveDataThread.FunctionCode == 15)
                    {
                        // Lese quantity
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.Quantity = wordData[0];

                        receiveDataThread.ByteCount = bytes[12 - 6 * Convert.ToInt32(SerialFlag)];

                        if ((receiveDataThread.ByteCount % 2) != 0)
                            receiveDataThread.ReceiveCoilValues = new ushort[receiveDataThread.ByteCount / 2 + 1];
                        else
                            receiveDataThread.ReceiveCoilValues = new ushort[receiveDataThread.ByteCount / 2];
                        // Lese Value
                        Buffer.BlockCopy(bytes, 13 - 6 * Convert.ToInt32(SerialFlag), receiveDataThread.ReceiveCoilValues, 0, receiveDataThread.ByteCount);
                    }
                    if (receiveDataThread.FunctionCode == 16)
                    {
                        // Lese quantity
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.Quantity = wordData[0];

                        receiveDataThread.ByteCount = bytes[12 - 6 * Convert.ToInt32(SerialFlag)];
                        receiveDataThread.ReceiveRegisterValues = new ushort[receiveDataThread.Quantity];
                        for (int i = 0; i < receiveDataThread.Quantity; i++)
                        {
                            // Lese Value
                            byteData[1] = bytes[13 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            byteData[0] = bytes[14 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            Buffer.BlockCopy(byteData, 0, receiveDataThread.ReceiveRegisterValues, i * 2, 2);
                        }

                    }
                    if (receiveDataThread.FunctionCode == 23)
                    {
                        // Lese starting Address Read
                        byteData[1] = bytes[8 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[9 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.StartingAddressRead = wordData[0];
                        // Lese quantity Read
                        byteData[1] = bytes[10 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[11 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.QuantityRead = wordData[0];
                        // Lese starting Address Write
                        byteData[1] = bytes[12 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[13 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.StartingAddressWrite = wordData[0];
                        // Lese quantity Write
                        byteData[1] = bytes[14 - 6 * Convert.ToInt32(SerialFlag)];
                        byteData[0] = bytes[15 - 6 * Convert.ToInt32(SerialFlag)];
                        Buffer.BlockCopy(byteData, 0, wordData, 0, 2);
                        receiveDataThread.QuantityWrite = wordData[0];

                        receiveDataThread.ByteCount = bytes[16 - 6 * Convert.ToInt32(SerialFlag)];
                        receiveDataThread.ReceiveRegisterValues = new ushort[receiveDataThread.QuantityWrite];
                        for (int i = 0; i < receiveDataThread.QuantityWrite; i++)
                        {
                            // Lese Value
                            byteData[1] = bytes[17 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            byteData[0] = bytes[18 + i * 2 - 6 * Convert.ToInt32(SerialFlag)];
                            Buffer.BlockCopy(byteData, 0, receiveDataThread.ReceiveRegisterValues, i * 2, 2);
                        }
                    }
                }
                catch (Exception exc)
                {
                    _log.Error("Exception in ProcessReceivedData", exc);
                }
                
                this.CreateAnswer(receiveDataThread, sendDataThread, stream, portIn, ipAddressIn);
                this.CreateLogData(receiveDataThread, sendDataThread);

                if (LogDataChanged != null)
                    LogDataChanged();
            }
        }
        #endregion
        private bool IsClientAuthorized(IPAddress clientIp, int functionCode)
        {
            // if not using white list, every client is authorised
            if (!UseWhiteList)
                return true;

            bool requiresWrite = false;
            switch (functionCode)
            {
                case 5:
                case 6:
                case 15:
                case 16:
                case 23:
                    requiresWrite = true;
                    break;
            }

            string clientIpStr = clientIp.ToString();
            var client = WhiteList?.FirstOrDefault(c => c.IpAddress == clientIpStr);
            if (client == null)
                return false; // client is not on the white list

            // if write operation, client needs "W" permission
            if (requiresWrite && !string.Equals(client.Permission, "W", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        #region Method CreateAnswer
        private void CreateAnswer(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            
            if (ClientIp !=null && !IsClientAuthorized(ClientIp, receiveData.FunctionCode))
            {
                // if client is not on the white list or does not have permissions – sending exception message
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 0x01; 
                sendData.Length = 0x03; 

                sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                return;
            }
            
            if (OperationModeHandler is ManualModeHandler && !IsRegisterDefined(receiveData))
            {
                    // odpowiedź exception:
                    sendData.ErrorCode = (byte)(receiveData.FunctionCode | 0x80);
                    sendData.ExceptionCode = 0x02; // Illegal Data Address
                    sendData.Length = 0x03; 

                    sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    return;
                
            }
                      
            

            switch (receiveData.FunctionCode)
            {
                // Read Coils
                case 1:
                    if (!FunctionCode1Disabled)
                        this.ReadCoils(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    break;
                // Read Input Registers
                case 2:
                    if (!FunctionCode2Disabled)
                        this.ReadDiscreteInputs(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    
                    break;
                // Read Holding Registers
                case 3:
                    if (!FunctionCode3Disabled)
                        this.ReadHoldingRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    
                    break;
                // Read Input Registers
                case 4:
                    if (!FunctionCode4Disabled)
                        this.ReadInputRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    
                    break;
                // Write single coil
                case 5:
                    if (!FunctionCode5Disabled)
                        this.WriteSingleCoil(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    
                    break;
                // Write single register
                case 6:
                    if (!FunctionCode6Disabled)
                        this.WriteSingleRegister(receiveData, sendData, stream, portIn, ipAddressIn);
                    else
                    {
                        sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                    }
                    
                        break;
                // Write Multiple coils
                case 15:
                        if (!FunctionCode15Disabled)
                            this.WriteMultipleCoils(receiveData, sendData, stream, portIn, ipAddressIn);
                        else
                        {
                            sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                            sendData.ExceptionCode = 1;
                            sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                        }

                        break;
                // Write Multiple registers
                case 16:
                        if (!FunctionCode16Disabled)
                            this.WriteMultipleRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                        else
                        {
                            sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                            sendData.ExceptionCode = 1;
                            sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                        }

                        break;
                // Error: Function Code not supported
                case 23:
                        if (!FunctionCode23Disabled)
                            this.ReadWriteMultipleRegisters(receiveData, sendData, stream, portIn, ipAddressIn);
                        else
                        {
                            sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                            sendData.ExceptionCode = 1;
                            sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                        }

                        break;
                // Error: Function Code not supported
                default: sendData.ErrorCode = (byte) (receiveData.FunctionCode + 0x80);
                        sendData.ExceptionCode = 1;
                        sendException(sendData.ErrorCode, sendData.ExceptionCode, receiveData, sendData, stream, portIn, ipAddressIn);
                        break;
            }
            sendData.TimeStamp = DateTime.Now;
        }
        
        private static bool IsRegisterDefined(ModbusProtocol receiveData)
        {
            var rm = RegisterManager.Instance;

            if (receiveData.FunctionCode == 3)
            {
                for (ushort i = 0; i < receiveData.Quantity; i++)
                {
                    ushort currentAddress = (ushort)(receiveData.StartingAdress + i);
                    var register = rm.HoldingRegisters.FirstOrDefault(
                        r => r.SlaveId == receiveData.UnitIdentifier &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.IsActive);

                    if (register == null)
                    {
                        return false;
                    }
                }
                return true;
            }

            if (receiveData.FunctionCode == 6 || receiveData.FunctionCode == 16)
            {
                for (ushort i = 0; i < receiveData.Quantity; i++)
                {
                    ushort currentAddress = (ushort)(receiveData.StartingAdress + i);
                    var register = rm.HoldingRegisters.FirstOrDefault(
                        r => r.SlaveId == receiveData.UnitIdentifier &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.AccessMode == "W" &&
                             r.IsActive);

                    if (register == null)
                    {
                        return false;
                    }
                }
                return true;
            }
            else if (receiveData.FunctionCode == 4)
            {
                for (ushort i = 0; i < receiveData.Quantity; i++)
                {
                    ushort currentAddress = (ushort)(receiveData.StartingAdress + i);
                    var register = rm.InputRegisters.FirstOrDefault(
                        r => r.SlaveId == receiveData.UnitIdentifier &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.IsActive);

                    if (register == null)
                    {
                        return false;
                    }
                }
                return true;
            }

            else if (receiveData.FunctionCode == 1)
            {
                for (ushort i = 0; i < receiveData.Quantity; i++)
                {
                    ushort currentAddress = (ushort)(receiveData.StartingAdress + i);
                    var register = rm.Coils.FirstOrDefault(
                        r => r.SlaveId == receiveData.UnitIdentifier &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.IsActive);

                    if (register == null)
                    {
                        return false;
                    }
                }
                return true;
            }
       
            else if(receiveData.FunctionCode==5 || receiveData.FunctionCode == 15)
            {
                for (ushort i = 0; i < receiveData.Quantity; i++)
                {
                    ushort currentAddress = (ushort)(receiveData.StartingAdress + i);
                    var register = rm.Coils.FirstOrDefault(
                        r => r.SlaveId == receiveData.UnitIdentifier &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.AccessMode == "W" &&
                             r.IsActive);

                    if (register == null)
                    {
                       
                        return false;
                    }
                }
                return true;
            }
            return false; 
            
        }
        #endregion

        private void ReadCoils(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            if ((receiveData.Quantity < 1) || (receiveData.Quantity > 0x07D0))  //Invalid quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if (((receiveData.StartingAdress + 1 + receiveData.Quantity) > 65535) || (receiveData.StartingAdress < 0))     //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0)
            {
                if ((receiveData.Quantity % 8) == 0)
                    sendData.ByteCount = (byte)(receiveData.Quantity / 8);
                else
                    sendData.ByteCount = (byte)(receiveData.Quantity / 8 + 1);

                sendData.SendCoilValues = new bool[receiveData.Quantity];
                lock (lockCoils)
                {
                    int offset = receiveData.UnitIdentifier * 10000;
                    Buffer.BlockCopy(coils.LocalArray,
                        offset + receiveData.StartingAdress,
                        sendData.SendCoilValues, 0,
                        receiveData.Quantity);
                }
            }
            if (true)
            {
                Byte[] data;

                if (sendData.ExceptionCode > 0)
                	data = new byte[9 + 2*Convert.ToInt32(SerialFlag)];
                else
                   	data = new byte[9 + sendData.ByteCount+ 2*Convert.ToInt32(SerialFlag)];

                Byte[] byteData;

                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];
                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;

                //ByteCount
                data[8] = sendData.ByteCount;

                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendCoilValues = null;
                }

                if (sendData.SendCoilValues != null)
                    for (int i = 0; i < (sendData.ByteCount); i++)
                    {
                        byteData = new byte[2];
                        for (int j = 0; j < 8; j++)
                        {

                            byte boolValue;
                            if (sendData.SendCoilValues[i * 8 + j])
                                boolValue = 1;
                            else
                                boolValue = 0;
                            byteData[1] = (byte)((byteData[1]) | (boolValue << j));
                            if ((i * 8 + j + 1) >= sendData.SendCoilValues.Length)
                                break;
                        }
                        data[9 + i] = byteData[1];
                    }
                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }
                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in ReadCoils", ex);
                }
            }  
        }

        private void ReadDiscreteInputs(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            if ((receiveData.Quantity < 1) || (receiveData.Quantity > 0x07D0))  //Invalid quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if (((receiveData.StartingAdress + 1 + receiveData.Quantity) > 65535) || (receiveData.StartingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0)
            {
                if ((receiveData.Quantity % 8) == 0)
                    sendData.ByteCount = (byte)(receiveData.Quantity / 8);
                else
                    sendData.ByteCount = (byte)(receiveData.Quantity / 8 + 1);

                sendData.SendCoilValues = new bool[receiveData.Quantity];
                Array.Copy(discreteInputs.LocalArray, receiveData.StartingAdress + 1, sendData.SendCoilValues, 0, receiveData.Quantity);
            }
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.ByteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData;
                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;

                //ByteCount
                data[8] = sendData.ByteCount;


                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendCoilValues = null;
                }

                if (sendData.SendCoilValues != null)
                    for (int i = 0; i < (sendData.ByteCount); i++)
                    {
                        byteData = new byte[2];
                        for (int j = 0; j < 8; j++)
                        {

                            byte boolValue;
                            if (sendData.SendCoilValues[i * 8 + j])
                                boolValue = 1;
                            else
                                boolValue = 0;
                            byteData[1] = (byte)((byteData[1]) | (boolValue << j));
                            if ((i * 8 + j + 1) >= sendData.SendCoilValues.Length)
                                break;
                        }
                        data[9 + i] = byteData[1];
                    }

                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }
                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if(debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in ReadDiscreteInputs", ex);
                }
            }
        }

        private void ReadHoldingRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            if ((receiveData.Quantity < 1) || (receiveData.Quantity > 0x007D))  //Invalid quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if (((receiveData.StartingAdress + 1 + receiveData.Quantity) > 65535)  || (receiveData.StartingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0)
            {
                sendData.ByteCount = (byte)(2 * receiveData.Quantity);
                sendData.SendRegisterValues = new Int16[receiveData.Quantity];
                lock (lockHoldingRegisters)
                {
                    int offset = receiveData.UnitIdentifier * 10000;
                    Buffer.BlockCopy(holdingRegisters.LocalArray,
                        (offset + receiveData.StartingAdress) * 2 ,
                        sendData.SendRegisterValues, 0,
                        receiveData.Quantity * 2);
                }
                   
                
            }
                if (sendData.ExceptionCode > 0)
                    sendData.Length = 0x03;
                else
                    sendData.Length = (ushort)(0x03 + sendData.ByteCount);
            
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.ByteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData;
                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;

                //ByteCount
                data[8] = sendData.ByteCount;

                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }
   

                if (sendData.SendRegisterValues != null)
                    for (int i = 0; i < (sendData.ByteCount / 2); i++)
                    {
                        byteData = BitConverter.GetBytes(sendData.SendRegisterValues[i]);
                        data[9 + i * 2] = byteData[1];
                        data[10 + i * 2] = byteData[0];
                    }
                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }
                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in ReadHoldingRegisters", ex);
                }
            }       
        }

        private void ReadInputRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            if ((receiveData.Quantity < 1) || (receiveData.Quantity > 0x007D))  //Invalid quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if (((receiveData.StartingAdress + 1 + receiveData.Quantity) > 65535)  || (receiveData.StartingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0)
            {
                sendData.ByteCount = (byte)(2 * receiveData.Quantity);
                sendData.SendRegisterValues = new Int16[receiveData.Quantity];

                int offset = receiveData.UnitIdentifier * 10000;
                Buffer.BlockCopy(inputRegisters.LocalArray,
                    (offset + receiveData.StartingAdress) * 2,
                    sendData.SendRegisterValues, 0,
                    receiveData.Quantity * 2);
            }
                if (sendData.ExceptionCode > 0)
                    sendData.Length = 0x03;
                else
                    sendData.Length = (ushort)(0x03 + sendData.ByteCount);
            
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.ByteCount + 2 * Convert.ToInt32(SerialFlag)];
                Byte[] byteData ;
                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;

                //ByteCount
                data[8] = sendData.ByteCount;

                
                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }


                if (sendData.SendRegisterValues != null)
                    for (int i = 0; i < (sendData.ByteCount / 2); i++)
                    {
                        byteData = BitConverter.GetBytes(sendData.SendRegisterValues[i]);
                        data[9 + i * 2] = byteData[1];
                        data[10 + i * 2] = byteData[0];
                    }
                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }

                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in ReadInputRegisters", ex);
                }
            }
        }

        private void WriteSingleCoil(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            sendData.StartingAdress = receiveData.StartingAdress;
            sendData.ReceiveCoilValues = receiveData.ReceiveCoilValues;
            if(receiveData.ReceiveCoilValues != null)
            {
                if ((receiveData.ReceiveCoilValues[0] != 0x0000) && (receiveData.ReceiveCoilValues[0] != 0xFF00))  //Invalid Value
                {
                    sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                    sendData.ExceptionCode = 3;
                }
                if (((receiveData.StartingAdress + 1) > 65535) || (receiveData.StartingAdress < 0))    //Invalid Starting adress or Starting address + quantity
                {
                    sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                    sendData.ExceptionCode = 2;
                }
                if (sendData.ExceptionCode == 0)
                {
                    if (receiveData.ReceiveCoilValues[0] == 0xFF00)
                    {
                        lock (lockCoils)
                            coils[receiveData.StartingAdress + 1] = true;
                    }
                    if (receiveData.ReceiveCoilValues[0] == 0x0000)
                    {
                        lock (lockCoils)
                            coils[receiveData.StartingAdress + 1] = false;
                    }
                }
                if (sendData.ExceptionCode > 0)
                    sendData.Length = 0x03;
                else
                    sendData.Length = 0x06;
            }
           
            
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData ;
                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;



                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.StartingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    if (receiveData.ReceiveCoilValues != null)
                    {
                        byteData = BitConverter.GetBytes((int)receiveData.ReceiveCoilValues[0]);
                        data[10] = byteData[1];
                        data[11] = byteData[0];
                    }
                }


                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }

                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in WriteSingleCoil", ex);
                }
                if (CoilsChanged != null)
                    CoilsChanged(receiveData.UnitIdentifier,receiveData.StartingAdress+1, 1);
            }
        }

        private void WriteSingleRegister(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            sendData.StartingAdress = receiveData.StartingAdress;
            sendData.ReceiveRegisterValues = receiveData.ReceiveRegisterValues;
            if (receiveData.ReceiveRegisterValues != null)
            {
                if ((receiveData.ReceiveRegisterValues[0] < 0x0000) || (receiveData.ReceiveRegisterValues[0] > 0xFFFF))  //Invalid Value
                {
                    sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                    sendData.ExceptionCode = 3;
                }
                if (((receiveData.StartingAdress + 1) > 65535) || (receiveData.StartingAdress < 0))    //Invalid Starting adress or Starting address + quantity
                {
                    sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                    sendData.ExceptionCode = 2;
                }
                if (sendData.ExceptionCode == 0)
                {
                    lock (lockHoldingRegisters)
                        holdingRegisters[receiveData.StartingAdress + 1] = unchecked((short)receiveData.ReceiveRegisterValues[0]);
                }
                if (sendData.ExceptionCode > 0)
                    sendData.Length = 0x03;
                else
                    sendData.Length = 0x06;
            }
           
            
            
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData;
                sendData.Length = (byte)(data.Length - 6);


                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;



                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.StartingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    if (receiveData.ReceiveRegisterValues != null)
                    {
                        byteData = BitConverter.GetBytes((int)receiveData.ReceiveRegisterValues[0]);
                        data[10] = byteData[1];
                        data[11] = byteData[0];
                    }
                    
                }


                try
                {
                    if (SerialFlag && serialport != null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }

                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient?.Send(data, data.Length, endPoint);
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in WriteSingleRegister ", ex);
                }
                if (HoldingRegistersChanged != null)
                    HoldingRegistersChanged(receiveData.UnitIdentifier,receiveData.StartingAdress+1, 1);
            }
        }

        private void WriteMultipleCoils(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            sendData.StartingAdress = receiveData.StartingAdress;
            sendData.Quantity = receiveData.Quantity;
            
            if ((receiveData.Quantity == 0x0000) || (receiveData.Quantity > 0x07B0))  //Invalid Quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if ((((int)receiveData.StartingAdress + 1 + (int)receiveData.Quantity) > 65535)  || (receiveData.StartingAdress < 0))    //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0)
            {
                lock (lockCoils)
                    for (int i = 0; i < receiveData.Quantity; i++)
                    {
                        int shift = i % 16;
                        int mask = 0x1;
                        mask = mask << (shift);
                        if (receiveData.ReceiveCoilValues!=null &&(receiveData.ReceiveCoilValues[i / 16] & (ushort)mask) == 0)
                        
                            coils[receiveData.StartingAdress + i + 1] = false;
                        else
                        
                            coils[receiveData.StartingAdress + i + 1] = true;

                    }
            }
            if (sendData.ExceptionCode > 0)
                sendData.Length = 0x03;
            else
                sendData.Length = 0x06;
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData;
                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;



                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.StartingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    byteData = BitConverter.GetBytes((int)receiveData.Quantity);
                    data[10] = byteData[1];
                    data[11] = byteData[0];
                }


                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }

                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in WriteMultipleCoils", ex);
                }
                if (CoilsChanged != null)
                    CoilsChanged(receiveData.UnitIdentifier,receiveData.StartingAdress+1, receiveData.Quantity);
            }
        }

        private void WriteMultipleRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;
            sendData.StartingAdress = receiveData.StartingAdress;
            sendData.Quantity = receiveData.Quantity;

            if ((receiveData.Quantity == 0x0000) || (receiveData.Quantity > 0x07B0))  //Invalid Quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if ((((int)receiveData.StartingAdress + 1 + (int)receiveData.Quantity) > 65535)  || (receiveData.StartingAdress < 0))   //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0 && receiveData.ReceiveRegisterValues!=null)
            {
                lock (lockHoldingRegisters)
                    for (int i = 0; i < receiveData.Quantity; i++)
                    {
                        holdingRegisters[receiveData.StartingAdress + i + 1] = unchecked((short)receiveData.ReceiveRegisterValues[i]);
                    }
            }
            if (sendData.ExceptionCode > 0)
                sendData.Length = 0x03;
            else
                sendData.Length = 0x06;
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[12 + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData;
                sendData.Length = (byte)(data.Length - 6);

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;



                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }
                else
                {
                    byteData = BitConverter.GetBytes((int)receiveData.StartingAdress);
                    data[8] = byteData[1];
                    data[9] = byteData[0];
                    byteData = BitConverter.GetBytes((int)receiveData.Quantity);
                    data[10] = byteData[1];
                    data[11] = byteData[0];
                }


                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }

                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                        

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                    }
                catch (Exception ex) 
                {
                    _log.Error("Error in WriteMultipleRegisters", ex);
                }
                if (HoldingRegistersChanged != null)
                    HoldingRegistersChanged(receiveData.UnitIdentifier,receiveData.StartingAdress+1, receiveData.Quantity);
            }
        }

        private void ReadWriteMultipleRegisters(ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.FunctionCode = receiveData.FunctionCode;


            if ((receiveData.QuantityRead < 0x0001) || (receiveData.QuantityRead > 0x007D) || (receiveData.QuantityWrite < 0x0001) || (receiveData.QuantityWrite > 0x0079) || (receiveData.ByteCount != (receiveData.QuantityWrite * 2)))  //Invalid Quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 3;
            }
            if ((((int)receiveData.StartingAddressRead + 1 + (int)receiveData.QuantityRead) > 65535) || (((int)receiveData.StartingAddressWrite + 1 + (int)receiveData.QuantityWrite) > 65535) || (receiveData.QuantityWrite < 0) || (receiveData.QuantityRead < 0))    //Invalid Starting adress or Starting address + quantity
            {
                sendData.ErrorCode = (byte)(receiveData.FunctionCode + 0x80);
                sendData.ExceptionCode = 2;
            }
            if (sendData.ExceptionCode == 0 && receiveData.ReceiveRegisterValues!=null)
            {
                sendData.SendRegisterValues = new Int16[receiveData.QuantityRead];
                lock (lockHoldingRegisters)
                    Buffer.BlockCopy(holdingRegisters.LocalArray, receiveData.StartingAddressRead * 2 + 2, sendData.SendRegisterValues, 0, receiveData.QuantityRead * 2);

                lock (holdingRegisters)
                    for (int i = 0; i < receiveData.QuantityWrite; i++)
                    {
                        holdingRegisters[receiveData.StartingAddressWrite + i + 1] = unchecked((short)receiveData.ReceiveRegisterValues[i]);
                    }
                sendData.ByteCount = (byte)(2 * receiveData.QuantityRead);
            }
            if (sendData.ExceptionCode > 0)
                sendData.Length = 0x03;
            else
                sendData.Length = Convert.ToUInt16(3 + 2 * receiveData.QuantityRead);
            if (true)
            {
                Byte[] data;
                if (sendData.ExceptionCode > 0)
                    data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                else
                    data = new byte[9 + sendData.ByteCount + 2 * Convert.ToInt32(SerialFlag)];

                Byte[] byteData;

                //Send Transaction identifier
                byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                data[0] = byteData[1];
                data[1] = byteData[0];

                //Send Protocol identifier
                byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                data[2] = byteData[1];
                data[3] = byteData[0];

                //Send length
                byteData = BitConverter.GetBytes((int)sendData.Length);
                data[4] = byteData[1];
                data[5] = byteData[0];

                //Unit Identifier
                data[6] = sendData.UnitIdentifier;

                //Function Code
                data[7] = sendData.FunctionCode;

                //ByteCount
                data[8] = sendData.ByteCount;


                if (sendData.ExceptionCode > 0)
                {
                    data[7] = sendData.ErrorCode;
                    data[8] = sendData.ExceptionCode;
                    sendData.SendRegisterValues = null;
                }
                else
                {
                    if (sendData.SendRegisterValues != null)
                        for (int i = 0; i < (sendData.ByteCount / 2); i++)
                        {
                            byteData = BitConverter.GetBytes(sendData.SendRegisterValues[i]);
                            data[9 + i * 2] = byteData[1];
                            data[10 + i * 2] = byteData[0];
                        }

                }


                try
                {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }

                    }
                    else if (UDPFlag)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        if (udpClient != null)
                        {
                            udpClient.Send(data, data.Length, endPoint);
                        }
                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                }
                catch (Exception ex) 
                {
                    _log.Error("Error in ReadWriteMultipleRegisters", ex);
                }
                if (HoldingRegistersChanged != null)
                    HoldingRegistersChanged(receiveData.UnitIdentifier,receiveData.StartingAddressWrite+1, receiveData.QuantityWrite);
            }
        }

        public void sendException(int errorCode, int exceptionCode, ModbusProtocol receiveData, ModbusProtocol sendData, NetworkStream stream, int portIn, IPAddress ipAddressIn)
        {
            sendData.Response = true;

            sendData.TransactionIdentifier = receiveData.TransactionIdentifier;
            sendData.ProtocolIdentifier = receiveData.ProtocolIdentifier;

            sendData.UnitIdentifier = receiveData.UnitIdentifier;
            sendData.ErrorCode = (byte)errorCode;
            sendData.ExceptionCode = (byte)exceptionCode;

             if (sendData.ExceptionCode > 0)
                sendData.Length = 0x03;
            else
                sendData.Length = (ushort)(0x03 + sendData.ByteCount);

             if (true)
             {
                 Byte[] data;
                 if (sendData.ExceptionCode > 0)
                     data = new byte[9 + 2 * Convert.ToInt32(SerialFlag)];
                 else
                     data = new byte[9 + sendData.ByteCount + 2 * Convert.ToInt32(SerialFlag)];
                 Byte[] byteData;
                 sendData.Length = (byte)(data.Length - 6);

                 //Send Transaction identifier
                 byteData = BitConverter.GetBytes((int)sendData.TransactionIdentifier);
                 data[0] = byteData[1];
                 data[1] = byteData[0];

                 //Send Protocol identifier
                 byteData = BitConverter.GetBytes((int)sendData.ProtocolIdentifier);
                 data[2] = byteData[1];
                 data[3] = byteData[0];

                 //Send length
                 byteData = BitConverter.GetBytes((int)sendData.Length);
                 data[4] = byteData[1];
                 data[5] = byteData[0];

                 //Unit Identifier
                 data[6] = sendData.UnitIdentifier;


                 data[7] = sendData.ErrorCode;
                 data[8] = sendData.ExceptionCode;


                 try
                 {
                    if (SerialFlag && serialport!=null)
                    {
                        if (!serialport.IsOpen)
                            throw new EasyModbus.Exceptions.SerialPortNotOpenedException("serial port not opened");
                        //Create CRC
                        sendData.Crc = EasyModbus.ModbusClient.calculateCRC(data, Convert.ToUInt16(data.Length - 8), 6);
                        byteData = BitConverter.GetBytes((int)sendData.Crc);
                        data[data.Length - 2] = byteData[0];
                        data[data.Length - 1] = byteData[1];
                        serialport.Write(data, 6, data.Length - 6);
                        if (debug)
                        {
                            byte[] debugData = new byte[data.Length - 6];
                            Array.Copy(data, 6, debugData, 0, data.Length - 6);
                            if (debug) StoreLogData.Instance.Store("Send Serial-Data: " + BitConverter.ToString(debugData), System.DateTime.Now);
                        }
                    }
                    else if (UDPFlag && udpClient!=null)
                    {
                        IPEndPoint endPoint = new IPEndPoint(ipAddressIn, portIn);
                        udpClient.Send(data, data.Length, endPoint);

                    }
                    else
                    {
                        stream.Write(data, 0, data.Length);
                        if (debug) StoreLogData.Instance.Store("Send Data: " + BitConverter.ToString(data), System.DateTime.Now);
                    }
                 }
                 catch (Exception ex) 
                {
                    _log.Error("Error in sendException", ex);
                }
             }
        }

        private void CreateLogData(ModbusProtocol receiveData, ModbusProtocol sendData)
        {
            for (int i = 0; i < 98; i++)
            {
                modbusLogData[99 - i] = modbusLogData[99 - i - 2];

            }
            modbusLogData[0] = receiveData;
            modbusLogData[1] = sendData;

        }



        public int NumberOfConnections
        {
            get
            {
                return numberOfConnections;
            }
        }

        public ModbusProtocol[] ModbusLogData
        {
            get
            {
                return modbusLogData;
            }
        }



        public bool UDPFlag { get; set; }

        public bool SerialFlag { get; set; }

        public int Baudrate { get; set; } = 9600;

        public System.IO.Ports.Parity Parity { get; set; } = Parity.Even;

        public System.IO.Ports.StopBits StopBits { get; set; } = StopBits.One;

        public string SerialPort
        {
            get
            {
                return serialPort;
            }
            set
            {
                serialPort = value;
                if (serialPort != null)
                    SerialFlag = true;
                else
                    SerialFlag = false;
            }
        }

        public byte UnitIdentifier { get; set; }




        /// <summary>
        /// Gets or Sets the Filename for the LogFile
        /// </summary>
        public string LogFileFilename
        {
            get
            {
                return StoreLogData.Instance.Filename;
            }
            set
            {
                StoreLogData.Instance.Filename = value;
                if (StoreLogData.Instance.Filename != null)
                    debug = true;
                else
                    debug = false;
            }
        }




    public class HoldingRegisters
    {
        public Int16[] LocalArray { get; set; } = new Int16[65535];
            readonly ModbusServer modbusServer;
     
       

            public HoldingRegisters(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public Int16 this[int x]
        {
            get { return this.LocalArray[x]; }
            set
            {              
                this.LocalArray[x] = value;
                
            }
        }
    }

    public class InputRegisters
    {
        public Int16[] LocalArray { get; set; } = new Int16[65535];
            readonly ModbusServer modbusServer;

        

            public InputRegisters(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public Int16 this[int x]
        {
            get { return this.LocalArray[x]; }
            set
            {
                this.LocalArray[x] = value;

            }
        }
    }

    public class Coils
    {
        public bool[] LocalArray { get; set; } = new bool[65535];
            readonly ModbusServer modbusServer;

        

            public Coils(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public bool this[int x]
        {
            get { return this.LocalArray[x]; }
            set
            {
                this.LocalArray[x] = value;
            
            }
        }
    }

    public class DiscreteInputs
    {
        public bool[] LocalArray { get; set; } = new bool[65535];
            readonly ModbusServer modbusServer;

        

            public DiscreteInputs(ModbusServer modbusServer)
            {
                this.modbusServer = modbusServer;
            }

            public bool this[int x]
        {
            get { return this.LocalArray[x]; }
            set
            {
                this.LocalArray[x] = value;
              
            }
        }

      
        }
    }
}
   