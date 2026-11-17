using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>BT-154 (report 66dd7ef2): a server whose port the listener table misses is up by its log line; a real connection tells whether the port works.</summary>
[TestFixture]
public class ServerHostTests {
    private string folder = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "ServerHostTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(folder, true);

    /// <summary>A port nothing listens on: bound once, then released. Never a game port, because a running game would hold it.</summary>
    private static int FreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public void IsUp_LogLineOnly_IsTrue() {
        File.WriteAllText(Path.Combine(folder, "logs", "login.log"), "LoginServer started on Port:1\r\n");
        using var host = new ServerHost(Settings.Load(folder));
        Assert.That(host.IsUp("Login", FreePort(), "started on Port:"), Is.True);
    }

    [Test]
    public void IsUp_NoRowNoLogLine_IsFalse() {
        File.WriteAllText(Path.Combine(folder, "logs", "login.log"), "");
        using var host = new ServerHost(Settings.Load(folder));
        Assert.That(host.IsUp("Login", FreePort(), "started on Port:"), Is.False);
    }

    [Test]
    public async Task CanConnectAsync_OpenThenClosed() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try {
            Assert.That(await ServerHost.CanConnectAsync(port), Is.True);
        } finally {
            listener.Stop();
        }
        Assert.That(await ServerHost.CanConnectAsync(port), Is.False);
    }

    /// <summary>BT-215: the report's Network line says whether a proxy takes 127.0.0.1 and names the variables set, never a value.</summary>
    [Test]
    public void NetworkLine_ProxyAndNamesOnly() {
        var environment = new Dictionary<string, string> {
            ["HTTP_PROXY"] = "http://someone:secret@proxy.example:8080", ["GRPC_WORLD_PORT"] = "21005", ["NO_PROXY"] = "",
        };
        Assert.That(ServerHost.NetworkLine(name => environment.GetValueOrDefault(name), new WebProxy("http://127.0.0.1:9")),
            Is.EqualTo("proxy for 127.0.0.1 YES; variables set: HTTP_PROXY, GRPC_WORLD_PORT"));
        Assert.That(ServerHost.NetworkLine(name => environment.GetValueOrDefault(name), new WebProxy("http://127.0.0.1:9", true)),
            Is.EqualTo("proxy for 127.0.0.1 no; variables set: HTTP_PROXY, GRPC_WORLD_PORT"), "a proxy that skips local addresses");
        Assert.That(ServerHost.NetworkLine(_ => null, new WebProxy()), Is.EqualTo("proxy for 127.0.0.1 no; variables set: none"));
        Assert.That(ServerHost.NetworkLine(_ => null, new SystemProxyWithoutAProxy()), Is.EqualTo("proxy for 127.0.0.1 no; variables set: none"),
            "the Windows system proxy");
    }

    // LC-02 (BT-249, report 1497cc8a): a process below a server that cannot be ended ("Not all processes in process tree could be
    // terminated. (Access is denied.)") is no problem of the session: the server is ended alone and the reason is returned for the log.
    [Test]
    public void EndTree_AProcessThatCannotBeEndedIsNoProblem() {
        int alone = 0;
        Assert.That(ServerHost.EndTree(() => { }, () => alone++), Is.Null, "the tree ended");
        Assert.That(ServerHost.EndTree(() => throw new InvalidOperationException(), () => alone++), Is.Null, "already gone");
        Assert.That(alone, Is.EqualTo(0));

        var denied = new AggregateException("Not all processes in process tree could be terminated.", new System.ComponentModel.Win32Exception(5));
        Assert.That(ServerHost.EndTree(() => throw denied, () => alone++), Is.EqualTo(denied.InnerException!.Message), "a descendant refused");
        Assert.That(alone, Is.EqualTo(1), "the server is ended alone");
        Assert.That(ServerHost.EndTree(() => throw new System.ComponentModel.Win32Exception(5), () => throw new InvalidOperationException()), Is.Not.Null,
            "the server itself refused, and ending it alone found it gone");
    }

    /// <summary>As .NET's Windows system proxy for an address it sends direct: never bypassed, and no proxy.</summary>
    private sealed class SystemProxyWithoutAProxy : IWebProxy {
        public ICredentials? Credentials { get; set; }
        public Uri? GetProxy(Uri destination) => null;
        public bool IsBypassed(Uri host) => false;
    }
}
