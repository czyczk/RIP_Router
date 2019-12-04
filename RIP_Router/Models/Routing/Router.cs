using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RIP_Router.Models.Routing.Messages;
using Serilog;

namespace RIP_Router.Models.Routing
{
    public class Router
    {
        public Options.Options Options { get; }

        private readonly RoutingTable _routingTable;

        // <adjacentPort, routerId>
        private readonly SortedList<uint, uint> _connectedRouterIds;

        // <adjacentPort, client>
        private readonly SortedList<uint, NetPeer> _connectedRouters;

        private readonly SortedList<uint, Stopwatch> _expirationTimers;
        private Task _expirationTimerChecker;

        private readonly SortedList<uint, Stopwatch> _updateTimers;
        private Task _updateTimerChecker;

        // Used to prevent immediate overwrites of outdated routing updates
        private Stopwatch _updateLockTimer;
        private Task _updateLockTimerChecker;

        private readonly EventBasedNetListener _netListener;
        private readonly NetManager _netManager;

        public Router(Options.Options options)
        {
            Options = options;
            _routingTable = new RoutingTable();
            _connectedRouterIds = new SortedList<uint, uint>();
            _netListener = new EventBasedNetListener();
            _netManager = new NetManager(_netListener);
            _connectedRouters = new SortedList<uint, NetPeer>();
            _expirationTimers = new SortedList<uint, Stopwatch>();
            _updateTimers = new SortedList<uint, Stopwatch>();
            _updateLockTimer = new Stopwatch();

            // The default routing table should have a route to itself
            _routingTable.Add(Options.RouterId, 0, null);

            // Define the default behaviors for the server and the client listeners
            _netListener.ConnectionRequestEvent += NetListenerOnConnectionRequestEvent;
            _netListener.PeerConnectedEvent += NetListenerOnPeerConnectedEvent;
            _netListener.NetworkReceiveEvent += NetListenerOnNetworkReceiveEvent;
        }

        // Accept the connection request from another peer. No extra things to do for now.
        private void NetListenerOnConnectionRequestEvent(ConnectionRequest request)
        {
            Log.Debug($"A router from port {request.RemoteEndPoint.Port} is trying to establish a connection.");
            request.Accept();
        }

        // After another peer has successfully connected to it, it informs its router ID.
        // It can not mark the other peer directly accessible for now since the router ID of the other peer is unknown.
        private void NetListenerOnPeerConnectedEvent(NetPeer peer)
        {
            var port = (uint) peer.EndPoint.Port;
            if (!_connectedRouters.ContainsKey(port))
            {
                // This is needed because `ConnectPort()` is not invoked for passive connections
                _connectedRouters.Add((uint)peer.EndPoint.Port, peer);
            }
            else
            {
                // Replace the old one in case the peer is back from recovery
                _connectedRouters[port] = peer;
            }

            Log.Debug(
                $"A connection from router at port {peer.EndPoint.Port} has been established. About to send the router ID.");

            var writer = new NetDataWriter();
            writer.Put(JsonConvert.SerializeObject(new RouterIdentity(Options.RouterId)));
            peer.Send(writer, DeliveryMethod.Unreliable);
        }

        // Behaviors when the server receives a Message
        private void NetListenerOnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            var port = (uint) peer.EndPoint.Port;
            var str = reader.GetString();
            reader.Recycle();
            var jObj = JsonConvert.DeserializeObject<JContainer>(str);

            switch (jObj)
            {
                case JObject _:
                    // The message is an object
                    if (jObj["RouterId"] != null)
                    {
                        // It's of type RouterIdentity
                        ProcessRouterIdentityMessage(port, str);
                    }
                    else
                    {
                        // It's of type TextMessage
                        // First check if the message is for it
                        var textMessage = JsonConvert.DeserializeObject<TextMessage>(str);
                        if (textMessage.DestinationRouterId != Options.RouterId)
                        {
                            // Forward it
                            SendMessage(textMessage.DestinationRouterId, str, true);
                            Log.Debug($"Message received from router {_connectedRouterIds[(uint) peer.EndPoint.Port]}. Forwarded.");
                        }
                        else
                        {
                            // Show it
                            Log.Information($"Message received from router {_connectedRouterIds[(uint) peer.EndPoint.Port]}. Source: router {textMessage.SourceRouterId}.\n{textMessage.Message}");
                        }
                    }
                    break;
                case JArray _:
                    // The Message is of type List<RoutingUpdateEntry>
                    ProcessRoutingUpdateEntryMessage(port, str);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void ProcessRouterIdentityMessage(uint port, string messageStr)
        {
            /*
             * 1. Bind the port with the router ID.
             * 2. Bind a expiration timer with it and start the timer.
             * 3. Bind an update timer with it and start the timer.
             * 4. Mark that the router can be directly connected.
             */
            var routerIdentity = JsonConvert.DeserializeObject<RouterIdentity>(messageStr);
            if (_connectedRouterIds.ContainsKey(port))
            {
                _connectedRouterIds[port] = routerIdentity.RouterId;
                Log.Information($"The connection to router {routerIdentity.RouterId} has been recovered.");
                _expirationTimers[port].Restart();
                _updateTimers[port].Restart();
            }
            else
            {
                _connectedRouterIds.Add(port, routerIdentity.RouterId);
                Log.Information($"The connection to router {routerIdentity.RouterId} has been established.");

                var expirationTimer = new Stopwatch();
                _expirationTimers.Add(port, expirationTimer);
                expirationTimer.Start();

                var updateTimer = new Stopwatch();
                _updateTimers.Add(port, updateTimer);
                updateTimer.Start();
            }

            // Mark that the router can be directly connected
            SelectivelyUpdate(routerIdentity.RouterId, 1, null);

            Log.Information($"Router identity: Port {port} -> Router ID {routerIdentity.RouterId}");
        }

        private void ProcessRoutingUpdateEntryMessage(uint port, string messageStr)
        {
            /*
             * 1. Restart the expiration timer associated with it.
             * 2. Selectively update the routing entries.
             */
            var routerId = _connectedRouterIds[port];

            // Return if locked
            if (_updateLockTimer.IsRunning)
                Log.Information($"Routing updates received from router {routerId} but the routing table is locked.");

            var routingUpdateEntries = JsonConvert.DeserializeObject<List<RoutingUpdateEntry>>(messageStr);
            _expirationTimers[port].Restart();

            #region DEBUG
            Log.Debug($"Routing updates received from router {routerId}. The update entries are:");
            foreach (var routingUpdateEntry in routingUpdateEntries)
            {
                Log.Debug($"{routingUpdateEntry.Target}\t{routingUpdateEntry.NumHops}");
            }
            #endregion

            // Mark that the router can be directly connected
            SelectivelyUpdate(routerId, 1, null);

            // Selectively update the routing entries
            foreach (var routingUpdateEntry in routingUpdateEntries)
            {
                // !!! Warning: uint upper bound
                if (routingUpdateEntry.NumHops != uint.MaxValue)
                    SelectivelyUpdate(routingUpdateEntry.Target, routingUpdateEntry.NumHops + 1, routerId);
                else
                    SelectivelyUpdate(routingUpdateEntry.Target, routingUpdateEntry.NumHops, routerId);
            }

            Log.Information($"Routing updates received from router {routerId} and have been applied.");
        }

        private void ExpirePort(uint port)
        {
            var routerId = _connectedRouterIds[port];
            // Stop the corresponding update timer
            _updateTimers[port].Stop();

            // Mark the related routing entries as unreachable
            _routingTable.ExpireId(routerId);

            // Trigger immediate routing updates
            Log.Debug($"Immediate routing updating triggered.");
            foreach (var (routerPort, updateTimer) in _updateTimers)
            {
                if (!updateTimer.IsRunning ||
                    updateTimer.ElapsedMilliseconds / 1000 < Options.UpdateTimer) continue;

                var writer = new NetDataWriter();
                var updatingEntries = Options.EnablePoisonReverse
                    ? GenerateRoutingUpdateEntriesWithPoisonReverse(routerId)
                    : GenerateRoutingUpdateEntries();
                writer.Put(JsonConvert.SerializeObject(updatingEntries));
                _connectedRouters[routerPort]
                    .Send(writer, DeliveryMethod.Unreliable);

                updateTimer.Restart();
            }

            // Lock the routing table for a while
            _updateLockTimer.Restart();
        }

        /// <summary>
        /// Start a server at the listening port specified in the options.
        /// </summary>
        public bool StartServer()
        {
            if (_netManager.IsRunning)
            {
                Log.Information("The server is already running. Trying to restart the server.");
                _netManager.Stop();
            }

            var isSuccess = _netManager.Start((int) Options.ListeningPort);
            if (!isSuccess)
            {
                Log.Error($"Cannot start the server at port {Options.ListeningPort}. Check if the port is occupied.");
                return false;
            }

            Log.Information($"Successfully started server at port {Options.ListeningPort}.");

            if (_updateTimerChecker == null)
            {
                PrepareUpdateTimerChecker();
                _updateTimerChecker.Start();

                Log.Information($"The server will send its routing table to its connected adjacent peers every {Options.UpdateTimer} seconds.");
            }

            if (_expirationTimerChecker == null)
            {
                PrepareExpirationTimerChecker();
                _expirationTimerChecker.Start();
            }

            if (_updateLockTimerChecker == null)
            {
                _updateLockTimerChecker = new Task(() =>
                {
                    while (true)
                    {
                        if (_updateLockTimer.IsRunning && _updateLockTimer.ElapsedMilliseconds / 1000 > Options.UpdateTimer)
                        {
                            _updateLockTimer.Stop();
                            _updateLockTimer.Reset();
                        }
                        Thread.Sleep(15);
                    }
                });

                _updateLockTimerChecker.Start();
            }

            // Let the server checks for new events every 15 ms without blocking the existing threads
            new Task(() =>
            {
                while (!Console.KeyAvailable)
                {
                    _netManager.PollEvents();
                    Thread.Sleep(15);
                }
            }).Start();

            return true;
        }

        private void PrepareUpdateTimerChecker()
        {
            // Create an update timer checker to constantly look for routers to send routing updates
            _updateTimerChecker = new Task(() =>
            {
                while (true)
                {
                    try
                    {
                        foreach (var (port, updateTimer) in _updateTimers)
                        {
                            if (!updateTimer.IsRunning ||
                                updateTimer.ElapsedMilliseconds / 1000 < Options.UpdateTimer) continue;

                            var routerId = _connectedRouterIds[port];

                            Log.Debug($"Sending routing updates to router {routerId}.");
                            var writer = new NetDataWriter();
                            writer.Put(JsonConvert.SerializeObject(GenerateRoutingUpdateEntries()));
                            _connectedRouters[port]
                                .Send(writer, DeliveryMethod.Unreliable);

                            updateTimer.Restart();
                        }
                        Thread.Sleep(15);
                    }
                    catch (InvalidOperationException)
                    {
                        /*
                         * In case of error 'Collection was modified after the enumerator was instantiated'.
                         * It's normal since the list `_updateTimers` is shared and written by another thread.
                         * Just restart the iteration.
                         */
                    }
                }
            });
        }

        private void PrepareExpirationTimerChecker()
        {
            // A constantly running checker to expire a port if nothing's heard from it after a certain period of time
            _expirationTimerChecker = new Task(() =>
            {
                
                while (true)
                {
                    try
                    {
                        foreach (var (port, expirationTimer) in _expirationTimers)
                        {
                            if (!expirationTimer.IsRunning ||
                                expirationTimer.ElapsedMilliseconds / 1000 < Options.ExpirationTimer) continue;
                            expirationTimer.Stop();
                            ExpirePort(port);
                            Log.Information(
                                $"Nothing heard from router {_connectedRouterIds[port]} for {Options.ExpirationTimer} seconds. The router has been expired.");
                        }
                        Thread.Sleep(15);
                    }
                    catch (InvalidOperationException)
                    {
                        /*
                         * In case of error 'Collection was modified after the enumerator was instantiated'.
                         * It's normal since the list `_updateTimers` is shared and written by another thread.
                         * Just restart the iteration.
                         */
                    }
                }
            });
        }

        private ReadOnlyCollection<RoutingUpdateEntry> GenerateRoutingUpdateEntries()
        {
            return _routingTable.GenerateRoutingUpdateEntries(Options.RouterId);
        }

        private ReadOnlyCollection<RoutingUpdateEntry> GenerateRoutingUpdateEntriesWithPoisonReverse(uint targetRouterId)
        {
            return _routingTable.GenerateRoutingUpdateEntriesWithPoisonReverse(Options.RouterId, targetRouterId);
        }

        /// <summary>
        /// Use a client to connect to a specified port.
        /// </summary>
        /// <param name="port">The target port</param>
        public void ConnectPort(uint port)
        {
            NetPeer connectedRouter;
            // Fetch the connected router from the connected list or create a new one
            if (_connectedRouters.ContainsKey(port))
            {
                connectedRouter = _connectedRouters[port];

                // Do nothing if the connected server is in a good state
                if (connectedRouter.ConnectionState != ConnectionState.Disconnected)
                    return;
                // Or connect to the port and replace the old NetPeer instance with the new one
                connectedRouter = _netManager.Connect(new IPEndPoint(IPAddress.Loopback, (int)port), "");
                _connectedRouters[port] = connectedRouter;
            }
            else
            {
                // Add the target router to the list of connected routers
                connectedRouter = _netManager.Connect(new IPEndPoint(IPAddress.Loopback, (int) port), "");
                _connectedRouters.Add(port, connectedRouter);
            }
        }

        /// <summary>
        /// Connect to all the ports that are specified in the options.
        /// </summary>
        public void ConnectAllPorts()
        {
            Log.Debug("About to connect all the ports specified in the options.");
            foreach (var port in Options.AdjacentRouterPorts)
            {
                ConnectPort(port);
            }
        }

        /// <summary>
        /// List all the routing entries.
        /// </summary>
        /// <returns>All the routing entries in the routing table.</returns>
        public ReadOnlyCollection<OwnedRoutingEntry> ListAllRoutingEntries()
        {
            return _routingTable.ListAll();
        }

        /// <summary>
        /// Selectively update a routing entry
        /// </summary>
        /// <param name="target">The ID of the destination router</param>
        /// <param name="numHops">The number of hops to reach the target</param>
        /// <param name="nextHop">The router ID of the next hop</param>
        public void SelectivelyUpdate(uint target, uint numHops, uint? nextHop)
        {
            // Ignore if the target points to itself
            if (target == Options.RouterId)
                return;

            // If the number of hops >= thresholdUnreachable, mark it unreachable
            if (numHops >= Options.ThresholdUnreachable)
            {
                numHops = uint.MaxValue;
            }

            if (!_routingTable.Contains(target))
            {
                // If the routing entry is not contained in the routing table, add a new one
                _routingTable.Add(target, numHops, nextHop);
                Log.Debug($"+:\t{target}\t{numHops}\t{(nextHop == null ? "-" : nextHop.ToString())}");
            }
            else
            {
                // Or, if an entry with the same target exists, update if numHops is smaller
                var routingEntry = _routingTable.Get(target);
                if (numHops < routingEntry.NumHops)
                {
                    #region DEBUG
                    Log.Debug($"U:\t{target}\t{(routingEntry.NumHops == uint.MaxValue ? "∞" : routingEntry.NumHops.ToString())}→{(numHops == uint.MaxValue ? "∞" : numHops.ToString())}\t{(routingEntry.NextHop == null ? "-" : routingEntry.NextHop.ToString())}→{(nextHop == null ? "-" : nextHop.ToString())}");
                    #endregion
                    _routingTable.Update(target, numHops, nextHop);
                }
                else if (numHops > routingEntry.NumHops && nextHop == routingEntry.NextHop)
                {
                    // Or if the numHops is larger and apply to the existing entry, update it
                    #region DEBUG
                    Log.Debug($"U:\t{target}\t{(routingEntry.NumHops == uint.MaxValue ? "∞" : routingEntry.NumHops.ToString())}→{(numHops == uint.MaxValue ? "∞" : numHops.ToString())}\t{(routingEntry.NextHop == null ? "-" : routingEntry.NextHop.ToString())}→{(nextHop == null ? "-" : nextHop.ToString())}");
                    #endregion
                    _routingTable.Update(target, numHops, nextHop);
                }
            }
        }

        public void SendMessage(uint targetRouterId, string message, bool isSerialized = false)
        {
            if (targetRouterId == Options.RouterId)
            {
                throw new ArgumentException("The target router ID points to the source.");
            }

            const string messageUnreachable = "Target router is unreachable.";

            if (!_routingTable.Contains(targetRouterId))
            {
                throw new ArgumentException(messageUnreachable);
            }

            var routingEntry = _routingTable.Get(targetRouterId);
            if (routingEntry.NumHops == uint.MaxValue)
                throw new ArgumentException(messageUnreachable);

            // The next hop is either indicated by the routing entry or can be directly connected
            var nextHop = routingEntry.NextHop ?? targetRouterId;

            var writer = new NetDataWriter();

            if (!isSerialized)
            {
                var textMessageInstance = new TextMessage(Options.RouterId, targetRouterId, message);
                writer.Put(JsonConvert.SerializeObject(textMessageInstance));
            }
            else
            {
                writer.Put(message);
            }

            var port = _connectedRouterIds.First(portIdMapping => portIdMapping.Value == nextHop).Key;
            _connectedRouters[port].Send(writer, DeliveryMethod.Unreliable);
        }
    }
}
