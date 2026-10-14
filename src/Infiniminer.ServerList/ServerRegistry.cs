using System.Globalization;
using System.Text;

namespace Infiniminer.ServerList
{
    public sealed class ServerListing
    {
        public string Name;
        public string Address;
        public int Port;
        public int PlayerCount;
        public int MaxPlayers;
        public string Extra;
        public DateTime LastSeen;
    }

    // The servers that have announced themselves recently. Servers re-announce every five minutes;
    // anything not heard from for a while is dropped.
    public sealed class ServerRegistry
    {
        public TimeSpan Expiry = TimeSpan.FromMinutes(12);
        public int MaxPerAddress = 8;
        public int MaxTotal = 1000;

        readonly Dictionary<(string address, int port), ServerListing> servers = new Dictionary<(string address, int port), ServerListing>();
        readonly object gate = new object();

        // Field names are the ones the original Infiniminer server posted. Returns an error, or null on success.
        // The address always comes from the connection, so nobody can list someone else's machine.
        public string Announce(string address, IReadOnlyDictionary<string, string> fields, DateTime now)
        {
            string Field(string name) => fields.TryGetValue(name, out string value) ? value : "";

            if (Field("game") != "INFINIMINER")
                return "unknown game";
            string name = Clean(Field("name"), 40);
            if (name == "")
                return "missing name";
            if (!int.TryParse(Field("player_count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerCount)
                || !int.TryParse(Field("player_capacity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxPlayers))
                return "bad player counts";
            int port = GameConstants.DefaultPort;
            if (Field("port") != "" && (!int.TryParse(Field("port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535))
                return "bad port";

            lock (gate)
            {
                RemoveExpired(now);
                (string, int) key = (address, port);
                if (!servers.ContainsKey(key))
                {
                    if (servers.Keys.Count(k => k.address == address) >= MaxPerAddress)
                        return "too many servers from this address";
                    if (servers.Count >= MaxTotal)
                        return "server list is full";
                }
                servers[key] = new ServerListing
                {
                    Name = name,
                    Address = address,
                    Port = port,
                    PlayerCount = Math.Clamp(playerCount, 0, 999),
                    MaxPlayers = Math.Clamp(maxPlayers, 0, 999),
                    Extra = Clean(Field("extra"), 40),
                    LastSeen = now,
                };
            }
            return null;
        }

        public List<ServerListing> List(DateTime now)
        {
            lock (gate)
            {
                RemoveExpired(now);
                return servers.Values.OrderByDescending(s => s.PlayerCount).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        void RemoveExpired(DateTime now)
        {
            foreach (var key in servers.Where(pair => now - pair.Value.LastSeen > Expiry).Select(pair => pair.Key).ToList())
                servers.Remove(key);
        }

        // Printable ASCII only, without the list format's separators.
        static string Clean(string value, int maxLength)
        {
            string cleaned = TextUtil.Sanitize(value).Replace(";", "").Trim();
            return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
        }

        // The original format, one server per line: name;ip;INFINIMINER;players;capacity;extra
        // plus a seventh field with the port (older clients ignored lines that weren't six fields long).
        public static string ToPlainText(IEnumerable<ServerListing> listings)
        {
            StringBuilder text = new StringBuilder();
            foreach (ServerListing s in listings)
                text.Append(s.Name).Append(';').Append(s.Address).Append(";INFINIMINER;")
                    .Append(s.PlayerCount).Append(';').Append(s.MaxPlayers).Append(';').Append(s.Extra).Append(';').Append(s.Port).Append("\r\n");
            return text.ToString();
        }
    }
}
