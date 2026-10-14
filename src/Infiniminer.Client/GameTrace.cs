namespace Infiniminer
{
    // With INFINIMINER_TRACE=1, the game prints what it is doing (screens entered, joining, rejoining)
    // so automated tests can wait for events instead of guessing how long things take.
    public static class GameTrace
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("INFINIMINER_TRACE") == "1";

        public static void Write(string text)
        {
            if (Enabled)
                Console.WriteLine("TRACE " + text);
        }
    }
}
