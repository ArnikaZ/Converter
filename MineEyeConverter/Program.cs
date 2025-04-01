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
using System.Net.WebSockets;
using MineEyeConverter.Config;
using Topshelf.Runtime;
using System.Text;


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

                
                string serviceName = string.Empty;

                var exitCode = HostFactory.Run(x =>
                {
                    x.AddCommandLineDefinition("name", f =>
                    {
                        serviceName = f;
                    });
                    x.ApplyCommandLine();

                    x.AfterInstall(installSettings => {
                        AddCommandLineParametersToStartupOptions(installSettings);
                    });

                    x.Service<ModbusService>(s =>
                    {
                        s.ConstructUsing(modbusService => new ModbusService(serviceName));
                        s.WhenStarted(modbusService => modbusService.Start());
                        s.WhenStopped(modbusService => modbusService.Stop());
                    });

                    x.RunAsLocalSystem();
                    x.SetServiceName(serviceName);
                    x.SetDisplayName(serviceName);
                    x.SetDescription("MineEyeCOnverter");
                    x.StartAutomatically();
                });

                int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
                Environment.ExitCode = exitCodeValue;
            }
        }
        private static void AddCommandLineParametersToStartupOptions(InstallHostSettings installSettings)
        {
            var serviceKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $"SYSTEM\\CurrentControlSet\\Services\\{installSettings.ServiceName}",
                true);

            if (serviceKey == null)
            {
                throw new Exception($"Could not locate Registry Key for service '{installSettings.ServiceName}'");
            }

            var arguments = Environment.GetCommandLineArgs();

            string programName = null;
            StringBuilder argumentsList = new StringBuilder();

            for (int i = 0; i < arguments.Length; i++)
            {
                if (i == 0)
                {
                    // program name is the first argument
                    programName = arguments[i];
                }
                else
                {
                    // Remove these servicename and instance arguments as TopShelf adds them as well
                    // Remove install switch
                    if (arguments[i].StartsWith("-servicename", StringComparison.InvariantCultureIgnoreCase) |
                        arguments[i].StartsWith("-instance", StringComparison.InvariantCultureIgnoreCase) |
                        arguments[i].StartsWith("install", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }
                    argumentsList.Append(" ");
                    argumentsList.Append(arguments[i]);
                }
            }

            // Apply the arguments to the ImagePath value under the service Registry key
            var imageName = $"\"{Environment.CurrentDirectory}\\{programName}\" {argumentsList.ToString()}";
            serviceKey.SetValue("ImagePath", imageName, Microsoft.Win32.RegistryValueKind.String);
        }

        private static void ChooseAction()
        {
            Console.Clear();
            Console.WriteLine("Available parameters");
            Console.WriteLine("install -name:ServiceName    : install service");
            Console.WriteLine("start -name:ServiceName      : start service");
            Console.WriteLine("stop -name:ServiceName       : stop service");
            Console.WriteLine("uninstall -name:ServiceName  : unistall service");
            Console.WriteLine();
            Console.WriteLine("Available options in console mode:");

            var menuOptions = new Dictionary<int, (string Description, Action action)>
            {
                {1, ("Add new instance configuration", AddNewInstance) },
                {2, ("Edit existing instance", EditInstance) },
                {3, ("Remove instance", RemoveInstance) },
                {4, ("View instance details", ViewInstanceDetails) },
                {5, ("Exit", ()=>Environment.Exit(0)) }
            };
            foreach (var option in menuOptions)
            {
                Console.WriteLine($"{option.Key}. {option.Value.Description}");
            }
            Console.WriteLine();
            int choice = ReadInt("Choose option: ");

            if (menuOptions.TryGetValue(choice, out var selectedOption))
            {
                selectedOption.action();
            }
            else
            {
                Console.WriteLine("Invalid option");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                ChooseAction();
            }
        }

        private static void AddNewInstance()
        {
            Console.Clear();
            try
            {
                ConfigLoader.EnsureConfigFolderExists();
                Console.WriteLine("Add new instance");
                string instanceName = ReadNonEmptyString("Enter instance name: ");
                string configPath = ConfigLoader.GetInstanceConfigPath(instanceName);
               

                if (File.Exists(configPath))
                {
                    Console.WriteLine($"Error: instance with name '{instanceName}' already exists in configuration.");
                    Console.WriteLine("Operation aborted. Press any key to continue...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }

                string operationMode = ValidateOperationMode();
                int listeningPort = ReadInt("Listening port: ");
                Console.WriteLine("Connection type: ");
                Console.WriteLine("1. Rtu over Tcp");
                Console.WriteLine("2. COM");
                string connectionTypeChoice = ReadFromOptions("Choose (1/2): ", "1", "2");
                string connectionType = connectionTypeChoice == "1" ? "RtuOverTcp" : "COM";
                var instance = new Instance
                {
                    Name = instanceName,
                    OperationMode = operationMode,
                    ListeningPort = listeningPort,
                    ConnectionType = connectionType,
                    RtuSettings = new RtuSettings(),
                    SlaveDeviceList = new SlaveDeviceList(),
                    ClientWhiteList = new ClientWhiteList()
                };
                if (connectionType == "RtuOverTcp")
                {
                    string ipAddress = ReadIpAddress("Device IP address: ");
                    int port = ReadInt("Device port: ");
                    instance.RtuSettings.IpAddress = ipAddress;
                    instance.RtuSettings.Port = port;
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

                    instance.RtuSettings.PortName = portName;
                    instance.RtuSettings.BaudRate = baudRate;
                    instance.RtuSettings.Parity = parity;
                    instance.RtuSettings.StopBits = int.Parse(stopBits);
                    instance.RtuSettings.DataBits = int.Parse(dataBits);
                }

                int slaveCount = ReadInt("Number of devices to add: ");
                for (int i = 0; i < slaveCount; i++)
                {
                    Console.WriteLine($"\nSlave device #{i + 1}:");
                    int unitId = ReadInt("UnitId: ");
                    string description = ReadNonEmptyString("Description: ");

                    instance.SlaveDeviceList.Slaves.Add(new Slave
                    {
                        UnitId = unitId,
                        Description = description
                    });
                }

                int clientCount = ReadInt("\nNumber of clients to add to the white list: ");
                for (int i = 0; i < clientCount; i++)
                {
                    Console.WriteLine($"\nClient #{i + 1}:");
                    string clientIp = ReadIpAddress("IP address: ");
                    string permission = ReadFromOptions("Permission (R - read only, W - read and write): ", "r", "w").ToUpper();

                    instance.ClientWhiteList.Clients.Add(new Client
                    {
                        IpAddress = clientIp,
                        Permission = permission
                    });
                }
                var config = new Configuration
                {
                    Instances = new List<Instance> { instance }
                };

                ConfigLoader.SaveInstanceConfiguration(config, instanceName);

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
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            ChooseAction();
        }

        private static void EditInstance()
        {
            Console.Clear();
            try
            {
                var instanceNames = ConfigLoader.GetAvailableInstances();

                if (instanceNames.Count == 0)
                {
                    Console.WriteLine("No instances found in configuration");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }

                Console.WriteLine("Available instances:");
                for (int i = 0; i < instanceNames.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {instanceNames[i]}");
                }
                int selection = ReadInt("Choose instance to edit (0 to cancel): ");
                if (selection == 0 || selection > instanceNames.Count)
                {
                    ChooseAction();
                    return;
                }

                string selectedInstanceName = instanceNames[selection - 1];
                var config = ConfigLoader.LoadInstanceConfiguration(selectedInstanceName);
                var instanceConfig = config.Instances.FirstOrDefault();
                if (instanceConfig == null)
                {
                    Console.WriteLine("Error: instance not found");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }
                Console.WriteLine($"Editing instance: {selectedInstanceName}");
                Console.WriteLine("1. Operation Mode");
                Console.WriteLine("2. Listening Port");
                Console.WriteLine("3. Connection Type and Settings");
                Console.WriteLine("4. Slave Devices");
                Console.WriteLine("5. Client Whitelist");
                Console.WriteLine("0. Cancel");
                int editChoice = ReadInt("Choose option: ");

                switch (editChoice)
                {
                    case 1:
                        string operationMode = ValidateOperationMode();
                        instanceConfig.OperationMode = operationMode;
                        break;

                    case 2:
                        int listeningPort = ReadInt("New listening port: ");
                        instanceConfig.ListeningPort = listeningPort;
                        break;

                    case 3:
                        EditConnectionSettings(instanceConfig);
                        break;

                    case 4:
                        EditSlaveDevices(instanceConfig);
                        break;

                    //case 5:
                    //    EditClientWhitelist(instanceConfig);
                    //    break;

                    default:
                        ChooseAction();
                        return;
                }
                ConfigLoader.SaveInstanceConfiguration(config, selectedInstanceName);
                Console.WriteLine($"Instance '{selectedInstanceName}' updated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            ChooseAction();
            
        }

        private static void RemoveInstance()
        {
            Console.Clear();
            try
            {
                var instanceNames = ConfigLoader.GetAvailableInstances();

                if (instanceNames.Count == 0)
                {
                    Console.WriteLine("No instances found in configuration");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }

                Console.WriteLine("Available instances:");
                for (int i = 0; i < instanceNames.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {instanceNames[i]}");
                }

                int selection = ReadInt("Choose instance to remove (0 to cancel): ");
                if (selection == 0 || selection > instanceNames.Count)
                {
                    ChooseAction();
                    return;
                }

                string selectedInstanceName = instanceNames[selection - 1];

                Console.WriteLine($"Are you sure you want to remove instance '{selectedInstanceName}'? (y/n)");
                string confirmation = Console.ReadLine()?.ToLower() ?? "";

                if (confirmation == "y" || confirmation == "yes")
                {
                    // Delete the instance config file
                    string configPath = ConfigLoader.GetInstanceConfigPath(selectedInstanceName);
                    if (File.Exists(configPath))
                    {
                        File.Delete(configPath);

                        try
                        {
                            // Update appsettings.json
                            string appSettingsPath = "appsettings.json";
                            string jsonContent = File.ReadAllText(appSettingsPath);

                            using (JsonDocument jsonDoc = JsonDocument.Parse(jsonContent))
                            {
                                JsonNode? rootNode = JsonNode.Parse(jsonContent);
                                if (rootNode == null || rootNode["Service"] == null)
                                {
                                    throw new InvalidOperationException("Failed to parse JSON content or Service section not found.");
                                }

                                if (rootNode["Service"] is JsonArray serviceArray)
                                {
                                    for (int i = 0; i < serviceArray.Count; i++)
                                    {
                                        var service = serviceArray[i];
                                        if (service != null && service["Name"]?.ToString() == selectedInstanceName)
                                        {
                                            serviceArray.RemoveAt(i);
                                            break;
                                        }
                                    }

                                    File.WriteAllText(appSettingsPath, rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                                    Console.WriteLine($"Instance '{selectedInstanceName}' removed from appsettings.json");
                                }
                            }
                        }
                        catch (Exception jsonEx)
                        {
                            Console.WriteLine($"Error while updating appsettings.json: {jsonEx.Message}");
                        }

                        Console.WriteLine($"Instance '{selectedInstanceName}' removed successfully.");
                    }
                    else
                    {
                        Console.WriteLine($"Error: Instance configuration file not found for '{selectedInstanceName}'");
                    }
                }
                else
                {
                    Console.WriteLine("Operation cancelled");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            ChooseAction();
        }

        private static void ViewInstanceDetails()
        {
            Console.Clear();
            try
            {
                var instanceNames = ConfigLoader.GetAvailableInstances();

                if (instanceNames.Count == 0)
                {
                    Console.WriteLine("No instances found in configuration");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }

                Console.WriteLine("Available instances:");
                for (int i = 0; i < instanceNames.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {instanceNames[i]}");
                }

                int selection = ReadInt("Choose instance to view (0 to cancel): ");
                if (selection == 0 || selection > instanceNames.Count)
                {
                    ChooseAction();
                    return;
                }

                string selectedInstanceName = instanceNames[selection - 1];

                // Load the instance configuration
                var config = ConfigLoader.LoadInstanceConfiguration(selectedInstanceName);
                var instanceConfig = config.Instances.FirstOrDefault();

                if (instanceConfig == null)
                {
                    Console.WriteLine("Error: Instance not found.");
                    ChooseAction();
                    return;
                }

                Console.WriteLine($"Instance: {selectedInstanceName}");
                Console.WriteLine($"Operation Mode: {instanceConfig.OperationMode}");
                Console.WriteLine($"Listening Port: {instanceConfig.ListeningPort}");
                Console.WriteLine($"Connection Type: {instanceConfig.ConnectionType}");

                Console.WriteLine("\nRTU Settings:");
                var rtuSettings = instanceConfig.RtuSettings;

                if (instanceConfig.ConnectionType.Equals("RtuOverTcp", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  IP Address: {rtuSettings.IpAddress}");
                    Console.WriteLine($"  Port: {rtuSettings.Port}");
                }
                else if (instanceConfig.ConnectionType.Equals("COM", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  Port Name: {rtuSettings.PortName}");
                    Console.WriteLine($"  Baud Rate: {rtuSettings.BaudRate}");
                    Console.WriteLine($"  Parity: {rtuSettings.Parity}");
                    Console.WriteLine($"  Stop Bits: {rtuSettings.StopBits}");
                    Console.WriteLine($"  Data Bits: {rtuSettings.DataBits}");
                }

                Console.WriteLine("\nDevices:");
                var slaveDevices = instanceConfig.SlaveDeviceList?.Slaves;
                if (slaveDevices == null || !slaveDevices.Any())
                {
                    Console.WriteLine("  No devices configured.");
                }
                else
                {
                    foreach (var slave in slaveDevices)
                    {
                        Console.WriteLine($"  UnitId: {slave.UnitId}, Description: {slave.Description}");
                    }
                }

                Console.WriteLine("\nClient Whitelist:");
                var clients = instanceConfig.ClientWhiteList?.Clients;
                if (clients == null || !clients.Any())
                {
                    Console.WriteLine("  No clients in whitelist.");
                }
                else
                {
                    foreach (var client in clients)
                    {
                        Console.WriteLine($"  IP: {client.IpAddress}, Permission: {client.Permission}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
            ChooseAction();
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
        private static void EditConnectionSettings(Instance instanceConfig)
        {

            Console.WriteLine("Connection type: ");
            Console.WriteLine("1. Rtu over Tcp");
            Console.WriteLine("2. COM");
            string connectionTypeChoice = ReadFromOptions("Choose (1/2): ", "1", "2");
            string connectionType = connectionTypeChoice == "1" ? "RtuOverTcp" : "COM";

            instanceConfig.ConnectionType = connectionType;

            // Clear existing RTU settings
            instanceConfig.RtuSettings = new RtuSettings();

            if (connectionType == "RtuOverTcp")
            {
                string ipAddress = ReadIpAddress("Device IP address: ");
                int port = ReadInt("Device port: ");
                instanceConfig.RtuSettings.IpAddress = ipAddress;
                instanceConfig.RtuSettings.Port = port;
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

                string stopBitsStr = ReadFromOptions("Stop bits (1/1.5/2): ", "1", "1.5", "2");
                int stopBits = stopBitsStr == "1.5" ? 0 : int.Parse(stopBitsStr);

                string dataBitsStr = ReadFromOptions("Data bits (5-8): ", "5", "6", "7", "8");
                int dataBits = int.Parse(dataBitsStr);

                instanceConfig.RtuSettings.PortName = portName;
                instanceConfig.RtuSettings.BaudRate = baudRate;
                instanceConfig.RtuSettings.Parity = parity;
                instanceConfig.RtuSettings.StopBits = stopBits;
                instanceConfig.RtuSettings.DataBits = dataBits;
            }
        }

        private static void EditSlaveDevices(Instance instanceConfig)
        {
            if (instanceConfig.SlaveDeviceList == null)
                instanceConfig.SlaveDeviceList = new SlaveDeviceList();

            var slaves = instanceConfig.SlaveDeviceList.Slaves;

            Console.WriteLine("Slave devices:");
            if (slaves.Count == 0)
            {
                Console.WriteLine("No slave devices configured.");
            }
            else
            {
                for (int i = 0; i < slaves.Count; i++)
                {
                    var slave = slaves[i];
                    Console.WriteLine($"{i + 1}. UnitId: {slave.UnitId}, Description: {slave.Description}");
                }
            }

            Console.WriteLine("\nOptions:");
            Console.WriteLine("1. Add new device");
            Console.WriteLine("2. Edit existing device");
            Console.WriteLine("3. Remove device");
            Console.WriteLine("0. Return to previous menu");

            int choice = ReadInt("Choose option: ");

            switch (choice)
            {
                case 1:
                    Console.WriteLine("Adding new device:");
                    int unitId = ReadInt("UnitId: ");
                    string description = ReadNonEmptyString("Description: ");

                    slaves.Add(new Slave
                    {
                        UnitId = unitId,
                        Description = description
                    });

                    Console.WriteLine("Device added successfully.");
                    break;

                case 2:
                    if (slaves.Count == 0)
                    {
                        Console.WriteLine("No devices to edit.");
                        break;
                    }

                    int editIndex = ReadInt("Enter device number to edit: ") - 1;
                    if (editIndex < 0 || editIndex >= slaves.Count)
                    {
                        Console.WriteLine("Invalid selection.");
                        break;
                    }

                    int newUnitId = ReadInt("UnitId: ");
                    string newDescription = ReadNonEmptyString("Description: ");

                    slaves[editIndex] = new Slave
                    {
                        UnitId = newUnitId,
                        Description = newDescription
                    };

                    Console.WriteLine("Device updated successfully.");
                    break;

                case 3:
                    if (slaves.Count == 0)
                    {
                        Console.WriteLine("No devices to remove.");
                        break;
                    }

                    int removeIndex = ReadInt("Enter device number to remove: ") - 1;
                    if (removeIndex < 0 || removeIndex >= slaves.Count)
                    {
                        Console.WriteLine("Invalid selection.");
                        break;
                    }

                    slaves.RemoveAt(removeIndex);
                    Console.WriteLine("Device removed successfully.");
                    break;

                case 0:
                default:
                    return;
            }
            }

        private static void EditClientWhitelist(XElement instanceElement)
        {
            XElement clientWhiteList = instanceElement.Element("ClientWhiteList");
            var clients = clientWhiteList.Elements("Client").ToList();

            Console.WriteLine("Client whitelist:");
            if (clients.Count == 0)
            {
                Console.WriteLine("No clients in whitelist.");
            }
            else
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    var client = clients[i];
                    Console.WriteLine($"{i + 1}. IP: {client.Element("IpAddress").Value}, Permission: {client.Element("Permission").Value}");
                }
            }

            Console.WriteLine("\nOptions:");
            Console.WriteLine("1. Add new client");
            Console.WriteLine("2. Edit existing client");
            Console.WriteLine("3. Remove client");
            Console.WriteLine("0. Return to previous menu");

            int choice = ReadInt("Choose option: ");

            switch (choice)
            {
                case 1:
                    Console.WriteLine("Adding new client:");
                    string clientIp = ReadIpAddress("IP address: ");
                    string permission = ReadFromOptions("Permission (R - read only, W - read and write): ", "r", "w").ToUpper();

                    clientWhiteList.Add(
                        new XElement("Client",
                            new XElement("IpAddress", clientIp),
                            new XElement("Permission", permission)
                        )
                    );
                    Console.WriteLine("Client added successfully.");
                    break;

                case 2:
                    if (clients.Count == 0)
                    {
                        Console.WriteLine("No clients to edit.");
                        break;
                    }

                    int editIndex = ReadInt("Enter client number to edit: ") - 1;
                    if (editIndex < 0 || editIndex >= clients.Count)
                    {
                        Console.WriteLine("Invalid selection.");
                        break;
                    }

                    var clientToEdit = clients[editIndex];
                    string newClientIp = ReadIpAddress("IP address: ");
                    string newPermission = ReadFromOptions("Permission (R - read only, W - read and write): ", "r", "w").ToUpper();

                    clientToEdit.Element("IpAddress").Value = newClientIp;
                    clientToEdit.Element("Permission").Value = newPermission;
                    Console.WriteLine("Client updated successfully.");
                    break;

                case 3:
                    if (clients.Count == 0)
                    {
                        Console.WriteLine("No clients to remove.");
                        break;
                    }

                    int removeIndex = ReadInt("Enter client number to remove: ") - 1;
                    if (removeIndex < 0 || removeIndex >= clients.Count)
                    {
                        Console.WriteLine("Invalid selection.");
                        break;
                    }

                    clients[removeIndex].Remove();
                    Console.WriteLine("Client removed successfully.");
                    break;

                case 0:
                default:
                    return;
            }
        }
    }

}

