using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Infiniminer.Tests
{
    // A pretend home router: answers SSDP searches and the port forwarding service's SOAP calls.
    sealed class FakeGateway : IDisposable
    {
        readonly UdpClient ssdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        readonly HttpListener http = new HttpListener();
        readonly int httpPort;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        public readonly List<string> Calls = new List<string>();
        public bool OnlyPermanentLeases;

        public IPEndPoint SearchEndPoint
        {
            get { return (IPEndPoint)ssdp.Client.LocalEndPoint; }
        }

        public FakeGateway()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            httpPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            http.Prefixes.Add("http://127.0.0.1:" + httpPort + "/");
            http.Start();
            Task.Run(AnswerSearches);
            Task.Run(ServeHttp);
        }

        async Task AnswerSearches()
        {
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult search = await ssdp.ReceiveAsync(stop.Token);
                if (!Encoding.ASCII.GetString(search.Buffer).StartsWith("M-SEARCH"))
                    continue;
                byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nLOCATION: http://127.0.0.1:" + httpPort + "/rootDesc.xml\r\n\r\n");
                await ssdp.SendAsync(reply, reply.Length, search.RemoteEndPoint);
            }
        }

        async Task ServeHttp()
        {
            while (http.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await http.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }
                string response;
                int status = 200;
                if (context.Request.HttpMethod == "GET")
                    response = "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>"
                        + "<deviceList><device><serviceList><service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>"
                        + "<controlURL>/ctl/IPConn</controlURL></service></serviceList></device></deviceList></device></root>";
                else
                {
                    string body = new StreamReader(context.Request.InputStream).ReadToEnd();
                    string action = context.Request.Headers["SOAPAction"].Trim('"').Split('#')[1];
                    lock (Calls)
                        Calls.Add(action + " " + body);
                    if (action == "AddPortMapping" && OnlyPermanentLeases && !body.Contains("<NewLeaseDuration>0<"))
                    {
                        status = 500;
                        response = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><detail><UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\">"
                            + "<errorCode>725</errorCode><errorDescription>OnlyPermanentLeasesSupported</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>";
                    }
                    else if (action == "GetExternalIPAddress")
                        response = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetExternalIPAddressResponse xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">"
                            + "<NewExternalIPAddress>203.0.113.7</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>";
                    else
                        response = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:" + action + "Response xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\"/></s:Body></s:Envelope>";
                }
                byte[] bytes = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = status;
                context.Response.ContentType = "text/xml";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public string[] CallsSoFar()
        {
            lock (Calls)
                return Calls.ToArray();
        }

        public void Dispose()
        {
            stop.Cancel();
            ssdp.Dispose();
            http.Close();
        }
    }

    public class PortMappingTests
    {
        static void WaitFor(Func<bool> condition, string what)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Timed out waiting for " + what);
                Thread.Sleep(20);
            }
        }

        [Fact]
        public void TheRouterIsAskedToForwardThePortAndToForgetItAfterwards()
        {
            using FakeGateway router = new FakeGateway();
            InternetPortForward forward = new InternetPortForward(5565, "Infiniminer test", router.SearchEndPoint);
            forward.Start();
            WaitFor(() => forward.ExternalEndPoint != null, "the port to be forwarded");

            Assert.Equal("203.0.113.7:5565", forward.ExternalEndPoint);
            Assert.Equal("OPEN TO THE INTERNET AT 203.0.113.7:5565", forward.Status);
            string mapping = router.CallsSoFar().Single(c => c.StartsWith("AddPortMapping"));
            Assert.Contains("<NewExternalPort>5565</NewExternalPort>", mapping);
            Assert.Contains("<NewInternalPort>5565</NewInternalPort>", mapping);
            Assert.Contains("<NewProtocol>UDP</NewProtocol>", mapping);
            Assert.Contains("<NewInternalClient>127.0.0.1</NewInternalClient>", mapping);
            Assert.Contains("<NewLeaseDuration>" + InternetPortForward.LeaseSeconds + "</NewLeaseDuration>", mapping);

            forward.Stop();
            Assert.Contains(router.CallsSoFar(), c => c.StartsWith("DeletePortMapping") && c.Contains("<NewExternalPort>5565<"));
        }

        [Fact]
        public void RoutersThatOnlyAllowPermanentForwardsGetOne()
        {
            using FakeGateway router = new FakeGateway { OnlyPermanentLeases = true };
            InternetPortForward forward = new InternetPortForward(6000, "Infiniminer test", router.SearchEndPoint);
            forward.Start();
            WaitFor(() => forward.ExternalEndPoint != null, "the port to be forwarded");
            Assert.Contains(router.CallsSoFar(), c => c.StartsWith("AddPortMapping") && c.Contains("<NewLeaseDuration>0<"));
            forward.Stop();
        }

        [Fact]
        public void WithoutAnAnsweringRouterTheHostIsToldWhatToDo()
        {
            // Searching somewhere nobody listens.
            using UdpClient silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            InternetPortForward forward = new InternetPortForward(5565, "Infiniminer test", (IPEndPoint)silent.Client.LocalEndPoint);
            forward.Start();
            WaitFor(() => !forward.Status.EndsWith("..."), "giving up");
            Assert.Null(forward.ExternalEndPoint);
            Assert.Contains("FORWARD UDP PORT 5565 BY HAND", forward.Status);
            forward.Stop();
        }
    }
}
