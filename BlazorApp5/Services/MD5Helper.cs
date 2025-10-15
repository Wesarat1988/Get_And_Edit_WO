using System.Security.Cryptography;
using System.Text;

namespace BlazorApp5.Helpers
{
    public static class MD5Helper
    {
        public static string Generate(string input)
        {
            using var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(bytes);
            return string.Concat(hashBytes.Select(b => b.ToString("X2")));
        }
    }
}