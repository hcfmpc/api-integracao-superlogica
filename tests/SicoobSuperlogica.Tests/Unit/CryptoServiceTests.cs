using FluentAssertions;
using SicoobSuperlogica.Worker.Services;
using System.Security.Cryptography;

namespace SicoobSuperlogica.Tests.Unit;

public class CryptoServiceTests
{
    private readonly CryptoService _sut = new("senha-mestre-teste-123");

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_ReturnsOriginalValue()
    {
        var original = new Payload("valor-secreto", 42);

        var blob = _sut.Encrypt(original);
        var result = _sut.Decrypt<Payload>(blob);

        result.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Encrypt_SameValue_ProducesDifferentBlobs()
    {
        var value = new Payload("mesmo-valor", 1);

        var blob1 = _sut.Encrypt(value);
        var blob2 = _sut.Encrypt(value);

        blob1.Should().NotEqual(blob2, "cada cifração usa salt e nonce aleatórios");
    }

    [Fact]
    public void Decrypt_WrongPassword_ThrowsCryptographicException()
    {
        var blob = _sut.Encrypt(new Payload("segredo", 1));

        var wrongKey = new CryptoService("senha-errada");
        var act = () => wrongKey.Decrypt<Payload>(blob);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_TamperedBlob_ThrowsCryptographicException()
    {
        var blob = _sut.Encrypt(new Payload("integridade", 99));
        blob[blob.Length / 2] ^= 0xFF; // flip bits in ciphertext

        var act = () => _sut.Decrypt<Payload>(blob);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_BlobTooShort_ThrowsCryptographicException()
    {
        var act = () => _sut.Decrypt<Payload>([0x00, 0x01]);

        act.Should().Throw<CryptographicException>()
            .WithMessage("*Blob too short*");
    }

    private record Payload(string Texto, int Numero);
}
