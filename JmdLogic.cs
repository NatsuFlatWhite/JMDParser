using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace JMDParser
{
    public enum JmdDataInfoProperty : uint
    {
        None = 0,
        Compressed = 2,
        PartialEncrypted = 4,
        FullEncrypted = 5,
        CompressedEncrypted = 7
    }

    public class JmdDataInfo
    {
        public uint Index { get; set; }
        public long Offset { get; set; }
        public int DataSize { get; set; }
        public int UncompressedSize { get; set; }
        public JmdDataInfoProperty BlockProperty { get; set; }
        public uint Checksum { get; set; }
    }

    public class JmdFile
    {
        public string Name { get; set; }
        public uint PropertyValue { get; set; }
        public int Size { get; set; }
        public uint DataIndex { get; set; }
        public uint Key { get; set; }

        public JmdFile(string name, uint propVal)
        {
            Name = name;
            PropertyValue = propVal;
        }
    }

    public class JmdFolder
    {
        public string Name { get; set; }
        public JmdFolder Parent { get; set; }
        public List<JmdFile> Files { get; set; } = new List<JmdFile>();
        public List<JmdFolder> Folders { get; set; } = new List<JmdFolder>();

        public JmdFolder(string name, JmdFolder parent = null)
        {
            Name = name;
            Parent = parent;
        }
    }

    public class JmdLogic : IDisposable
    {
        public JmdFolder RootFolder { get; private set; }
        private readonly Dictionary<uint, JmdDataInfo> _dataInfoMap = new Dictionary<uint, JmdDataInfo>();
        private FileStream _fs;
        private uint _jmdKey;

        public JmdLogic()
        {
            RootFolder = new JmdFolder("__ROOT__");
        }

        public void Open(string jmdPath)
        {
            string jmdFileNameNoExt = Path.GetFileNameWithoutExtension(jmdPath);
            _jmdKey = JmdCrypto.GetJmdKey(jmdFileNameNoExt);

            _fs = new FileStream(jmdPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            try
            {
                _fs.Seek(0x80, SeekOrigin.Begin);
                byte[] encryptedHeader = new byte[0x80];
                int readBytes = _fs.Read(encryptedHeader, 0, 0x80);
                if (readBytes != 0x80)
                    throw new Exception("Failed to read JMD encrypted header.");

                byte[] decryptedHeader = JmdCrypto.ProcessData(_jmdKey, encryptedHeader);

                uint infoChecksum = BitConverter.ToUInt32(decryptedHeader, 0);

                byte[] headerPayload = new byte[decryptedHeader.Length - 4];
                Buffer.BlockCopy(decryptedHeader, 4, headerPayload, 0, headerPayload.Length);
                uint verifyChecksum = JmdCrypto.Adler32(headerPayload, 0);

                if (infoChecksum != verifyChecksum)
                    throw new Exception("JMD header checksum mismatch!!!!");

                int dataInfoCount = BitConverter.ToInt32(decryptedHeader, 8);
                byte[] dataInfoKey = new byte[32];
                Buffer.BlockCopy(decryptedHeader, 16, dataInfoKey, 0, 32);

                _fs.Seek(0x100, SeekOrigin.Begin);
                for (int i = 0; i < dataInfoCount; i++)
                {
                    byte[] encryptedInfo = new byte[0x20];
                    if (_fs.Read(encryptedInfo, 0, 0x20) != 0x20)
                        throw new Exception("Failed to read encrypted data info.");

                    byte[] decryptedInfo = JmdCrypto.ProcessDataInfo(dataInfoKey, encryptedInfo);

                    uint index = BitConverter.ToUInt32(decryptedInfo, 0);
                    int offsetShifted = BitConverter.ToInt32(decryptedInfo, 4);
                    int dataSize = BitConverter.ToInt32(decryptedInfo, 8);
                    int uncompressedSize = BitConverter.ToInt32(decryptedInfo, 12);
                    uint propVal = BitConverter.ToUInt32(decryptedInfo, 16);
                    uint checksum = BitConverter.ToUInt32(decryptedInfo, 20);

                    var info = new JmdDataInfo
                    {
                        Index = index,
                        Offset = (long)offsetShifted << 8,
                        DataSize = dataSize,
                        UncompressedSize = uncompressedSize,
                        BlockProperty = (JmdDataInfoProperty)propVal,
                        Checksum = checksum
                    };
                    _dataInfoMap[index] = info;
                }

                uint folderKey = JmdCrypto.GetDirectoryDataKey(_jmdKey);
                var processQueue = new Queue<(uint DataIndex, JmdFolder Folder)>();
                processQueue.Enqueue((0xFFFFFFFF, RootFolder));

                while (processQueue.Count > 0)
                {
                    var (folderDataIndex, currentFolder) = processQueue.Dequeue();
                    byte[] folderData = ReadAndDecryptBlock(folderDataIndex, folderKey, null);

                    int offset = 0;
                    if (folderData.Length < 4)
                        continue;

                    int folderCount = BitConverter.ToInt32(folderData, offset);
                    offset += 4;

                    for (int i = 0; i < folderCount; i++)
                    {
                        int startOffset = offset;
                        while (offset + 1 < folderData.Length && (folderData[offset] != 0 || folderData[offset + 1] != 0))
                        {
                            offset += 2;
                        }
                        string name = Encoding.Unicode.GetString(folderData, startOffset, offset - startOffset).Trim();
                        offset += 2;

                        if (offset + 4 > folderData.Length)
                            break;

                        uint subFolderIndex = BitConverter.ToUInt32(folderData, offset);
                        offset += 4;

                        var subFolder = new JmdFolder(name, currentFolder);
                        currentFolder.Folders.Add(subFolder);
                        processQueue.Enqueue((subFolderIndex, subFolder));
                    }

                    if (offset + 4 > folderData.Length)
                        continue;

                    int fileCount = BitConverter.ToInt32(folderData, offset);
                    offset += 4;

                    for (int i = 0; i < fileCount; i++)
                    {
                        int startOffset = offset;
                        while (offset + 1 < folderData.Length && (folderData[offset] != 0 || folderData[offset + 1] != 0))
                        {
                            offset += 2;
                        }
                        string fileName = Encoding.Unicode.GetString(folderData, startOffset, offset - startOffset).Trim();
                        offset += 2;

                        if (offset + 16 > folderData.Length)
                            break;

                        uint extInt = BitConverter.ToUInt32(folderData, offset);
                        uint propVal = BitConverter.ToUInt32(folderData, offset + 4);
                        uint dataIndex = BitConverter.ToUInt32(folderData, offset + 8);
                        int fileSize = BitConverter.ToInt32(folderData, offset + 12);
                        offset += 16;

                        string extStr = "";
                        try
                        {
                            byte[] extBytes = BitConverter.GetBytes(extInt);
                            extStr = Encoding.ASCII.GetString(extBytes).TrimEnd('\0').Trim();
                        }
                        catch { }

                        string fullFileName = !string.IsNullOrEmpty(extStr) ? $"{fileName}.{extStr}" : fileName;
                        fullFileName = fullFileName.Trim();

                        uint fileKey = JmdCrypto.GetFileKey(_jmdKey, fileName, extInt);

                        var fileObj = new JmdFile(fullFileName, propVal)
                        {
                            DataIndex = dataIndex,
                            Size = fileSize,
                            Key = fileKey
                        };
                        currentFolder.Files.Add(fileObj);
                    }
                }
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }

        public void ExtractAll(string outputDir)
        {
            if (_fs == null)
                throw new InvalidOperationException("JMD file is not open.");

            ExtractRecursive(RootFolder, outputDir);
        }

        private void ExtractRecursive(JmdFolder folder, string currentPath)
        {
            if (folder.Name != "__ROOT__")
            {
                currentPath = Path.Combine(currentPath, folder.Name);
            }

            if (!Directory.Exists(currentPath))
            {
                Directory.CreateDirectory(currentPath);
            }

            foreach (var fileObj in folder.Files)
            {
                string outFilePath = Path.Combine(currentPath, fileObj.Name);
                try
                {
                    byte[] data = ReadAndDecryptBlock(fileObj.DataIndex, fileObj.Key, fileObj.PropertyValue);
                    File.WriteAllBytes(outFilePath, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to extract file {fileObj.Name}: {ex.Message}");
                }
            }

            foreach (var subFolder in folder.Folders)
            {
                ExtractRecursive(subFolder, currentPath);
            }
        }

        private byte[] ReadAndDecryptBlock(uint dataIndex, uint key, uint? propVal)
        {
            if (dataIndex == 0xFFFFFFFF) dataIndex = uint.MaxValue;

            if (!_dataInfoMap.TryGetValue(dataIndex, out var dataInfo))
            {
                if (_dataInfoMap.Count > 0)
                {
                    uint firstKey = 0;
                    foreach (var k in _dataInfoMap.Keys)
                    {
                        firstKey = k;
                        break;
                    }

                    if (_dataInfoMap[firstKey].BlockProperty == JmdDataInfoProperty.FullEncrypted)
                    {
                        dataIndex = firstKey;
                        dataInfo = _dataInfoMap[dataIndex];
                    }
                    else
                    {
                        throw new Exception($"Data index {dataIndex} not found.");
                    }
                }
                else
                {
                    throw new Exception($"Data index {dataIndex} not found.");
                }
            }

            _fs.Seek(dataInfo.Offset, SeekOrigin.Begin);
            byte[] rawData = new byte[dataInfo.DataSize];
            int read = _fs.Read(rawData, 0, dataInfo.DataSize);
            if (read != dataInfo.DataSize)
                throw new Exception($"Failed to read data block at offset {dataInfo.Offset}.");

            byte[] data;
            if (dataInfo.BlockProperty == JmdDataInfoProperty.Compressed ||
                dataInfo.BlockProperty == JmdDataInfoProperty.CompressedEncrypted)
            {
                data = DecompressZlib(rawData);
            }
            else
            {
                data = rawData;
            }

            bool isEncrypted = ((uint)dataInfo.BlockProperty & (uint)JmdDataInfoProperty.PartialEncrypted) != 0;

            if (isEncrypted)
            {
                if (propVal == 5)
                {
                    byte[] decryptedPart = JmdCrypto.ProcessData(key, data);
                    uint nextBlockIndex = dataIndex + 1;
                    if (_dataInfoMap.TryGetValue(nextBlockIndex, out var nextDataInfo))
                    {
                        _fs.Seek(nextDataInfo.Offset, SeekOrigin.Begin);
                        byte[] plainPart = new byte[nextDataInfo.DataSize];
                        _fs.Read(plainPart, 0, nextDataInfo.DataSize);

                        byte[] combined = new byte[decryptedPart.Length + plainPart.Length];
                        Buffer.BlockCopy(decryptedPart, 0, combined, 0, decryptedPart.Length);
                        Buffer.BlockCopy(plainPart, 0, combined, decryptedPart.Length, plainPart.Length);
                        data = combined;
                    }
                    else
                    {
                        data = decryptedPart;
                    }
                }
                else
                {
                    data = JmdCrypto.ProcessData(key, data);
                }
            }

            return data;
        }

        public static byte[] DecompressZlib(byte[] data)
        {
            try
            {
                if (data.Length < 6) return data;

                byte cmf = data[0];
                byte flg = data[1];

                if ((cmf & 0x0F) == 8 && (((cmf << 8) | flg) % 31) == 0)
                {
                    using (var msInput = new MemoryStream(data, 2, data.Length - 6))
                    using (var msOutput = new MemoryStream())
                    using (var deflate = new DeflateStream(msInput, CompressionMode.Decompress))
                    {
                        deflate.CopyTo(msOutput);
                        return msOutput.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Zlib decompression error: {ex.Message}");
            }
            return data;
        }

        public void Dispose()
        {
            if (_fs != null)
            {
                _fs.Dispose();
                _fs = null;
            }
        }
    }
}
