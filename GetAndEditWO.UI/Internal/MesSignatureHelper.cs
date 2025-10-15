using System.Security.Cryptography;
using System.Text;

namespace GetAndEditWO.UI.Internal;

internal static class MesSignatureHelper
{
    public static string CreateSignature(string? secretKey, string message)
    {
        var payload = string.Concat(secretKey ?? string.Empty, message ?? string.Empty);
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hashBytes = md5.ComputeHash(bytes);
        return ConvertToHex(hashBytes);
    }

    private static string ConvertToHex(byte[] hashBytes)
    {
        var builder = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            builder.Append(b.ToString("X2"));
        }

        return builder.ToString();
    }
}
