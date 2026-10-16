using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Infiniminer
{
    public class ServerInformation
    {
        public IPEndPoint ipEndPoint;
        public string serverName;
        public string serverExtra;
        public string numPlayers;
        public string maxPlayers;
        public bool lanServer;
        public string instanceId;

        // From the server's own reply (null for a public list entry that hasn't answered); details are only there when
        // the server is new enough to send them (Details.HasDetails).
        public DiscoveryInfo Details;
        public int PingMs = -1;         // -1: not measured
        public double LastSeen;         // when it last answered (real seconds)

        // A server that answered a discovery request.
        public ServerInformation(IPEndPoint endPoint, DiscoveryInfo info)
        {
            ipEndPoint = endPoint;
            serverName = info.ServerName;
            serverExtra = info.Extra;
            numPlayers = info.PlayerCount.ToString();
            maxPlayers = info.MaxPlayers.ToString();
            instanceId = info.InstanceId;
            lanServer = true;
            Details = info;
        }

        // A server from the public server list.
        public ServerInformation(IPEndPoint endPoint, string name, string extra, string numPlayers, string maxPlayers)
        {
            ipEndPoint = endPoint;
            serverName = name;
            serverExtra = extra;
            this.numPlayers = numPlayers;
            this.maxPlayers = maxPlayers;
            lanServer = false;
        }

        public string GetServerDesc()
        {
            string serverDesc = TextUtil.Sanitize(serverName).Trim() + " ( " + numPlayers.Trim() + " / " + maxPlayers.Trim() + " )";
            if (lanServer)
                serverDesc += " - LAN SERVER";
            if (serverExtra.Trim() != "")
                serverDesc += (lanServer ? ", " : " - ") + TextUtil.Sanitize(serverExtra).Trim();
            return serverDesc;
        }

        // What the server list shows. Servers that don't send details are described from their summary ("10k, hill, 3 bots").

        bool HasDetails
        {
            get { return Details != null && Details.HasDetails; }
        }

        string Summary
        {
            get { return (serverExtra ?? "").ToLowerInvariant(); }
        }

        public string Name
        {
            get { return TextUtil.Sanitize(serverName).Trim().ToUpperInvariant(); }
        }

        // Everyone in the game, computer players included.
        public int Players
        {
            get { return int.TryParse(numPlayers, out int n) ? n : 0; }
        }

        public int Capacity
        {
            get { return int.TryParse(maxPlayers, out int n) ? n : 0; }
        }

        public int Bots
        {
            get
            {
                if (HasDetails)
                    return Details.Bots;
                Match match = Regex.Match(Summary, @"(\d+) bots?");
                return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            }
        }

        // People playing (bots make way for people, so they don't count towards a full server).
        public int People
        {
            get { return Math.Max(0, Players - Bots); }
        }

        public bool Full
        {
            get { return Capacity > 0 && People >= Capacity; }
        }

        public bool Sandbox
        {
            get { return HasDetails ? Details.Sandbox : Summary.Contains("sandbox"); }
        }

        public GameMode Mode
        {
            get { return HasDetails ? Details.Mode : Summary.Contains("hill") ? GameMode.Hill : GameMode.Cash; }
        }

        public string Version
        {
            get { return HasDetails ? Details.Version : ""; }
        }

        // A server running another version of the game can't be joined.
        public bool WrongVersion
        {
            get { return Version != "" && Version != GameConstants.Version; }
        }

        public string ModeLabel
        {
            get { return Sandbox ? "SANDBOX" : Mode == GameMode.Hill ? "KING OF THE HILL" : "CASH RACE"; }
        }

        public string WorldLabel
        {
            get { return HasDetails && Details.MapSize > 0 ? Details.MapSize + "X" + Details.MapSize : "-"; }
        }

        public string PlayersLabel
        {
            get { return People + "/" + Capacity; }
        }

        public string BotsLabel
        {
            get { return Bots > 0 ? Bots.ToString(CultureInfo.InvariantCulture) : "-"; }
        }

        public string PingLabel
        {
            get { return PingMs < 0 ? "-" : PingMs < 1 ? "<1" : PingMs.ToString(CultureInfo.InvariantCulture); }
        }

        // One line about the game, for the selected server.
        public string DetailLine
        {
            get
            {
                List<string> parts = new List<string>();
                if (Sandbox)
                    parts.Add("SANDBOX: FREE BUILDING, NO WINNER");
                else
                {
                    string target = HasDetails && Details.WinningCash > 0
                        ? " TO $" + Details.WinningCash.ToString("#,0", CultureInfo.InvariantCulture) : "";
                    parts.Add(Mode == GameMode.Hill ? "KING OF THE HILL" + target : "CASH RACE" + target);
                }
                if (HasDetails)
                {
                    parts.Add(Details.Lava ? "LAVA" : "NO LAVA");
                    if (Details.MapSize > 0)
                        parts.Add(Details.MapSize + "X" + Details.MapSize + " WORLD");
                }
                else if (Summary.Contains("!lava"))
                    parts.Add("NO LAVA");
                parts.Add(People == 1 ? "1 PERSON" : People + " PEOPLE");
                if (Bots > 0)
                    parts.Add(Bots == 1 ? "1 COMPUTER PLAYER" : Bots + " COMPUTER PLAYERS");
                if (Version != "")
                    parts.Add(Version.ToUpperInvariant());
                return string.Join("  -  ", parts);
            }
        }

        // QUICK PLAY's choice: a game that can be joined (not full, same version, not a sandbox), preferring your own
        // hosted game, then games with people in them, then LAN games, then the lowest ping. Null if there is none.
        public static ServerInformation PickForQuickPlay(IEnumerable<ServerInformation> servers, Func<ServerInformation, bool> isOwn)
        {
            return servers.Where(s => !s.Full && !s.WrongVersion && !s.Sandbox)
                .OrderByDescending(isOwn)
                .ThenByDescending(s => s.People > 0)
                .ThenByDescending(s => s.lanServer)
                .ThenBy(s => s.PingMs < 0 ? int.MaxValue : s.PingMs)
                .FirstOrDefault();
        }

        public override bool Equals(object obj)
        {
            if (obj is not ServerInformation serverInfo)
                return false;

            if (instanceId != null && instanceId == serverInfo.instanceId)
                return true;

            return ipEndPoint.Equals(serverInfo.ipEndPoint);
        }

        public override int GetHashCode()
        {
            return ipEndPoint.GetHashCode();
        }
    }
}
