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
        public List<Instance> Instances { get; set; } = new List<Instance>();
    }

    public class Instance
    {
        [XmlAttribute("name")]
        public string Name { get; set; } = string.Empty;
        [XmlElement("OperationMode")]
        public string OperationMode { get; set; } = string.Empty;

        [XmlElement("ListeningPort")]
        public int ListeningPort { get; set; }

        [XmlElement("ConnectionType")]
        public string ConnectionType { get; set; } = string.Empty;

        [XmlElement("RtuSettings")]
        public RtuSettings RtuSettings { get; set; } = new RtuSettings();

        [XmlElement("SlaveDeviceList")]
        public SlaveDeviceList SlaveDeviceList { get; set; } = new SlaveDeviceList();

        [XmlElement("ClientWhiteList")]
        public ClientWhiteList ClientWhiteList { get; set; } = new ClientWhiteList();
    }

    public class RtuSettings
    {
        [XmlElement("IpAddress")]
        public string IpAddress { get; set; } = string.Empty;

        [XmlElement("Port")]
        public int? Port { get; set; }


        [XmlElement("PortName")]
        public string PortName { get; set; } = string.Empty;

        [XmlElement("BaudRate")]
        public int? BaudRate { get; set; }

        [XmlElement("Parity")]
        public string Parity { get; set; } = string.Empty;

        [XmlElement("StopBits")]
        public int? StopBits { get; set; }

        [XmlElement("DataBits")]
        public int? DataBits { get; set; }
    }
    public class SlaveDeviceList
    {
        [XmlElement("Slave")]
        public List<Slave> Slaves { get; set; } = new List<Slave>();
    }
    public class Slave
    {
        [XmlElement("UnitId")]
        public int UnitId { get; set; }
        public string Description { get; set; } = string.Empty;
    }
    public class ClientWhiteList
    {
        [XmlElement("Client")]
        public List<Client> Clients { get; set; } = new List<Client>();
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
        public string IpAddress { get; set; } = string.Empty;

        [XmlElement("Permission")]
        public string Permission { get; set; } = string.Empty;
    }

}
