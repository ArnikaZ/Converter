using EasyModbus;
using EasyModbus.Exceptions;
using NModbus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    /// Implements the Manual operation mode which only allows access to registers explicitly defined in configuration.
    /// </summary>
    public class ManualModeHandler : IOperationModeHandler
    {
        private RegisterManager registerManager;
        private HashSet<string> _reportedErrorMessages = new HashSet<string>();
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private readonly string filePath = "registers.xml";
        public ManualModeHandler()
        {
            registerManager = RegisterManager.Instance;
            registerManager.LoadFromFile(filePath);
        }

        public void HandleCoilsChanged(byte slaveId, int coil, int numberOfPoints, ModbusServer tcpServer, ClientHandler rtuClient, Dictionary<byte, ModbusSlaveDevice> slaveDevices)
        {
            try
            {
                // is register defined in file
                var manualCoil = registerManager.Coils.FirstOrDefault(r =>
                    r.SlaveId == slaveId &&
                    r.IsActive &&
                    (coil >= r.StartAddress && coil < r.StartAddress + r.Quantity));

                if (manualCoil != null)
                {
                    // fetch values from TCP server
                    bool[] values = new bool[numberOfPoints];
                    for (int i = 0; i < numberOfPoints; i++)
                    {
                        values[i] = tcpServer.coils[coil+i];
                    }

                    // if conditions are met, pass the data to the RTU
                    if (slaveDevices.ContainsKey(slaveId))
                    {
                        rtuClient.WriteMultipleCoils(slaveId, (ushort)(coil-1), values);
                        _log.DebugFormat("Transferred coils change to device {0}, starting address: {1}, number of points: {2}", slaveId, coil, numberOfPoints);
                    }
                   
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
        }

        public void HandleHoldingRegistersChanged(byte slaveId, int register, int numberOfPoints, ModbusServer tcpServer, ClientHandler rtuClient, Dictionary<byte, ModbusSlaveDevice> slaveDevices)
        {
            try
            {
                var manualRegister = registerManager.HoldingRegisters.FirstOrDefault(r =>
                    r.SlaveId == slaveId &&
                    r.IsActive &&
                    (register >= r.StartAddress && register < r.StartAddress + r.Quantity));

                if (manualRegister != null)
                {
                    ushort[] values = new ushort[numberOfPoints];
                    for (int i = 0; i < numberOfPoints; i++)
                    {
                        values[i] = (ushort)tcpServer.holdingRegisters[register+i];
                    }

                    if (slaveDevices.ContainsKey(slaveId))
                    {
                        rtuClient.WriteMultipleRegisters(slaveId, (ushort)(register-1), values);
                        _log.DebugFormat("Transferred holding registers change to device {0}, starting address: {1}, number of registers: {2}", slaveId, slaveId, numberOfPoints);
                    }
                   
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
        }

        public void ReadHoldingRegisters(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server)
        {

            for (ushort startAddress = server.LastStartingAddress; startAddress < (server.LastQuantity + server.LastStartingAddress); startAddress += 125)
            {
                // calculating how many registers remain till the end
                ushort registersToRead = (ushort)Math.Min(125, (server.LastStartingAddress + server.LastQuantity) - startAddress);
                try
                {
                    var data = master.ReadHoldingRegisters(slave.UnitId, startAddress, registersToRead);
                    Array.Copy(data, 0, slave.HoldingRegisters, startAddress, registersToRead);
                    int offset = slave.UnitId * 10000;
                    Array.Copy(slave.HoldingRegisters, server.LastStartingAddress,
                        server.holdingRegisters.localArray, offset + server.LastStartingAddress,
                        server.LastQuantity);
                }
                catch (NModbus.SlaveException ex)
                {
                    string errorKey = $"Holding_{slave.UnitId}_{startAddress}_{registersToRead}";
                    if (!_reportedErrorMessages.Contains(errorKey))
                    {
                        _log.Error($"Holding registers {startAddress}-{startAddress + registersToRead - 1} are not available for device {slave.UnitId}");
                        _reportedErrorMessages.Add(errorKey);
                    }
                }
                catch (Exception ex)
                {
                    string errorKey = $"General_{slave.UnitId}_{ex.Message}";
                    if (!_reportedErrorMessages.Contains(errorKey))
                    {
                        _log.ErrorFormat("Error reading holding registers for device {0}: {1}", slave.UnitId, ex.Message);
                        _reportedErrorMessages.Add(errorKey);
                    }
                }
            }

        }
        public void ReadInputRegisters(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server)
        {
            try
            {
                for (ushort startAddress = server.LastStartingAddress; startAddress < (server.LastQuantity + server.LastStartingAddress); startAddress += 125)
                {
                    ushort registersToRead = (ushort)Math.Min(125, (server.LastStartingAddress + server.LastQuantity) - startAddress);
                    try
                    {
                        var data = master.ReadInputRegisters(slave.UnitId, startAddress, registersToRead);
                        Array.Copy(data, 0, slave.InputRegisters, startAddress, registersToRead);
                        int offset = slave.UnitId * 10000;
                        Array.Copy(slave.InputRegisters, server.LastStartingAddress,
                            server.inputRegisters.localArray, offset + server.LastStartingAddress,
                            server.LastQuantity);
                    }
                    catch (NModbus.SlaveException ex)
                    {

                        string errorKey = $"Input_{slave.UnitId}_{startAddress}_{registersToRead}";
                        if (!_reportedErrorMessages.Contains(errorKey))
                        {
                            _log.ErrorFormat($"Input registers {startAddress}-{startAddress + registersToRead - 1} are not available for device {slave.UnitId}");
                            _reportedErrorMessages.Add(errorKey);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorKey = $"General_{slave.UnitId}_{ex.Message}";
                if (!_reportedErrorMessages.Contains(errorKey))
                {
                    _log.ErrorFormat("Error reading input registers for device {0}: {1}", slave.UnitId, ex.Message);
                    _reportedErrorMessages.Add(errorKey);
                }
            }
        }
        public void ReadCoils(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server)
        {
            try
            {
                for (ushort startAddress = server.LastStartingAddress; startAddress < (server.LastQuantity + server.LastStartingAddress); startAddress += 125)
                {
                    ushort coilsToRead = (ushort)Math.Min(125, (server.LastStartingAddress + server.LastQuantity) - startAddress);
                    try
                    {
                        var data = master.ReadCoils(slave.UnitId, startAddress, coilsToRead);
                        Array.Copy(data, 0, slave.Coils, startAddress, coilsToRead);
                        int offset = slave.UnitId * 10000;
                        Array.Copy(slave.Coils, server.LastStartingAddress,
                            server.coils.localArray, offset + server.LastStartingAddress,
                            server.LastQuantity);
                    }
                    catch (NModbus.SlaveException ex)
                    {

                        string errorKey = $"Coils{slave.UnitId}_{startAddress}_{coilsToRead}";
                        if (!_reportedErrorMessages.Contains(errorKey))
                        {
                            _log.ErrorFormat($"Coils {startAddress}-{startAddress + coilsToRead - 1} are not available for device {slave.UnitId}");
                            _reportedErrorMessages.Add(errorKey);
                        }
                    }


                }
            }
            catch (Exception ex)
            {
                string errorKey = $"General_{slave.UnitId}_{ex.Message}";
                if (!_reportedErrorMessages.Contains(errorKey))
                {
                    _log.ErrorFormat("Error reading coils from device {0}: {1}", slave.UnitId, ex.Message);
                    _reportedErrorMessages.Add(errorKey);
                }
            }
        }
        public void WriteSingleRegister(IModbusMaster master, byte address, ushort startRegister, ushort value)
        {
            try
            {
                var register = registerManager.HoldingRegisters.FirstOrDefault
                    (r => r.SlaveId == address && 
                    r.StartAddress == startRegister &&
                    r.AccessMode=="W" && r.IsActive==true) ;
                if (register != null)
                {
                    master.WriteSingleRegister(address, startRegister, value);
                }
                else
                {
                    _log.ErrorFormat("No active register at address {0} with write permissions for slave {1}", startRegister, address);
                }
            }
            catch(Exception ex)
            {
                _log.Error(ex);
            }
     
        }

        public void WriteMultipleRegisters(IModbusMaster master, byte address, ushort startRegister, ushort[] values)
        {
            try
            {
                for (ushort i = 0; i < values.Length; i++)
                {
                    ushort currentAddress = (ushort)(startRegister + i);
                    var register = registerManager.HoldingRegisters.FirstOrDefault(
                        r => r.SlaveId == address &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.AccessMode == "W" &&
                             r.IsActive);

                    if (register == null)
                    {
                        _log.ErrorFormat("No active register at address {0} with write permissions for slave {1}", startRegister, address);
                        return;
                    }
                }

                master.WriteMultipleRegisters(address, startRegister, values);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
        }

        public void WriteSingleCoil(IModbusMaster master, byte address, ushort startRegister, bool value)
        {
            try
            {
                var register = registerManager.Coils.FirstOrDefault
                    (r => r.SlaveId == address &&
                    r.StartAddress == startRegister &&
                    r.AccessMode == "W" && r.IsActive == true);
                if (register != null)
                {
                    master.WriteSingleCoil(address, startRegister, value);
                }
                else
                {
                    _log.ErrorFormat("No active register at address {0} with write permissions for slave {1}", startRegister, address);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }

        }

        public void WriteMultipleCoils(IModbusMaster master, byte address, ushort startRegister, bool[] values)
        {
            try
            {
                for (ushort i = 0; i < values.Length; i++)
                {
                    ushort currentAddress = (ushort)(startRegister + i);
                    var register = registerManager.Coils.FirstOrDefault(
                        r => r.SlaveId == address &&
                             currentAddress >= r.StartAddress &&
                             currentAddress < r.StartAddress + r.Quantity &&
                             r.AccessMode == "W" &&
                             r.IsActive);

                    if (register == null)
                    {
                        _log.ErrorFormat("No active register at address {0} with write permissions for slave {1}", startRegister, address);
                        return;
                    }
                }

                master.WriteMultipleCoils(address, startRegister, values);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
        }
    }
    
}
