using EasyModbus;
using NModbus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EasyModbus.ModbusServer;
using System.Xml.Linq;
using NModbus.IO;
using NModbus.Serial;
using System.IO.Ports;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using static System.Runtime.InteropServices.JavaScript.JSType;
using log4net;

namespace MineEyeConverter
{
    public class LearningModeHandler 
    {
        private readonly IModbusFactory factory;
        private IModbusMaster ?master;
        private readonly List<byte> _slaveIds;
        private readonly string xmlFilePath;

        private SerialPort? serialPort;
        private SerialPortAdapter? _serialPortAdapter;
        private TcpClient? tcpClient;
        private TcpClientAdapter? _tcpClientAdapter;

        private readonly ILog _log = LogManager.GetLogger(typeof(LearningModeHandler));
        public LearningModeHandler(string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                throw new ArgumentException("Instance name must not be null or empty.", nameof(instanceName));
            }
            var config = Config.ConfigLoader.LoadInstanceConfiguration(instanceName);
            var instanceConfig = config.Instances.FirstOrDefault();
            if (instanceConfig == null)
            {
                _log.ErrorFormat("Instance configuration not found for '{0}'", instanceName);
                throw new KeyNotFoundException($"Instance configuration not found for '{instanceName}'");
            }
            factory = new ModbusFactory();
            _slaveIds = [];
            xmlFilePath = instanceName + ".xml";

            switch (instanceConfig.ConnectionType.ToLower())
            {
                case "com":
                    InitializeComConnection(instanceConfig.RtuSettings);
                    break;
                case "rtuovertcp":
                    InitializeRtuOverTcpConnection(instanceConfig.RtuSettings);
                    break;
                default:
                    _log.ErrorFormat("Unsupported connection type: {0}", instanceConfig.ConnectionType);
                    throw new ArgumentException($"Unsupported connection type: {instanceConfig.ConnectionType}");
            }
            InitializeSlaveList(instanceConfig);
    
        }

        private Instance GetInstanceConfig(Configuration config, string instanceName)
        {
            var instanceConfig = config.Instances
                .FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));

            if (instanceConfig == null)
            {
                _log.ErrorFormat("Instance '{0}' not found in configuration", instanceName);
                throw new KeyNotFoundException($"Instance '{instanceName}' not found in configuration.");
            }

            return instanceConfig;
        }
        private void InitializeRtuOverTcpConnection(RtuSettings rtuSettings)
        {
            if (string.IsNullOrWhiteSpace(rtuSettings.IpAddress))
            {
                throw new ArgumentException("IpAddress must be specified for RtuOverTcp connection.");
            }

            int port = rtuSettings.Port ?? 502;
            master?.Dispose();
            tcpClient?.Dispose();
            _tcpClientAdapter?.Dispose();
            tcpClient = new TcpClient(rtuSettings.IpAddress, port);
            _tcpClientAdapter = new TcpClientAdapter(tcpClient);
            master = factory.CreateRtuMaster(_tcpClientAdapter);
            _log.InfoFormat("Tcp provider: {0} {1}", rtuSettings.IpAddress, port);
        }

        private void InitializeComConnection(RtuSettings rtuSettings)
        {
            if (string.IsNullOrWhiteSpace(rtuSettings.PortName))
            {
                throw new ArgumentException("PortName must be specified for COM connection.");
            }
            int baudRate = rtuSettings.BaudRate ?? 9600;
            Parity parity = ParseParity(rtuSettings.Parity);
            int dataBits = rtuSettings.DataBits ?? 8;
            StopBits stopBits = rtuSettings.StopBits.HasValue ? ParseStopBits(rtuSettings.StopBits.Value) : StopBits.One;

            master?.Dispose();
            serialPort?.Dispose();
            _serialPortAdapter?.Dispose();
            serialPort = new SerialPort(rtuSettings.PortName, baudRate, parity, dataBits, stopBits);
            _serialPortAdapter = new SerialPortAdapter(serialPort);
            serialPort.Open();
            var transport = factory.CreateRtuTransport(_serialPortAdapter);
            master = factory.CreateMaster(transport);
            _log.InfoFormat("COM provider: {0}", rtuSettings.PortName);
        }
        private void InitializeSlaveList(Instance instanceConfig)
        {
            if (instanceConfig.SlaveDeviceList != null && instanceConfig.SlaveDeviceList.Slaves != null)
            {
                foreach (var slaveConfig in instanceConfig.SlaveDeviceList.Slaves)
                {
                    byte unitId = (byte)slaveConfig.UnitId;
                    _slaveIds.Add(unitId);
                }
            }
            else
            {
                _log.Warn("No slave devices configuration found.");
            }
        }
        public List<SlaveConfiguration> DiscoverSlaves()
        {
            
            List<SlaveConfiguration> configs = new();
            if (master == null)
            {
                _log.Error("Master instance is null. Cannot discover slaves.");
                return configs;
            }
            ushort maxHoldingAddress = 100;  // 65535 zakres adresów
            ushort holdingBlockSize = 10;
            ushort maxInputAddress = 100;
            ushort inputBlockSize = 10;
            ushort maxCoilsAddress = 100;
            ushort coilsBlockSize = 10;

            foreach (byte slaveId in _slaveIds)
            {
                SlaveConfiguration config = new SlaveConfiguration { SlaveId = slaveId };
                for(ushort start = 0; start < maxHoldingAddress; start += holdingBlockSize)
                {
                    try
                    {
                        ushort[] data = master.ReadHoldingRegisters(slaveId, start, holdingBlockSize); //próba odczytu bloku rejestrów holding
                        if (data != null && data.Length > 0)
                        {
                            // Dodaj blok, jeśli nie istnieje 
                            bool exists = config.HoldingRegisters.Any(r =>
                                r.SlaveId==slaveId &&
                                r.StartAddress == start && 
                                r.Quantity == holdingBlockSize && 
                                r.functionCode == 3);
                            if (!exists)
                            {
                                config.HoldingRegisters.Add(new ModbusRegister
                                {
                                    SlaveId = slaveId,
                                    StartAddress = start,
                                    Quantity = holdingBlockSize,
                                    IsActive = true,
                                    functionCode = 3,
                                    AccessMode = "W"
                                });
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        _log.ErrorFormat("SlaveId: {0}: no holding register data available for block starting at {1}. Exception: {2}", slaveId, start, ex);
                    }
                }
               
                for(ushort start = 0; start < maxInputAddress; start += inputBlockSize)
                {
                    try
                    {
                        ushort[] data = master.ReadInputRegisters(slaveId, start, inputBlockSize);
                        if (data != null && data.Length > 0)
                        {
                            bool exists = config.InputRegisters.Any(r =>
                                r.SlaveId==slaveId &&
                                r.StartAddress == start && 
                                r.Quantity == inputBlockSize && 
                                r.functionCode == 4);
                            if (!exists)
                            {
                                config.InputRegisters.Add(new ModbusRegister
                                {
                                    SlaveId = slaveId,
                                    StartAddress = start,
                                    Quantity = inputBlockSize,
                                    IsActive = true,
                                    functionCode = 4,
                                    AccessMode = "R"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.ErrorFormat("SlaveId: {0}: no input register data available for block starting at {1}. Exception: {2}", slaveId, start, ex);
                    }
                }
                for(ushort start = 0; start < maxCoilsAddress; start += coilsBlockSize)
                {
                    try
                    {
                        bool[] data = master.ReadCoils(slaveId, start, coilsBlockSize);
                        if (data != null && data.Length > 0)
                        {
                            bool exists = config.Coils.Any(r =>
                                r.SlaveId==slaveId &&
                                r.StartAddress == start && 
                                r.Quantity == coilsBlockSize && 
                                r.functionCode == 1);
                            if (!exists)
                            {
                                config.Coils.Add(new ModbusRegister
                                {
                                    SlaveId = slaveId,
                                    StartAddress = start,
                                    Quantity = coilsBlockSize,
                                    IsActive = true,
                                    functionCode = 1,
                                    AccessMode = "W"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.ErrorFormat("SlaveId: {0}: no coil data available for block starting at {1}. Exception: {2}", slaveId, start, ex);
                    }
                }
                
                configs.Add(config);
            }
            return configs;
        }

        public void SaveConfigurationToXml(List<SlaveConfiguration> configs)
        {
            XElement root = new XElement("RegisterManager");

            var holdingElements = new XElement("HoldingRegisters",
                from config in configs
                from reg in config.HoldingRegisters
                select new XElement("ModbusRegister",
                    new XElement("SlaveId", config.SlaveId),
                    new XElement("StartAddress", reg.StartAddress),
                    new XElement("Quantity", reg.Quantity),
                    new XElement("IsActive", "true"),
                    new XElement("functionCode", reg.functionCode),
                    new XElement("AccessMode", reg.AccessMode)
                )
            );

            var inputElements = new XElement("InputRegisters",
                from config in configs
                from reg in config.InputRegisters
                select new XElement("ModbusRegister",
                    new XElement("SlaveId", config.SlaveId),
                    new XElement("StartAddress", reg.StartAddress),
                    new XElement("Quantity", reg.Quantity),
                    new XElement("IsActive", "true"),
                    new XElement("functionCode", reg.functionCode),
                    new XElement("AccessMode", reg.AccessMode)
                )
            );

            var coilsElements = new XElement("Coils",
                from config in configs
                from reg in config.Coils
                select new XElement("ModbusRegister",
                    new XElement("SlaveId", config.SlaveId),
                    new XElement("StartAddress", reg.StartAddress),
                    new XElement("Quantity", reg.Quantity),
                    new XElement("IsActive", "true"),
                    new XElement("functionCode", reg.functionCode),
                    new XElement("AccessMode", reg.AccessMode)
                )
            );

            root.Add(holdingElements);
            root.Add(inputElements);
            root.Add(coilsElements);

            XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);

            try
            {
                doc.Save(xmlFilePath);
                _log.InfoFormat("Configuration saved to XML file: {0} ", xmlFilePath);
            }
            catch (Exception ex)
            {
                _log.ErrorFormat("Error in writing to file: {0}", ex.Message);
            }
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
                _ => throw new ArgumentException($"Invalid parity value: {parity}")
            };
        }
        private static StopBits ParseStopBits(int stopBits)
        {
            return stopBits switch
            {
                1 => StopBits.One,
                2 => StopBits.Two,
                _ => throw new ArgumentException($"Invalid stop bits value: {stopBits}")
            };
        }

      
    }
}
