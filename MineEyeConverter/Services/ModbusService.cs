using MineEyeConverter.Config;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Topshelf;

namespace MineEyeConverter
{
    /// <summary>
    ///  Service class for running the Modbus converter as a Windows service.
    ///  Handles starting and stopping the service.
    /// </summary>
    public class ModbusService 
    {
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(ModbusService));
        public ModbusTcpServer? Server { get; set; }
        private readonly string instanceName;
        private const string CONFIG_FOLDER = "config";


        public ModbusService(string instanceName)
        {
            this.instanceName = instanceName;

            
        }

        public void Start()
        {
            try
            {
                string debugLogPath = "C:\\MineEyeConverter_debug.log";
                File.AppendAllText(debugLogPath, $"[{DateTime.Now}] Starting service for instance: {instanceName}\n");
                File.AppendAllText(debugLogPath, $"[{DateTime.Now}] Current Directory: {Directory.GetCurrentDirectory()}\n");
                File.AppendAllText(debugLogPath, $"[{DateTime.Now}] Base Directory: {AppDomain.CurrentDomain.BaseDirectory}\n");


                _log.InfoFormat("Current directory: {0}", Directory.GetCurrentDirectory());
                _log.InfoFormat("Base directory: {0}", AppDomain.CurrentDomain.BaseDirectory);
                _log.InfoFormat("Starting service for instance: {0}", instanceName);
                Console.WriteLine($"===== Starting service for instance: {instanceName} =====");
                Console.WriteLine("cw z modbusservice");
                // Ścieżka do pliku konfiguracyjnego dla tej instancji
                string configFilePath = GetConfigFilePath(instanceName);
                File.AppendAllText(debugLogPath, $"[{DateTime.Now}] Config File Path: {configFilePath}\n");
                File.AppendAllText(debugLogPath, $"[{DateTime.Now}] Config File Exists: {File.Exists(configFilePath)}\n");
                _log.InfoFormat("Using configuration file: {0}", configFilePath);

                if (!File.Exists(configFilePath))
                {
                    string errorMessage = $"Configuration file not found: {configFilePath}";
                    _log.Error(errorMessage);
                    throw new FileNotFoundException(errorMessage);
                }

                // Wczytaj konfigurację bezpośrednio z pliku XML
                Configuration config = LoadConfigurationFromFile(configFilePath);

                var instanceConfig = config.Instances.FirstOrDefault();
                if (instanceConfig == null)
                {
                    string errorMessage = $"No instance configuration found in file: {configFilePath}";
                    _log.Error(errorMessage);
                    throw new ConfigurationErrorsException(errorMessage);
                }

                _log.InfoFormat("Configuration loaded for instance: {0}", instanceName);
                _log.InfoFormat("Listening port: {0}, Operation mode: {1}",
                    instanceConfig.ListeningPort,
                    instanceConfig.OperationMode);

                string operationMode = instanceConfig.OperationMode.ToLower();
                if (operationMode == "auto" || operationMode == "manual")
                {
                    Server = new ModbusTcpServer(instanceName);
                    Server.Start();
                    _log.InfoFormat("Server started in {0} mode", operationMode);
                }
                else if (operationMode == "learning")
                {
                    _log.Info("Starting in learning mode");
                    LearningModeHandler lm = new LearningModeHandler(instanceName);
                    List<SlaveConfiguration> discoveredConfigs = lm.DiscoverSlaves();
                    lm.SaveConfigurationToXml(discoveredConfigs);
                    _log.Info("Learning mode completed");
                }
            }
            catch (Exception ex)
            {
                _log.Error("Error starting service: ", ex);
                File.AppendAllText("C:\\MineEyeConverter_error.log", $"[{DateTime.Now}] Error: {ex.Message}\n{ex.StackTrace}\n");
                throw;
            }
        }

        public void Stop()
        {
            _log.InfoFormat("Stopping service: {0}", instanceName);
            Server?.Stop();
            _log.Info("Service stopped");
        }

        private string GetConfigFilePath(string instanceName)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, CONFIG_FOLDER, $"{instanceName}.xml");
            return configPath;
        }

        private Configuration LoadConfigurationFromFile(string filePath)
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Configuration));

                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var config = serializer.Deserialize(fileStream) as Configuration;

                    if (config == null)
                    {
                        throw new InvalidOperationException("Failed to deserialize configuration file");
                    }

                    return config;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Error loading configuration from file {filePath}: {ex.Message}", ex);
                throw;
            }
        }
    }
}
