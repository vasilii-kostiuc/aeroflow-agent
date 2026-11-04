using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RabbitMQ.Client;

namespace AeroFlow.Agent.Messaging;

/// <summary>
/// Certificate checks for an amqps:// broker (task 038). The name in the
/// certificate is always checked. With a CA file, the chain must end at that CA
/// and at no other: this is how the server's private broker CA is trusted without
/// adding it to the system store. Without one, the system store decides.
/// </summary>
public static class BrokerTls
{
    private static readonly Oid ServerAuthentication = new("1.3.6.1.5.5.7.3.1");

    public static void Configure(ConnectionFactory factory, string caCertPath)
    {
        if (!factory.Ssl.Enabled)
        {
            return;
        }

        // For amqps:// the client by default accepts a certificate for another name.
        factory.Ssl.AcceptablePolicyErrors = SslPolicyErrors.None;
        if (string.IsNullOrEmpty(caCertPath))
        {
            return;
        }

        var ca = X509CertificateLoader.LoadCertificateFromFile(caCertPath);
        // Chain errors are expected (the CA is not in the system store) and settled
        // by the chain to the CA; any other error, a wrong name first of all, is not.
        factory.Ssl.CertificateValidationCallback = (_, certificate, _, errors) =>
            certificate is not null
            && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
            && ChainsTo(ca, certificate);
    }

    private static bool ChainsTo(X509Certificate2 ca, X509Certificate certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        // A private CA publishes no revocation lists.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(ServerAuthentication);

        return chain.Build(certificate as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()));
    }
}
