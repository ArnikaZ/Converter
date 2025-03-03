using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    /// Represents a Modbus register configuration
    /// Used for Manual mode to define which registers are accessible.
    /// </summary>
    public class ModbusRegister
    {
        public int SlaveId { get; set; }
        public ushort StartAddress { get; set; }
        public ushort Quantity { get; set; }
        public bool IsActive { get; set; }
        public ushort functionCode { get; set; }
        public string AccessMode { get; set; }
    }
}
