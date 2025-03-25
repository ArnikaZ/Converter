using MineEyeConverter.Config;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Topshelf;

namespace MineEyeConverter
{
    /// <summary>
    ///  Service class for running the Modbus converter as a Windows service.
    ///  Handles starting and stopping the service.
    /// </summary>
    public class ModbusService 
    {

        public ModbusTcpServer? Server { get; set; }
        private readonly string instanceName;
        private readonly string filePath = Path.Combine(AppContext.BaseDirectory, "config.xml");
        private readonly string logFilePath;

        public ModbusService(string instanceName)
        {
            this.instanceName = instanceName;
            logFilePath = Path.Combine(AppContext.BaseDirectory, "instanceName.log");
        }
        public void LogInstanceName()
        {

            try
            {
                string logMessage = $"Instance name: {instanceName} - {DateTime.Now}" + Environment.NewLine;
                File.AppendAllText(logFilePath, logMessage);
            }
            catch (Exception ex)
            {
                // Obsługa wyjątku – można zapisać błąd do dziennika zdarzeń lub wykonać inne działania
                Console.WriteLine("Błąd zapisu do pliku: " + ex.Message);
            }
        }
        public void Start()
        {
            LogInstanceName();
            Configuration _config = ConfigLoader.LoadConfiguration(filePath);
            var instanceConfig = _config.Instances.FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
            if (instanceConfig == null)
            {
                throw new ConfigurationErrorsException("Nie znaleziono konfiguracji dla instancji: " + instanceName);
            }
            string operationMode = instanceConfig.OperationMode.ToLower();
            if (operationMode == "auto" || operationMode == "manual")
            {
                Server = new ModbusTcpServer(instanceName, true);
                Server.Start();
            }
            else if (operationMode == "learning")
            {
                
                LearningModeHandler lm = new LearningModeHandler(instanceName);
                List<SlaveConfiguration> discoveredConfigs = lm.DiscoverSlaves();
                lm.SaveConfigurationToXml(discoveredConfigs);
            }

        }


        public void Stop()
        {
            
            Server?.Stop();
        }

    }
}
