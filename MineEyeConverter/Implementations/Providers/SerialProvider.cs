using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    ///  Provider for Serial (COM port) connections.
    ///  Contains all parameters needed to configure a serial connection.
    /// </summary>
    public class SerialProvider :IProvider
    {
        public string SerialName { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public Parity PortParity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
      
    }
}
