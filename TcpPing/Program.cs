using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace TcpPing
{
    public class Program
    {
        private const int _port = 5201;

        private static Stopwatch _stopwatch = Stopwatch.StartNew();
        private static long _requests;
        private static long _connections;

        static void Main(string[] args)
        {
            if (args.Length == 1 && args[0].Equals("-s", StringComparison.OrdinalIgnoreCase))
            {
                Init();
                RunServer();
            }
            else if ((args.Length == 2 || args.Length == 3) && args[0].StartsWith("-c", StringComparison.OrdinalIgnoreCase))
            {
                Init();
                var threads = (args.Length == 3) ? Int32.Parse(args[2]) : 1;
                RunClient(args[1], threads);
            }
            else
            {
                Console.WriteLine("TcpPing [-s] [-c server parallel]");
                return;
            }
        }

        private static void Init()
        {
#if DEBUG
            Console.WriteLine($"Configuration: Debug");
#else
            Console.WriteLine($"Configuration: Release");
#endif

            var gc = GCSettings.IsServerGC ? "server" : "client";
            Console.WriteLine($"GC: {gc}");

            Console.WriteLine();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            WriteResults();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private static void RunClient(string server, int threads)
        {
            var threadObjects = new Thread[threads];

            for (var i = 0; i < threads; i++)
            {
                var thread = new Thread(() =>
                {
                    var buf = new byte[1] { 1 };
                    while (true)
                    {
                        var tcs = new TaskCompletionSource<Socket>();

                        var socketArgs = new SocketAsyncEventArgs();
                        socketArgs.RemoteEndPoint = new DnsEndPoint(server, _port);
                        socketArgs.Completed += (s, e) => tcs.TrySetResult(e.ConnectSocket);

                        // Must use static ConnectAsync(), since instance Connect() does not support DNS names on OSX/Linux.
                        if (Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, socketArgs))
                        {
                            tcs.Task.Wait();
                        }

                        try
                        {
                            Interlocked.Increment(ref _connections);

                            using (var socket = socketArgs.ConnectSocket)
                            {
                                while (socket.Send(buf) == 1)
                                {
                                    socket.Receive(buf);
                                    Interlocked.Increment(ref _requests);
                                }
                            }
                        }
                        catch
                        {
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _connections);
                        }
                    }
                });
                threadObjects[i] = thread;
                thread.Start();
            }

            for (var i = 0; i < threads; i++)
            {
                threadObjects[i].Join();
            }
        }

        private static void RunServer()
        {
            var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
            server.DualMode = true;
            server.Bind(new IPEndPoint(IPAddress.IPv6Any, _port));
            server.Listen(int.MaxValue);

            Console.WriteLine($"Listening on {_port}");

            while (true)
            {
                var socket = server.Accept();

                var thread = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref _connections);
                        var buf = new byte[1];
                        using (socket)
                        {
                            while (socket.Receive(buf) == 1)
                            {
                                socket.Send(buf);
                                Interlocked.Increment(ref _requests);
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _connections);
                    }
                });
                thread.Start();
            }
        }

        private static async Task WriteResults()
        {
            var lastRequests = (long)0;
            var lastElapsed = TimeSpan.Zero;

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var currentRequests = _requests - lastRequests;
                lastRequests = _requests;

                var elapsed = _stopwatch.Elapsed;
                var currentElapsed = elapsed - lastElapsed;
                lastElapsed = elapsed;

                WriteResult(_requests, currentRequests, currentElapsed);
            }
        }

        private static void WriteResult(long totalRequests, long currentRequests, TimeSpan elapsed)
        {
            Console.WriteLine($"{DateTime.UtcNow.ToString("o")}\tConnections\t{_connections}\tRequests\t{totalRequests}\tRPS\t{Math.Round(currentRequests / elapsed.TotalSeconds)}");
        }
    }
}
