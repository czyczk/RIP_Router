using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RIP_Router.Models.Options;
using RIP_Router.Models.Routing;
using RIP_Router.Utils;
using Serilog;

namespace RIP_Router
{
    class Startup
    {
        private Router _router;

        public void Start(string[] args)
        {
            Options options;
            try
            {
                options = OptionParser.ParseOptions(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return;
            }

            ConfigureLogger();

            _router = new Router(options);
            var isSuccess = _router.StartServer();
            if (!isSuccess)
                return;

            _router.ConnectAllPorts();

            StartAcceptingInput();
        }

        private void StartAcceptingInput()
        {
            while (true)
            {
                var command = Console.ReadLine();
                if (command.Trim().Equals("N", StringComparison.OrdinalIgnoreCase))
                {
                    CommandExecutor.DisplayAdjacentRouters(_router);
                }
                else if (command.Trim().Equals("T", StringComparison.OrdinalIgnoreCase))
                {
                    CommandExecutor.DisplayRoutingTable(_router);
                }
                else if (command.StartsWith("D ", StringComparison.OrdinalIgnoreCase))
                {
                    var strs = command.Split(' ');
                    var messageContents = "";

                    if (strs.Length < 2)
                    {
                        Console.WriteLine("Target router ID not specified.");
                        continue;
                    }
                    var isSuccess = uint.TryParse(strs[1], out var targetRouterId);
                    if (!isSuccess)
                    {
                        Console.WriteLine("Invalid target router ID.");
                        continue;
                    }

                    if (strs.Length > 2)
                    {
                        var informativeParts = new List<string>();
                        for (var i = 2; i < strs.Length; i++)
                            informativeParts.Add(strs[i]);
                        messageContents = string.Join(' ', informativeParts);
                    }

                    try
                    {
                        CommandExecutor.SendMessage(_router, targetRouterId, messageContents);
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine(ex.Message);
                        continue;
                    }
                }
                else
                {
                    Console.WriteLine("Unknown command.");
                }
            }
        }

        private static void ConfigureLogger()
        {
            var loggerConfig = new LoggerConfiguration();
#if DEBUG
            loggerConfig = loggerConfig.MinimumLevel.Debug();
#endif
            Log.Logger = loggerConfig.
                WriteTo.Console().
                CreateLogger();

        }
    }
}
