using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Infiniminer
{
    // A server run inside the game (HOST GAME), so a game can be started without a separate server.
    // It runs on a background thread; other players on the LAN find it like any other server.
    public sealed class HostedServer
    {
        public readonly ServerConfig Config;
        readonly string dataDirectory;
        readonly Thread thread;
        readonly ConcurrentQueue<string> log = new ConcurrentQueue<string>();
        volatile bool stopRequested;
        volatile InfiniminerServer server;

        public volatile bool Ready;         // listening; Port is set
        public volatile string Error;       // set if the server could not start
        public int Port { get; private set; }

        // The router port forward, when the game is open to the internet.
        public InternetPortForward PortForward
        {
            get { return server?.PortForward; }
        }

        public bool Running
        {
            get { return thread.IsAlive; }
        }

        HostedServer(ServerConfig config, string dataDirectory)
        {
            Config = config;
            this.dataDirectory = dataDirectory;
            thread = new Thread(Run) { IsBackground = true, Name = "Hosted server" };
        }

        // Starts generating the world and listening in the background; watch Ready and Error.
        public static HostedServer Start(ServerConfig config, string dataDirectory)
        {
            // LAN players only look for servers on the standard port; if something else (another server)
            // has it, use any free port, which this machine can still join directly.
            if (!PortIsFree(config.Port))
                config.Port = 0;
            HostedServer hosted = new HostedServer(config, dataDirectory);
            hosted.thread.Start();
            return hosted;
        }

        static bool PortIsFree(int port)
        {
            try
            {
                using UdpClient probe = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public IPEndPoint EndPoint
        {
            get { return new IPEndPoint(IPAddress.Loopback, Port); }
        }

        void Run()
        {
            try
            {
                // After a match ends the server restarts with a new map, as a dedicated server does.
                while (!stopRequested)
                {
                    InfiniminerServer instance = new InfiniminerServer(Config, dataDirectory);
                    instance.Log = line =>
                    {
                        log.Enqueue(line);
                        while (log.Count > 200)
                            log.TryDequeue(out _);
                    };
                    server = instance;
                    if (!instance.Initialize())
                    {
                        Error = "COULD NOT START THE SERVER ON PORT " + Config.Port;
                        return;
                    }
                    Port = instance.Port;
                    Config.Port = Port;     // a restart keeps the same port
                    Ready = true;

                    while (!stopRequested && instance.Tick())
                        Thread.Sleep(1);
                    instance.Shutdown("The host ended the game.");
                }
            }
            catch (Exception e)
            {
                Error = "THE SERVER CRASHED: " + TextUtil.Sanitize(e.Message).ToUpperInvariant();
                Console.Error.WriteLine(e);
                server?.Shutdown("The server crashed.");
            }
        }

        // Ends the game for everyone and waits (briefly) for the server to close.
        public void Stop()
        {
            stopRequested = true;
            thread.Join(TimeSpan.FromSeconds(5));
        }

        public IEnumerable<string> RecentLog
        {
            get { return log.ToArray(); }
        }
    }
}
