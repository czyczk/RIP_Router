using System;
using System.Collections.Generic;
using System.Text;

namespace RIP_Router.Models.Routing
{
    public abstract class AbstractRoutingEntry
    {
        /// <summary>
        /// The destination router ID of this routing entry
        /// </summary>
        public uint Target { get; set; }

        /// <summary>
        /// The number of hops to reach the target
        /// </summary>
        public uint NumHops { get; set; }
    }
}
