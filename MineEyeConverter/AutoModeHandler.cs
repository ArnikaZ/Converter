using EasyModbus;
using log4net;
using Microsoft.Win32;
using NModbus;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static EasyModbus.ModbusServer;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MineEyeConverter
{
    /// <summary>
    /// Implements the Auto operation mode which transfers data between TCP and RTU with 1:1 mapping.
    /// This mode allows full access to all registers without any filtering.
    /// </summary>
    public class AutoModeHandler : IOperationModeHandler
    {
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private HashSet<string> _reportedErrorMessages = new HashSet<string>();

        public void HandleCoilsChanged(byte slaveId, int coil, int numberOfPoints, ModbusServer tcpServer, ClientHandler rtuClient, Dictionary<byte, ModbusSlaveDevice> slaveDevices)
        {
            // Fetch data from the TCP server
            bool[] values = new bool[numberOfPoints];
            for (int i = 0; i < numberOfPoints; i++)
            {
                values[i] = tcpServer.coils[coil+i];
            }

            if (slaveDevices.ContainsKey(slaveId))
            {
                // Save data to RTU device
                rtuClient.WriteMultipleCoils(slaveId, (ushort)(coil-1), values);
                _log.DebugFormat("Transferred coils change to device {0}, starting address: {1}, number of points: {2}", slaveId, coil, numberOfPoints);
            }
         
        }
        public void HandleHoldingRegistersChanged(byte slaveId, int register, int numberOfPoints, ModbusServer tcpServer, ClientHandler rtuClient, Dictionary<byte, ModbusSlaveDevice> slaveDevices)
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

       public void ReadHoldingRegisters(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server)
        {
            
            for (ushort startAddress = server.LastStartingAddress; startAddress < (server.LastQuantity + server.LastStartingAddress); startAddress += 125)
            {
                // Calculation how many registers remain until the end
                ushort registersToRead = (ushort)Math.Min(125, (server.LastStartingAddress + server.LastQuantity) - startAddress);
                try
                {
                    var data = master.ReadHoldingRegisters(slave.UnitId, startAddress, registersToRead);
                    Array.Copy(data, 0, slave.HoldingRegisters, startAddress, registersToRead);
                    int offset = slave.UnitId * 10000;
                    Array.Copy(slave.HoldingRegisters, server.LastStartingAddress,
                        server.holdingRegisters.localArray,offset+ server.LastStartingAddress,
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
            
            master.WriteSingleRegister(address, startRegister, value);
        }

        public void WriteMultipleRegisters(IModbusMaster master, byte address, ushort startRegister, ushort[] values)
        {
            master.WriteMultipleRegisters(address, startRegister, values);
        }
        public void WriteSingleCoil(IModbusMaster master, byte address, ushort startRegister, bool value)
        {
            master.WriteSingleCoil(address, startRegister, value);
        }
        public void WriteMultipleCoils(IModbusMaster master, byte address, ushort startRegister, bool[] values)
        {
            master.WriteMultipleCoils(address, startRegister, values);
        }
        
    }
}
