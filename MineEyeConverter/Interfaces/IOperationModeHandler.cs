using EasyModbus;
using NModbus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    ///  Defines the interface for different operation modes that control how data is transferred between Modbus TCP and RTU devices
    /// </summary>
    public interface IOperationModeHandler
    {
        /// <summary>
        ///  Handles changes to Modbus data from the TCP server and transfers them to RTU devices.
        /// </summary>
        /// <param name="slaveId">The Modbus slave device identifier.</param>
        /// <param name="startAddress">The starting address that was changed.</param>
        /// <param name="numberOfPoints">The number of consecutive data points that were changed.</param>
        /// <param name="tcpServer">Reference to the Modbus TCP server.</param>
        /// <param name="rtuClient">Reference to the RTU client handler.</param>
        /// <param name="slaveDevices">Dictionary of available slave devices indexed by unit ID.</param>
        void HandleCoilsChanged(byte slaveId, int startAddress, int numberOfPoints, ModbusServer tcpServer, ClientHandler rtuClient, Dictionary<byte, ModbusSlaveDevice> slaveDevices);
       
        /// <inheritdoc cref="HandleCoilsChanged"/>
        void HandleHoldingRegistersChanged(byte slaveId, int startAddress, int numberOfPoints, ModbusServer tcpServer, ClientHandler rtuClient, Dictionary<byte, ModbusSlaveDevice> slaveDevices);
       
        /// <summary>
        /// Reads data from an RTU slave device and updates the TCP server's data.
        /// </summary>
        /// <param name="slave">The slave device to read from.</param>
        /// <param name="master">The Modbus master for communication with the slave.</param>
        /// <param name="server">The Modbus TCP server to update with the read data.</param>
        void ReadHoldingRegisters(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server);
        
        /// <inheritdoc cref="ReadHoldingRegisters"/>
     
        void ReadInputRegisters(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server);
        
        ///<inheritdoc cref="ReadHoldingRegisters"/>
        void ReadCoils(ModbusSlaveDevice slave, IModbusMaster master, ModbusServer server);

        /// <summary>
        /// Writes a single value to an RTU slave device.
        /// </summary>
        /// <param name="master">The Modbus master for communication with the slave.</param>
        /// <param name="address">The slave device address.</param>
        /// <param name="startRegister">The register address to write to.</param>
        /// <param name="value">The value to write.</param>
        void WriteSingleRegister(IModbusMaster master, byte address, ushort startRegister, ushort value);

        /// <summary>
        /// Writes multiple values to an RTU slave device.
        /// </summary>
        /// <param name="master">The Modbus master for communication with the slave.</param>
        /// <param name="address">The slave device address.</param>
        /// <param name="startRegister">The starting register address to write to.</param>
        /// <param name="values">The values to write.</param>
        void WriteMultipleRegisters(IModbusMaster master, byte address, ushort startRegister, ushort[] values);

        ///<inheritdoc cref="WriteSingleRegister"/>
        void WriteSingleCoil(IModbusMaster master, byte address, ushort startRegister, bool value);

        /// <inheritdoc cref="WriteMultipleRegisters"/>
        void WriteMultipleCoils(IModbusMaster master, byte address, ushort startRegister, bool[] values);
    }
}
