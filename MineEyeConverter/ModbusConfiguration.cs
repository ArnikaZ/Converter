using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace MineEyeConverter
{
    /// <summary>
    /// Represents the root configuration for the application
    /// </summary>
    [XmlRoot("Configuration")]
    public class Configuration
    {
        [XmlArray("Instances")]
        [XmlArrayItem("Instance")]
        public List<Instance> Instances { get; set; }
    }

    public class Instance
    {
        [XmlAttribute("name")]
        public string Name { get; set; }
        [XmlElement("OperationMode")]
        public string OperationMode { get; set; }

        [XmlElement("ListeningPort")]
        public int ListeningPort { get; set; }

        [XmlElement("ConnectionType")]
        public string ConnectionType { get; set; }

        [XmlElement("RtuSettings")]
        public RtuSettings RtuSettings { get; set; }

        [XmlElement("SlaveDeviceList")]
        public SlaveDeviceList SlaveDeviceList { get; set; }

        [XmlElement("ClientWhiteList")]
        public ClientWhiteList ClientWhiteList { get; set; }
    }

    public class RtuSettings
    {
        [XmlElement("IpAddress")]
        public string IpAddress { get; set; }

        [XmlElement("Port")]
        public int? Port { get; set; } 

 
        [XmlElement("PortName")]
        public string PortName { get; set; }

        [XmlElement("BaudRate")]
        public int? BaudRate { get; set; }

        [XmlElement("Parity")]
        public string Parity { get; set; }

        [XmlElement("StopBits")]
        public int? StopBits { get; set; }

        [XmlElement("DataBits")]
        public int? DataBits { get; set; }
    }
    public class SlaveDeviceList
    {
        [XmlElement("Slave")]
        public List<Slave> Slaves { get; set; }
    }
    public class Slave
    {
        [XmlElement("UnitId")]
        public int UnitId { get; set; }
        public string Description { get; set; }
    }
    public class ClientWhiteList
    {
        [XmlElement("Client")]
        public List<Client> Clients { get; set; }
        public bool CanClientRead(string ip)
        {
            return Clients.Any(c => string.Equals(c.IpAddress, ip));
        }
        public bool CanClientWrite(string ip)
        {
            return Clients.Any(c =>
                string.Equals(c.IpAddress, ip, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Permission, "W", StringComparison.OrdinalIgnoreCase));
        }
    }

    public class Client
    {
        [XmlElement("IpAddress")]
        public string IpAddress { get; set; }

        [XmlElement("Permission")]
        public string Permission { get; set; }
    }

}
