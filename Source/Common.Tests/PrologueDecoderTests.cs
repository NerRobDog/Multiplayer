using MultiplayerCommon.Tracing;
using Xunit;

public class Amd64PrologueDecoderTests
{
    private static readonly IPrologueDecoder Decoder = new Amd64PrologueDecoder();

    [Fact]
    public void Short_frame_alloc_returns_byte_operand()
    {
        // sub rsp,0x28  →  48 83 EC 28
        var code = new byte[] { 0x48, 0x83, 0xEC, 0x28, 0x90, 0x90, 0x90, 0x90 };
        var r = Decoder.Decode(code);
        Assert.Equal(PrologueKind.FrameAlloc, r.Kind);
        Assert.Equal(0x28, r.StackUsage);
        Assert.Equal(4, r.BytesConsumed);
    }

    [Fact]
    public void Long_frame_alloc_returns_dword_operand()
    {
        // sub rsp,0x00001234  →  48 81 EC 34 12 00 00
        var code = new byte[] { 0x48, 0x81, 0xEC, 0x34, 0x12, 0x00, 0x00, 0x90 };
        var r = Decoder.Decode(code);
        Assert.Equal(PrologueKind.FrameAlloc, r.Kind);
        Assert.Equal(0x1234, r.StackUsage);
        Assert.Equal(7, r.BytesConsumed);
    }

    [Fact]
    public void Push_rbp_is_reported_as_frame_pointer_based()
    {
        // push rbp  →  55
        var code = new byte[] { 0x55, 0x48, 0x89, 0xE5, 0x90, 0x90, 0x90, 0x90 };
        var r = Decoder.Decode(code);
        Assert.Equal(PrologueKind.FramePointerBased, r.Kind);
    }

    [Fact]
    public void Unknown_header_is_reported_not_thrown()
    {
        var code = new byte[] { 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC };
        var r = Decoder.Decode(code);
        Assert.Equal(PrologueKind.Unknown, r.Kind);
    }
}

public class Arm64PrologueDecoderTests
{
    private static readonly IPrologueDecoder Decoder = new Arm64PrologueDecoder();

    private static byte[] Word(uint w) => new[]
    {
        (byte)(w & 0xFF),
        (byte)((w >> 8) & 0xFF),
        (byte)((w >> 16) & 0xFF),
        (byte)((w >> 24) & 0xFF),
    };

    [Fact]
    public void Sub_sp_immediate_returns_frame_size()
    {
        // sub sp, sp, #0x40  →  imm12 = 0x40
        uint w = 0xD10003FF | (0x40u << 10);
        var r = Decoder.Decode(Word(w));
        Assert.Equal(PrologueKind.FrameAlloc, r.Kind);
        Assert.Equal(0x40, r.StackUsage);
        Assert.Equal(4, r.BytesConsumed);
    }

    [Fact]
    public void Sub_sp_immediate_zero_is_still_frame_alloc()
    {
        var r = Decoder.Decode(Word(0xD10003FF));
        Assert.Equal(PrologueKind.FrameAlloc, r.Kind);
        Assert.Equal(0, r.StackUsage);
    }

    [Fact]
    public void Stp_x29_x30_pre_index_is_frame_pointer_based()
    {
        // stp x29, x30, [sp, #-16]!  →  imm7 = -2 (в единицах по 8 байт)
        uint imm7 = 0x7Eu; // -2 в семи битах
        uint w = 0xA9807BFD | (imm7 << 15);
        var r = Decoder.Decode(Word(w));
        Assert.Equal(PrologueKind.FramePointerBased, r.Kind);
    }

    [Fact]
    public void Unrecognised_word_is_unknown()
    {
        var r = Decoder.Decode(Word(0x00000000));
        Assert.Equal(PrologueKind.Unknown, r.Kind);
    }

    [Fact]
    public void Truncated_input_is_unknown_not_crash()
    {
        var r = Decoder.Decode(new byte[] { 0xFF, 0x03 });
        Assert.Equal(PrologueKind.Unknown, r.Kind);
    }
}

public class PrologueDecoderSelectionTests
{
    [Fact]
    public void Selects_a_decoder_matching_process_architecture()
    {
        var decoder = PrologueDecoders.ForCurrentProcess();

        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64)
            Assert.IsType<Arm64PrologueDecoder>(decoder);
        else
            Assert.IsType<Amd64PrologueDecoder>(decoder);
    }
}
