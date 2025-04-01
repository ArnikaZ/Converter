using EasyModbus;
using MineEyeConverter.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    /// Main server class that coordinates communication between Modbus TCP clients and RTU/Serial devices.
    /// Manages the TCP server, RTU client, and handles data transfer between them.
    /// </summary>
    public class ModbusTcpServer
    {
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(ModbusTcpServer));
        private readonly ModbusServer _tcpServer; 
        private readonly ClientHandler _rtuClient; 
        private readonly Dictionary<byte, ModbusSlaveDevice> _slaveDevices; //klucz - adres urządzenia, wartość - obiekt slave
        private bool _isRunning; 
        private readonly object _syncLock = new object();

        private List<Client> modbusClientAccounts { get; set; }
        public IOperationModeHandler OperationModeHandler { get; set; }

        private int _previousConnectionCount = -1;



        public ModbusTcpServer(string instanceName, bool useWhiteList=false)
        {
            _log.InfoFormat("Initializing Modbus server for instance '{0}'", instanceName);

            var config = ConfigLoader.LoadInstanceConfiguration(instanceName);
            var instanceConfig = config.Instances.FirstOrDefault();
           
            if (instanceConfig == null)
            {
                _log.ErrorFormat("Instance '{0}' not found in configuration", instanceName);
                throw new InvalidOperationException($"Instance '{instanceName}' not found in configuration");
            }
            int listeningPort = instanceConfig.ListeningPort;
            _log.InfoFormat("Setting up TCP server with listening port: {0}", listeningPort);
            string connectionType = instanceConfig.ConnectionType;
            RtuSettings rtuSettings = instanceConfig.RtuSettings;
            modbusClientAccounts = instanceConfig.ClientWhiteList.Clients;
            string operationMode = instanceConfig.OperationMode;
            _log.InfoFormat("Operation mode: {0}", operationMode);
            switch (operationMode.ToLower())
            {
                case "auto":
                    OperationModeHandler = new AutoModeHandler();
                    break;
                case "manual":
                    OperationModeHandler = new ManualModeHandler();
                    break;
                default:
                    _log.ErrorFormat("Unknown operation mode {0}", operationMode);
                    OperationModeHandler = new AutoModeHandler();
                    break;
            }

            _tcpServer = new ModbusServer
            {
                LocalIPAddress = IPAddress.Any,
                Port = listeningPort,
                UseWhiteList = useWhiteList,
                WhiteList = modbusClientAccounts,
                FunctionCode1Disabled = false,
                FunctionCode2Disabled = false,
                FunctionCode3Disabled = false,
                FunctionCode4Disabled = false,
                FunctionCode5Disabled = false,
                FunctionCode6Disabled = false,
                FunctionCode15Disabled = false,
                FunctionCode16Disabled = false
            };


            if (connectionType.Equals("COM", StringComparison.OrdinalIgnoreCase))
            {
                _rtuClient = new ClientHandler(OperationModeHandler, _tcpServer)
                {
                    SerialDataProvider = new SerialProvider
                    {
                        SerialName = rtuSettings.PortName,
                        BaudRate = rtuSettings.BaudRate.HasValue ? rtuSettings.BaudRate.Value : 9600,
                        PortParity = ParseParity(rtuSettings.Parity),
                        DataBits = rtuSettings.DataBits.HasValue ? rtuSettings.DataBits.Value : 8,
                        StopBits = rtuSettings.StopBits.HasValue ? ParseStopBits(rtuSettings.StopBits.Value) : StopBits.One   
                    }
                };
                _log.InfoFormat("COM provider: {0}", rtuSettings.PortName);
            }
            else if (connectionType.Equals("RtuOverTcp", StringComparison.OrdinalIgnoreCase))
            {
                _rtuClient = new ClientHandler(OperationModeHandler, _tcpServer)
                {
                    TcpDataProvider = new TcpProvider
                    {
                        Ip = rtuSettings.IpAddress,
                        Port = rtuSettings.Port.HasValue ? rtuSettings.Port.Value : 503
                    }
                };
                _log.InfoFormat("Tcp provider: {0} {1}", rtuSettings.IpAddress, rtuSettings.Port);
            }
            else
            {
                _log.ErrorFormat("Unsupported connection type: {0}", connectionType);
                throw new ArgumentException($"Unsupported connection type: {connectionType}");
            }


            _slaveDevices = new Dictionary<byte, ModbusSlaveDevice>();
            if (instanceConfig.SlaveDeviceList != null && instanceConfig.SlaveDeviceList.Slaves != null)
            {
                foreach (var slaveConfig in instanceConfig.SlaveDeviceList.Slaves)
                {
                    byte unitId = (byte)slaveConfig.UnitId;
                    AddSlaveDevice(unitId);
                }
            }
            else
            {
                _log.Warn("No slave devices configuration found.");
            }

            _tcpServer.CoilsChanged += HandleCoilsChanged;
            _tcpServer.HoldingRegistersChanged += HandleHoldingRegistersChanged;
            _tcpServer.NumberOfConnectedClientsChanged += HandleClientConnectionChanged;
            _tcpServer.OperationModeHandler = OperationModeHandler;
            
        }

        private static Parity ParseParity(string parity)
        {
            return parity switch
            {
                "None" => Parity.None,
                "Even" => Parity.Even,
                "Odd" => Parity.Odd,
                "Mark" => Parity.Mark,
                "Space" => Parity.Space,
                _ => Parity.None,
            };
        }
        private static StopBits ParseStopBits(int stopBits)
        {
            return stopBits switch
            {
                1 => StopBits.One,
                2 => StopBits.Two,
                _ => StopBits.One,
            };
        }

        public void AddSlaveDevice(byte unitId)
        {
            var slave = new ModbusSlaveDevice(unitId);
            _slaveDevices[unitId] = slave;
            _rtuClient.SlaveList.Add(slave);
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _tcpServer.Listen();
            
            _log.InfoFormat("TCP server is listening on port {0}", _tcpServer.Port);
            Task.Run(() => _rtuClient.Start());
          
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _tcpServer.StopListening();
            _rtuClient.Stop();
            _log.Info("Server stopped");
        }
        
        private void HandleCoilsChanged(byte slaveId, int coil, int numberOfPoints)
        {
            try
            {
                lock (_syncLock)
                {

                    OperationModeHandler.HandleCoilsChanged(slaveId, coil, numberOfPoints, _tcpServer, _rtuClient, _slaveDevices);

                }

            }
            catch (Exception ex)
            {
                _log.ErrorFormat("Error handling coils change: {0}", ex.Message);
            }
        }

        private void HandleHoldingRegistersChanged(byte slaveId, int register, int numberOfPoints)
        {
            try
            {
                lock (_syncLock)
                {
                    OperationModeHandler.HandleHoldingRegistersChanged(slaveId, register, numberOfPoints, _tcpServer, _rtuClient, _slaveDevices);
                    
                   
                }

            }
            catch (Exception ex)
            {
                _log.ErrorFormat("Error handling holding registers change: {0}", ex.Message);
            }
        }

        private void HandleClientConnectionChanged()
        {
            int currentCount = _tcpServer.NumberOfConnections;
            if (currentCount != _previousConnectionCount)
            {
                _previousConnectionCount = currentCount;
                _log.InfoFormat("TCP client connection count changed. Current count: {0}", currentCount);
            }
        }

    }
}