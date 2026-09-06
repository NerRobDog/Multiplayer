using System;

namespace MultiplayerCommon.Tracing
{
    public enum PrologueKind
    {
        /// <summary>Пролог выделил кадр известного размера.</summary>
        FrameAlloc,

        /// <summary>Кадр адресуется через регистр-указатель кадра.</summary>
        FramePointerBased,

        /// <summary>Пролог не распознан. Вызывающий решает, что делать.</summary>
        Unknown,
    }

    public readonly struct PrologueResult
    {
        public readonly PrologueKind Kind;
        public readonly long StackUsage;
        public readonly int BytesConsumed;

        public PrologueResult(PrologueKind kind, long stackUsage, int bytesConsumed)
        {
            Kind = kind;
            StackUsage = stackUsage;
            BytesConsumed = bytesConsumed;
        }

        public static PrologueResult Unknown => new(PrologueKind.Unknown, 0, 0);
    }

    /// <summary>
    /// Разбирает пролог функции, сгенерированной Mono, и сообщает размер кадра.
    /// Реализация зависит от архитектуры процессора.
    /// </summary>
    public interface IPrologueDecoder
    {
        PrologueResult Decode(ReadOnlySpan<byte> code);
    }

    /// <summary>
    /// Декодер под amd64. Последовательности взяты из генератора Mono:
    /// mono/mini/mini-amd64.c и mono/arch/amd64/amd64-codegen.h.
    /// </summary>
    public sealed class Amd64PrologueDecoder : IPrologueDecoder
    {
        public PrologueResult Decode(ReadOnlySpan<byte> code)
        {
            // sub rsp,XX  →  48 83 EC XX
            if (code.Length >= 4 && code[0] == 0x48 && code[1] == 0x83 && code[2] == 0xEC)
                return new PrologueResult(PrologueKind.FrameAlloc, code[3], 4);

            // sub rsp,XXXXXXXX  →  48 81 EC XX XX XX XX
            if (code.Length >= 7 && code[0] == 0x48 && code[1] == 0x81 && code[2] == 0xEC)
            {
                long usage = code[3] | ((long)code[4] << 8) | ((long)code[5] << 16) | ((long)code[6] << 24);
                return new PrologueResult(PrologueKind.FrameAlloc, usage, 7);
            }

            // push rbp  →  55
            if (code.Length >= 1 && code[0] == 0x55)
                return new PrologueResult(PrologueKind.FramePointerBased, 0, 1);

            return PrologueResult.Unknown;
        }
    }

    /// <summary>
    /// Декодер под ARM64. Инструкции фиксированной длины по 4 байта, little-endian.
    /// </summary>
    public sealed class Arm64PrologueDecoder : IPrologueDecoder
    {
        // sub sp, sp, #imm12   (64-битная форма SUB immediate, Rn = Rd = sp)
        private const uint SubSpMask  = 0xFF8003FFu;
        private const uint SubSpValue = 0xD10003FFu;

        // stp x29, x30, [sp, #imm]!   (пре-индексная запись пары)
        private const uint StpFpLrMask  = 0xFFC07FFFu;
        private const uint StpFpLrValue = 0xA9807BFDu;

        public PrologueResult Decode(ReadOnlySpan<byte> code)
        {
            if (code.Length < 4)
                return PrologueResult.Unknown;

            uint word = (uint)(code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24));

            if ((word & SubSpMask) == SubSpValue)
            {
                long usage = (word >> 10) & 0xFFF;
                return new PrologueResult(PrologueKind.FrameAlloc, usage, 4);
            }

            if ((word & StpFpLrMask) == StpFpLrValue)
                return new PrologueResult(PrologueKind.FramePointerBased, 0, 4);

            return PrologueResult.Unknown;
        }
    }

    public static class PrologueDecoders
    {
        private static readonly IPrologueDecoder Current =
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64
                ? new Arm64PrologueDecoder()
                : (IPrologueDecoder)new Amd64PrologueDecoder();

        public static IPrologueDecoder ForCurrentProcess() => Current;
    }
}
