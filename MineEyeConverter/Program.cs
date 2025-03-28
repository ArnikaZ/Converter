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
                string configPath = "config.xml";
                XDocument doc = XDocument.Load(configPath);
                Console.WriteLine("Add new instance");
                string instanceName = ReadNonEmptyString("Enter instance name: ");
                var existingInstance = doc.Root?.Element("Instances")?.Elements("Instance").FirstOrDefault(e => e.Attribute("name")?.Value == instanceName);

                if (existingInstance != null)
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
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            ChooseAction();
        }

        private static void EditInstance()
        {
            Console.Clear();
            try
            {
                string configFile = "config.xml";
                XDocument doc = XDocument.Load(configFile);

                var instances = doc.Root?.Element("Instances")?.Elements("Instance")
                    .Select(e => e.Attribute("name")?.Value)
                    .Where(name => name != null)
                    .ToList();
                if (instances == null || instances.Count == 0)
                {
                    Console.WriteLine("No instances found in configuration");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }
                Console.WriteLine("Available instances:");
                for (int i = 0; i < instances.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {instances[i]}");
                }
                int selection = ReadInt("Choose instance to edit (0 to cancel): ");
                if (selection == 0 || selection > instances.Count)
                {
                    ChooseAction();
                    return;
                }
                string? selectedInstanceName = instances[selection - 1];
                var instanceElement = doc.Root?.Element("Instances")?.Elements("Instance")
                    .FirstOrDefault(e => e.Attribute("name")?.Value == selectedInstanceName);
                if (instanceElement == null)
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
                        instanceElement.Element("OperationMode")!.Value = operationMode;
                        break;

                    case 2:
                        int listeningPort = ReadInt("New listening port: ");
                        instanceElement.Element("ListeningPort")!.Value = listeningPort.ToString();
                        break;

                    case 3:
                        EditConnectionSettings(instanceElement);
                        break;

                    case 4:
                        EditSlaveDevices(instanceElement);
                        break;

                    case 5:
                        EditClientWhitelist(instanceElement);
                        break;

                    default:
                        ChooseAction();
                        return;
                }
                doc.Save(configFile);
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
                string configFile = "config.xml";
                XDocument doc = XDocument.Load(configFile);
                var instances = doc.Root?.Element("Instances")?.Elements("Instance")
                    .Select(e => e.Attribute("name")?.Value)
                    .Where(name => name != null)
                    .ToList();
                if(instances==null || instances.Count == 0)
                {
                    Console.WriteLine("No instances found in configuration");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }
                Console.WriteLine("Available instances");
                for(int i = 0; i < instances.Count; i++)
                {
                    Console.WriteLine($"{i+1}. {instances[i]}");
                }
                int selection = ReadInt("Choose instance to remove (0 to cancel):");
                if(selection==0 || selection > instances.Count)
                {
                    ChooseAction();
                    return;
                }
                string? selectedInstance = instances[selection - 1];
                Console.WriteLine($"Are you sure you want to remove instance '{selectedInstance}'? (y/n)");
                string confirmation = Console.ReadLine()?.ToLower() ?? "";
                if (confirmation == "y" || confirmation == "yes")
                {
                    var instanceElement = doc.Root?.Element("Instances")?.Elements("Instance")
                        .FirstOrDefault(e => e.Attribute("name").Value == selectedInstance);
                    if (instanceElement != null)
                    {
                        instanceElement.Remove();
                        doc.Save(configFile);
                        try
                        {
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
                                        if (service != null && service["Name"]?.ToString() == selectedInstance)
                                        {
                                            serviceArray.RemoveAt(i);
                                            break;
                                        }
                                    }

                                    File.WriteAllText(appSettingsPath, rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                                    Console.WriteLine($"Instance '{selectedInstance}' removed from appsettings.json");
                                }
                            }
                        }
                        catch (Exception jsonEx)
                        {
                            Console.WriteLine($"Error while updating appsettings.json: {jsonEx.Message}");
                        }

                        Console.WriteLine($"Instance '{selectedInstance}' removed successfully.");
                    }
                }
                else
                {
                    Console.WriteLine("Operation cancelled");
                }
            }
            catch(Exception ex)
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
                string configPath = "config.xml";
                XDocument doc = XDocument.Load(configPath);

                var instances = doc.Root?.Element("Instances")?.Elements("Instance")
                    .Select(e => e.Attribute("name")?.Value)
                    .Where(name => name != null)
                    .ToList();

                if (instances == null || instances.Count == 0)
                {
                    Console.WriteLine("No instances found in configuration.");
                    Console.WriteLine("Press any key to return to main menu...");
                    Console.ReadKey();
                    ChooseAction();
                    return;
                }

                Console.WriteLine("Available instances:");
                for (int i = 0; i < instances.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {instances[i]}");
                }

                int selection = ReadInt("Choose instance to view (0 to cancel): ");
                if (selection == 0 || selection > instances.Count)
                {
                    ChooseAction();
                    return;
                }

                string? selectedInstanceName = instances[selection - 1];
                var instanceElement = doc.Root?.Element("Instances")?.Elements("Instance")
                    .FirstOrDefault(e => e.Attribute("name")?.Value == selectedInstanceName);

                if (instanceElement == null)
                {
                    Console.WriteLine("Error: Instance not found.");
                    ChooseAction();
                    return;
                }

                Console.WriteLine($"Instance: {selectedInstanceName}");
                Console.WriteLine($"Operation Mode: {instanceElement.Element("OperationMode")?.Value}");
                Console.WriteLine($"Listening Port: {instanceElement.Element("ListeningPort")?.Value}");
                Console.WriteLine($"Connection Type: {instanceElement.Element("ConnectionType")?.Value}");
                Console.WriteLine("\nRTU Settings:");
                var rtuSettings = instanceElement.Element("RtuSettings");
                foreach (var setting in rtuSettings.Elements())
                {
                    Console.WriteLine($"  {setting.Name}: {setting.Value}");
                }

                Console.WriteLine("\nDevices:");
                var slaveDevices = instanceElement.Element("SlaveDeviceList")?.Elements("Slave");
                if (slaveDevices == null || !slaveDevices.Any())
                {
                    Console.WriteLine("No devices configured.");
                }
                else
                {
                    foreach (var slave in slaveDevices)
                    {
                        Console.WriteLine($"  UnitId: {slave.Element("UnitId")?.Value}, Description: {slave.Element("Description")?.Value}");
                    }
                }

                Console.WriteLine("\nClient Whitelist:");
                var clients = instanceElement.Element("ClientWhiteList")?.Elements("Client");
                if (clients == null || !clients.Any())
                {
                    Console.WriteLine("  No clients in whitelist.");
                }
                else
                {
                    foreach (var client in clients)
                    {
                        Console.WriteLine($"  IP: {client.Element("IpAddress")?.Value}, Permission: {client.Element("Permission")?.Value}");
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
        private static void EditConnectionSettings(XElement instanceElement)
        {
            Console.WriteLine("Connection type: ");
            Console.WriteLine("1. Rtu over Tcp");
            Console.WriteLine("2. COM");
            string connectionTypeChoice = ReadFromOptions("Choose (1/2): ", "1", "2");
            string connectionType = connectionTypeChoice == "1" ? "RtuOverTcp" : "COM";

            instanceElement.Element("ConnectionType").Value = connectionType;

            XElement rtuSettings = instanceElement.Element("RtuSettings");
            rtuSettings.RemoveAll();

            if (connectionType == "RtuOverTcp")
            {
                string ipAddress = ReadIpAddress("Device IP address: ");
                int port = ReadInt("Device port: ");
                rtuSettings.Add(
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

                rtuSettings.Add(
                    new XElement("PortName", portName),
                    new XElement("BaudRate", baudRate),
                    new XElement("Parity", parity),
                    new XElement("StopBits", stopBits),
                    new XElement("DataBits", dataBits)
                );
            }
        }

        private static void EditSlaveDevices(XElement instanceElement)
        {
            XElement slaveDeviceList = instanceElement.Element("SlaveDeviceList");
            var slaves = slaveDeviceList.Elements("Slave").ToList();

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
                    Console.WriteLine($"{i + 1}. UnitId: {slave.Element("UnitId").Value}, Description: {slave.Element("Description").Value}");
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

                    slaveDeviceList.Add(
                        new XElement("Slave",
                            new XElement("UnitId", unitId),
                            new XElement("Description", description)
                        )
                    );
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

                    var slaveToEdit = slaves[editIndex];
                    int newUnitId = ReadInt("UnitId: ");
                    string newDescription = ReadNonEmptyString("Description: ");

                    slaveToEdit.Element("UnitId").Value = newUnitId.ToString();
                    slaveToEdit.Element("Description").Value = newDescription;
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

                    slaves[removeIndex].Remove();
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

