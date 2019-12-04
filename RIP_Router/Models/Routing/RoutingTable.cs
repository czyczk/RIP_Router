using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using RIP_Router.Models.Routing.Messages;

namespace RIP_Router.Models.Routing
{
    public class RoutingTable
    {
        private readonly SortedList<uint, OwnedRoutingEntry> _routingEntries = new SortedList<uint, OwnedRoutingEntry>();

        public ReadOnlyCollection<OwnedRoutingEntry> ListAll()
        {
            return new ReadOnlyCollection<OwnedRoutingEntry>(_routingEntries.Values.ToList());
        }

        /// <summary>
        /// Check if the routing table contains the specified target.
        /// </summary>
        /// <param name="target">The ID of the destination router</param>
        /// <returns></returns>
        public bool Contains(uint target)
        {
            return _routingEntries.ContainsKey(target);
        }

        /// <summary>
        /// Get a routing entry with the specified target.
        /// Null if it does not exist.
        /// </summary>
        /// <param name="target">The ID of the destination router</param>
        /// <returns>The routing entry with the specified target</returns>
        public OwnedRoutingEntry Get(uint target)
        {
            return !Contains(target) ? null : _routingEntries[target];
        }

        /// <summary>
        /// Add the routing entry to the routing table.
        /// </summary>
        /// <param name="target">The ID of the destination router</param>
        /// <param name="numHops">The number of hops to reach the target</param>
        /// <param name="nextHop">The ID of the next hop</param>
        /// <exception cref="InvalidOperationException">Exception thrown if the entry exists in the table.</exception>
        public void Add(uint target, uint numHops, uint? nextHop)
        {
            if (Contains(target))
                throw new InvalidOperationException("The routing entry already exists in the table.");

            _routingEntries.Add(target, new OwnedRoutingEntry {Target = target, NumHops = numHops, NextHop = nextHop});
        }

        /// <summary>
        /// Remove the routing entry from the routing table.
        /// </summary>
        /// <param name="target">The ID of the destination router</param>
        /// <exception cref="InvalidOperationException">Exception thrown if the entry does not exist in the table.</exception>
        public void Remove(uint target)
        {
            if (!Contains(target))
                throw new InvalidOperationException("The routing entry does not exist in the table.");

            _routingEntries.Remove(target);
        }

        /// <summary>
        /// Update the routing entry in the routing table.
        /// </summary>
        /// <param name="target">The ID of the destination router in the table to be updated</param>
        /// <param name="numHops">The updated number of hops to reach the target</param>
        /// <param name="nextHop">The updated ID of the next hop</param>
        /// <exception cref="InvalidOperationException">Exception thrown if the entry does not exist in the table.</exception>
        public void Update(uint target, uint numHops, uint? nextHop)
        {
            if (!Contains(target))
                throw new InvalidOperationException("The routing entry does not exist in the table.");

            _routingEntries[target] = new OwnedRoutingEntry {Target = target, NumHops = numHops, NextHop = nextHop};
        }

        public void ExpireId(uint routerId)
        {
            foreach (var routingEntry in _routingEntries.Values)
            {
                if (routingEntry.Target == routerId || routingEntry.NextHop != null && routingEntry.NextHop == routerId)
                {
                    routingEntry.NumHops = uint.MaxValue;
                }
            }
        }

        /// <summary>
        /// Routing update entries are sent to other routers for updating their routing tables.
        /// The entry containing itself should be excluded.
        /// </summary>
        /// <param name="ownerId">The router ID by which the routing table is owned</param>
        /// <returns></returns>
        public ReadOnlyCollection<RoutingUpdateEntry> GenerateRoutingUpdateEntries(uint ownerId)
        {
            var result = (
                    from routingEntry
                    in _routingEntries.Values
                    where routingEntry.Target != ownerId
                    select new RoutingUpdateEntry {Target = routingEntry.Target, NumHops = routingEntry.NumHops})
                .ToList().AsReadOnly();

            return result;
        }

        /// <summary>
        /// Routing update entries are sent to other routers for updating their routing tables.
        /// The entry containing itself should be excluded.
        /// </summary>
        /// <param name="ownerId">The router ID by which the routing table is owned</param>
        /// <param name="targetRouterId">The router ID to which the update entries are sent</param>
        /// <returns></returns>
        public ReadOnlyCollection<RoutingUpdateEntry> GenerateRoutingUpdateEntriesWithPoisonReverse(uint ownerId, uint targetRouterId)
        {
            var list = new List<RoutingUpdateEntry>();
            foreach (var routingEntry in _routingEntries.Values)
            {
                if (routingEntry.Target == ownerId)
                    continue;
                if (routingEntry.Target == targetRouterId ||
                    routingEntry.NextHop != null && routingEntry.NextHop == targetRouterId)
                    list.Add(new RoutingUpdateEntry { Target = routingEntry.Target, NumHops = uint.MaxValue });
                else
                    list.Add(new RoutingUpdateEntry { Target = routingEntry.Target, NumHops = routingEntry.NumHops });
            }

            var result = list.AsReadOnly();

            return result;
        }
    }
}
