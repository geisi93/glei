using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using TriasDeparture.Alexa.Models;

namespace TriasDeparture.Alexa.Verification;

/// <summary>
/// Checks that a request really comes from Alexa for this skill, as Amazon requires for HTTPS endpoints:
/// skill ID, timestamp, signing certificate (URL, validity, SAN, chain) and body signature.
/// https://developer.amazon.com/docs/custom-skills/host-a-custom-skill-as-a-web-service.html
/// </summary>
public sealed class AlexaRequestVerifier(
    IHttpClientFactory httpClients,
    IOptions<AlexaOptions> options,
    TimeProvider time,
    ILogger<AlexaRequestVerifier> logger)
{
    public const string HttpClientName     = "alexa-certificates";
    public const string CertChainUrlHeader = "SignatureCertChainUrl";
    public const string Signature256Header = "Signature-256";
    public const string SignatureHeader    = "Signature";

    private const string CertificateSan = "echo-api.amazon.com";
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(150);

    private readonly ConcurrentDictionary<string, CertificateChain> _certificates = new();
    private AlexaOptions Options => options.Value;

    /// <summary>Returns null if the request is valid, otherwise the reason it was rejected.</summary>
    public async Task<string?> VerifyAsync(IHeaderDictionary headers, byte[] body, SkillRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(Options.SkillId) && request.ApplicationId != Options.SkillId)
            return $"Unexpected skill ID '{request.ApplicationId}'.";

        if (!Options.VerifyRequests)
            return null;

        var now = time.GetUtcNow();
        if (!IsTimestampValid(request.Request.Timestamp, now))
            return $"Request timestamp {request.Request.Timestamp:O} is outside the {TimestampTolerance.TotalSeconds} s tolerance.";

        var certUrl = headers[CertChainUrlHeader].ToString();
        if (!IsValidCertificateUrl(certUrl))
            return $"Invalid {CertChainUrlHeader} '{certUrl}'.";

        // Prefer the SHA-256 signature; the SHA-1 "Signature" header is legacy
        var (signature, hash) = headers.TryGetValue(Signature256Header, out var sha256) && !string.IsNullOrEmpty(sha256)
            ? (sha256.ToString(), HashAlgorithmName.SHA256)
            : (headers[SignatureHeader].ToString(), HashAlgorithmName.SHA1);
        if (string.IsNullOrEmpty(signature))
            return "Missing signature header.";

        CertificateChain chain;
        try
        {
            chain = await GetCertificateChainAsync(certUrl, now, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException)
        {
            logger.LogWarning(ex, "Could not load Alexa signing certificate from {Url}", certUrl);
            return "Signing certificate could not be loaded.";
        }

        return ValidateCertificate(chain, now) ?? (VerifySignature(chain.Leaf, signature, body, hash)
            ? null
            : "Signature does not match the request body.");
    }

    // ── Individual checks (internal for tests) ───────────────────────────────

    internal static bool IsTimestampValid(DateTimeOffset timestamp, DateTimeOffset now) =>
        (now - timestamp).Duration() <= TimestampTolerance;

    /// <summary>
    /// https, host s3.amazonaws.com (any case), port 443 if given, path starting with /echo.api/
    /// (case-sensitive, after resolving "..").
    /// </summary>
    internal static bool IsValidCertificateUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "s3.amazonaws.com", StringComparison.OrdinalIgnoreCase)
        && uri.Port == 443
        && uri.AbsolutePath.StartsWith("/echo.api/", StringComparison.Ordinal);

    /// <summary>Leaf valid now, issued for echo-api.amazon.com and chaining up to a trusted root.</summary>
    internal static string? ValidateCertificate(CertificateChain certificates, DateTimeOffset now)
    {
        var leaf = certificates.Leaf;
        if (now < leaf.NotBefore.ToUniversalTime() || now > leaf.NotAfter.ToUniversalTime())
            return "Signing certificate is not valid at this time.";

        var sanNames = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(e => e.EnumerateDnsNames());
        if (!sanNames.Contains(CertificateSan, StringComparer.OrdinalIgnoreCase))
            return $"Signing certificate is not issued for {CertificateSan}.";

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode   = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;
        chain.ChainPolicy.ExtraStore.AddRange(certificates.Intermediates);

        return chain.Build(leaf) ? null : "Signing certificate chain is not trusted.";
    }

    internal static bool VerifySignature(X509Certificate2 leaf, string base64Signature, byte[] body, HashAlgorithmName hash)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(base64Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        using var rsa = leaf.GetRSAPublicKey();
        return rsa is not null && rsa.VerifyData(body, signature, hash, RSASignaturePadding.Pkcs1);
    }

    // ── Certificate download + cache ─────────────────────────────────────────

    private async Task<CertificateChain> GetCertificateChainAsync(string url, DateTimeOffset now, CancellationToken ct)
    {
        if (_certificates.TryGetValue(url, out var cached) && now < cached.Leaf.NotAfter.ToUniversalTime())
            return cached;

        var pem = await httpClients.CreateClient(HttpClientName).GetStringAsync(url, ct);

        var collection = new X509Certificate2Collection();
        collection.ImportFromPem(pem);
        if (collection.Count == 0)
            throw new CryptographicException("No certificate in PEM.");

        // The first certificate is the signing certificate, the rest is the chain
        var chain = new CertificateChain(collection[0], new X509Certificate2Collection(collection.Skip(1).ToArray()));
        _certificates[url] = chain;
        return chain;
    }
}

/// <summary>Signing certificate plus the intermediates delivered with it.</summary>
public sealed record CertificateChain(X509Certificate2 Leaf, X509Certificate2Collection Intermediates);
