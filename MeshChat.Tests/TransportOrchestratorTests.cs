using Xunit;
using FluentAssertions;
using Moq;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;
using MeshChat.Core.Services;

namespace MeshChat.Tests;

/// <summary>
/// Unit tests for the TransportOrchestrator class.
/// </summary>
public class TransportOrchestratorTests
{
    private readonly Mock<IBleService> _bleServiceMock;
    private readonly Mock<INetworkTransport> _localTransportMock;
    private readonly Mock<INetworkTransport> _relayTransportMock;
    private readonly TransportOrchestrator _orchestrator;

    public TransportOrchestratorTests()
    {
        _bleServiceMock = new Mock<IBleService>();
        _localTransportMock = new Mock<INetworkTransport>();
        _relayTransportMock = new Mock<INetworkTransport>();
        _orchestrator = new TransportOrchestrator(
            _bleServiceMock.Object,
            _localTransportMock.Object,
            _relayTransportMock.Object);
    }

    [Fact]
    public async Task SendPacket_BleAvailable_UsesBle()
    {
        // Arrange
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "peer1",
            SenderHash = "self",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _bleServiceMock.Setup(x => x.IsAvailable).Returns(true);
        _bleServiceMock.Setup(x => x.SendAsync(It.IsAny<MeshPacket>())).Returns(Task.CompletedTask);

        // Act
        await _orchestrator.SendPacket(packet);

        // Assert
        _bleServiceMock.Verify(x => x.SendAsync(packet), Times.Once);
        _localTransportMock.Verify(x => x.SendAsync(It.IsAny<MeshPacket>()), Times.Never);
        _relayTransportMock.Verify(x => x.SendAsync(It.IsAny<MeshPacket>()), Times.Never);
    }

    [Fact]
    public async Task SendPacket_BleFails_FallsToLocalTransport()
    {
        // Arrange
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "peer1",
            SenderHash = "self",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _bleServiceMock.Setup(x => x.IsAvailable).Returns(true);
        _bleServiceMock.Setup(x => x.SendAsync(It.IsAny<MeshPacket>())).ThrowsAsync(new Exception("BLE failed"));

        _localTransportMock.Setup(x => x.IsAvailable).Returns(true);
        _localTransportMock.Setup(x => x.SendAsync(It.IsAny<MeshPacket>())).Returns(Task.CompletedTask);

        // Act
        await _orchestrator.SendPacket(packet);

        // Assert
        _bleServiceMock.Verify(x => x.SendAsync(packet), Times.Once);
        _localTransportMock.Verify(x => x.SendAsync(packet), Times.Once);
        _relayTransportMock.Verify(x => x.SendAsync(It.IsAny<MeshPacket>()), Times.Never);
    }

    [Fact]
    public async Task SendPacket_BleUnavailable_SkipsToBestAvailable()
    {
        // Arrange
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "peer1",
            SenderHash = "self",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _bleServiceMock.Setup(x => x.IsAvailable).Returns(false);
        _localTransportMock.Setup(x => x.IsAvailable).Returns(false);
        _relayTransportMock.Setup(x => x.IsAvailable).Returns(true);
        _relayTransportMock.Setup(x => x.SendAsync(It.IsAny<MeshPacket>())).Returns(Task.CompletedTask);

        // Act
        await _orchestrator.SendPacket(packet);

        // Assert
        _bleServiceMock.Verify(x => x.SendAsync(It.IsAny<MeshPacket>()), Times.Never);
        _localTransportMock.Verify(x => x.SendAsync(It.IsAny<MeshPacket>()), Times.Never);
        _relayTransportMock.Verify(x => x.SendAsync(packet), Times.Once);
    }

    [Fact]
    public async Task SendPacket_NoTransportsAvailable_ThrowsException()
    {
        // Arrange
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "peer1",
            SenderHash = "self",
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _bleServiceMock.Setup(x => x.IsAvailable).Returns(false);
        _localTransportMock.Setup(x => x.IsAvailable).Returns(false);
        _relayTransportMock.Setup(x => x.IsAvailable).Returns(false);

        // Act & Assert
        var act = () => _orchestrator.SendPacket(packet);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No transports*");
    }

    [Fact]
    public void PacketReceived_FromAnyTransport_Unified()
    {
        // Arrange
        _orchestrator.WireUp();

        var receivedPackets = new List<MeshPacket>();
        _orchestrator.PacketReceived += p => receivedPackets.Add(p);

        var blePacket = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "self",
            SenderHash = "peer1",
            EncryptedPayload = new byte[] { 1 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var wifiPacket = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "self",
            SenderHash = "peer2",
            EncryptedPayload = new byte[] { 2 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var relayPacket = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            DestinationHash = "self",
            SenderHash = "peer3",
            EncryptedPayload = new byte[] { 3 },
            Ttl = 5,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act — raise PacketReceived from each transport mock
        _bleServiceMock.Raise(x => x.PacketReceived += null, blePacket);
        _localTransportMock.Raise(x => x.PacketReceived += null, wifiPacket);
        _relayTransportMock.Raise(x => x.PacketReceived += null, relayPacket);

        // Assert
        receivedPackets.Should().HaveCount(3);
        receivedPackets[0].SenderHash.Should().Be("peer1");
        receivedPackets[1].SenderHash.Should().Be("peer2");
        receivedPackets[2].SenderHash.Should().Be("peer3");
    }
}
