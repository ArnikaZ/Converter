using CommandLine;
using System.IO.Ports;
using Topshelf;
using System.ServiceProcess;
using System.Reflection;
using System.Configuration.Install;
using System.Diagnostics;
using Serilog;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.CommandLine;
using MineEyeConverter;
using Topshelf.HostConfigurators;
using System.Xml;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Runtime.CompilerServices;

[assembly: log4net.Config.XmlConfigurator(Watch =true)]
namespace MineEyeConverter
{
   /// <summary>
   /// Entry point for the MineEyeConverter application.
   /// Handles command-line arguments and service configuration.
   /// </summary>
    internal static class Program
    {
        
        static void Main(string[] args)
        {
            log4net.ILog _log = log4net.LogManager.GetLogger(typeof(Program));

            if (args.Length == 0)
            {
                ChooseAction();
            }
            else
            {
                var switchMappings = new Dictionary<string, string>
            {
                { "-name", "name" },
                { "--name", "name" }
            };

                // Configure application from appsettings.json and command-line arguments
                var config = new ConfigurationBuilder()
                   .SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                   .AddCommandLine(args, switchMappings)
                   .Build();


                var services = config.GetSection("Service").GetChildren().ToList();

                string serviceName = string.Empty;

                var exitCode = HostFactory.Run(x =>
                {
                    x.AddCommandLineDefinition("name", f =>
                    {
                        serviceName = f;
                        if (args.Contains("install"))
                        {
                            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "service_name.txt"), f);
                        }
                    });
                    x.ApplyCommandLine();

                    if (string.IsNullOrEmpty(serviceName))
                    {
                        string serviceNameFile = Path.Combine(AppContext.BaseDirectory, "service_name.txt");
                        if (File.Exists(serviceNameFile))
                        {
                            serviceName = File.ReadAllText(serviceNameFile).Trim();
                            _log.InfoFormat("Service name loaded from file: {0} ", serviceName);

                        }
                        
                    }

                    var selectedServiceConfig = services.FirstOrDefault(
                        s => s.GetValue<string>("Name")?.Equals(serviceName, StringComparison.OrdinalIgnoreCase) == true);

                    if (selectedServiceConfig == null)
                    {
                       
                        _log.ErrorFormat("Service configuration not found for {0}", serviceName);
                    }
                    else
                    {
                        _log.InfoFormat("Service configuration found for: {0}", serviceName);
                           
                    }

                 
                    string serviceDescription = selectedServiceConfig?.GetValue<string>("Description")
                                                ?? "TCP <=> RTU Converter";

               
                    x.Service<ModbusService>(s =>
                    {
                        s.ConstructUsing(modbusService =>
                        {
                            return new ModbusService(serviceName);
                        });
                        s.WhenStarted(modbusService => modbusService.Start());
                        s.WhenStopped(modbusService => modbusService.Stop());
                    });

                    x.RunAsLocalSystem();
                    x.SetServiceName(serviceName);
                    x.SetDisplayName(serviceName);
                    x.SetDescription(serviceDescription);
                    x.StartAutomatically();
                });

                int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
                Environment.ExitCode = exitCodeValue;
            }
        }

        private static void ChooseAction()
        {
            Console.WriteLine("Available parameters");
            Console.WriteLine("install -name:ServiceName    : install service");
            Console.WriteLine("start -name:ServiceName      : start service");
            Console.WriteLine("stop -name:ServiceName       : stop service");
            Console.WriteLine("uninstall -name:ServiceName  : unistall service");
            Console.WriteLine();
            Console.WriteLine("Available options in console mode:");
            Console.WriteLine("1. Add new instance configuration");
            Console.WriteLine("2. Run service in console mode");
            Console.WriteLine("3. Exit");

            int choice = ReadInt("Choose a number: ");

            switch (choice)
            {
                case 1:
                    AddNewInstance();
                    break;
                case 2:
                    RunInConsole();
                    break;
                case 3:
                    Environment.Exit(0);
                    break;
                default:
                    Console.WriteLine("Invalid option");
                    break;
            }
        }

        private static void AddNewInstance()
        {
            Console.Clear();
            try
            {
                string configPath = "config.xml";
                XDocument doc = XDocument.Load(configPath);
                Console.WriteLine("Add new instance");
                Console.Write("Enter instance name: ");
                string instanceName = ReadNonEmptyString("Enter instance name: ");
                var existingInstance = doc.Root?.Element("Instances")?.Elements("Instance").FirstOrDefault(e => e.Attribute("name")?.Value == instanceName);

                if (existingInstance != null)
                {
                    Console.WriteLine($"Error: instance with name '{instanceName}' already exists in configuration.");
                    Console.WriteLine("Operation aborted.");
                    ChooseAction();
                }

                string operationMode = ValidateOperationMode();
                int listeningPort = ReadInt("Listening port: ");
                Console.WriteLine("Connection type: ");
                Console.WriteLine("1. Rtu over Tcp");
                Console.WriteLine("2. COM");
                string connectionTypeChoice = ReadFromOptions("Choose (1/2): ", "1", "2");
                string connectionType = connectionTypeChoice == "1" ? "RtuOverTcp" : "COM";
                XElement newInstance = new XElement("Instance",
                    new XAttribute("name", instanceName),
                    new XElement("OperationMode", operationMode),
                    new XElement("ListeningPort", listeningPort),
                    new XElement("ConnectionType", connectionType),
                    new XElement("RtuSettings"),
                    new XElement("SlaveDeviceList"),
                    new XElement("ClientWhiteList")
                    );
                XElement? rtuSettings = newInstance.Element("RtuSettings");
                if (connectionType == "RtuOverTcp")
                {
                    string ipAddress = ReadIpAddress("Device IP address: ");
                    int port = ReadInt("Device port: ");
                    rtuSettings?.Add(
                        new XElement("IpAddress", ipAddress),
                        new XElement("Port", port)
                        );
                }
                else
                {
                    string portName = ReadNonEmptyString("Port name: ");
                    int baudRate = ReadInt("Baud rate: ");

                    Console.WriteLine("Parity:");
                    Console.WriteLine("1. None");
                    Console.WriteLine("2. Odd");
                    Console.WriteLine("3. Even");
                    string parityChoice = ReadFromOptions("Choose (1/2/3): ", "1", "2", "3");
                    string parity = "None";
                    if (parityChoice == "2") parity = "Odd";
                    else if (parityChoice == "3") parity = "Even";

                    string stopBits = ReadFromOptions("Stop bits (1/1.5/2): ", "1", "1.5", "2");
                    string dataBits = ReadFromOptions("Data bits (5-8): ", "5", "6", "7", "8");

                    rtuSettings?.Add(
                        new XElement("PortName", portName),
                        new XElement("BaudRate", baudRate),
                        new XElement("Parity", parity),
                        new XElement("StopBits", stopBits),
                        new XElement("DataBits", dataBits)
                    );
                }

                XElement? slaveDeviceList = newInstance.Element("SlaveDeviceList");
                int slaveCount = ReadInt("Number of devices to add: ");

                for (int i = 0; i < slaveCount; i++)
                {
                    Console.WriteLine($"\nSlave device #{i + 1}:");
                    int unitId = ReadInt("UnitId: ");

                    string description = ReadNonEmptyString("Description: ");

                    slaveDeviceList?.Add(
                        new XElement("Slave",
                            new XElement("UnitId", unitId),
                            new XElement("Description", description)
                        )
                    );
                }

                XElement? clientWhiteList = newInstance.Element("ClientWhiteList");
                int clientCount = ReadInt("\nNumber of clients to add to the white list: ");

                for (int i = 0; i < clientCount; i++)
                {
                    Console.WriteLine($"\nClient #{i + 1}:");
                    string clientIp = ReadIpAddress("IP address: ");

                    Console.Write("Permission (R - read only, W - read and write): ");
                    string permission = ReadFromOptions("Permission (R - read only, W - read and write): ", "r", "w").ToUpper();

                    clientWhiteList?.Add(
                        new XElement("Client",
                            new XElement("IpAddress", clientIp),
                            new XElement("Permission", permission)
                        )
                    );
                }
                doc.Root?.Element("Instances")?.Add(newInstance);

                doc.Save(configPath);

                try
                {
                    string appSettingsPath = "appsettings.json";
                    string jsonContent = File.ReadAllText(appSettingsPath);

                    using (JsonDocument jsonDoc = JsonDocument.Parse(jsonContent))
                    {
                        JsonNode? rootNode = JsonNode.Parse(jsonContent);
                        if (rootNode == null)
                        {
                            throw new InvalidOperationException("Failed to parse JSON content.");
                        }
                        if (rootNode["Service"] == null)
                        {
                            rootNode["Service"] = new JsonArray();
                        }

                        JsonObject newService = new JsonObject
                        {
                            ["Name"] = instanceName,
                            ["Description"] = "TCP <=> RTU Converter"
                        };

                        if (rootNode["Service"] is JsonArray serviceArray)
                        {
                            serviceArray.Add(newService);
                        }
                        else
                        {
                            rootNode["Service"] = new JsonArray { newService };
                        }

                        File.WriteAllText(appSettingsPath, rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                        Console.WriteLine($"Instance '{instanceName}' added to appsettings.json");
                    }
                }
                catch (Exception jsonEx)
                {
                    Console.WriteLine($"Error while updating appsettings.json: {jsonEx.Message}");
                }

                Console.WriteLine($"\nInstance '{instanceName}' was successfully added to the configuration file.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            Console.WriteLine();
            ChooseAction();
        }

        private static void RunInConsole()
        {
            Console.Clear();
            string instanceName = ReadNonEmptyString("Enter service name: ");
            string configPath = "config.xml";
            XDocument doc = XDocument.Load(configPath);
            var existingInstance = doc.Root?.Element("Instances")?.Elements("Instance").FirstOrDefault(e => e.Attribute("name")?.Value == instanceName);
            if (existingInstance == null)
            {
                Console.WriteLine($"Service {instanceName} is not configured");
                ChooseAction();
            }
            else
            {

                var exitCode = HostFactory.Run(x =>
                {
                    x.Service<ModbusService>(s =>
                    {
                        s.ConstructUsing(service => new ModbusService(instanceName));
                        s.WhenStarted(service => service.Start());
                        s.WhenStopped(service => service.Stop());
                    });
                    x.RunAsLocalSystem();
                    x.SetServiceName(instanceName);
                    x.SetDisplayName(instanceName);
                    x.SetDescription("RTU <=> TCP Converter");
                });
                int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
                Environment.ExitCode = exitCodeValue;
               
            }
        }

        private static string ReadNonEmptyString(string prompt)
        {
            string? input;
            do
            {
                Console.Write(prompt);
                input = Console.ReadLine();
            }
            while (string.IsNullOrWhiteSpace(input));
            return input;
        }

        private static string ValidateOperationMode()
        {
            string? input;
            while (true)
            {
                Console.Write("Choose operation mode [auto/manual/learning]: ");
                input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    input = input.ToLower();
                    if (input == "auto" || input == "manual" || input == "learning")
                    {
                        return input;
                    }
                }
            }
        }

        private static int ReadInt(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                if (int.TryParse(Console.ReadLine(), out int input))
                    return input;
            }
        }

        private static string ReadFromOptions(string prompt, params string[] options)
        {
            string? input;
            do
            {
                Console.Write(prompt);
                input = Console.ReadLine()?.Trim().ToLower();
            } while (input == null || !options.Contains(input));
            return input;
        }

        private static string ReadIpAddress(string prompt)
        {
            string? input;
            while (true)
            {
                Console.Write(prompt);
                input = Console.ReadLine();
                if (System.Net.IPAddress.TryParse(input, out _))
                    return input;
                Console.WriteLine("Invalid IP address. Try again.");
            }
        }

    }

}

