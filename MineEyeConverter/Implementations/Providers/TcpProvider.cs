using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MineEyeConverter
{
    /// <summary>
    /// Provider for TCP connections when using RTU over TCP.
    /// Contains IP address and port settings for the connection.
    /// </summary>
    public class TcpProvider :IProvider
    {
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
       
    }
}
