using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    /// Represents a Modbus slave device with its registers and coils.
    /// </summary>
    public class ModbusSlaveDevice
    {
        public byte UnitId { get; set; }
        public ushort[] HoldingRegisters { get; set; }
        public ushort[] InputRegisters { get; set; }

        public bool[] Coils { get; set; }

        public ModbusSlaveDevice()
        {
            HoldingRegisters = new ushort[65536];
            InputRegisters = new ushort[65536];
            Coils = new bool[65536];
        }

        public ModbusSlaveDevice(byte unitId)
        {
            UnitId = unitId;
            HoldingRegisters = new ushort[65536];
            InputRegisters = new ushort[65536];
            Coils = new bool[65536];
        }

    }
}
