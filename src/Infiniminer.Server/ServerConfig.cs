namespace Infiniminer
{
    public class ServerConfig
    {
        public string ServerName = "Unnamed Server";
        public uint MaxPlayers = 16;
        public bool PublicServer = false;
        public uint WinningCashAmount = 10000;
        public bool IncludeLava = true;
        public uint OreFactor = 10;
        public bool SandboxMode = false;
        public int Port = GameConstants.DefaultPort;
        public bool HomeBases = false;      // each team gets a base to spawn at
        public bool ShockTouch = false;     // shock blocks kill on any touch, not just from below

        // The original public server list (apps.keithholman.net) no longer exists, so listing is opt-in
        // through this setting. The endpoint receives the same form POST the original server sent.
        public string ServerListUrl = "";

        public static ServerConfig Load(string filename)
        {
            DatafileLoader dataFile = new DatafileLoader(filename);
            ServerConfig config = new ServerConfig();
            config.WinningCashAmount = dataFile.GetUInt("winningcash", config.WinningCashAmount);
            config.IncludeLava = dataFile.GetBool("includelava", config.IncludeLava);
            config.OreFactor = dataFile.GetUInt("orefactor", config.OreFactor);
            config.MaxPlayers = Math.Min(32, dataFile.GetUInt("maxplayers", config.MaxPlayers));
            config.PublicServer = dataFile.GetBool("public", config.PublicServer);
            config.ServerName = dataFile.GetString("servername", config.ServerName);
            config.SandboxMode = dataFile.GetBool("sandbox", config.SandboxMode);
            config.Port = dataFile.GetInt("port", config.Port);
            config.HomeBases = dataFile.GetBool("homebases", config.HomeBases);
            config.ShockTouch = dataFile.GetBool("shocktouch", config.ShockTouch);
            config.ServerListUrl = dataFile.GetString("serverlist", config.ServerListUrl);
            return config;
        }
    }
}
