using System;
using System.Collections.Generic;
using System.Text;

namespace RIP_Router.Models.Options
{
    public class Options
    {
        /// <summary>
        /// Router ID. It should be read from the arguments.
        /// </summary>
        public uint RouterId { get; set; }

        /// <summary>
        /// The router listens at the listening port for incoming route updates. It should be read from the arguments.
        /// </summary>
        public uint ListeningPort { get; set; }

        /// <summary>
        /// Adjacent router ports indicate the routers that this router can directly communicate with. They should be read from the arguments.
        /// </summary>
        public uint[] AdjacentRouterPorts { get; set; }

        /// <summary>
        /// Any number of hop equals to or larger than this threshold is considered unreachable for this router. It should be read from the config file.
        /// </summary>
        public uint ThresholdUnreachable { get; set; }

        /// <summary>
        /// The interval after which the router should send its routing table to its adjacent routers. It should be read from the config file.
        /// </summary>
        public uint UpdateTimer { get; set; }

        /// <summary>
        /// The interval after which the routing entry is considered unreachable if no updates are received from it.
        /// </summary>
        public uint ExpirationTimer { get; set; }
    }
}
