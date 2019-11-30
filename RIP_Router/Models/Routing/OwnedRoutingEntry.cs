using System;
using System.Collections.Generic;
using System.Text;

namespace RIP_Router.Models.Routing
{
    /// <summary>
    /// A routing entry owned by a router.
    /// </summary>
    public class OwnedRoutingEntry : AbstractRoutingEntry
    {
        /// <summary>
        /// The router ID of the next hop to reach the target.
        /// A null value indicates that the target can be directly connected.
        /// </summary>
        public uint? NextHop { get; set; }

        public override string ToString()
        {
            return $"{Target}\t{(NumHops == uint.MaxValue ? "∞" : NumHops.ToString())}\t{(NextHop == null ? "-" : NextHop.ToString())}";
        }
    }
}
