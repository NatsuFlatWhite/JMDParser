using System;
using System.Text;

namespace JMDParser
{
    public static class JmdCrypto
    {
        public static uint Adler32(byte[] data, uint seed = 0)
        {
            uint a = seed & 0xFFFF;
            uint b = (seed >> 16) & 0xFFFF;
            const uint MOD_ADLER = 65521;

            int len = data.Length;
            int index = 0;

            while (len > 0)
            {
                int k = len < 5552 ? len : 5552;
                len -= k;
                for (int i = 0; i < k; i++)
                {
                    a += data[index++];
                    b += a;
                }
                a %= MOD_ADLER;
                b %= MOD_ADLER;
            }

            return (b << 16) | a;
        }

        public static uint GetJmdKey(string fileName)
        {
            byte[] stringData = Encoding.Unicode.GetBytes(fileName);
            uint adler = Adler32(stringData, 0);
            return adler + 0x3de90dc3;
        }

        public static uint GetDirectoryDataKey(uint jmdKey)
        {
            return jmdKey - 0x41014EBF;
        }

        public static uint GetFileKey(uint jmdKey, string fileNameWithoutExt, uint extNum)
        {
            byte[] strData = Encoding.Unicode.GetBytes(fileNameWithoutExt);
            uint key = Adler32(strData, 0);
            key += extNum;
            key += (jmdKey - 0x7E2AF33D);
            return key;
        }

        public static byte[] ExtendKey(uint originalKey)
        {
            byte[] outArray = new byte[64];
            uint curData = originalKey ^ 0x8473FBC1;

            for (int i = 0; i < 16; i++)
            {
                byte[] bytes = BitConverter.GetBytes(curData);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                Array.Copy(bytes, 0, outArray, i * 4, 4);
                curData -= 0x7B8C043F;
            }

            return outArray;
        }

        public static byte[] ProcessData(uint key, byte[] data)
        {
            byte[] extendedKey = ExtendKey(key);
            byte[] output = new byte[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                output[i] = (byte)(data[i] ^ extendedKey[i & 63]);
            }

            return output;
        }

        public static byte[] ProcessDataInfo(byte[] key, byte[] data)
        {
            if (data.Length != 32)
                throw new ArgumentException("Data length must be 32 bytes");

            byte[] output = new byte[32];
            for (int i = 0; i < 32; i++)
            {
                output[i] = (byte)(data[i] ^ key[i]);
            }

            return output;
        }
    }
}
