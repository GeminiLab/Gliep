using System;
using System.Runtime.InteropServices;

namespace GeminiLab.Glos.Serialization {
    [StructLayout(LayoutKind.Explicit)]
    public struct GlosUnitHeader {
        public const uint MagicValue   = 0x44614c47;
        public const byte MagicValueB0 = 0x47;
        public const byte MagicValueB1 = 0x4c;
        public const byte MagicValueB2 = 0x61;
        public const byte MagicValueB3 = 0x44;

        [FieldOffset(0x00)] public uint   Magic;
        [FieldOffset(0x04)] public uint   FileVersion;
        [FieldOffset(0x04)] public ushort FileVersionMinor;
        [FieldOffset(0x06)] public ushort FileVersionMajor;
        [FieldOffset(0x08)] public uint   FileLength;
        [FieldOffset(0x0c)] public uint   FirstSectionOffset;
    }

    public enum GlosUnitSectionType : ushort {
        FunctionSection = 0x0001,
        StringSection   = 0x0002,
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct GlosUnitSectionHeader {
        [FieldOffset(0x00)] public uint   SectionHeaderLength;
        [FieldOffset(0x04)] public uint   SectionLength;
        [FieldOffset(0x08)] public uint   NextOffset;
        [FieldOffset(0x0c)] public ushort SectionType;
        [FieldOffset(0x0e)] public ushort Reserved;
    }

    [Flags]
    public enum GlosUnitFunctionFlags : uint {
        Default = 0,
        
        Entry = 0b1,
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public struct GlosUnitFunctionHeader {
        [FieldOffset(0x00)] public uint FunctionHeaderSize;
        [FieldOffset(0x04)] public uint FunctionSize;
        [FieldOffset(0x08)] public uint CodeLength;
        [FieldOffset(0x0c)] public uint Flags;
        [FieldOffset(0x10)] public uint VariableInContextCount;
        [FieldOffset(0x14)] public uint LocalVariableCount;
    }
}
