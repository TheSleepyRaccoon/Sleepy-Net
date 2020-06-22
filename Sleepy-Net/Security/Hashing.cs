using System;
using System.Security.Cryptography;
using UnityEngine;

namespace Sleepy.Security
{
    /// <summary>
    /// Custom Hashing algorithums for passwords. Hash ~12ms, Verify ~8ms
    /// </summary>
    public static class Hasher
    {
        const int SaltSize = 16;
        const int HashSize = 20;
        const string HashVersion = "$ZHV1$";

        public static string Hash(string password)
        {
            byte[] salt;
            byte[] hashBytes = new byte[SaltSize + HashSize];

            new RNGCryptoServiceProvider().GetBytes(salt = new byte[SaltSize]);
            int iterations = SaltToIteration(salt);
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(new Rfc2898DeriveBytes(password, salt, iterations).GetBytes(HashSize), 0, hashBytes, SaltSize, HashSize);

            return HashVersion + Convert.ToBase64String(hashBytes);
        }

        public static bool Verify(string password, string hashedPassword)
        {
            if (!hashedPassword.Contains(HashVersion)) return false;
            byte[] RawHash = Convert.FromBase64String(hashedPassword.Replace(HashVersion, ""));

            byte[] salt = RawHash.SubArray(0, SaltSize);
            byte[] hashBytes = RawHash.SubArray(SaltSize, HashSize);
            int iterations = SaltToIteration(salt);

            byte[] hash = new Rfc2898DeriveBytes(password, salt, iterations).GetBytes(HashSize);

            for (int i = 0; i < HashSize; ++i) if (hashBytes[i] != hash[i]) return false;
            return true;
        }

        static int SaltToIteration(byte[] salt)
        {
            int i = 0;
            for (int i1 = 0; i1 < salt.Length; ++i1) i += salt[i1];
            return i /= 2;
        }

        public static byte[] SubArray(this byte[] data, int index, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

    }

    // Test Of Hasher
    //public class PasswordHashing
    //{
    //    public PasswordHashing(string Password)
    //    {
    //        System.Diagnostics.Stopwatch s = new System.Diagnostics.Stopwatch();
    //        s.Start();
    //        string hash = Hasher.Hash(Password);
    //        s.Stop();
    //        Log.WriteNow("Hashed In: " + s.ElapsedMilliseconds + "ms");
    //        Debug.Log(hash);
    //        bool correct = Hasher.Verify("ihsdfhgsidgfsdf", hash);
    //        Debug.Log(correct);
    //        correct = Hasher.Verify(Password, hash);
    //        Debug.Log(correct);
    //
    //        s = new System.Diagnostics.Stopwatch();
    //        s.Start();
    //        hash = Hasher.Hash(Password);
    //        s.Stop();
    //        Log.WriteNow("Hashed In: " + s.ElapsedMilliseconds + "ms");
    //        Debug.Log(hash);
    //        correct = Hasher.Verify("ihsdfhgsidgfsdf", hash);
    //        Debug.Log(correct);
    //        correct = Hasher.Verify(Password, hash);
    //        Debug.Log(correct);
    //    }
    //}
}