﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.CodeAnalysis.BinaryParsers.PortableExecutable
{
    public class PE : IDisposable
    {
        internal SafePointer m_pImage;		// pointer to the beginning of the file in memory
        private string[] _asImports;
        private string _sha1Hash;
        private string _sha256Hash;
        private long? _length;
        private string _versionDetails;
        private string _version;
        private bool? _isResourceOnly = null;
        private bool? _isManagedResourceOnly;
        private bool? _isKernelMode;

        private FileStream _fs;
        private PEReader _peReader;
        private MetadataReader _metadataReader;
        public PE(string fileName)
        {
            FileName = Path.GetFullPath(fileName);
            Uri = new Uri(FileName);
            IsPEFile = false;
            try
            {
                _fs = File.OpenRead(FileName);

                byte byteRead = (byte)_fs.ReadByte();
                if (byteRead != 'M') { return; }

                byteRead = (byte)_fs.ReadByte();
                if (byteRead != 'Z') { return; }
                _fs.Seek(0, SeekOrigin.Begin);

                _peReader = new PEReader(_fs);
                PEHeaders = _peReader.PEHeaders;
                IsPEFile = true;

                m_pImage = new SafePointer(_peReader.GetEntireImage().GetContent().ToBuilder().ToArray());

                if (IsManaged)
                {
                    _metadataReader = _peReader.GetMetadataReader();
                }
            }
            catch (IOException e) { LoadException = e; }
            catch (BadImageFormatException e) { LoadException = e; }
            catch (UnauthorizedAccessException e) { LoadException = e; }
        }

        public void Dispose()
        {
            if (_peReader != null)
            {
                _peReader.Dispose();
                _peReader = null;
            }

            if (_fs != null)
            {
                _fs.Dispose();
                _fs = null;
            }
        }


        public Exception LoadException { get; set; }

        public Uri Uri { get; set; }

        public string FileName { get; set; }

        public PEHeaders PEHeaders { get; private set; }

        public bool IsPEFile { get; set; }

        public bool IsDotNetNative
        {
            get
            {
                if (this.Imports != null)
                {
                    for (int i = 0; i < this.Imports.Length; i++)
                    {
                        if (this.Imports[i].Equals("mrt100.dll", StringComparison.OrdinalIgnoreCase) ||
                            this.Imports[i].Equals("mrt100_app.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public bool Is64Bit
        {
            get
            {
                if (PEHeaders.PEHeader == null)
                {
                    return false;
                }

                return PEHeaders.PEHeader.Magic == PEMagic.PE32Plus;
            }
        }

        public string[] Imports
        {
            get
            {
                if (_asImports == null)
                {
                    DirectoryEntry importTableDirectory = PEHeaders.PEHeader.ImportTableDirectory;
                    if (PEHeaders.PEHeader.ImportTableDirectory.Size == 0)
                    {
                        _asImports = new string[0];
                    }
                    else
                    {
                        SafePointer sp = new SafePointer(m_pImage._array, importTableDirectory.RelativeVirtualAddress);

                        ArrayList al = new ArrayList();

                        if (sp.Address != 0)
                        {
                            sp = RVA2VA(sp);

                            while (((UInt32)sp != 0) || ((UInt32)(sp + 12) != 0) || ((UInt32)(sp + 16) != 0))
                            {
                                SafePointer spName = sp;
                                spName.Address = (int)(UInt32)(sp + 12);

                                spName = RVA2VA(spName);

                                string name = (string)(spName);

                                al.Add(name);

                                sp += 20;   // size of struct
                            }
                        }

                        _asImports = (string[])al.ToArray(typeof(string));
                    }
                }

                return _asImports;
            }
        }

        public SafePointer RVA2VA(SafePointer rva)
        {
            // find which section is our rva in
            SectionHeader ish = new SectionHeader();
            foreach (SectionHeader sectionHeader in PEHeaders.SectionHeaders)
            {
                if ((rva.Address >= sectionHeader.VirtualAddress) &&
                    (rva.Address < sectionHeader.VirtualAddress + sectionHeader.SizeOfRawData))
                {
                    ish = sectionHeader;
                    break;
                }
            }

            if (ish.VirtualAddress == 0) throw new InvalidOperationException("RVA does not belong to any section");

            // calculate the VA
            rva.Address = (int)(rva.Address - ish.VirtualAddress + ish.PointerToRawData);

            return rva;
        }

        public byte[] ImageBytes
        {
            get
            {
                if (m_pImage._array != null)
                {
                    return m_pImage._array;
                }

                throw new InvalidOperationException("Image bytes cannot be retrieved when data is backed by a stream.");
            }
        }


        /// <summary>
        /// Calculate SHA1 over the file contents
        /// </summary>
        public string SHA1Hash
        {
            get
            {
                if (_sha1Hash != null)
                {
                    return _sha1Hash;
                }

                // processing buffer
                byte[] buffer = new byte[4096];

                // create the hash object
                SHA1 sha1 = SHA1.Create();

                // open the input file
                using (FileStream fs = new FileStream(FileName, FileMode.Open, FileAccess.Read))
                {
                    int readBytes = -1;

                    // pump the file through the hash
                    do
                    {
                        readBytes = fs.Read(buffer, 0, buffer.Length);

                        sha1.TransformBlock(buffer, 0, readBytes, buffer, 0);
                    } while (readBytes > 0);

                    // need to call this to finalize the calculations
                    sha1.TransformFinalBlock(buffer, 0, readBytes);
                }

                // get the string representation
                _sha1Hash = BitConverter.ToString(sha1.Hash).Replace("-", "");

                return _sha1Hash;
            }
        }

        /// <summary>
        /// Calculate SHA256 hash of file contents
        /// </summary>
        public string SHA256Hash
        {
            get
            {
                if (_sha256Hash != null)
                {
                    return _sha256Hash;
                }
                _sha256Hash = ComputeSha256Hash(FileName);

                return _sha256Hash;
            }
        }

        public static string ComputeSha256Hash(string fileName)
        {
            string sha256Hash = null;

            try
            {
                using (FileStream stream = File.OpenRead(fileName))
                {
                    using (var bufferedStream = new BufferedStream(stream, 1024 * 32))
                    {
                        var sha = new SHA256Cng();
                        byte[] checksum = sha.ComputeHash(bufferedStream);
                        sha256Hash = BitConverter.ToString(checksum).Replace("-", String.Empty);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return sha256Hash;
        }

        /// <summary>
        /// File length
        /// </summary>
        public long Length
        {
            get
            {
                if (_length != null)
                {
                    return (long)_length;
                }

                FileInfo fi = new FileInfo(FileName);
                _length = fi.Length;

                return (long)_length;
            }
        }

        /// <summary>
        /// File Version details string
        /// </summary>
        public string FileVersionDetails
        {
            get
            {
                if (_versionDetails == null)
                {
                    System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.GetFullPath(FileName));

                    _versionDetails = RemoveUnprintableChars(fvi.ToString());
                    _version = RemoveUnprintableChars(fvi.FileVersion);
                }

                return _versionDetails;
            }
        }

        /// <summary>
        /// File version
        /// </summary>
        public string FileVersion
        {
            get
            {
                if (_version == null)
                {
                    string s = FileVersionDetails;
                }
                return _version;
            }
        }

        public Packer Packer
        {
            get
            {
                if (PEHeaders != null)
                {
                    foreach (SectionHeader sh in PEHeaders.SectionHeaders)
                    {
                        if (sh.Name.StartsWith("UPX")) { return Packer.Upx; }
                    }
                }
                return Packer.UnknownOrNotPacked;
            }
        }
       
        public bool IsPacked
        {
            get
            {
                return Packer != Packer.UnknownOrNotPacked;
            }
        }

        /// <summary>
        /// Returns true if the PE is managed
        /// </summary>
        public bool IsManaged
        {
            get
            {
                return PEHeaders != null && PEHeaders.CorHeader != null;
            }
        }

        /// <summary>
        /// Returns true if the PE is pure managed
        /// </summary>
        public bool IsILOnly
        {
            get
            {
                return PEHeaders.CorHeader != null &&
                       (PEHeaders.CorHeader.Flags & CorFlags.ILOnly) == CorFlags.ILOnly;
            }
        }

        /// <summary>
        /// Returns true if the only directory present is Resource Directory (this also covers hxs and hxi files)
        /// </summary>
        public bool IsResourceOnly
        {
            get
            {
                if (_isResourceOnly != null)
                {
                    return (bool)_isResourceOnly;
                }

                if (IsILOnly)
                {
                    return IsManagedResourceOnly;
                }

                PEHeader peHeader = PEHeaders.PEHeader;
                if (peHeader == null)
                {
                    return false;
                }

                // IMAGE_DIRECTORY_ENTRY_RESOURCE = 2;
                if (peHeader.ResourceTableDirectory.RelativeVirtualAddress == 0)
                {
                    return false;
                }

                return (peHeader.ThreadLocalStorageTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_TLS = 9;
                        peHeader.ImportAddressTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_IAT = 12;
                        peHeader.GlobalPointerTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_GLOBALPTR = 8;
                        peHeader.DelayImportTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT = 13;
                        peHeader.BoundImportTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT = 11;
                        peHeader.LoadConfigTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_LOAD_CONFIG = 10;
                        peHeader.CopyrightTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_ARCHITECTURE = 7;
                        peHeader.ExceptionTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_EXCEPTION = 3;	
                        peHeader.CorHeaderTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR 14
                        peHeader.ExportTableDirectory.RelativeVirtualAddress == 0 && // IMAGE_DIRECTORY_ENTRY_EXPORT = 0;
                        peHeader.ImportTableDirectory.RelativeVirtualAddress == 0); // IMAGE_DIRECTORY_ENTRY_IMPORT = 1;                                                

                // These are explicitly ignored. Debug info is sometimes present in resource
                // only binaries. We've seen cases where help resource-only DLLs had a bogus
                // non-empty relocation section. Security digital signatures are ok.
                //
                // IMAGE_DIRECTORY_ENTRY_DEBUG = 6;	// Debug Directory
                // IMAGE_DIRECTORY_ENTRY_BASERELOC = 5;	// Base Relocation Table
                // IMAGE_DIRECTORY_ENTRY_SECURITY = 4;	// Security Directory
            }
        }

        /// <summary>
        /// Returns true is the assembly is pure managed and doesn't have any methods
        /// </summary>
        public bool IsManagedResourceOnly
        {
            get
            {
                if (_isManagedResourceOnly != null)
                {
                    return (bool)_isManagedResourceOnly;
                }

                if (!IsILOnly)
                {
                    _isManagedResourceOnly = false;
                    return _isManagedResourceOnly.Value;
                }

                _isManagedResourceOnly = _metadataReader.MethodDefinitions.Count == 0;
                return _isManagedResourceOnly.Value;
            }
        }

        /// <summary>
        /// Returns true is the binary is likely compiled for kernel mode
        /// </summary>
        public bool IsKernelMode
        {
            get
            {
                if (_isKernelMode != null)
                {
                    return (bool)_isKernelMode;
                }


                _isKernelMode = false;

                if (!IsPEFile || PEHeaders.PEHeader == null)
                {
                    return _isKernelMode.Value;
                }

                string[] imports = Imports;

                foreach (string import in imports)
                {
                    if (import.StartsWith("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("hal.dll", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("scsiport.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("win32k.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("ataport.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("drmk.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("ks.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("mcd.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("pciidex.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("storport.sys", StringComparison.OrdinalIgnoreCase) ||
                        import.StartsWith("tape.sys", StringComparison.OrdinalIgnoreCase))
                    {
                        _isKernelMode = true;
                        break;
                    }
                }

                return _isKernelMode.Value;
            }
        }

        /// <summary>
        /// Returns true if the binary is built for XBox
        /// </summary>
        public bool IsXBox
        {
            get
            {
                if (PEHeaders.PEHeader != null)
                {
                    return PEHeaders.PEHeader.Subsystem == System.Reflection.PortableExecutable.Subsystem.Xbox;
                }
                return false;
            }
        }

        /// <summary>
        /// Machine type
        /// </summary>
        public Machine Machine
        {
            get
            {
                if (PEHeaders.PEHeader != null)
                {
                    return PEHeaders.CoffHeader.Machine;
                }
                return Machine.Unknown;
            }
        }

        /// <summary>
        /// Subsystem type
        /// </summary>
        public Subsystem Subsystem
        {
            get
            {
                if (PEHeaders.PEHeader != null)
                {
                    return PEHeaders.PEHeader.Subsystem;
                }
                return Subsystem.Unknown;
            }
        }

        /// <summary>
        /// OS version from the PE Optional Header
        /// </summary>
        public Version OSVersion
        {
            get
            {
                PEHeader optionalHeader = PEHeaders.PEHeader;

                if (PEHeaders.PEHeader != null)
                {
                    UInt16 major = PEHeaders.PEHeader.MajorOperatingSystemVersion;
                    UInt16 minor = PEHeaders.PEHeader.MinorOperatingSystemVersion;

                    return new Version(major, minor);
                }

                return null;
            }
        }

        /// <summary>
        /// Subsystem version from the PE Optional Header
        /// </summary>
        public Version SubsystemVersion
        {
            get
            {
                PEHeader optionalHeader = PEHeaders.PEHeader;

                if (optionalHeader != null)
                {
                    UInt16 major = optionalHeader.MajorImageVersion;
                    UInt16 minor = optionalHeader.MinorSubsystemVersion;

                    return new Version(major, minor);
                }

                return null;
            }
        }

        /// <summary>
        /// Linker version from the PE Optional Header
        /// </summary>
        public Version LinkerVersion
        {
            get
            {
                PEHeader optionalHeader = PEHeaders.PEHeader;

                if (optionalHeader != null)
                {
                    byte major = optionalHeader.MajorLinkerVersion;
                    byte minor = optionalHeader.MinorLinkerVersion;

                    return new Version(major, minor);
                }

                return null;
            }
        }


        /// <summary>
        /// Remove all characters <0x20
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static string RemoveUnprintableChars(string s)
        {
            if (s == null)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c >= 0x20)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Counts the number of bits set in a 64bit value.
        /// </summary>
        /// <param name="data">Input value</param>
        /// <returns>The count of bits set.</returns>
        private static int CountBitSet(UInt64 data)
        {
            UInt64 output = data;
            output = (output & 0x5555555555555555) + ((output & 0xAAAAAAAAAAAAAAAA) >> 1);
            output = (output & 0x3333333333333333) + ((output & 0xCCCCCCCCCCCCCCCC) >> 2);
            output = (output & 0x0F0F0F0F0F0F0F0F) + ((output & 0xF0F0F0F0F0F0F0F0) >> 4);
            output = (output & 0x00FF00FF00FF00FF) + ((output & 0xFF00FF00FF00FF00) >> 8);
            output = (output & 0x0000FFFF0000FFFF) + ((output & 0xFFFF0000FFFF0000) >> 16);
            output = (output & 0x00000000FFFFFFFF) + ((output & 0xFFFFFFFF00000000) >> 32);
            return (int)output;
        }
    }
}
