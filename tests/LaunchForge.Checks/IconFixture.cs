using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

internal static class IconFixture
{
    public static byte[] Create()
    {
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result);
        const int size = 16;
        const int payloadSize = 40 + size * size * 4 + size * 4;
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)size); writer.Write((byte)size); writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(payloadSize); writer.Write(22);
        writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(0); writer.Write(size * size * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int pixel = 0; pixel < size * size; pixel++) { writer.Write((byte)0x35); writer.Write((byte)0xC5); writer.Write((byte)0xEF); writer.Write((byte)255); }
        writer.Write(new byte[size * 4]);
        return result.ToArray();
    }

    public static byte[] ReadEmbeddedImage(string executable)
    {
        using var stream = File.OpenRead(executable);
        using var pe = new PEReader(stream);
        var resource = pe.PEHeaders.PEHeader!.ResourceTableDirectory;
        var data = pe.GetSectionData(resource.RelativeVirtualAddress).GetContent().ToArray();
        uint Read32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        int Count(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 12, 2)) + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 14, 2));
        uint child = 0;
        for (int index = 0; index < Count(0); index++) if (Read32(16 + index * 8) == 3) { child = Read32(20 + index * 8); break; }
        if (child == 0) throw new InvalidDataException("No native icon resource in exported EXE.");
        while ((child & 0x80000000) != 0)
        {
            int directory = (int)(child & 0x7fffffff);
            if (Count(directory) == 0) throw new InvalidDataException("Empty icon resource.");
            child = Read32(directory + 20);
        }
        int entry = (int)child;
        return pe.GetSectionData((int)Read32(entry)).GetContent(0, (int)Read32(entry + 4)).ToArray();
    }
}
