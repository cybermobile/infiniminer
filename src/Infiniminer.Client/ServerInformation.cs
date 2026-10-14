using System.Net;

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

        // A server that answered our LAN discovery broadcast.
        public ServerInformation(IPEndPoint endPoint, DiscoveryInfo info)
        {
            ipEndPoint = endPoint;
            serverName = info.ServerName;
            serverExtra = info.Extra;
            numPlayers = info.PlayerCount.ToString();
            maxPlayers = info.MaxPlayers.ToString();
            instanceId = info.InstanceId;
            lanServer = true;
        }

        // A server from the public server list.
        public ServerInformation(IPAddress ip, string name, string extra, string numPlayers, string maxPlayers)
        {
            ipEndPoint = new IPEndPoint(ip, GameConstants.DefaultPort);
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
