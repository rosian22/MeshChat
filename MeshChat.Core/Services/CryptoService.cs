namespace MeshChat.Core.Services;

using System.Security.Cryptography;
using System.Text;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// Cryptographic service implementation using System.Security.Cryptography.
/// Uses ECDH P-256 for key agreement and AES-256-GCM for authenticated encryption.
/// </summary>
public class CryptoService : ICryptoService
{
    private byte[]? _privateKey;

    /// <summary>
    /// Initializes a new instance of the CryptoService class.
    /// </summary>
    public CryptoService()
    {
    }

    /// <summary>
    /// Initializes a new instance of the CryptoService class with an existing private key.
    /// </summary>
    /// <param name="privateKey">The PKCS#8 private key bytes to use.</param>
    public CryptoService(byte[] privateKey)
    {
        _privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
    }

    /// <summary>
    /// Sets the private key for this service instance.
    /// </summary>
    /// <param name="privateKey">The PKCS#8 private key bytes.</param>
    public void SetPrivateKey(byte[] privateKey)
    {
        _privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
    }

    /// <inheritdoc />
    public (byte[] publicKey, byte[] privateKey) GenerateIdentity()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var publicKeyBytes = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        var privateKeyBytes = ecdh.ExportPkcs8PrivateKey();

        _privateKey = privateKeyBytes;

        return (publicKeyBytes, privateKeyBytes);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Output format: ephemeralPublicKey(91 bytes for P-256 SPKI) + nonce(12) + tag(16) + ciphertext.
    /// The first 4 bytes encode the length of the ephemeral public key.
    /// Full format: [2-byte epk length][ephemeral public key][12-byte nonce][16-byte tag][ciphertext]
    /// </remarks>
    public byte[] Encrypt(string plaintext, byte[] recipientPublicKey)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Plaintext cannot be null or empty.", nameof(plaintext));
        if (recipientPublicKey == null || recipientPublicKey.Length == 0)
            throw new ArgumentException("Recipient public key cannot be null or empty.", nameof(recipientPublicKey));

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        // Generate ephemeral ECDH key pair for forward secrecy
        using var ephemeralEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ephemeralPublicKey = ephemeralEcdh.PublicKey.ExportSubjectPublicKeyInfo();

        // Import recipient's public key and derive shared secret
        using var recipientEcdh = ECDiffieHellman.Create();
        recipientEcdh.ImportSubjectPublicKeyInfo(recipientPublicKey, out _);

        var sharedSecret = ephemeralEcdh.DeriveKeyFromHash(
            recipientEcdh.PublicKey,
            HashAlgorithmName.SHA256);

        // Use first 32 bytes of derived key for AES-256
        var aesKey = sharedSecret.AsSpan(0, 32).ToArray();

        // AES-256-GCM encryption
        using var aes = new AesGcm(aesKey, 16);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Output: [2-byte epk length][ephemeral public key][nonce][tag][ciphertext]
        var epkLength = (ushort)ephemeralPublicKey.Length;
        var result = new byte[2 + ephemeralPublicKey.Length + 12 + 16 + ciphertext.Length];

        // Write ephemeral public key length (little-endian)
        result[0] = (byte)(epkLength & 0xFF);
        result[1] = (byte)((epkLength >> 8) & 0xFF);

        var offset = 2;
        Buffer.BlockCopy(ephemeralPublicKey, 0, result, offset, ephemeralPublicKey.Length);
        offset += ephemeralPublicKey.Length;
        Buffer.BlockCopy(nonce, 0, result, offset, 12);
        offset += 12;
        Buffer.BlockCopy(tag, 0, result, offset, 16);
        offset += 16;
        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);

        return result;
    }

    /// <inheritdoc />
    public string Decrypt(MeshPacket packet, byte[] senderPublicKey)
    {
        if (packet == null)
            throw new ArgumentNullException(nameof(packet));
        if (packet.EncryptedPayload == null || packet.EncryptedPayload.Length < 2)
            throw new ArgumentException("Encrypted payload is too short.", nameof(packet));
        if (_privateKey == null)
            throw new InvalidOperationException("Private key not set. Call SetPrivateKey or GenerateIdentity first.");

        // Note: senderPublicKey is not used for decryption — the ephemeral key is embedded in the payload.
        // The parameter exists so the caller can verify the sender's identity separately.

        var payload = packet.EncryptedPayload;

        // Read ephemeral public key length
        var epkLength = (ushort)(payload[0] | (payload[1] << 8));
        if (payload.Length < 2 + epkLength + 12 + 16)
            throw new ArgumentException("Encrypted payload is malformed.", nameof(packet));

        // Extract ephemeral public key
        var ephemeralPublicKey = new byte[epkLength];
        Buffer.BlockCopy(payload, 2, ephemeralPublicKey, 0, epkLength);

        var offset = 2 + epkLength;

        // Extract nonce, tag, ciphertext
        var nonce = new byte[12];
        Buffer.BlockCopy(payload, offset, nonce, 0, 12);
        offset += 12;

        var tag = new byte[16];
        Buffer.BlockCopy(payload, offset, tag, 0, 16);
        offset += 16;

        var ciphertext = new byte[payload.Length - offset];
        Buffer.BlockCopy(payload, offset, ciphertext, 0, ciphertext.Length);

        // Load our private key and derive shared secret with ephemeral public key
        using var ecdh = ECDiffieHellman.Create();
        ecdh.ImportPkcs8PrivateKey(_privateKey, out _);

        using var ephemeralEcdhPub = ECDiffieHellman.Create();
        ephemeralEcdhPub.ImportSubjectPublicKeyInfo(ephemeralPublicKey, out _);

        var sharedSecret = ecdh.DeriveKeyFromHash(
            ephemeralEcdhPub.PublicKey,
            HashAlgorithmName.SHA256);

        var aesKey = sharedSecret.AsSpan(0, 32).ToArray();

        // Decrypt
        using var aes = new AesGcm(aesKey, 16);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <inheritdoc />
    public string GetPublicKeyHash(byte[] publicKey)
    {
        if (publicKey == null || publicKey.Length == 0)
            throw new ArgumentException("Public key cannot be null or empty.", nameof(publicKey));

        var hash = SHA256.HashData(publicKey);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
