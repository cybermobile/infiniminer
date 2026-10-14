namespace Infiniminer
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static int Main(string[] args)
        {
            using InfiniminerGame game = new InfiniminerGame(args);
            try
            {
                game.Run();
                return 0;
            }
            catch (Exception e) when (!System.Diagnostics.Debugger.IsAttached)
            {
                // The original showed a Windows message box; log to a file next to the config instead.
                Console.Error.WriteLine(e);
                try
                {
                    File.WriteAllText(Path.Combine(game.DataDirectory, "crash.log"), e.ToString());
                }
                catch (Exception)
                {
                }
                return 1;
            }
        }
    }
}
