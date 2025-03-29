using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace MineEyeConverter.Config
{
    /// <summary>
    /// Loads a Configuration object from an XML file.
    /// </summary>
    public static class ConfigLoader
    {
        private const string CONFIG_FOLDER = "config";
        public static Configuration LoadInstanceConfiguration(string instanceName)
        {
            EnsureConfigFolderExists();
            string filePath = GetInstanceConfigPath(instanceName);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file for instance '{instanceName}' not found at path: {filePath}");
            }
            XmlSerializer serializer = new(typeof(Configuration));
            using FileStream fs = new(filePath, FileMode.Open);
            return (serializer.Deserialize(fs) as Configuration) ?? throw new InvalidOperationException("Deserialization error – null value returned");
        }

        public static void EnsureConfigFolderExists()
        {
            string configFolderPath = Path.Combine(AppContext.BaseDirectory, CONFIG_FOLDER);
            if (!Directory.Exists(configFolderPath))
            {
                Directory.CreateDirectory(configFolderPath);
            }
        }

        public static string GetInstanceConfigPath(string instanceName)
        {
            return Path.Combine(AppContext.BaseDirectory, CONFIG_FOLDER, $"{instanceName}.xml");
        }
        public static void SaveInstanceConfiguration(Configuration config, string instanceName)
        {
            EnsureConfigFolderExists();

            string filePath = GetInstanceConfigPath(instanceName);
            XmlSerializer serializer = new(typeof(Configuration));

            using FileStream fs = new(filePath, FileMode.Create);
            serializer.Serialize(fs, config);
        }
        public static List<string> GetAvailableInstances()
        {
            EnsureConfigFolderExists();

            string configFolderPath = Path.Combine(AppContext.BaseDirectory, CONFIG_FOLDER);
            var configFiles = Directory.GetFiles(configFolderPath, "*.xml");

            List<string> instanceNames = new List<string>();
            foreach (var file in configFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                instanceNames.Add(fileName);
            }

            return instanceNames;
        }

    }
}
