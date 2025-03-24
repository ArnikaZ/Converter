using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace MineEyeConverter
{
    /// <summary>
    ///  Singleton class that manages the register configuration for Manual mode.
    ///  Provides access to the lists of configured holding registers, input registers, and coils.
    /// </summary>
    [Serializable]
    public class RegisterManager
    {
        private static readonly Lazy<RegisterManager> _instance =
        new Lazy<RegisterManager>(() => new RegisterManager());

        public static RegisterManager Instance => _instance.Value;
        public byte SlaveId { get; set; }
        public List<ModbusRegister> HoldingRegisters { get; set; }
        public List<ModbusRegister> InputRegisters { get; set; }
        public List<ModbusRegister> Coils { get; set; }

        private RegisterManager()
        {
            HoldingRegisters = new List<ModbusRegister>();
            InputRegisters = new List<ModbusRegister>();
            Coils = new List<ModbusRegister>();
        }

        public void SaveToFile(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(RegisterManager));
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, this);
            }
        }
        public void LoadFromFile(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(RegisterManager));
            using (StreamReader reader = new StreamReader(filePath))
            {
                if (serializer.Deserialize(reader) is not RegisterManager loaded)
                {
                    throw new InvalidOperationException("Deserialization failed.");
                }
                HoldingRegisters = loaded.HoldingRegisters;
                InputRegisters = loaded.InputRegisters;
                Coils = loaded.Coils;
            }
        }
       
    }
}
