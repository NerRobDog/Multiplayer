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
