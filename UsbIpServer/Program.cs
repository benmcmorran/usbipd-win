﻿// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer, Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

[assembly: CLSCompliant(true)]
[assembly: SupportedOSPlatform("windows8.0")]

namespace UsbIpServer
{
    static class Program
    {
        static string Product => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        static string Copyright => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;

        static void ShowCopyright()
        {
            Console.WriteLine($@"{Product} {GitVersionInformation.MajorMinorPatch}
{Copyright}

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, version 2.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
");
        }

        public static string Truncate(this string value, int maxChars)
        {
            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 3) + "...";
        }

        static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = Path.GetFileName(Process.GetCurrentProcess().ProcessName),
            };
            app.VersionOption("-v|--version", GitVersionInformation.MajorMinorPatch, GitVersionInformation.InformationalVersion);

            void DefaultCmdLine(CommandLineApplication cmd)
            {
                // all commands (as well as the top-level executable) have these
                cmd.FullName = Product;
                cmd.ShortVersionGetter = app.ShortVersionGetter;
                cmd.HelpOption("-h|--help").ShowInHelpText = false;
            }

            DefaultCmdLine(app);
            app.OptionHelp.ShowInHelpText = true;
            app.Command("license", (cmd) =>
            {
                cmd.Description = "Display license information";
                DefaultCmdLine(cmd);
                cmd.OnExecute(() =>
                {
                    ShowCopyright();
                    return 0;
                });
            });

            app.Command("list", (cmd) =>
            {
                cmd.Description = "List connected USB devices.";
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                    var persistedDevices = RegistryUtils.GetPersistedDevices(connectedDevices);
                    var deviceChecker = new DeviceInfoChecker();
                    Console.WriteLine("Present:");
                    Console.WriteLine($"{"BUS-ID",-8}{"Device",-60}{"Status",-5}");
                    foreach (var device in connectedDevices)
                    {
                        Console.WriteLine($"{device.BusId, -8}{Truncate(deviceChecker.GetDeviceName(device), 60),-60}{ (RegistryUtils.IsDeviceShared(device)? RegistryUtils.IsDeviceAttached(device)? "Attached" : "Shared" : "Not-Shared"), -5}");
                    }
                    Console.Write("\n");
                    Console.WriteLine("Persisted:");
                    Console.WriteLine($"{"GUID",-38}{"BUS-ID",-8}{"Device",-60}");
                    foreach (var device in persistedDevices)
                    {
                        Console.WriteLine($"{device.Guid,-38}{device.BusId, -8}{Truncate(device.Name, 60),-60}");
                    }

                    if (!Server.IsServerRunning())
                    {
                        Console.WriteLine("\nWARNING: Server is currently not running.");
                    }

                    return 0;
                });
            });

            app.Command("bind", (cmd) =>
            {
                cmd.Description = "Bind device";
                var busId = cmd.Option("-b|--busid=<busid>", "Share device having <busid>", CommandOptionType.SingleValue);
                var bindAll = cmd.Option("-a|--all", "Share all devices.", CommandOptionType.NoValue);
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                    if (bindAll.HasValue())
                    {
                        foreach (var device in connectedDevices)
                        {
                            var checker = new DeviceInfoChecker();
                            RegistryUtils.ShareDevice(device, checker.GetDeviceName(device));
                        }

                        return 0;
                    }

                    try
                    {
                        var targetDevice = connectedDevices.Where(x => x.BusId == busId.Value()).First();
                        if (targetDevice != null && !RegistryUtils.IsDeviceShared(targetDevice))
                        {
                            var checker = new DeviceInfoChecker();
                            RegistryUtils.ShareDevice(targetDevice, checker.GetDeviceName(targetDevice));
                        }

                        return 0;
                    } catch (InvalidOperationException)
                    {
                        Console.Error.WriteLine("There's no device with the specified BUS-ID.");
                        return 1;
                    }
                });
            });

            app.Command("unbind", (cmd) =>
            {
                cmd.Description = "Unbind device";
                var busId = cmd.Option("-b|--busid=<busid>", "Stop sharing device having <busid>", CommandOptionType.SingleValue);
                var guid = cmd.Option("-g|--guid=<guid>", "Stop sharing persisted device having <guid>", CommandOptionType.SingleValue);
                var unbindAll = cmd.Option("-a|--all", "Stop sharing all devices.", CommandOptionType.NoValue);
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    
                    if (unbindAll.HasValue())
                    {                        
                        RegistryUtils.StopSharingAllDevices();
                        return 0;
                    }

                    if (busId.HasValue())
                    {
                        try
                        {
                            var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                            var targetDevice = connectedDevices.Where(x => x.BusId == busId.Value()).First();
                            if (targetDevice != null && RegistryUtils.IsDeviceShared(targetDevice))
                            {
                                RegistryUtils.StopSharingDevice(targetDevice);
                            }

                            return 0;
                        } catch (InvalidOperationException)
                        {
                            Console.Error.WriteLine("There's no device with the specified BUS-ID.");
                            return 1;
                        }   
                    }

                    if (guid.HasValue())
                    {
                        try
                        {
                            RegistryUtils.StopSharingDevice(guid.Value());
                            return 0;
                        } catch (ArgumentException)
                        {
                            Console.Error.WriteLine("There's no device with the specified GUID.");
                            return 1;
                        }
                    }

                    return 0;
                });
            });

            app.Command("server", (cmd) =>
            {
                cmd.Description = "Run the server stand-alone on the console";
                DefaultCmdLine(cmd);
                cmd.Argument("key=value", ".NET configuration override", true);
                cmd.OnExecute(() => ExecuteServer(cmd.Arguments.Single().Values.ToArray()));
            });

            app.OnExecute(() =>
            {
                app.ShowRootCommandFullNameAndVersion();
                app.ShowHint();
                return 0;
            });

            try
            {
                return app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static int ExecuteServer(string[] args)
        {
            Host.CreateDefaultBuilder()
                .UseWindowsService()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var defaultConfig = new Dictionary<string, string>();
                    if (WindowsServiceHelpers.IsWindowsService())
                    {
                        // EventLog defaults to Warning, which is OK for .NET components,
                        //      but we want to specifically log Information from our own component.
                        defaultConfig.Add($"Logging:EventLog:LogLevel:{nameof(UsbIpServer)}", "Information");
                    }
                    else
                    {
                        // When not running as a Windows service, do not spam the EventLog.
                        defaultConfig.Add("Logging:EventLog:LogLevel:Default", "None");
                    }
                    // set the above as defaults
                    builder.AddInMemoryCollection(defaultConfig);
                    // allow overrides from the environment
                    builder.AddEnvironmentVariables();
                    // allow overrides from the command line
                    builder.AddCommandLine(args);
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddEventLog(settings =>
                    {
                        settings.SourceName = Product;
                    });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Server>();
                    services.AddScoped<ClientContext>();
                    services.AddScoped<ConnectedClient>();
                    services.AddScoped<AttachedClient>();
                    services.AddSingleton<RegistryWatcher>();
                })
                .Build()
                .Run();
            return 0;
        }
    }
}
