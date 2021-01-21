using System.Security.Cryptography;
using System.Text;

namespace Matterbridge
{
    public class Identicon
    {
        public static string GenerateUrl(string username)
        {
            string usernameHash = GetHashString(username);
            return $"https://identicon-api.herokuapp.com/{usernameHash}/256?format=png";
        }
        private static byte[] GetHash(string inputString)
        {
            using (HashAlgorithm algorithm = SHA256.Create())
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }

        private static string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in GetHash(inputString))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }
    }
    
}