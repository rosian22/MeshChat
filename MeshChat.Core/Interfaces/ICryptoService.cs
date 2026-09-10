namespace MeshChat.Core.Interfaces;

using MeshChat.Core.Models;

/// <summary>
/// Service for cryptographic operations including key generation, encryption, and decryption.
/// </summary>
public interface ICryptoService
{
    /// <summary>
    /// Generates a new ECDH P-256 key pair for identity.
    /// </summary>
    /// <returns>A tuple containing the public key and private key bytes.</returns>
    (byte[] publicKey, byte[] privateKey) GenerateIdentity();

    /// <summary>
    /// Encrypts plaintext for a recipient using their public key.
    /// </summary>
    /// <param name="plaintext">The plaintext to encrypt.</param>
    /// <param name="recipientPublicKey">The recipient's public key.</param>
    /// <returns>The encrypted ciphertext.</returns>
    byte[] Encrypt(string plaintext, byte[] recipientPublicKey);

    /// <summary>
    /// Decrypts a mesh packet using the sender's public key.
    /// </summary>
    /// <param name="packet">The mesh packet containing encrypted payload.</param>
    /// <param name="senderPublicKey">The sender's public key.</param>
    /// <returns>The decrypted plaintext.</returns>
    string Decrypt(MeshPacket packet, byte[] senderPublicKey);

    /// <summary>
    /// Computes the hash of a public key for use as a peer ID.
    /// </summary>
    /// <param name="publicKey">The public key to hash.</param>
    /// <returns>Base64url encoded SHA256 hash of the public key.</returns>
    string GetPublicKeyHash(byte[] publicKey);
}
