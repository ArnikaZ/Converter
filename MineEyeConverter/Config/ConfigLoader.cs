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
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(ConfigLoader));

        public static Configuration LoadInstanceConfiguration(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName))
            {
                string errorMessage = "Instance name cannot be null or empty";
                _log.Error(errorMessage);
                throw new ArgumentException(errorMessage, nameof(instanceName));
            }

            _log.InfoFormat("Loading configuration for instance: {0}", instanceName);

            EnsureConfigFolderExists();
            string filePath = GetInstanceConfigPath(instanceName);

            _log.InfoFormat("Configuration file path: {0}", filePath);

            if (!File.Exists(filePath))
            {
                string errorMessage = $"Configuration file not found: {filePath}";
                _log.Error(errorMessage);
                throw new FileNotFoundException(errorMessage, filePath);
            }

            try
            {
                XmlSerializer serializer = new(typeof(Configuration));
                using FileStream fs = new(filePath, FileMode.Open);

                var config = serializer.Deserialize(fs) as Configuration;

                if (config == null)
                {
                    string errorMessage = "Failed to deserialize configuration file";
                    _log.Error(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                // Log configuration details for debugging
                var instanceConfig = config.Instances.FirstOrDefault();
                if (instanceConfig != null)
                {
                    _log.InfoFormat("Configuration loaded successfully for {0}", instanceName);
                    _log.InfoFormat("Listening port: {0}, Operation mode: {1}",
                        instanceConfig.ListeningPort,
                        instanceConfig.OperationMode);
                }
                else
                {
                    _log.Warn("Configuration loaded but no instances found");
                }

                return config;
            }
            catch (Exception ex)
            {
                _log.Error($"Error loading configuration: {ex.Message}", ex);
                throw;
            }
        }

        public static void EnsureConfigFolderExists()
        {
            string configFolderPath = Path.Combine(AppContext.BaseDirectory, CONFIG_FOLDER);

            if (!Directory.Exists(configFolderPath))
            {
                _log.InfoFormat("Creating configuration directory: {0}", configFolderPath);
                Directory.CreateDirectory(configFolderPath);
            }
        }

        public static string GetInstanceConfigPath(string instanceName)
        {
            return Path.Combine(AppContext.BaseDirectory, CONFIG_FOLDER, $"{instanceName}.xml");
        }

        public static void SaveInstanceConfiguration(Configuration config, string instanceName)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrEmpty(instanceName))
            {
                throw new ArgumentException("Instance name cannot be null or empty", nameof(instanceName));
            }

            _log.InfoFormat("Saving configuration for instance: {0}", instanceName);

            EnsureConfigFolderExists();
            string filePath = GetInstanceConfigPath(instanceName);

            try
            {
                XmlSerializer serializer = new(typeof(Configuration));
                using FileStream fs = new(filePath, FileMode.Create);
                serializer.Serialize(fs, config);

                _log.InfoFormat("Configuration saved to: {0}", filePath);
            }
            catch (Exception ex)
            {
                _log.Error($"Error saving configuration: {ex.Message}", ex);
                throw;
            }
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

            _log.InfoFormat("Found {0} configuration files", instanceNames.Count);
            return instanceNames;
        }

    }
}
