using EasyModbus;
using NModbus;
using NModbus.IO;
using NModbus.Serial;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    ///  Handles client-side communication with Modbus RTU devices over serial or TCP connections.
    /// Responsible for reading from and writing to Modbus slave devices.
    /// </summary>
    public class ClientHandler : IDisposable
    {
        private log4net.ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        
        public bool Communicate { get; set; } //flag indicating whether communication with slave devices should continue.
        private bool _disposed;

        public ICollection<ModbusSlaveDevice> SlaveList { get; set; } // collection of Modbus slave devices that this client can communicate with.

        private ModbusFactory factory;

        private IModbusMaster master;
        private SerialPort serialPort;
        private SerialPortAdapter _serialPortAdapter;

        private TcpClient tcpClient;
        private TcpClientAdapter _tcpClientAdapter;

        private TcpProvider _tcpProvider;
        private SerialProvider _serialProvider;
        private bool isConnected;
        private IOperationModeHandler operationModeHandler;

        private ModbusServer _server;
        public virtual IProvider DataProvider //Gets or sets the active data provider (TCP or Serial) used for communication.
        {
            get
            {
                if (_tcpProvider != null)
                    return _tcpProvider;
                return _serialProvider;
            }
            set
            {
                if (value is TcpProvider provider)
                {
                    _tcpProvider = provider;
                }
                if (value is SerialProvider provider1)
                {
                    _serialProvider = provider1;
                }
            }
        }

        public virtual IProvider TcpDataProvider
        {
            get { return _tcpProvider; }
            set { _tcpProvider = (TcpProvider)value; }
        }

        public virtual IProvider SerialDataProvider
        {
            get { return _serialProvider; }
            set { _serialProvider = (SerialProvider)value; }
        }

      

        public ClientHandler(IOperationModeHandler operationMode, ModbusServer server)
        {
            SlaveList = new List<ModbusSlaveDevice>();
            factory = new ModbusFactory();
            Communicate = true;
            operationModeHandler = operationMode;
            _server = server;
        }

        
        public void Stop()
        {
            Communicate = false;
            isConnected = false;
            Log.Info("Communication stopped");
        }
        /// <summary>
        /// Starts communication with slave devices.
        /// Establishes connection using the configured provider (TCP or Serial) and
        /// continuously polls slave devices for data as long as the Communicate flag is true.
        /// </summary>
        public void Start()
        {
            Communicate = true;
            if(factory is null)
            {
                factory = new ModbusFactory();
            }
            if(Log is null)
            {
                Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
            }
            while (Communicate)
            {
                try
                {
                    if (DataProvider is TcpProvider provider)
                    {
                        master?.Dispose();
                        tcpClient?.Close();
                        _tcpClientAdapter?.Dispose();
                        tcpClient = new TcpClient(provider.Ip, provider.Port);
                        _tcpClientAdapter = new TcpClientAdapter(tcpClient);
                        master = factory.CreateRtuMaster(_tcpClientAdapter);
                        Log.DebugFormat("TCP connection established with {0} : {1}", provider.Ip, provider.Port);
                    }
                    else
                    {
                        if (DataProvider is SerialProvider provider1)
                        {
                            master?.Dispose();
                            serialPort?.Dispose();
                            _serialPortAdapter?.Dispose();
                            serialPort = new SerialPort(provider1.SerialName, provider1.BaudRate, provider1.PortParity, provider1.DataBits, provider1.StopBits);
                            _serialPortAdapter = new SerialPortAdapter(serialPort);
                            serialPort.Open();
                            var transport = factory.CreateRtuTransport(_serialPortAdapter);
                            master = factory.CreateMaster(transport);
                            Log.DebugFormat("Serial port {0} opened", provider1.SerialName);
                        }
                    }
                    if (master is null)
                    {
                        Log.Warn("Master is null, retrying connection in 5 seconds.");
                        isConnected = false;
                        Task.Delay(5000).Wait();
                        continue;
                    }
                    master.Transport.Retries = 5;
                    master.Transport.ReadTimeout = 1000;
                    master.Transport.WriteTimeout = 1000;
                    master.Transport.StreamResource.DiscardInBuffer();
                    isConnected = true;
                    

                    while (isConnected)
                    {
                        try
                        {
                            var slave = SlaveList.FirstOrDefault(s => s.UnitId == _server.CurrentUnitIdentifier);
                            if (slave != null)
                                try
                                {
                                    switch (_server.FunctionCode)
                                    {
                                        case 3:
                                            try
                                            {
                                                operationModeHandler.ReadHoldingRegisters(slave, master, _server);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.ErrorFormat("Error reading holding registers from device {0}: {1}", slave.UnitId, ex.Message);
                                            }
                                            break;
                                        case 4:
                                            try
                                            {
                                                operationModeHandler.ReadInputRegisters(slave, master, _server);

                                            }
                                            catch (Exception ex)
                                            {
                                                Log.ErrorFormat("Error reading input registers from device {0}: {1}", slave.UnitId, ex.Message);
                                            }
                                            break;
                                        case 1:
                                            try
                                            {
                                                operationModeHandler.ReadCoils(slave, master, _server);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.ErrorFormat("Error reading coils from device {0}: {1}", slave.UnitId, ex.Message);
                                            }
                                            break;
                                    }

                                    // Delay between readings of the next device
                                    Task.Delay(100).Wait();
                                }
                                catch (Exception slaveEx)
                                {
                                    Log.ErrorFormat("Error communicating with slave {0}: {1}", slave.UnitId, slaveEx.Message);
                                    isConnected = false;
                                    break;
                                }
                            // Delay between complete reading cycles.
                            Task.Delay(250).Wait();
                        }
                        catch (Exception mainEx)
                        {
                            isConnected = false;
                            Log.Error(mainEx);
                            Task.Delay(1000).Wait();
                        }
                    }
                }
                catch (Exception exc)
                {
                    Log.Error(exc);
                }
                Task.Delay(5000).Wait();
            }
        }


        public bool WriteRegister(byte address, ushort startRegister, ushort value)
        {
            try
            {
                if (master is null)
                {
                    Log.Error("WriteRegister master is null");
                    try
                    {
                        if (DataProvider is TcpProvider provider)
                        {
                            master?.Dispose();
                            tcpClient?.Close();
                            _tcpClientAdapter?.Dispose();
                            tcpClient = new TcpClient(provider.Ip, provider.Port);
                            _tcpClientAdapter = new TcpClientAdapter(tcpClient);
                            master = factory.CreateRtuMaster(_tcpClientAdapter);
                        }
                        else if (DataProvider is SerialProvider provider1)
                        {
                            master?.Dispose();
                            serialPort?.Close();
                            _serialPortAdapter?.Dispose();
                            serialPort = new SerialPort(provider1.SerialName, provider1.BaudRate, provider1.PortParity, provider1.DataBits, provider1.StopBits);
                            _serialPortAdapter = new SerialPortAdapter(serialPort);
                            serialPort.Open();
                            var transport = factory.CreateRtuTransport(_serialPortAdapter);
                            master = factory.CreateMaster(transport);
                        }
                        else
                        {
                            return false;
                        }

                        master.Transport.Retries = 5;
                        master.Transport.ReadTimeout = 100;
                        master.Transport.WriteTimeout = 100;
                        master.Transport.StreamResource.DiscardInBuffer();
                    }
                    catch (Exception exc2)
                    {
                        Log.Error(exc2);
                        return false;
                    }
                }

                if (isConnected)
                {
                    if(master!=null)
                    {
                        operationModeHandler.WriteSingleRegister(master, address, startRegister, value);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception exc)
            {
                Log.Error(exc);
                return false;
            }
        }

        public bool WriteMultipleRegisters(byte address, ushort startAddress, ushort[] values)
        {
            try
            {
                if (master is null)
                {
                    Log.Error("WriteRegister master is null");
                    try
                    {
                        if (DataProvider is TcpProvider provider)
                        {
                            master?.Dispose();
                            tcpClient?.Close();
                            _tcpClientAdapter?.Dispose();
                            tcpClient = new TcpClient(provider.Ip, provider.Port);
                            _tcpClientAdapter = new TcpClientAdapter(tcpClient);
                            master = factory.CreateRtuMaster(_tcpClientAdapter);
                        }
                        else if (DataProvider is SerialProvider provider1)
                        {
                            master?.Dispose();
                            serialPort?.Close();
                            _serialPortAdapter?.Dispose();
                            serialPort = new SerialPort(provider1.SerialName, provider1.BaudRate, provider1.PortParity, provider1.DataBits, provider1.StopBits);
                            _serialPortAdapter = new SerialPortAdapter(serialPort);
                            serialPort.Open();
                            var transport = factory.CreateRtuTransport(_serialPortAdapter);
                            master = factory.CreateMaster(transport);
                        }
                        else
                        {
                            return false;
                        }

                        master.Transport.Retries = 5;
                        master.Transport.ReadTimeout = 100;
                        master.Transport.WriteTimeout = 100;
                        master.Transport.StreamResource.DiscardInBuffer();
                    }
                    catch (Exception exc2)
                    {
                        Log.Error(exc2);
                        return false;
                    }
                }

                if (isConnected)
                {
                    if (master != null)
                    {
                        operationModeHandler.WriteMultipleRegisters(master, address, startAddress, values);
                        return true;
                    }

                }
                return false;
            }
            catch (Exception exc)
            {
                Log.Error(exc);
                return false;
            }
        }

        public bool WriteSingleCoil(byte address, ushort coilAddress, bool value)
        {
            try
            {
                if (master is null)
                {
                    Log.Error("WriteRegister master is null");
                    try
                    {
                        if (DataProvider is TcpProvider provider)
                        {
                            master?.Dispose();
                            tcpClient?.Close();
                            _tcpClientAdapter?.Dispose();
                            tcpClient = new TcpClient(provider.Ip, provider.Port);
                            _tcpClientAdapter = new TcpClientAdapter(tcpClient);
                            master = factory.CreateRtuMaster(_tcpClientAdapter);
                        }
                        else if (DataProvider is SerialProvider provider1)
                        {
                            master?.Dispose();
                            serialPort?.Close();
                            _serialPortAdapter?.Dispose();
                            serialPort = new SerialPort(provider1.SerialName, provider1.BaudRate, provider1.PortParity, provider1.DataBits, provider1.StopBits);
                            _serialPortAdapter = new SerialPortAdapter(serialPort);
                            serialPort.Open();
                            var transport = factory.CreateRtuTransport(_serialPortAdapter);
                            master = factory.CreateMaster(transport);
                        }
                        else
                        {
                            return false;
                        }

                        master.Transport.Retries = 5;
                        master.Transport.ReadTimeout = 100;
                        master.Transport.WriteTimeout = 100;
                        master.Transport.StreamResource.DiscardInBuffer();
                    }
                    catch (Exception exc2)
                    {
                        Log.Error(exc2);
                        return false;
                    }
                }

                if (isConnected)
                {
                    if (master != null)
                    {
                        operationModeHandler.WriteSingleCoil(master, address, coilAddress, value);
                        return true;
                    }
                    
                }
                return false;
            }
            catch (Exception exc)
            {
                Log.Error(exc);
                return false;
            }
        }

        public bool WriteMultipleCoils(byte address, ushort startAddress, bool[] values)
        {
            try
            {
                if (master is null)
                {
                    Log.Error("WriteRegister master is null");
                    try
                    {
                        if (DataProvider is TcpProvider provider)
                        {
                            master?.Dispose();
                            tcpClient?.Close();
                            _tcpClientAdapter?.Dispose();
                            tcpClient = new TcpClient(provider.Ip, provider.Port);
                            _tcpClientAdapter = new TcpClientAdapter(tcpClient);
                            master = factory.CreateRtuMaster(_tcpClientAdapter);
                        }
                        else if (DataProvider is SerialProvider provider1)
                        {
                            master?.Dispose();
                            serialPort?.Close();
                            _serialPortAdapter?.Dispose();
                            serialPort = new SerialPort(provider1.SerialName, provider1.BaudRate, provider1.PortParity, provider1.DataBits, provider1.StopBits);
                            _serialPortAdapter = new SerialPortAdapter(serialPort);
                            serialPort.Open();
                            var transport = factory.CreateRtuTransport(_serialPortAdapter);
                            master = factory.CreateMaster(transport);
                        }
                        else
                        {
                            return false;
                        }

                        master.Transport.Retries = 5;
                        master.Transport.ReadTimeout = 100;
                        master.Transport.WriteTimeout = 100;
                        master.Transport.StreamResource.DiscardInBuffer();
                    }
                    catch (Exception exc2)
                    {
                        Log.Error(exc2);
                        return false;
                    }
                }

                if (isConnected)
                {
                    if (master != null)
                    {
                        operationModeHandler.WriteMultipleCoils(master, address, startAddress, values);
                        return true;
                    }
                    
                }
                return false;
            }
            catch (Exception exc)
            {
                Log.Error(exc);
                return false;
            }
        }

        ~ClientHandler()
        {
            Dispose(false);
        }

        private void ReleaseUnmanagedResources()
        {
            Communicate = false;
            master?.Dispose();
            tcpClient?.Close();
            _tcpClientAdapter?.Dispose();
            serialPort?.Close();
            _serialPortAdapter?.Dispose();
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                ReleaseUnmanagedResources();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
