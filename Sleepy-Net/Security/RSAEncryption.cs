using System;
using System.IO;
using System.Security.Cryptography;

namespace Sleepy.Security
{
    public static class RSAEncryption
    {
        /// <summary>
        /// RSA Encyption class, capable of storing keys, encypting or decrypting data in a contained way.
        /// </summary>
        public class RSAKeys
        {
            //readonly RSACryptoServiceProvider RSA; 

            RSAParameters _Public;
            public RSAParameters Public 
            { 
                get { return _Public; } 
                set { _Public = value; _PublicBytes = null; } 
            }

            RSAParameters _Private;
            public RSAParameters Private 
            { 
                get { return _Private; } 
                set { _Private = value; _PrivateBytes = null; } 
            }

            byte[] _PublicBytes;
            public byte[] PublicBytes
            {
                get { if (_PublicBytes == null) _PublicBytes = KeyToBytes(Public); return _PublicBytes; }
                set { Public = BytesToKey(value); _PublicBytes = value; }
            }

            byte[] _PrivateBytes;
            public byte[] PrivateBytes
            {
                get { if (_PrivateBytes == null) _PrivateBytes = KeyToBytes(Public); return _PrivateBytes; }
                set { Public = BytesToKey(value); _PrivateBytes = value; }
            }

            // ===================== Constructor ===========================
            
            public RSAKeys() { }

            public RSAKeys(RSAParameters? _public = null, RSAParameters? _private = null)
            {
                if (_public != null) _Public = (RSAParameters)_public;
                if (_private != null) _Private = (RSAParameters)_private;


                //if (_public != null && _private == null)
                //{
                //    RSA = new RSACryptoServiceProvider();
                //    RSA.ImportParameters(_Public);
                //}
                //else if (_private != null)
                //{
                //    RSA = new RSACryptoServiceProvider();
                //    RSA.ImportParameters(_Private);
                //}
            }

            public RSAKeys(byte[] _public = null, byte[] _private = null)
            {
                if (_public != null) _Public = BytesToKey(_public);
                if (_private != null) _Private = BytesToKey(_private);


                //if (_public != null && _private == null)
                //{
                //    RSA = new RSACryptoServiceProvider();
                //    RSA.ImportParameters(_Public);
                //}
                //else if (_private != null)
                //{
                //    RSA = new RSACryptoServiceProvider();
                //    RSA.ImportParameters(_Private);
                //}
            }

            //public RSAKeys(RSACryptoServiceProvider rsa)
            //{
            //    _Public = rsa.ExportParameters(false);
            //    _Private = rsa.ExportParameters(true);
            //    RSA = rsa;
            //}

            public static RSAKeys Generate(int keySize)
            {
                using (RSACryptoServiceProvider RSA = new RSACryptoServiceProvider(keySize))
                {
                    return new RSAKeys(RSA.ExportParameters(false), RSA.ExportParameters(true));
                }
            }

            // ====================== Encrypt/Decrypt ============================

            /// <summary>
            /// Encypt some data. This requires this object to have a public key.
            /// </summary>
            /// <param name="data">Data to encypt</param>
            /// <returns>Encypted Data</returns>
            public byte[] Encrypt(byte[] data)
            {
                return RSAEncryption.Encrypt(data, _Public);
            }

            /// <summary>
            /// Decrypt some data. This requires this object to have the private key for the data.
            /// </summary>
            /// <param name="data">Data to decrypt</param>
            /// <returns>Decrypted Data</returns>
            public byte[] Decrypt(byte[] data)
            {
                return RSAEncryption.Decrypt(data, _Private);
            }

            // ======================== Conversion ===============================

            /// <summary>
            /// Convert a RSA Key into bytes for transport.
            /// </summary>
            public static byte[] KeyToBytes(RSAParameters key)
            {
                using (MemoryStream mem = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(mem))
                    {
                        writer.Write(key.D != null ? key.D.Length : 0);
                        if (key.D != null) writer.Write(key.D);

                        writer.Write(key.DP != null ? key.DP.Length : 0);
                        if (key.DP != null) writer.Write(key.DP);

                        writer.Write(key.DQ != null ? key.DQ.Length : 0);
                        if (key.DQ != null) writer.Write(key.DQ);

                        writer.Write(key.Exponent != null ? key.Exponent.Length : 0);
                        if (key.Exponent != null) writer.Write(key.Exponent);

                        writer.Write(key.InverseQ != null ? key.InverseQ.Length : 0);
                        if (key.InverseQ != null) writer.Write(key.InverseQ);

                        writer.Write(key.Modulus != null ? key.Modulus.Length : 0);
                        if (key.Modulus != null) writer.Write(key.Modulus);

                        writer.Write(key.P != null ? key.P.Length : 0);
                        if (key.P != null) writer.Write(key.P);

                        writer.Write(key.Q != null ? key.Q.Length : 0);
                        if (key.Q != null) writer.Write(key.Q);
                    }
                    return mem.ToArray();
                }
            }

            /// <summary>
            /// Convert bytes into an RSA key.
            /// </summary>
            public static RSAParameters BytesToKey(byte[] key)
            {
                using (MemoryStream mem = new MemoryStream(key))
                {
                    using (BinaryReader reader = new BinaryReader(mem))
                    {
                        RSAParameters RSAKey = new RSAParameters();

                        int DLen = reader.ReadInt32();
                        if (DLen > 0) RSAKey.D = reader.ReadBytes(DLen);

                        int DPLen = reader.ReadInt32();
                        if (DPLen > 0) RSAKey.DP = reader.ReadBytes(DPLen);

                        int DQLen = reader.ReadInt32();
                        if (DQLen > 0) RSAKey.DQ = reader.ReadBytes(DQLen);

                        int EXLen = reader.ReadInt32();
                        if (EXLen > 0) RSAKey.Exponent = reader.ReadBytes(EXLen);

                        int IQLen = reader.ReadInt32();
                        if (IQLen > 0) RSAKey.InverseQ = reader.ReadBytes(IQLen);

                        int ModLen = reader.ReadInt32();
                        if (ModLen > 0) RSAKey.Modulus = reader.ReadBytes(ModLen);

                        int PLen = reader.ReadInt32();
                        if (PLen > 0) RSAKey.P = reader.ReadBytes(PLen);

                        int QLen = reader.ReadInt32();
                        if (QLen > 0) RSAKey.Q = reader.ReadBytes(QLen);

                        return RSAKey;
                    }
                }
            }
        }

        public static RSAKeys GenerateKeys(int keySize) => RSAKeys.Generate(keySize);

        // ======================= Global Encypt/Decrypt =======================

        static readonly RSACryptoServiceProvider GloablRSA = new RSACryptoServiceProvider();


        /// <summary>
        /// Encrypt some data with the Provided public key. Using RSA
        /// </summary>
        /// <param name="data">Data to encypt</param>
        /// <param name="publicKey">Public Key to encypt with</param>
        /// <returns>Byte[] | Encrypted Data</returns>
        public static byte[] Encrypt(byte[] data, RSAParameters publicKey)
        {
            GloablRSA.ImportParameters(publicKey);
            return GloablRSA.Encrypt(data, false);
        }

        /// <summary>
        /// Decrypt some data with the provided private key. Using RSA
        /// </summary>
        /// <param name="data">Data to Decrypt</param>
        /// <param name="privateKey">Private Key to encypt with</param>
        /// <returns>Byte[] | Decrypted Data</returns>
        public static byte[] Decrypt(byte[] data, RSAParameters privateKey)
        {
            GloablRSA.ImportParameters(privateKey);
            return GloablRSA.Decrypt(data, false);
        }
    }
}