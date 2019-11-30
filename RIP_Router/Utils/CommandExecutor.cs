using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RIP_Router.Models.Routing;

namespace RIP_Router.Utils
{
    public class CommandExecutor
    {
        public static void DisplayAdjacentRouters(Router router)
        {
            var routingEntries = router.ListAllRoutingEntries();
            var adjacentRoutingEntries = routingEntries
                .Where(routingEntry => routingEntry.NextHop == null && routingEntry.NumHops == 1).ToList();

            if (adjacentRoutingEntries.Any())
            {
                Console.WriteLine($"Connected to {adjacentRoutingEntries.Count} adjacent router(s):");
                foreach (var adjacentRoutingEntry in adjacentRoutingEntries)
                {
                    Console.WriteLine($"Router {adjacentRoutingEntry.Target}");
                }
            }
            else
            {
                Console.WriteLine("No connected adjacent routers.");
            }
        }

        public static void DisplayRoutingTable(Router router)
        {
            var routingEntries = router.ListAllRoutingEntries();
            Console.WriteLine($"{routingEntries.Count} entries in total:");
            foreach (var routingEntry in routingEntries)
            {
                Console.WriteLine($"\t{routingEntry}");
            }
        }

        public static void SendMessage(Router router, uint targetRouterId, string message)
        {
            router.SendMessage(targetRouterId, message);
        }
    }
}
