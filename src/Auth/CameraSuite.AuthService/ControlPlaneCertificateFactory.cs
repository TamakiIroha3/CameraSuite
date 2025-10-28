using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CameraSuite.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraSuite.AuthService;

public sealed class ControlPlaneCertificateFactory
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ControlPlaneCertificateFactory> _logger;
    private X509Certificate2? _cachedCertificate;
    private readonly object _sync = new();

    public ControlPlaneCertificateFactory(
        IOptions<CameraSuiteOptions> options,
        TimeProvider timeProvider,
        ILogger<ControlPlaneCertificateFactory> logger)
    {
        _options = options.Value.Auth;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool UseTls => _options.UseTls;

    public X509Certificate2 GetOrCreateCertificate()
    {
        if (!UseTls)
        {
            throw new InvalidOperationException("TLS is not enabled.");
        }

        lock (_sync)
        {
            if (_cachedCertificate is not null)
            {
                return _cachedCertificate;
            }

            if (!string.IsNullOrWhiteSpace(_options.CertificatePath) && File.Exists(_options.CertificatePath))
            {
                _cachedCertificate = new X509Certificate2(
                    _options.CertificatePath,
                    string.IsNullOrEmpty(_options.CertificatePassword) ? null : _options.CertificatePassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);

                _logger.LogInformation("Loaded TLS certificate from {Path}", _options.CertificatePath);
                return _cachedCertificate;
            }

            if (!_options.AutoGenerateCertificate)
            {
                throw new InvalidOperationException("TLS enabled but no certificate configured.");
            }

            _cachedCertificate = GenerateSelfSignedCertificate();
            _logger.LogWarning("Generated self-signed TLS certificate for control plane. Distribute trust information to clients.");
            return _cachedCertificate;
        }
    }

    private X509Certificate2 GenerateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(4096);
        var subject = new X500DistinguishedName(_options.CertificateSubject ?? "CN=CameraSuiteAuth");
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = _timeProvider.GetUtcNow();
        var notBefore = now.AddMinutes(-5);
        var notAfter = now.AddDays(Math.Max(1, _options.CertificateValidityDays));

        var certificate = request.CreateSelfSigned(notBefore, notAfter);
        if (OperatingSystem.IsWindows())
        {
            certificate.FriendlyName = "CameraSuite Auth Self-Signed";
        }
        return certificate;
    }
}

