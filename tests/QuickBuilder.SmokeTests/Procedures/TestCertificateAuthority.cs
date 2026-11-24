using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace QuickBuilder.SmokeTests.Procedures;

/// <summary>
/// A throwaway certificate authority and one certificate for every example host name, standing
/// in for Let's Encrypt in the proxy procedures (026 T063; contracts/runnable-snippets.md §2,
/// <c>test-certificates</c>). Nothing but the test trusts it, and it expires in a day.
/// </summary>
internal sealed class TestCertificateAuthority : IDisposable
{
    public static readonly string[] HostNames = ["example.com", "www.example.com", "shop.example.net", "admin.example.com"];

    private readonly X509Certificate2 _root;

    public TestCertificateAuthority()
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=QuickBuilder procedure test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        _root = rootRequest.CreateSelfSigned(now, now.AddDays(1));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=example.com", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        foreach (var host in HostNames)
        {
            names.AddDnsName(host);
        }

        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var leaf = leafRequest.Create(_root, now, now.AddDays(1), Guid.NewGuid().ToByteArray()[..8]);

        FullChainPem = leaf.ExportCertificatePem() + "\n" + _root.ExportCertificatePem() + "\n";
        PrivateKeyPem = leafKey.ExportPkcs8PrivateKeyPem() + "\n";
    }

    public string FullChainPem { get; }

    public string PrivateKeyPem { get; }

    /// <summary>Writes <c>fullchain.pem</c>, <c>privkey.pem</c> and Traefik's <c>tls.yml</c> into <paramref name="directory"/>.</summary>
    public void WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "fullchain.pem"), FullChainPem);
        File.WriteAllText(Path.Combine(directory, "privkey.pem"), PrivateKeyPem);
        File.WriteAllText(Path.Combine(directory, "tls.yml"), """
            tls:
              stores:
                default:
                  defaultCertificate:
                    certFile: /certs/fullchain.pem
                    keyFile: /certs/privkey.pem
            """);
    }

    /// <summary>
    /// A client that reaches <paramref name="port"/> on 127.0.0.1 for any host name, the way
    /// <c>curl --resolve</c> does, and trusts only this authority.
    /// </summary>
    public HttpClient ClientFor(int port, string hostName)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                await socket.ConnectAsync(System.Net.IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(_root);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    using var presented = new X509Certificate2(certificate);
                    return chain.Build(presented) && presented.MatchesHostname(hostName);
                },
            },
            UseCookies = false,
            AllowAutoRedirect = false,
        };

        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"https://{hostName}:{port}") };
    }

    public void Dispose() => _root.Dispose();
}
