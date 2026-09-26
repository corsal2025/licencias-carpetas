using System.Security.Cryptography;
using System.Text;

namespace LicenciasCarpetas.Persistence;

/// <summary>
/// Cifra los respaldos (contienen RUT, nombres, correos y celulares). AES-256-GCM con clave derivada
/// de una frase (PBKDF2-SHA256, sal aleatoria por archivo). Formato: "LCBK1" | sal(16) | nonce(12) |
/// tag(16) | datos. GCM autentica: una clave equivocada o un archivo alterado falla, nunca devuelve
/// basura que parezca una base válida.
/// </summary>
public static class BackupCipher
{
    public const string Extension = ".enc";
    private static readonly byte[] Magic = "LCBK1"u8.ToArray();
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 200_000;

    public static byte[] Encrypt(byte[] plain, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(DeriveKey(passphrase, salt), TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag, Magic);
        }

        return [.. Magic, .. salt, .. nonce, .. tag, .. cipher];
    }

    /// <exception cref="CryptographicException">Clave incorrecta, archivo alterado o no es un respaldo cifrado.</exception>
    public static byte[] Decrypt(byte[] data, string passphrase)
    {
        var header = Magic.Length + SaltSize + NonceSize + TagSize;
        if (data.Length < header || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException("El archivo no es un respaldo cifrado de LicenciasCarpetas.");
        }

        var salt = data.AsSpan(Magic.Length, SaltSize);
        var nonce = data.AsSpan(Magic.Length + SaltSize, NonceSize);
        var tag = data.AsSpan(Magic.Length + SaltSize + NonceSize, TagSize);
        var cipher = data.AsSpan(header);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(DeriveKey(passphrase, salt.ToArray()), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Magic);
        return plain;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
}
