using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Sleepy.Security
{
    public static class AESEncryption
    {
        static readonly int _saltSize = 32;

        // ===================== Encrypt/Decrypt ==========================

        /// <summary>
        /// Encrypts the plainText input using the given Key.
        /// A 128 bit random salt will be generated and prepended to the ciphertext before it is base64 encoded.
        /// </summary>
        /// <param name="plainText">The plain text to encrypt.</param>
        /// <param name="key">The plain text encryption key.</param>
        /// <returns>The salt and the ciphertext, Base64 encoded for convenience.</returns>
        public static byte[] Encrypt(byte[] data, string key)
        {
            // Derive a new Salt and IV from the Key
            using (Rfc2898DeriveBytes keyDerivationFunction = new Rfc2898DeriveBytes(key, _saltSize))
            {
                byte[] saltBytes = keyDerivationFunction.Salt;
                byte[] keyBytes = keyDerivationFunction.GetBytes(32);
                byte[] ivBytes = keyDerivationFunction.GetBytes(16);

                using (AesManaged aesManaged = new AesManaged())
                {
                    //aesManaged.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform encryptor = aesManaged.CreateEncryptor(keyBytes, ivBytes))
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                        using (BinaryWriter streamWriter = new BinaryWriter(cryptoStream))
                        {
                            streamWriter.Write(data);
                        }

                        byte[] cipherBytes = memoryStream.ToArray();
                        Array.Resize(ref saltBytes, saltBytes.Length + cipherBytes.Length);
                        Array.Copy(cipherBytes, 0, saltBytes, _saltSize, cipherBytes.Length);

                        return saltBytes;
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts the ciphertext using the Key.
        /// </summary>
        /// <param name="ciphertext">The ciphertext to decrypt.</param>
        /// <param name="key">The plain text encryption key.</param>
        /// <returns>The decrypted text.</returns>
        public static byte[] Decrypt(byte[] data, string key)
        {
            // Extract the salt from our ciphertext
            byte[] saltBytes = data.Take(_saltSize).ToArray();
            byte[] ciphertextBytes = data.Skip(_saltSize).Take(data.Length - _saltSize).ToArray();

            using (Rfc2898DeriveBytes keyDerivationFunction = new Rfc2898DeriveBytes(key, saltBytes))
            {
                // Derive the previous IV from the Key and Salt
                byte[] keyBytes = keyDerivationFunction.GetBytes(32);
                byte[] ivBytes = keyDerivationFunction.GetBytes(16);

                // Create a decrytor to perform the stream transform.
                // Create the streams used for decryption.
                // The default Cipher Mode is CBC and the Padding is PKCS7 which are both good
                using (AesManaged aesManaged = new AesManaged())
                {
                    //aesManaged.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aesManaged.CreateDecryptor(keyBytes, ivBytes))
                    using (MemoryStream memoryStream = new MemoryStream(ciphertextBytes))
                    using (CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                    using (BinaryReader streamReader = new BinaryReader(cryptoStream))
                    {
                        return streamReader.ReadBytes((int)memoryStream.Length);
                    }
                }
                
            }
        }

        // ====================== Strings ============================

        /// <summary>
        /// Encrypts the plainText input using the given Key.
        /// A 128 bit random salt will be generated and prepended to the ciphertext before it is base64 encoded.
        /// </summary>
        /// <param name="plainText">The plain text to encrypt.</param>
        /// <param name="key">The plain text encryption key.</param>
        /// <returns>The salt and the ciphertext, Base64 encoded for convenience.</returns>
        public static string EncryptString(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException("plainText");
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException("key");

            // Derive a new Salt and IV from the Key
            using (Rfc2898DeriveBytes keyDerivationFunction = new Rfc2898DeriveBytes(key, _saltSize))
            {
                byte[] saltBytes = keyDerivationFunction.Salt;
                byte[] keyBytes = keyDerivationFunction.GetBytes(32);
                byte[] ivBytes = keyDerivationFunction.GetBytes(16);

                // Create an encryptor to perform the stream transform.
                // Create the streams used for encryption.
                using (AesManaged aesManaged = new AesManaged())
                using (ICryptoTransform encryptor = aesManaged.CreateEncryptor(keyBytes, ivBytes))
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter streamWriter = new StreamWriter(cryptoStream))
                    {
                        // Send the data through the StreamWriter, through the CryptoStream, to the underlying MemoryStream
                        streamWriter.Write(plainText);
                    }

                    // Return the encrypted bytes from the memory stream, in Base64 form so we can send it right to a database (if we want).
                    byte[] cipherTextBytes = memoryStream.ToArray();
                    Array.Resize(ref saltBytes, saltBytes.Length + cipherTextBytes.Length);
                    Array.Copy(cipherTextBytes, 0, saltBytes, _saltSize, cipherTextBytes.Length);

                    return Convert.ToBase64String(saltBytes);
                }
            }
        }

        /// <summary>
        /// Decrypts the ciphertext using the Key.
        /// </summary>
        /// <param name="ciphertext">The ciphertext to decrypt.</param>
        /// <param name="key">The plain text encryption key.</param>
        /// <returns>The decrypted text.</returns>
        public static string DecryptString(string ciphertext, string key)
        {
            if (string.IsNullOrEmpty(ciphertext))
                throw new ArgumentNullException("cipherText");
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException("key");

            // Extract the salt from our ciphertext
            byte[] allTheBytes = Convert.FromBase64String(ciphertext);
            byte[] saltBytes = allTheBytes.Take(_saltSize).ToArray();
            byte[] ciphertextBytes = allTheBytes.Skip(_saltSize).Take(allTheBytes.Length - _saltSize).ToArray();

            using (Rfc2898DeriveBytes keyDerivationFunction = new Rfc2898DeriveBytes(key, saltBytes))
            {
                // Derive the previous IV from the Key and Salt
                byte[] keyBytes = keyDerivationFunction.GetBytes(32);
                byte[] ivBytes = keyDerivationFunction.GetBytes(16);
                
                // Create a decrytor to perform the stream transform.
                // Create the streams used for decryption.
                // The default Cipher Mode is CBC and the Padding is PKCS7 which are both good
                using (AesManaged aesManaged = new AesManaged())
                using (ICryptoTransform decryptor = aesManaged.CreateDecryptor(keyBytes, ivBytes))
                using (MemoryStream memoryStream = new MemoryStream(ciphertextBytes))
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                using (StreamReader streamReader = new StreamReader(cryptoStream))
                {
                    // Return the decrypted bytes from the decrypting stream.
                    return streamReader.ReadToEnd();
                }
            }
        }
    }
}