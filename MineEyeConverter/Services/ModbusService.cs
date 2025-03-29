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
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(ModbusService));
        public ModbusTcpServer? Server { get; set; }
        private readonly string instanceName;
        

        public ModbusService(string instanceName)
        {
            this.instanceName = instanceName;
        }
       
        public void Start()
        {
            try
            {
                Configuration _config = ConfigLoader.LoadInstanceConfiguration(instanceName);
                
                var instanceConfig = _config.Instances.FirstOrDefault();
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
            catch (Exception ex)
            {
                _log.Error("Error starting service: ", ex);
                throw new ConfigurationErrorsException($"Configuration file for instance '{instanceName}' not found. {ex.Message}");
            }
        }

        public void Stop()
        {
            
            Server?.Stop();
        }

    }
}
