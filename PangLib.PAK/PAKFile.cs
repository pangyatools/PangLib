﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PangLib.Utilities.Cryptography;
using PangLib.Utilities.Compression;

namespace PangLib.PAK
{
    /// <summary>
    /// Main PAK file class
    /// </summary>
    public class PAKFile
    {
        public List<FileEntry> Entries = new List<FileEntry>();
        private string FilePath;
        
        private uint FileListOffset;
        private uint FileCount;
        private byte Signature;

        private dynamic Key;

        /// <summary>
        /// Constructor for the PAK file instance
        /// </summary>
        /// <param name="filePath">Path of the PAK file</param>
        /// <param name="key">Decryption key for encrypted fields</param>
        public PAKFile(string filePath, dynamic key)
        {
            FilePath = filePath;
            Key = key;

            ReadMetadata();
        }

        /// <summary>
        /// Reads the PAK file metadata, including file list information and the file list
        /// </summary>
        private void ReadMetadata()
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(FilePath))))
            {
                reader.BaseStream.Seek(-9L, SeekOrigin.End);

                FileListOffset = reader.ReadUInt32();
                FileCount = reader.ReadUInt32();
                Signature = reader.ReadByte();

                reader.BaseStream.Seek(FileListOffset, SeekOrigin.Begin);

                for (uint i = 0; i < FileCount; i++)
                {
                    FileEntry fileEntry = new FileEntry();

                    fileEntry.FileNameLength = reader.ReadByte();
                    fileEntry.Compression = reader.ReadByte();
                    fileEntry.Offset = reader.ReadUInt32();
                    fileEntry.FileSize = reader.ReadUInt32();
                    fileEntry.RealFileSize = reader.ReadUInt32();

                    byte[] tempName = reader.ReadBytes(fileEntry.FileNameLength);

                    if (fileEntry.Compression < 4 && fileEntry.Compression > -1)
                    {
                        uint decryptionKey = (uint) Key;

                        reader.BaseStream.Seek(1L, SeekOrigin.Current);
                        fileEntry.FileName = Encoding.UTF8.GetString(XOR.Cipher(tempName, decryptionKey));
                    }
                    else
                    {
                        uint[] decryptionKey = (uint[]) Key;

                        fileEntry.Compression ^= 0x20;

                        fileEntry.FileName = DecryptFileName(tempName, decryptionKey);

                        uint[] decryptionData = new uint[]
                        {
                            fileEntry.Offset,
                            fileEntry.RealFileSize
                        };

                        uint[] resultData = XTEA.Decipher(16, decryptionData, decryptionKey);

                        fileEntry.Offset = resultData[0];
                        fileEntry.RealFileSize = resultData[1]; 
                    }

                    Entries.Add(fileEntry);
                }
            }
        }

        /// <summary>
        /// Extracts the files from the file list
        ///
        /// Also performs decompression or creation of folders where necessary
        /// </summary>
        public void ExtractFiles()
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(FilePath))))
            {
                byte[] data = null;

                Entries.ForEach(fileEntry =>
                {
                    reader.BaseStream.Seek(fileEntry.Offset, SeekOrigin.Begin);
                    data = reader.ReadBytes((int)fileEntry.FileSize);

                    switch (fileEntry.Compression)
                    {
                        case 1:
                        case 3:
                            data = LZ77.Decompress(data, fileEntry.FileSize, fileEntry.RealFileSize, fileEntry.Compression);
                            break;
                        case 2:
                            Directory.CreateDirectory(fileEntry.FileName);
                            break;
                    }

                    if (fileEntry.FileSize != 0) {
                        File.WriteAllBytes(fileEntry.FileName, data);
                    }
                });
            }
        }

        /// <summary>
        /// Decrypts the name of a file using XTEA
        /// </summary>
        /// <param name="fileNameBuffer">Bytes of the file name</param>
        /// <param name="key">Key to decrypt the filename with</param>
        /// <returns>The decrypted filename</returns>
        private string DecryptFileName(byte[] fileNameBuffer, uint[] key)
        {
            Span<byte> nameSpan = fileNameBuffer;

            for (int j = 0; j < nameSpan.Length; j = j + 8)
            {
                Span<byte> chunk = nameSpan.Slice(j, 8);
                Span<uint> decrypted = XTEA.Decipher(16, MemoryMarshal.Cast<byte, uint>(chunk).ToArray(), key);
                Span<byte> resource = MemoryMarshal.AsBytes(decrypted);
                resource.CopyTo(chunk);
            }

            return Encoding.UTF8.GetString(nameSpan.ToArray().TakeWhile(x => x != 0x00).ToArray());
        }
    }

    /// <summary>
    /// Main structure of file entries
    /// </summary>
    public struct FileEntry
    {
        public byte FileNameLength;
        public byte Compression;
        public uint Offset;
        public uint FileSize;
        public uint RealFileSize;
        public string FileName;
    }
}
