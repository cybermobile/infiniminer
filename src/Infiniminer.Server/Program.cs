using System.Runtime.InteropServices;

namespace Infiniminer.Server
{
    class Program
    {
        static volatile InfiniminerServer currentServer;
        static int quitRequests = 0;

        // The first Ctrl+C / SIGTERM shuts down cleanly; a second one is left to terminate the process.
        static bool RequestQuit()
        {
            if (Interlocked.Increment(ref quitRequests) > 1)
                return false;
            currentServer?.EnqueueCommand("quit");
            return true;
        }

        // Console commands are read on a background thread and handed to the game loop, which works the same
        // in an interactive terminal, under systemd/Docker (no stdin), and with redirected input.
        static void StartConsoleReader()
        {
            Thread reader = new Thread(() =>
            {
                while (true)
                {
                    string line;
                    try
                    {
                        line = Console.ReadLine();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    if (line == null)
                        return; // stdin closed; keep serving without a console.
                    if (line.Trim() != "")
                        currentServer?.EnqueueCommand(line.Trim());
                }
            });
            reader.IsBackground = true;
            reader.Name = "Console input";
            reader.Start();
        }

        static int Main(string[] args)
        {
            Console.CancelKeyPress += (sender, e) => e.Cancel = RequestQuit();
            PosixSignalRegistration sigterm = null;
            try
            {
                sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => context.Cancel = RequestQuit());
            }
            catch (PlatformNotSupportedException)
            {
            }

            StartConsoleReader();

            string dataDirectory = DatafileLoader.ResolveDataDirectory("server.config.txt", "banlist.txt");
            Console.WriteLine("Type 'help' for a list of server commands.");

            bool restartServer = true;
            while (restartServer)
            {
                ServerConfig config = ServerConfig.Load(Path.Combine(dataDirectory, "server.config.txt"));
                InfiniminerServer infiniminerServer = new InfiniminerServer(config, dataDirectory);
                currentServer = infiniminerServer;
                try
                {
                    restartServer = infiniminerServer.Run();
                    Interlocked.Exchange(ref quitRequests, 0);
                }
                catch (Exception e) when (!System.Diagnostics.Debugger.IsAttached)
                {
                    Console.Error.WriteLine(e);
                    infiniminerServer.Shutdown("The server crashed.");
                    return 1;
                }
            }
            sigterm?.Dispose();
            return 0;
        }
    }
}
