using System.Security.Cryptography;
using System.Text;

namespace CameraSuite.Shared.Security;

public sealed record AesKeyMaterial(byte[] Key, byte[] Iv)
{
    public static AesKeyMaterial Create()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        return new AesKeyMaterial(key, iv);
    }

    public string ToHexKey() => Convert.ToHexString(Key);

    public string ToHexIv() => Convert.ToHexString(Iv);

    public string ToBase64Key() => Convert.ToBase64String(Key);

    public string ToBase64Iv() => Convert.ToBase64String(Iv);

    public string ToPassphrase() => DerivePassphrase(Key);

    public IDictionary<string, string> ToDictionary() => new Dictionary<string, string>
    {
        ["KeyHex"] = ToHexKey(),
        ["IvHex"] = ToHexIv(),
        ["KeyBase64"] = ToBase64Key(),
        ["IvBase64"] = ToBase64Iv(),
        ["Passphrase"] = ToPassphrase(),
    };

    public override string ToString() => $"Key={ToHexKey()}, Iv={ToHexIv()}";

    public static AesKeyMaterial FromBase64(string keyBase64, string ivBase64)
        => new(Convert.FromBase64String(keyBase64), Convert.FromBase64String(ivBase64));

    public static AesKeyMaterial FromHex(string keyHex, string ivHex)
        => new(Convert.FromHexString(keyHex), Convert.FromHexString(ivHex));

    public static string DerivePassphrase(ReadOnlySpan<byte> key)
    {
        const int length = 32;
        var hex = Convert.ToHexString(key);
        return hex.Length >= length
            ? hex[..length]
            : hex.PadRight(length, '0');
    }
}
