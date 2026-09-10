using Xunit;
using FluentAssertions;
using MeshChat.Core.Models;
using MeshChat.Core.Services;

namespace MeshChat.Tests;

/// <summary>
/// Unit tests for the CryptoService class.
/// </summary>
public class CryptoServiceTests
{
    [Fact]
    public void GenerateIdentity_ReturnsValidKeyPair()
    {
        // Arrange
        var crypto = new CryptoService();

        // Act
        var (publicKey, privateKey) = crypto.GenerateIdentity();

        // Assert
        publicKey.Should().NotBeNullOrEmpty();
        privateKey.Should().NotBeNullOrEmpty();
        publicKey.Should().NotBeEquivalentTo(privateKey);
    }

    [Fact]
    public void GenerateIdentity_CreatesUniqueKeys()
    {
        // Arrange
        var crypto1 = new CryptoService();
        var crypto2 = new CryptoService();

        // Act
        var (publicKey1, _) = crypto1.GenerateIdentity();
        var (publicKey2, _) = crypto2.GenerateIdentity();

        // Assert
        publicKey1.Should().NotBeEquivalentTo(publicKey2);
    }

    [Fact]
    public void EncryptDecrypt_Roundtrip_ReturnsOriginalText()
    {
        // Arrange — two parties: Alice (sender) and Bob (recipient)
        var alice = new CryptoService();
        alice.GenerateIdentity(); // Alice needs a key to sign (though encrypt uses ephemeral)

        var bob = new CryptoService();
        var (bobPublicKey, _) = bob.GenerateIdentity();

        var originalMessage = "This is a secret message";

        // Act — Alice encrypts for Bob
        var encryptedPayload = alice.Encrypt(originalMessage, bobPublicKey);

        // Bob decrypts
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            EncryptedPayload = encryptedPayload,
            SenderHash = "alice",
            DestinationHash = "bob",
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var alicePublicKey = new byte[] { 1 }; // Not used in decryption, but required by interface
        var decryptedMessage = bob.Decrypt(packet, alicePublicKey);

        // Assert
        decryptedMessage.Should().Be(originalMessage);
    }

    [Fact]
    public void Encrypt_DifferentNoncesEachTime()
    {
        // Arrange
        var crypto = new CryptoService();
        crypto.GenerateIdentity();

        var recipient = new CryptoService();
        var (recipientPubKey, _) = recipient.GenerateIdentity();

        var message = "Same message";

        // Act
        var encrypted1 = crypto.Encrypt(message, recipientPubKey);
        var encrypted2 = crypto.Encrypt(message, recipientPubKey);

        // Assert — different ciphertexts due to different ephemeral keys and nonces
        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    [Fact]
    public void EncryptDecrypt_SpecialCharacters_RoundTrip()
    {
        // Arrange
        var sender = new CryptoService();
        sender.GenerateIdentity();

        var recipient = new CryptoService();
        var (recipientPubKey, _) = recipient.GenerateIdentity();

        var messageWithSpecialChars = "Hello 世界 مرحبا мир";

        // Act
        var encrypted = sender.Encrypt(messageWithSpecialChars, recipientPubKey);
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            EncryptedPayload = encrypted,
            SenderHash = "sender",
            DestinationHash = "recipient",
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var decrypted = recipient.Decrypt(packet, new byte[] { 1 });

        // Assert
        decrypted.Should().Be(messageWithSpecialChars);
    }

    [Fact]
    public void GetPublicKeyHash_ReturnsDeterministicHash()
    {
        // Arrange
        var crypto = new CryptoService();
        var publicKey = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var hash1 = crypto.GetPublicKeyHash(publicKey);
        var hash2 = crypto.GetPublicKeyHash(publicKey);

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrEmpty();
        // Base64url: no +, /, or = characters
        hash1.Should().NotContain("+");
        hash1.Should().NotContain("/");
        hash1.Should().NotContain("=");
    }

    [Fact]
    public void Encrypt_NullPlaintext_ThrowsArgumentException()
    {
        // Arrange
        var crypto = new CryptoService();

        // Act & Assert
        var act = () => crypto.Encrypt(null!, new byte[] { 1, 2, 3 });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrypt_WithoutPrivateKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var crypto = new CryptoService(); // No key set
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            EncryptedPayload = new byte[100],
            SenderHash = "sender",
            DestinationHash = "dest",
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act & Assert
        var act = () => crypto.Decrypt(packet, new byte[] { 1 });
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Private key*");
    }
}
