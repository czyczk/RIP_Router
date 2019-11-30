using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RIP_Router.Models.Options;

namespace RIP_Router.Utils
{
    /// <summary>
    /// A util for parsing the options.
    /// </summary>
    public class OptionParser
    {
        /// <summary>
        /// Parse the options from the arguments and the config file.
        /// The arguments should be separated by blank spaces.
        /// </summary>
        /// <param name="args">Arguments from main()</param>
        public static Options ParseOptions(string[] args)
        {
            var result = new Options();
            ParseArguments(args, result);
            ParseConfigFile(result);

            return result;
        }

        /// <summary>
        /// Parse the arguments and update the options.
        /// The arguments should be separated by blank spaces.
        /// </summary>
        /// <param name="args">Arguments from main()</param>
        /// <param name="options"></param>
        /// <exception cref="ArgumentException">Exception thrown there's argument containing invalid information.</exception>
        /// <exception cref="InvalidCastException">Exception thrown if there's argument in incorrect type.</exception>
        private static void ParseArguments(string[] args, Options options)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            // Error if the arguments are not sufficient
            if (args.Length < 2)
            {
                throw new ArgumentException("Not sufficient arguments.\n" +
                                             "Usage:\n" +
                                             "\trouter ${routerId} ${listeningPort} [${adjacentRouterPorts}[]]\n" +
                                             "Example:\n" +
                                             "\trouter 1 3001" +
                                             "\trouter 2 3002 3001" +
                                             "\trouter 3 3003 3001 3002");
            }

            // Parse router ID
            var isSuccess = uint.TryParse(args[0], out var routerId);
            if (isSuccess)
                options.RouterId = routerId;
            else
                throw new InvalidCastException("Invalid router ID. It should be a number >= 0.");

            // Parse listening port
            isSuccess = uint.TryParse(args[1], out var listeningPort);
            if (isSuccess)
                options.ListeningPort = listeningPort;
            else
                throw new InvalidCastException("Invalid listening port. It should be a number >= 0.");

            // Parse adjacent router ports
            if (args.Length == 2)
            {
                options.AdjacentRouterPorts = new uint[] { };
            }
            else
            {
                var adjacentRouterPorts = new LinkedList<uint>();
                const string messageInvalidAdjacentRouterPorts = "At least 1 invalid adjacent router port. They should be numbers >= 0 and should not contain the listening port.";
                for (var i = 2; i < args.Length; i++)
                {
                    isSuccess = uint.TryParse(args[i], out var adjacentRouterPort);
                    if (isSuccess)
                    {
                        if (adjacentRouterPort == listeningPort)
                            throw new ArgumentException(messageInvalidAdjacentRouterPorts);

                        adjacentRouterPorts.AddLast(adjacentRouterPort);
                    }
                    else
                        throw new InvalidCastException();
                }

                options.AdjacentRouterPorts = adjacentRouterPorts.ToArray();
            }
        }

        // Parse the config file "config.json" and update the options.
        private static void ParseConfigFile(Options options)
        {
            using var file = File.OpenText(@"config.json");
            using var reader = new JsonTextReader(file);

            var serializer = new JsonSerializer();
            var config = (JObject) serializer.Deserialize(reader);

            // Parse threshold for the unreachable number of hops
            string thresholdUnreachableStr;
            try
            {
                thresholdUnreachableStr = config["ThresholdUnreachable"].Value<string>();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Invalid config file. ThresholdUnreachable not found or invalid.");
            }
            var messageInvalidThresholdUnreachable =
                "Invalid threshold for the unreachable number of hops. It should be > 0.";
            var isSuccess = uint.TryParse(thresholdUnreachableStr, out var thresholdUnreachable);
            if (isSuccess)
            {
                if (thresholdUnreachable > 0)
                    options.ThresholdUnreachable = thresholdUnreachable;
                else
                    throw new ArgumentException(messageInvalidThresholdUnreachable);
            }
            else
                throw new ArgumentException(messageInvalidThresholdUnreachable);

            // Parse update timer
            string updateTimerStr;
            try
            {
                updateTimerStr = config["UpdateTimer"].Value<string>();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Invalid config file. UpdateTimer not found or invalid.");
            }
            var messageInvalidUpdateTimer =
                "Invalid update timer. It should be > 0.";
            isSuccess = uint.TryParse(updateTimerStr, out var updateTimer);
            if (isSuccess)
            {
                if (updateTimer > 0)
                    options.UpdateTimer = updateTimer;
                else
                    throw new ArgumentException(messageInvalidUpdateTimer);
            }
            else
                throw new ArgumentException(messageInvalidUpdateTimer);

            // Parse expiration timer
            string expirationTimerStr;
            try
            {
                expirationTimerStr = config["ExpirationTimer"].Value<string>();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Invalid config file. ExpirationTimer not found or invalid.");
            }
            var messageInvalidExpirationTimerStr =
                "Invalid value for expiration timer. It should be > 0 and > update timer.";
            isSuccess = uint.TryParse(expirationTimerStr, out var expirationTimer);
            if (isSuccess)
            {
                if (expirationTimer > 0 && expirationTimer > updateTimer)
                    options.ExpirationTimer = expirationTimer;
                else
                    throw new ArgumentException(messageInvalidExpirationTimerStr);
            }
            else
                throw new ArgumentException(messageInvalidExpirationTimerStr);
        }
    }
}
