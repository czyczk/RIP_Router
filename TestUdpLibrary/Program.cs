using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace TestUdpLibrary
{
    class Program
    {
        static void Main(string[] args)
        {
            var task1 = new Task(() =>
            {
                RunMainTask(args);
            });
            task1.Start();

            var task2 = new Task(() => { Console.ReadLine(); });
            task2.Start();
            task2.Wait();
        }

        private static void RunMainTask(string[] args)
        {
            var port = int.Parse(args[0]);
            var isClient = bool.Parse(args[1]);
            int targetPort = 0;
            if (isClient)
            {
                targetPort = int.Parse(args[2]);
            }

            var listener = new EventBasedNetListener();

            listener.ConnectionRequestEvent += request =>
            {
                Console.WriteLine($"有人要连我: {request.RemoteEndPoint.Port}");
                request.Accept();
            };

            listener.PeerConnectedEvent += peer =>
            {
                Console.WriteLine($"连上了：{peer.EndPoint.Port}");
                if (!isClient)
                {
                    Console.WriteLine($"我是服务器，我先来：1");
                    var writer = new NetDataWriter();
                    writer.Put("111111");
                    peer.Send(writer, DeliveryMethod.Unreliable);
                }
            };

            listener.PeerDisconnectedEvent += (peer, info) => { Console.WriteLine($"没连成。 {info.Reason}"); };

            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                var dataSize = reader.RawDataSize;
                var msg = reader.GetString();
                Console.WriteLine($"收到了 {msg}");
                var digit = int.Parse(msg);
                Console.WriteLine($"往下：{++digit}");
                var writer = new NetDataWriter();
                writer.Put(digit.ToString());
                Thread.Sleep(100);
                peer.Send(writer, DeliveryMethod.Unreliable);
            };

            var netManager = new NetManager(listener);
            netManager.Start(port);
            Console.WriteLine("端口开好了。");

            if (isClient)
            {
                var peer = netManager.Connect("localhost", targetPort, "");
                Console.WriteLine(peer.ConnectionState);
                Console.WriteLine($"连上了 {netManager.ConnectedPeerList.Count} 个");
            }

            while (!Console.KeyAvailable)
            {
                netManager.PollEvents();
                Thread.Sleep(15);
            }
        }
    }
}
