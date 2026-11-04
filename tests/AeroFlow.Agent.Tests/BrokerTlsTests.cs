using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AeroFlow.Agent.Messaging;
using RabbitMQ.Client;

namespace AeroFlow.Agent.Tests;

public sealed class BrokerTlsTests : IDisposable
{
    private const string BrokerUri = "amqps://aeroflow-agent:secret@broker.test:5671/%2f";

    private readonly X509Certificate2 _ca = CreateCa("Broker CA");
    private readonly string _caPath = Path.Combine(Path.GetTempPath(), $"aeroflow-agent-ca-{Guid.NewGuid()}.crt");

    public BrokerTlsTests() => File.WriteAllText(_caPath, _ca.ExportCertificatePem());

    [Fact]
    public void AcceptsTheCertificateIssuedByTheCa()
    {
        using var certificate = IssueServerCertificate(_ca);

        Assert.True(Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void RejectsACertificateForAnotherName()
    {
        using var certificate = IssueServerCertificate(_ca);

        Assert.False(Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void RejectsACertificateIssuedByAnotherCa()
    {
        using var otherCa = CreateCa("Other CA");
        using var certificate = IssueServerCertificate(otherCa);

        Assert.False(Validate(certificate, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void WithoutACaFileTheSystemStoreDecidesButTheNameIsChecked()
    {
        var factory = new ConnectionFactory { Uri = new Uri(BrokerUri) };

        BrokerTls.Configure(factory, "");

        Assert.Equal(SslPolicyErrors.None, factory.Ssl.AcceptablePolicyErrors);
        Assert.Null(factory.Ssl.CertificateValidationCallback);
    }

    [Fact]
    public void LeavesPlainAmqpWithoutTls()
    {
        var factory = new ConnectionFactory { Uri = new Uri("amqp://guest:guest@localhost:5672/") };

        BrokerTls.Configure(factory, _caPath);

        Assert.False(factory.Ssl.Enabled);
        Assert.Null(factory.Ssl.CertificateValidationCallback);
    }

    public void Dispose()
    {
        _ca.Dispose();
        File.Delete(_caPath);
    }

    private bool Validate(X509Certificate2 certificate, SslPolicyErrors errors)
    {
        var factory = new ConnectionFactory { Uri = new Uri(BrokerUri) };
        BrokerTls.Configure(factory, _caPath);

        return factory.Ssl.CertificateValidationCallback!(this, certificate, null, errors);
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static X509Certificate2 IssueServerCertificate(X509Certificate2 ca)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=broker.test", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("broker.test");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

        return request.Create(ca, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMonths(6), RandomNumberGenerator.GetBytes(8));
    }
}
