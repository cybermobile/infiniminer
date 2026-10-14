using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Infiniminer
{
    // Asks the home router (an Internet Gateway Device, over UPnP) to forward a UDP port to this computer, so
    // players outside the local network can join. This is what "open a port on your router" does by hand.
    // Many routers support it; some have it switched off, in which case this simply fails.
    public sealed class UpnpGateway
    {
        public static readonly IPEndPoint SsdpMulticast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

        static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        };

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public readonly Uri ControlUrl;
        public readonly string ServiceType;
        public readonly IPAddress LocalAddress;   // this computer's address as the router sees it

        UpnpGateway(Uri controlUrl, string serviceType, IPAddress localAddress)
        {
            ControlUrl = controlUrl;
            ServiceType = serviceType;
            LocalAddress = localAddress;
        }

        // Finds the router: an SSDP search (multicast, or sent to `searchEndPoint` in tests), then its
        // description, for the address of its port forwarding service. Returns null if there is none.
        public static async Task<UpnpGateway> DiscoverAsync(TimeSpan timeout, IPEndPoint searchEndPoint = null)
        {
            searchEndPoint ??= SsdpMulticast;
            using UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            foreach (string target in SearchTargets)
            {
                byte[] search = Encoding.ASCII.GetBytes(
                    "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + target + "\r\n\r\n");
                try
                {
                    await udp.SendAsync(search, search.Length, searchEndPoint).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    return null;    // no network to search (offline, or no multicast route)
                }
            }

            HashSet<string> tried = new HashSet<string>();
            using CancellationTokenSource deadline = new CancellationTokenSource(timeout);
            while (!deadline.IsCancellationRequested)
            {
                UdpReceiveResult reply;
                try
                {
                    reply = await udp.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                string location = Header(Encoding.ASCII.GetString(reply.Buffer), "LOCATION");
                if (location == null || !tried.Add(location) || !Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri))
                    continue;
                try
                {
                    UpnpGateway gateway = await FromDescriptionAsync(locationUri, reply.RemoteEndPoint.Address).ConfigureAwait(false);
                    if (gateway != null)
                        return gateway;
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is System.Xml.XmlException)
                {
                    // Not a usable gateway; keep listening for others.
                }
            }
            return null;
        }

        static string Header(string response, string name)
        {
            foreach (string line in response.Split("\r\n"))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        static async Task<UpnpGateway> FromDescriptionAsync(Uri location, IPAddress routerAddress)
        {
            XDocument description = XDocument.Parse(await http.GetStringAsync(location).ConfigureAwait(false));
            foreach (XElement service in description.Descendants().Where(e => e.Name.LocalName == "service"))
            {
                string type = service.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value.Trim() ?? "";
                string control = service.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value.Trim();
                if (control == null || !(type.Contains(":WANIPConnection:") || type.Contains(":WANPPPConnection:")))
                    continue;
                return new UpnpGateway(new Uri(location, control), type, LocalAddressFor(routerAddress));
            }
            return null;
        }

        // The address of the network interface that talks to the router.
        static IPAddress LocalAddressFor(IPAddress router)
        {
            using Socket probe = new Socket(router.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(router, 1900));
            return ((IPEndPoint)probe.LocalEndPoint).Address;
        }

        async Task<XDocument> CallAsync(string action, params (string name, string value)[] arguments)
        {
            StringBuilder body = new StringBuilder();
            body.Append("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ")
                .Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>")
                .Append("<u:").Append(action).Append(" xmlns:u=\"").Append(ServiceType).Append("\">");
            foreach (var (name, value) in arguments)
                body.Append('<').Append(name).Append('>').Append(SecurityElement.Escape(value)).Append("</").Append(name).Append('>');
            body.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ControlUrl);
            request.Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml");
            request.Headers.TryAddWithoutValidation("SOAPAction", "\"" + ServiceType + "#" + action + "\"");
            using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
            XDocument reply = XDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!response.IsSuccessStatusCode)
            {
                string code = reply.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value ?? ((int)response.StatusCode).ToString();
                string text = reply.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorDescription")?.Value ?? response.ReasonPhrase;
                throw new UpnpException(code, text);
            }
            return reply;
        }

        public async Task<IPAddress> GetExternalAddressAsync()
        {
            XDocument reply = await CallAsync("GetExternalIPAddress").ConfigureAwait(false);
            string address = reply.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value;
            return IPAddress.TryParse(address, out IPAddress parsed) ? parsed : null;
        }

        // Forwards UDP `port` on the router to the same port here, for `leaseSeconds` (renew before it ends).
        public async Task AddPortMappingAsync(int port, string description, int leaseSeconds)
        {
            (string, string)[] arguments(int lease) => new[]
            {
                ("NewRemoteHost", ""),
                ("NewExternalPort", port.ToString(CultureInfo.InvariantCulture)),
                ("NewProtocol", "UDP"),
                ("NewInternalPort", port.ToString(CultureInfo.InvariantCulture)),
                ("NewInternalClient", LocalAddress.ToString()),
                ("NewEnabled", "1"),
                ("NewPortMappingDescription", description),
                ("NewLeaseDuration", lease.ToString(CultureInfo.InvariantCulture)),
            };
            try
            {
                await CallAsync("AddPortMapping", arguments(leaseSeconds)).ConfigureAwait(false);
            }
            catch (UpnpException e) when (e.Code == "725")
            {
                // OnlyPermanentLeasesSupported: such routers want a lease of 0 (until removed).
                await CallAsync("AddPortMapping", arguments(0)).ConfigureAwait(false);
            }
        }

        public Task DeletePortMappingAsync(int port)
        {
            return CallAsync("DeletePortMapping",
                ("NewRemoteHost", ""), ("NewExternalPort", port.ToString(CultureInfo.InvariantCulture)), ("NewProtocol", "UDP"));
        }
    }

    public sealed class UpnpException : Exception
    {
        public readonly string Code;

        public UpnpException(string code, string message) : base("UPnP error " + code + ": " + message)
        {
            Code = code;
        }
    }

    // Keeps a port forwarded for as long as a server runs: maps it, renews the lease, and removes it at the end.
    public sealed class InternetPortForward
    {
        public const int LeaseSeconds = 2 * 60 * 60;
        readonly int port;
        readonly string description;
        readonly IPEndPoint searchEndPoint;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        UpnpGateway gateway;
        Task worker;

        // Plain words for the host: "OPENING...", the address players can use, or why it didn't work.
        public volatile string Status = "OPENING YOUR ROUTER (UPNP)...";
        public volatile string ExternalEndPoint;    // "203.0.113.5:5565" once open

        // A few words for tight spaces (the in-game menu).
        public string ShortStatus
        {
            get
            {
                if (ExternalEndPoint != null)
                    return ExternalEndPoint;
                return Status.EndsWith("...") ? "OPENING..." : "NOT OPEN: FORWARD UDP " + port + " BY HAND";
            }
        }
        public Action<string> Log = _ => { };

        public InternetPortForward(int port, string description, IPEndPoint searchEndPoint = null)
        {
            this.port = port;
            this.description = description;
            this.searchEndPoint = searchEndPoint;
        }

        public void Start()
        {
            worker = Task.Run(RunAsync);
        }

        async Task RunAsync()
        {
            try
            {
                gateway = await UpnpGateway.DiscoverAsync(TimeSpan.FromSeconds(4), searchEndPoint).ConfigureAwait(false);
                if (gateway == null)
                {
                    Status = "NO ROUTER ANSWERED (UPNP MAY BE OFF): FORWARD UDP PORT " + port + " BY HAND";
                    Log(Status);
                    return;
                }
                while (!stop.IsCancellationRequested)
                {
                    await gateway.AddPortMappingAsync(port, description, LeaseSeconds).ConfigureAwait(false);
                    IPAddress external = await gateway.GetExternalAddressAsync().ConfigureAwait(false);
                    ExternalEndPoint = external != null ? external + ":" + port : null;
                    Status = ExternalEndPoint != null ? "OPEN TO THE INTERNET AT " + ExternalEndPoint : "OPEN TO THE INTERNET ON PORT " + port;
                    Log(Status);
                    await Task.Delay(TimeSpan.FromSeconds(LeaseSeconds / 2), stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Status = "THE ROUTER REFUSED (" + TextUtil.Sanitize(e.Message).ToUpperInvariant() + "): FORWARD UDP PORT " + port + " BY HAND";
                Log(Status);
            }
        }

        // Stops renewing and removes the forward (waiting at most a couple of seconds).
        public void Stop()
        {
            stop.Cancel();
            try
            {
                worker?.Wait(TimeSpan.FromSeconds(2));
                if (gateway != null && ExternalEndPoint != null)
                    gateway.DeletePortMappingAsync(port).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // The lease runs out by itself anyway.
            }
        }
    }
}
