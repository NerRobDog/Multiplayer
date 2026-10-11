using System.Collections.Generic;
using Multiplayer.Common;
using Xunit;

public class Arm64FrameChainTests
{
    private sealed class FakeMemory : IMemoryReader
    {
        public readonly Dictionary<long, long> Words = new();
        public long ReadLong(long address) => Words.TryGetValue(address, out var v) ? v : 0;
        public void Frame(long fp, long nextFp, long ret) { Words[fp] = nextFp; Words[fp + 8] = ret; }
    }

    [Fact]
    public void Walks_chain_to_last_frame()
    {
        var m = new FakeMemory();
        m.Frame(0x1000, 0x1040, 0xAAAA);
        m.Frame(0x1040, 0x10A0, 0xBBBB);
        m.Frame(0x10A0, 0, 0xCCCC);

        var rets = new List<long>();
        long fp = 0x1000;
        while (Arm64FrameChain.TryStep(m, fp, out var ret, out var next))
        {
            rets.Add(ret);
            if (next == 0) break;
            fp = next;
        }

        Assert.Equal(new long[] { 0xAAAA, 0xBBBB, 0xCCCC }, rets);
    }

    [Fact]
    public void Rejects_null_and_misaligned_fp()
    {
        var m = new FakeMemory();
        Assert.False(Arm64FrameChain.TryStep(m, 0, out _, out _));
        Assert.False(Arm64FrameChain.TryStep(m, 0x1004, out _, out _));
    }

    [Fact]
    public void Rejects_chain_that_goes_down_the_stack()
    {
        var m = new FakeMemory();
        m.Frame(0x2000, 0x1000, 0xAAAA); // следующий кадр ниже — цепочка испорчена
        Assert.False(Arm64FrameChain.TryStep(m, 0x2000, out _, out _));
    }

    [Fact]
    public void Rejects_chain_that_loops()
    {
        var m = new FakeMemory();
        m.Frame(0x2000, 0x2000, 0xAAAA);
        Assert.False(Arm64FrameChain.TryStep(m, 0x2000, out _, out _));
    }

    [Fact]
    public void Rejects_jump_beyond_max_span()
    {
        var m = new FakeMemory();
        m.Frame(0x2000, 0x2000 + Arm64FrameChain.MaxFrameSpan + 8, 0xAAAA);
        Assert.False(Arm64FrameChain.TryStep(m, 0x2000, out _, out _));
    }

    [Fact]
    public void Rejects_zero_return_address()
    {
        var m = new FakeMemory();
        m.Frame(0x2000, 0x2040, 0);
        Assert.False(Arm64FrameChain.TryStep(m, 0x2000, out _, out _));
    }
}

public class Arm64CodeScannerTests
{
    private const long Magic = 0x0123456789ABCDEF;

    private static uint Movz(int rd, int imm16, int hw) => 0xD2800000u | (uint)(hw << 21) | (uint)(imm16 << 5) | (uint)rd;
    private static uint Movk(int rd, int imm16, int hw) => 0xF2800000u | (uint)(hw << 21) | (uint)(imm16 << 5) | (uint)rd;
    private static uint StrImm(int rt, int rn, int byteOffset) => 0xF9000000u | (uint)((byteOffset / 8) << 10) | (uint)(rn << 5) | (uint)rt;
    private static uint Stur(int rt, int rn, int simm9) => 0xF8000000u | (uint)((simm9 & 0x1FF) << 12) | (uint)(rn << 5) | (uint)rt;

    private static byte[] Bytes(params uint[] words)
    {
        var b = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            b[i * 4] = (byte)words[i];
            b[i * 4 + 1] = (byte)(words[i] >> 8);
            b[i * 4 + 2] = (byte)(words[i] >> 16);
            b[i * 4 + 3] = (byte)(words[i] >> 24);
        }
        return b;
    }

    private static uint[] LoadMagic(int rd) => new[]
    {
        Movz(rd, 0xCDEF, 0), Movk(rd, 0x89AB, 1), Movk(rd, 0x4567, 2), Movk(rd, 0x0123, 3),
    };

    [Fact]
    public void Finds_str_to_fp_after_magic_load()
    {
        var words = new List<uint> { 0xA9BF7BFDu /* stp x29,x30,[sp,#-16]! */, 0x910003FDu /* mov x29,sp */ };
        words.AddRange(LoadMagic(8));
        words.Add(StrImm(8, 29, 24));
        words.Add(0xD65F03C0u /* ret */);

        Assert.True(Arm64CodeScanner.TryFindMagicStore(Bytes(words.ToArray()), Magic, out var baseReg, out var offset));
        Assert.Equal(29, baseReg);
        Assert.Equal(24, offset);
    }

    [Fact]
    public void Finds_stur_with_negative_offset()
    {
        var words = new List<uint>(LoadMagic(0)) { Stur(0, 29, -8) };
        Assert.True(Arm64CodeScanner.TryFindMagicStore(Bytes(words.ToArray()), Magic, out var baseReg, out var offset));
        Assert.Equal(29, baseReg);
        Assert.Equal(-8, offset);
    }

    [Fact]
    public void Accepts_sp_as_base()
    {
        var words = new List<uint>(LoadMagic(9)) { StrImm(9, 31, 16) };
        Assert.True(Arm64CodeScanner.TryFindMagicStore(Bytes(words.ToArray()), Magic, out var baseReg, out var offset));
        Assert.Equal(31, baseReg);
        Assert.Equal(16, offset);
    }

    [Fact]
    public void Ignores_store_of_other_register_and_other_value()
    {
        var words = new List<uint>(LoadMagic(8))
        {
            StrImm(9, 29, 8),   // другой регистр
            Movz(8, 1, 0),      // регистр перезаписан другим значением
            StrImm(8, 29, 32),
        };
        Assert.False(Arm64CodeScanner.TryFindMagicStore(Bytes(words.ToArray()), Magic, out _, out _));
    }

    [Fact]
    public void Ignores_store_through_other_base_register()
    {
        var words = new List<uint>(LoadMagic(8)) { StrImm(8, 19, 8) };
        Assert.False(Arm64CodeScanner.TryFindMagicStore(Bytes(words.ToArray()), Magic, out _, out _));
    }

    [Fact]
    public void Empty_or_garbage_code_is_not_found()
    {
        Assert.False(Arm64CodeScanner.TryFindMagicStore(new byte[0], Magic, out _, out _));
        Assert.False(Arm64CodeScanner.TryFindMagicStore(new byte[] { 1, 2, 3 }, Magic, out _, out _));
    }
}
