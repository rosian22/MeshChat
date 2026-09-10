using Xunit;
using FluentAssertions;
using Moq;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;
using MeshChat.Core.Services;

namespace MeshChat.Tests;

/// <summary>
/// Unit tests for the MeshRouter class.
/// </summary>
public class MeshRouterTests : IDisposable
{
    private readonly Mock<ICryptoService> _cryptoServiceMock;
    private readonly Mock<IMessageStore> _messageStoreMock;
    private readonly Mock<IPeerStore> _peerStoreMock;
    private readonly MeshRouter _router;
    private const string MyHash = "my-hash-abc123";

    public MeshRouterTests()
    {
        _cryptoServiceMock = new Mock<ICryptoService>();
        _messageStoreMock = new Mock<IMessageStore>();
        _peerStoreMock = new Mock<IPeerStore>();
        _router = new MeshRouter(
            _cryptoServiceMock.Object,
            _messageStoreMock.Object,
            _peerStoreMock.Object,
            MyHash);
    }

    public void Dispose()
    {
        _router.Stop();
    }

    [Fact]
    public async Task OnPacketReceived_ForMe_DecryptsAndRaisesEvent()
    {
        // Arrange
        var senderId = "sender-hash";
        var decryptedContent = "Hello, World!";
        var senderPublicKey = new byte[] { 1, 2, 3 };

        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = MyHash,
            SenderHash = senderId,
            EncryptedPayload = new byte[] { 10, 20, 30 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var senderPeer = new Peer
        {
            Id = senderId,
            DisplayName = "Sender",
            PublicKey = senderPublicKey
        };

        _peerStoreMock
            .Setup(x => x.GetById(senderId))
            .ReturnsAsync(senderPeer);

        _cryptoServiceMock
            .Setup(x => x.Decrypt(It.IsAny<MeshPacket>(), senderPublicKey))
            .Returns(decryptedContent);

        Message? receivedMessage = null;
        _router.MessageReceived += msg => receivedMessage = msg;

        // Act
        await _router.OnPacketReceived(packet);

        // Assert
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Content.Should().Be(decryptedContent);
        receivedMessage.SenderId.Should().Be(senderId);
        receivedMessage.Status.Should().Be(MessageStatus.Delivered);

        _messageStoreMock.Verify(x => x.Save(It.Is<Message>(m => m.Content == decryptedContent)), Times.Once);
    }

    [Fact]
    public async Task OnPacketReceived_NotForMe_RelaysWithDecrementedTtl()
    {
        // Arrange
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "someone-else",
            SenderHash = "peer1",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        MeshPacket? relayedPacket = null;
        _router.RelayRequested += p => relayedPacket = p;

        // Act
        await _router.OnPacketReceived(packet);

        // Assert
        relayedPacket.Should().NotBeNull();
        relayedPacket!.Ttl.Should().Be(4);
        relayedPacket.MessageId.Should().Be(packet.MessageId);
        relayedPacket.DestinationHash.Should().Be("someone-else");
    }

    [Fact]
    public async Task OnPacketReceived_DuplicatePacket_Ignored()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var packet = new MeshPacket
        {
            MessageId = messageId,
            DestinationHash = "someone-else",
            SenderHash = "peer1",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var relayCount = 0;
        _router.RelayRequested += _ => relayCount++;

        // Act
        await _router.OnPacketReceived(packet);
        await _router.OnPacketReceived(packet); // Same packet again

        // Assert
        relayCount.Should().Be(1); // Should only relay once
    }

    [Fact]
    public async Task OnPacketReceived_TtlExpired_NotRelayed()
    {
        // Arrange
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "someone-else",
            SenderHash = "peer1",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 0, // TTL expired
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        MeshPacket? relayedPacket = null;
        _router.RelayRequested += p => relayedPacket = p;

        // Act
        await _router.OnPacketReceived(packet);

        // Assert
        relayedPacket.Should().BeNull();
    }

    [Fact]
    public void CreatePacket_SetsCorrectFields()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var destinationHash = "peer1-hash";
        var content = "Test message";
        var recipientPublicKey = new byte[] { 1, 2, 3, 4 };
        var encryptedPayload = new byte[] { 99, 88, 77 };

        _cryptoServiceMock
            .Setup(x => x.Encrypt(content, recipientPublicKey))
            .Returns(encryptedPayload);

        // Act
        var packet = _router.CreatePacket(messageId, destinationHash, content, recipientPublicKey);

        // Assert
        packet.Should().NotBeNull();
        packet.MessageId.Should().Be(messageId);
        packet.DestinationHash.Should().Be(destinationHash);
        packet.SenderHash.Should().Be(MyHash);
        packet.EncryptedPayload.Should().BeEquivalentTo(encryptedPayload);
        packet.Ttl.Should().Be(5); // PacketTtlMax
        packet.Timestamp.Should().BeGreaterThan(0);
    }
}
