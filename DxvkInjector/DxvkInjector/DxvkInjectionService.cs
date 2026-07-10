using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DxvkInjector.Dxvk
{
    /// <summary>
    /// Handles the actual filesystem work of turning DXVK "on" or "off" for a game
    /// directory: architecture detection, and dll copy/removal.
    /// </summary>
    public class DxvkInjectionService
    {
        private static readonly Dictionary<DxvkModule, string> ModuleFileNames = new Dictionary<DxvkModule, string>
        {
            { DxvkModule.D3D9, "d3d9.dll" },
            { DxvkModule.D3D10Core, "d3d10core.dll" },
            { DxvkModule.D3D11, "d3d11.dll" },
            { DxvkModule.Dxgi, "dxgi.dll" },
        };

        /// <summary>
        /// Reads the PE header of the exe to determine x86 vs x64. Cheap and reliable —
        /// no need to launch the process or shell out to anything.
        /// </summary>
        public GameArchitecture DetectArchitecture(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                if (fs.Length < 0x40) return GameArchitecture.Unknown;

                fs.Seek(0x3C, SeekOrigin.Begin); // e_lfanew: offset to PE header
                var peHeaderOffset = reader.ReadInt32();

                fs.Seek(peHeaderOffset, SeekOrigin.Begin);
                var peSignature = reader.ReadUInt32(); // should be "PE\0\0"
                if (peSignature != 0x00004550) return GameArchitecture.Unknown;

                var machine = reader.ReadUInt16();
                switch (machine)
                {
                    case 0x8664: return GameArchitecture.X64; // IMAGE_FILE_MACHINE_AMD64
                    case 0x014c: return GameArchitecture.X86; // IMAGE_FILE_MACHINE_I386
                    default: return GameArchitecture.Unknown;
                }
            }
        }

        /// <summary>
        /// Reports which Direct3D API an exe targets, so auto-inject can target DX9–DX11
        /// titles only and leave DX12 (and anything unrecognized) alone. Three passes,
        /// each only tried if the previous one came up empty:
        ///  1. The regular PE import table (works for most titles).
        ///  2. The delay-import table — a lot of modern engines (and anything wrapped by an
        ///     anti-tamper/anti-cheat loader) delay-load d3d11.dll/dxgi.dll instead of
        ///     statically importing it, which the regular import table misses entirely.
        ///  3. A filename heuristic (looking for "dx9"/"d3d9"/"dx11"/"d3d11"/etc. in the exe's
        ///     own name) for titles that resolve the API via LoadLibrary with a string built
        ///     at runtime — nothing static can see that, so this is a last-resort guess only.
        /// If the exe imports/delay-imports d3d12.dll at all it's reported as D3D12 even if it
        /// also references d3d11.dll (common for titles with a DX12 default path), since that's
        /// the conservative choice for something running unattended.
        /// </summary>
        public DxvkApiVersion DetectDirectXVersion(string exePath)
        {
            var fromImports = DetectFromImportTables(exePath);
            if (fromImports != DxvkApiVersion.Unknown)
                return fromImports;

            return DetectFromFileName(exePath);
        }

        /// <summary>
        /// True when this exe's PE import/delay-import tables report a real API — i.e.
        /// DetectDirectXVersion above didn't have to fall back to guessing from the filename.
        /// Callers use this to flag a filename-only guess as needing manual confirmation.
        /// </summary>
        public bool DetectedFromImportTable(string exePath) =>
            DetectFromImportTables(exePath) != DxvkApiVersion.Unknown;

        private static DxvkApiVersion DetectFromImportTables(string exePath)
        {
            List<string> imports;
            try
            {
                imports = PeImportReader.ReadImportedDllNames(exePath);
            }
            catch
            {
                imports = new List<string>();
            }

            bool Imports(string dll) => imports.Any(n => string.Equals(n, dll, StringComparison.OrdinalIgnoreCase));

            if (Imports("d3d12.dll")) return DxvkApiVersion.D3D12;
            if (Imports("d3d11.dll")) return DxvkApiVersion.D3D11;
            if (Imports("d3d10core.dll") || Imports("d3d10.dll") || Imports("d3d10_1.dll")) return DxvkApiVersion.D3D10;
            if (Imports("d3d9.dll")) return DxvkApiVersion.D3D9;

            return DxvkApiVersion.Unknown;
        }

        private static DxvkApiVersion DetectFromFileName(string exePath)
        {
            var name = Path.GetFileNameWithoutExtension(exePath)?.ToLowerInvariant() ?? "";

            bool Has(string token) => name.Contains(token);

            // Most-specific/newest first, same order as the import-table checks.
            if (Has("dx12") || Has("d3d12")) return DxvkApiVersion.D3D12;
            if (Has("dx11") || Has("d3d11")) return DxvkApiVersion.D3D11;
            if (Has("dx10") || Has("d3d10")) return DxvkApiVersion.D3D10;
            if (Has("dx9") || Has("d3d9")) return DxvkApiVersion.D3D9;

            return DxvkApiVersion.Unknown;
        }

        /// <summary>
        /// Copies the requested DXVK modules into the game directory, overwriting any
        /// existing file of the same name.
        /// </summary>
        public DxvkGameConfig Inject(string gameDir, string dxvkVersionDir, GameArchitecture arch,
            DxvkModule modules, DxvkVariant variant, string version)
        {
            if (arch == GameArchitecture.Unknown)
                throw new InvalidOperationException(
                    "Could not determine game architecture — refusing to guess which DXVK build to use.");

            var srcDir = Path.Combine(dxvkVersionDir, arch == GameArchitecture.X64 ? "x64" : "x32");
            if (!Directory.Exists(srcDir))
                throw new DirectoryNotFoundException($"DXVK build missing expected folder: {srcDir}");

            var config = new DxvkGameConfig
            {
                Enabled = true,
                InstalledVersion = version,
                Variant = variant,
                ActiveModules = modules
            };

            foreach (var kvp in ModuleFileNames)
            {
                if ((modules & kvp.Key) == 0) continue;

                var fileName = kvp.Value;
                var srcFile = Path.Combine(srcDir, fileName);
                var destFile = Path.Combine(gameDir, fileName);

                if (!File.Exists(srcFile))
                    continue; // this DXVK build doesn't ship this module (e.g. old d3d10core naming)

                File.Copy(srcFile, destFile, overwrite: true);
            }

            return config;
        }

        /// <summary>
        /// Removes DXVK's dlls from the game directory.
        /// Idempotent — safe to call even if partially injected.
        /// </summary>
        public void Remove(string gameDir, DxvkGameConfig config)
        {
            foreach (var kvp in ModuleFileNames)
            {
                if ((config.ActiveModules & kvp.Key) == 0) continue;

                var destFile = Path.Combine(gameDir, kvp.Value);

                if (File.Exists(destFile))
                    File.Delete(destFile);
            }

            config.Enabled = false;
        }
    }

    /// <summary>
    /// Minimal PE import-table reader — lists the DLL names an exe imports (both regular
    /// and delay-loaded), used to guess which Direct3D API a game targets. Not a
    /// general-purpose PE parser (no forwarded exports, no bound-import handling, etc.),
    /// but that's all auto-detect needs.
    /// </summary>
    internal static class PeImportReader
    {
        private struct SectionInfo
        {
            public uint VirtualAddress;
            public uint VirtualSize;
            public uint PointerToRawData;
        }

        /// <summary>
        /// Returns DLL names from both the regular import table (DataDirectory[1]) and the
        /// delay-import table (DataDirectory[13]). Delay-loaded imports are common in modern
        /// engines and anything wrapped by an anti-tamper/anti-cheat loader, and are invisible
        /// to a regular-import-only reader.
        /// </summary>
        public static List<string> ReadImportedDllNames(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                fs.Seek(0x3C, SeekOrigin.Begin);
                var peHeaderOffset = reader.ReadInt32();

                fs.Seek(peHeaderOffset, SeekOrigin.Begin);
                if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
                    throw new InvalidDataException("Not a valid PE file.");

                reader.ReadUInt16(); // Machine
                var numberOfSections = reader.ReadUInt16();
                reader.ReadUInt32(); // TimeDateStamp
                reader.ReadUInt32(); // PointerToSymbolTable
                reader.ReadUInt32(); // NumberOfSymbols
                var sizeOfOptionalHeader = reader.ReadUInt16();
                reader.ReadUInt16(); // Characteristics

                var optionalHeaderStart = fs.Position;
                var magic = reader.ReadUInt16();
                bool isPe32Plus = magic == 0x20B;

                // Offset from the start of the optional header to the start of the DataDirectory array.
                var dataDirectoryOffset = isPe32Plus ? 112 : 96;

                // DataDirectory[1] = Import Table, DataDirectory[13] = Delay Import Descriptor.
                fs.Seek(optionalHeaderStart + dataDirectoryOffset + 1 * 8, SeekOrigin.Begin);
                var importTableRva = reader.ReadUInt32();
                var importTableSize = reader.ReadUInt32();

                fs.Seek(optionalHeaderStart + dataDirectoryOffset + 13 * 8, SeekOrigin.Begin);
                var delayImportRva = reader.ReadUInt32();
                var delayImportSize = reader.ReadUInt32();

                var names = new List<string>();
                if ((importTableRva == 0 || importTableSize == 0) && (delayImportRva == 0 || delayImportSize == 0))
                    return names;

                // Section headers immediately follow the optional header.
                var sectionHeaderStart = optionalHeaderStart + sizeOfOptionalHeader;
                var sections = new List<SectionInfo>();
                fs.Seek(sectionHeaderStart, SeekOrigin.Begin);
                for (int i = 0; i < numberOfSections; i++)
                {
                    fs.Seek(8, SeekOrigin.Current); // Name[8]
                    var virtualSize = reader.ReadUInt32();
                    var virtualAddress = reader.ReadUInt32();
                    var sizeOfRawData = reader.ReadUInt32();
                    var pointerToRawData = reader.ReadUInt32();
                    fs.Seek(16, SeekOrigin.Current); // relocs/linenums/characteristics
                    sections.Add(new SectionInfo
                    {
                        VirtualAddress = virtualAddress,
                        VirtualSize = Math.Max(virtualSize, sizeOfRawData),
                        PointerToRawData = pointerToRawData
                    });
                }

                long RvaToOffset(uint rva)
                {
                    foreach (var s in sections)
                    {
                        if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize)
                            return s.PointerToRawData + (rva - s.VirtualAddress);
                    }
                    throw new InvalidDataException($"RVA 0x{rva:X} not found in any section.");
                }

                string ReadAsciiZ(long offset)
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    var bytes = new List<byte>(64);
                    byte b;
                    while ((b = reader.ReadByte()) != 0)
                        bytes.Add(b);
                    return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
                }

                // Regular import table: array of IMAGE_IMPORT_DESCRIPTOR (20 bytes), name RVA
                // at offset 12, terminated by an all-zero descriptor.
                if (importTableRva != 0 && importTableSize != 0)
                {
                    try
                    {
                        var descriptorOffset = RvaToOffset(importTableRva);
                        const int descriptorSize = 20;

                        while (true)
                        {
                            fs.Seek(descriptorOffset, SeekOrigin.Begin);
                            var originalFirstThunk = reader.ReadUInt32();
                            var timeDateStamp = reader.ReadUInt32();
                            var forwarderChain = reader.ReadUInt32();
                            var nameRva = reader.ReadUInt32();
                            var firstThunk = reader.ReadUInt32();

                            if (originalFirstThunk == 0 && timeDateStamp == 0 && forwarderChain == 0 &&
                                nameRva == 0 && firstThunk == 0)
                                break; // null terminator descriptor

                            if (nameRva != 0)
                                names.Add(ReadAsciiZ(RvaToOffset(nameRva)));

                            descriptorOffset += descriptorSize;
                        }
                    }
                    catch (InvalidDataException)
                    {
                        // A bad RVA in this table shouldn't stop us from still trying the
                        // delay-import table below.
                    }
                }

                // Delay-import table: array of IMAGE_DELAYLOAD_DESCRIPTOR (32 bytes), name RVA
                // at offset 4, terminated by an all-zero descriptor.
                if (delayImportRva != 0 && delayImportSize != 0)
                {
                    try
                    {
                        var descriptorOffset = RvaToOffset(delayImportRva);
                        const int descriptorSize = 32;

                        while (true)
                        {
                            fs.Seek(descriptorOffset, SeekOrigin.Begin);
                            var attributes = reader.ReadUInt32();
                            var dllNameRva = reader.ReadUInt32();

                            if (attributes == 0 && dllNameRva == 0)
                                break; // null terminator descriptor

                            if (dllNameRva != 0)
                            {
                                try { names.Add(ReadAsciiZ(RvaToOffset(dllNameRva))); }
                                catch (InvalidDataException) { /* skip malformed entry */ }
                            }

                            descriptorOffset += descriptorSize;
                        }
                    }
                    catch (InvalidDataException)
                    {
                        // Malformed delay-import table — ignore, regular imports (if any) still count.
                    }
                }

                return names;
            }
        }
    }
}
