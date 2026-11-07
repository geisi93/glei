using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TriasDeparture.Alexa.Models;
using TriasDeparture.Alexa.Verification;
using static TriasDeparture.Alexa.Tests.TestData;

namespace TriasDeparture.Alexa.Tests;

public sealed class VerificationTests
{
    // ── Certificate URL (examples from Amazon's documentation) ──────────────

    [Theory]
    [InlineData("https://s3.amazonaws.com/echo.api/echo-api-cert.pem", true)]
    [InlineData("https://s3.amazonaws.com:443/echo.api/echo-api-cert.pem", true)]
    [InlineData("https://S3.AMAZONAWS.COM/echo.api/echo-api-cert.pem", true)]
    [InlineData("https://s3.amazonaws.com/echo.api/../echo.api/echo-api-cert.pem", true)]
    [InlineData("http://s3.amazonaws.com/echo.api/echo-api-cert.pem", false)]
    [InlineData("https://notamazon.com/echo.api/echo-api-cert.pem", false)]
    [InlineData("https://s3.amazonaws.com/EcHo.aPi/echo-api-cert.pem", false)]
    [InlineData("https://s3.amazonaws.com/invalid.path/echo-api-cert.pem", false)]
    [InlineData("https://s3.amazonaws.com:563/echo.api/echo-api-cert.pem", false)]
    [InlineData("", false)]
    public void CertificateUrl(string url, bool valid) =>
        AlexaRequestVerifier.IsValidCertificateUrl(url).ShouldBe(valid);

    [Fact]
    public void Timestamp_WithinTolerance()
    {
        AlexaRequestVerifier.IsTimestampValid(Now, Now.AddSeconds(149)).ShouldBeTrue();
        AlexaRequestVerifier.IsTimestampValid(Now, Now.AddSeconds(-149)).ShouldBeTrue();
        AlexaRequestVerifier.IsTimestampValid(Now, Now.AddSeconds(151)).ShouldBeFalse();
    }

    // ── Signature / certificate ──────────────────────────────────────────────

    [Fact]
    public void Signature_VerifiesBodyBytes()
    {
        using var cert = SelfSigned("echo-api.amazon.com", out var rsa);
        var body = Encoding.UTF8.GetBytes("""{"version":"1.0"}""");
        var signature = Convert.ToBase64String(rsa.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        AlexaRequestVerifier.VerifySignature(cert, signature, body, HashAlgorithmName.SHA256).ShouldBeTrue();
        AlexaRequestVerifier.VerifySignature(cert, signature, Encoding.UTF8.GetBytes("""{"version":"2.0"}"""),
            HashAlgorithmName.SHA256).ShouldBeFalse();
        AlexaRequestVerifier.VerifySignature(cert, "not base64!", body, HashAlgorithmName.SHA256).ShouldBeFalse();
    }

    [Fact]
    public void Certificate_RejectsWrongSan()
    {
        using var cert = SelfSigned("evil.example.com", out _);

        AlexaRequestVerifier.ValidateCertificate(new CertificateChain(cert, []), DateTimeOffset.UtcNow)
            .ShouldBe("Signing certificate is not issued for echo-api.amazon.com.");
    }

    [Fact]
    public void Certificate_RejectsExpired_AndUntrusted()
    {
        using var cert = SelfSigned("echo-api.amazon.com", out _);

        AlexaRequestVerifier.ValidateCertificate(new CertificateChain(cert, []), DateTimeOffset.UtcNow.AddYears(2))
            .ShouldBe("Signing certificate is not valid at this time.");
        AlexaRequestVerifier.ValidateCertificate(new CertificateChain(cert, []), DateTimeOffset.UtcNow)
            .ShouldBe("Signing certificate chain is not trusted.");
    }

    // ── VerifyAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_RejectsOtherSkill()
    {
        var verifier = Verifier(verify: false);
        var request  = Request(new RequestBody("LaunchRequest", "r", Now, "de-DE"), skillId: "amzn1.ask.skill.other");

        (await verifier.VerifyAsync(new HeaderDictionary(), [], request)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Verify_Disabled_AcceptsOwnSkill()
    {
        var verifier = Verifier(verify: false);
        var request  = Request(new RequestBody("LaunchRequest", "r", Now, "de-DE"));

        (await verifier.VerifyAsync(new HeaderDictionary(), [], request)).ShouldBeNull();
    }

    [Fact]
    public async Task Verify_Enabled_RejectsOldTimestamp_AndMissingHeaders()
    {
        var verifier = Verifier(verify: true);

        var old = Request(new RequestBody("LaunchRequest", "r", Now.AddMinutes(-10), "de-DE"));
        (await verifier.VerifyAsync(new HeaderDictionary(), [], old)).ShouldNotBeNull().ShouldContain("timestamp");

        var current = Request(new RequestBody("LaunchRequest", "r", Now, "de-DE"));
        (await verifier.VerifyAsync(new HeaderDictionary(), [], current)).ShouldNotBeNull().ShouldContain("SignatureCertChainUrl");

        var headers = new HeaderDictionary
        {
            [AlexaRequestVerifier.CertChainUrlHeader] = "https://s3.amazonaws.com/echo.api/echo-api-cert.pem"
        };
        (await verifier.VerifyAsync(headers, [], current)).ShouldBe("Missing signature header.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AlexaRequestVerifier Verifier(bool verify) => new(
        new NoHttpClientFactory(),
        Options.Create(new AlexaOptions { SkillId = "amzn1.ask.skill.test", VerifyRequests = verify }),
        new FixedTime(new DateTimeOffset(Now)),
        NullLogger<AlexaRequestVerifier>.Instance);

    private static X509Certificate2 SelfSigned(string dnsName, out RSA rsa)
    {
        rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network in tests.");
    }
}
