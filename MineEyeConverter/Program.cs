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
                x.AddCommandLineDefinition("name", f => {
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
                        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                            $"Wczytano nazwę usługi z pliku: {serviceName} {DateTime.Now}{Environment.NewLine}");
                    }
                    else
                    {
                        serviceName = config["name"] ?? string.Empty;

                    }
                }

                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                    $"Szukana nazwa usługi: {serviceName ?? "brak"} {DateTime.Now}{Environment.NewLine}");

                var selectedServiceConfig = services.FirstOrDefault(
                    s => s.GetValue<string>("Name")?.Equals(serviceName, StringComparison.OrdinalIgnoreCase) == true);

                if (selectedServiceConfig == null)
                {
                    if (!string.IsNullOrEmpty(serviceName))
                    {
                        // Użyj nazwy z argumentów, ale bez dodatkowej konfiguracji
                        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                            $"Nie znaleziono konfiguracji dla usługi {serviceName}, używam domyślnych wartości {DateTime.Now}{Environment.NewLine}");
                    }
                    else if (services.Any())
                    {
                        // Jako ostateczność, użyj pierwszej usługi z konfiguracji
                        selectedServiceConfig = services[0];
                        serviceName = selectedServiceConfig.GetValue<string>("Name",string.Empty);
                        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                            $"Nie podano nazwy usługi, używam pierwszej z konfiguracji: {serviceName} {DateTime.Now}{Environment.NewLine}");
                    }
                    else
                    {
                        // Jeśli nie ma konfiguracji usług, użyj domyślnej nazwy
                        serviceName = "ModbusConverter";
                        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                            $"Brak konfiguracji usług, używam domyślnej nazwy: {serviceName} {DateTime.Now}{Environment.NewLine}");
                    }
                }
                else
                {
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                        $"Znaleziono konfigurację dla usługi: {serviceName} {DateTime.Now}{Environment.NewLine}");
                }

                // Zapisz nazwę usługi do pliku, jeśli jeszcze nie istnieje
                // To zapewni, że nazwa zostanie zapamiętana nawet jeśli nie była podana przy instalacji
                if (!string.IsNullOrEmpty(serviceName) && !File.Exists(Path.Combine(AppContext.BaseDirectory, "service_name.txt")))
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "service_name.txt"), serviceName);
                }

                // Pobranie opisu usługi z wybranej konfiguracji lub użycie domyślnego
                string serviceDescription = selectedServiceConfig?.GetValue<string>("Description")
                                            ?? "TCP <=> RTU Converter";

                // Konfiguracja usługi
                x.Service<ModbusService>(s =>
                {
                    s.ConstructUsing(modbusService => {
                        // Przekaż całą konfigurację usługi do konstruktora, aby ModbusService
                        // mógł uzyskać wszystkie specyficzne parametry
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



            //Example code for running in console mode
            //var server = new ModbusTcpServer("Przenosnik8", false);
            //server.Start();
            //Console.WriteLine("Naciśnij dowolny klawisz, aby zakończyć...");
            //Console.ReadKey();
            //server.Stop();

            //LearningModeHandler lm = new LearningModeHandler("Przenosnik15");
            //List<SlaveConfiguration> discoveredConfigs = lm.DiscoverSlaves();
            //lm.SaveConfigurationToXml(discoveredConfigs);
            //Console.ReadKey();

            //var client = new ModbusClient("127.0.0.1", 502, 1000);
            //client.AddSlaveDevice(1);
            //client.AddSlaveDevice(2);
            //client.AddSlaveDevice(3);
            //try
            //{
            //    Console.WriteLine("Starting UGS communication...");
            //    client.Start();

            //    Thread.Sleep(3000);

            //    foreach (var device in client.SlaveDevices.Values)
            //    {
            //        Console.WriteLine($"Device ID: {device.UnitId}");
            //        Console.WriteLine("Holding Registers (0-19):");
            //        for (int i = 0; i < 20; i++)
            //        {
            //            Console.Write($"{device.HoldingRegisters[i]} ");
            //            if ((i + 1) % 5 == 0) Console.WriteLine();
            //        }
            //        Console.WriteLine();
            //    }

            //    Console.ReadKey();
            //}
            //finally
            //{
            //    client.Stop();
            //    client.Dispose();
            //}


        }

    }

}


