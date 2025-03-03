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

[assembly: log4net.Config.XmlConfigurator(Watch =true)]
namespace MineEyeConverter
{
   /// <summary>
   /// Entry point for the MineEyeConverter application.
   /// Handles command-line arguments and service configuration.
   /// </summary>
    internal class Program
    {
        
        
        static void Main(string[] args)
        {
           // Configure application from appsettings.json and command - line arguments
            var config = new ConfigurationBuilder()
           .SetBasePath(AppContext.BaseDirectory)
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddCommandLine(args)
           .Build();


            var instanceName = config["Service:Name"] ?? config["name"];
            var serviceDescription = config["Service:Description"] ?? "TCP <=> RTU Converter";

            var exitCode = HostFactory.Run(x =>
            {
                x.AddCommandLineDefinition("name", f => { instanceName = f; });
                x.ApplyCommandLine();
                x.Service<ModbusService>(s =>
                {
                    s.ConstructUsing(modbusService => new ModbusService(instanceName));
                    s.WhenStarted(modbusService => modbusService.Start());
                    s.WhenStopped(modbusService => modbusService.Stop());
                });

                x.RunAsLocalSystem();
                x.SetServiceName(instanceName);
                x.SetDisplayName(instanceName);
                x.SetDescription(serviceDescription);
                x.StartAutomatically();
            });


            // int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
            // Environment.ExitCode = exitCodeValue;



            //Example code for running in console mode
            //var server = new ModbusTcpServer("Przenosnik15", false);
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
                
            //    Thread.Sleep(3000);//time to perform initial polling

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
            //    // Clean up
            //    client.Stop();
            //    client.Dispose();
            //}

        }


    }

}


