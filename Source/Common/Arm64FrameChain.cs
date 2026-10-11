using System;

namespace Multiplayer.Common
{
    /// <summary>Чтение слова из памяти процесса. За интерфейсом, чтобы обход кадров проверялся без игры.</summary>
    public interface IMemoryReader
    {
        long ReadLong(long address);
    }

    /// <summary>
    /// Обход цепочки кадров ARM64. По ABI Apple указатель кадра (x29) обязателен для всего кода:
    /// запись кадра [fp] = предыдущий fp, [fp+8] = адрес возврата. Размер кадра и раскладка
    /// LMF для обхода не нужны, нативные кадры проходятся той же цепочкой.
    /// </summary>
    public static class Arm64FrameChain
    {
        /// <summary>Предел расстояния между соседними кадрами. Больше — цепочка считается испорченной.</summary>
        public const long MaxFrameSpan = 16 * 1024 * 1024;

        /// <summary>
        /// Читает запись кадра по <paramref name="fp"/>. Возвращает false, если указатель
        /// не похож на кадр. <paramref name="nextFp"/> == 0 означает последний кадр потока:
        /// адрес возврата при этом всё равно отдан.
        /// </summary>
        public static bool TryStep(IMemoryReader mem, long fp, out long ret, out long nextFp)
        {
            ret = 0;
            nextFp = 0;

            if (fp == 0 || (fp & 7) != 0)
                return false;

            long next = mem.ReadLong(fp);
            long r = mem.ReadLong(fp + 8);

            if (r == 0)
                return false;

            if (next != 0)
            {
                // стек растёт вниз: кадр вызывающего лежит выше, выровнен и не слишком далеко
                if (next <= fp || (next & 7) != 0 || next - fp > MaxFrameSpan)
                    return false;
            }

            ret = r;
            nextFp = next;
            return true;
        }
    }

    /// <summary>
    /// Разбор машинного кода ARM64, который Mono сгенерировал для <c>long x = Magic;</c>:
    /// movz/movk собирают константу в регистр, затем str/stur кладёт её в локаль по [fp+смещение].
    /// По смещению считается адрес кадра, как GetRbp на amd64.
    /// </summary>
    public static class Arm64CodeScanner
    {
        public const int RegSp = 31;

        /// <summary>
        /// Ищет первое сохранение регистра, в котором собрана <paramref name="magic"/>.
        /// <paramref name="baseReg"/> — база адресации (29 = fp, 31 = sp), <paramref name="offset"/> — смещение в байтах.
        /// </summary>
        public static bool TryFindMagicStore(ReadOnlySpan<byte> code, long magic, out int baseReg, out int offset)
        {
            baseReg = 0;
            offset = 0;

            var regs = new long[32];
            var known = new bool[32];

            for (int i = 0; i + 4 <= code.Length; i += 4)
            {
                uint w = (uint)(code[i] | (code[i + 1] << 8) | (code[i + 2] << 16) | (code[i + 3] << 24));

                // movz xd, #imm16, lsl #(16*hw)
                if ((w & 0xFF800000u) == 0xD2800000u)
                {
                    int rd = (int)(w & 0x1F);
                    int hw = (int)((w >> 21) & 3);
                    regs[rd] = (long)((ulong)((w >> 5) & 0xFFFF) << (16 * hw));
                    known[rd] = true;
                    continue;
                }

                // movk xd, #imm16, lsl #(16*hw)
                if ((w & 0xFF800000u) == 0xF2800000u)
                {
                    int rd = (int)(w & 0x1F);
                    int hw = (int)((w >> 21) & 3);
                    if (known[rd])
                    {
                        ulong mask = 0xFFFFul << (16 * hw);
                        regs[rd] = (long)(((ulong)regs[rd] & ~mask) | ((ulong)((w >> 5) & 0xFFFF) << (16 * hw)));
                    }
                    continue;
                }

                int rt = (int)(w & 0x1F);
                int rn = (int)((w >> 5) & 0x1F);

                // str xt, [xn, #imm12*8]
                if ((w & 0xFFC00000u) == 0xF9000000u && known[rt] && regs[rt] == magic && (rn == 29 || rn == RegSp))
                {
                    baseReg = rn;
                    offset = (int)((w >> 10) & 0xFFF) * 8;
                    return true;
                }

                // stur xt, [xn, #simm9]
                if ((w & 0xFFE00C00u) == 0xF8000000u && known[rt] && regs[rt] == magic && (rn == 29 || rn == RegSp))
                {
                    int imm9 = (int)((w >> 12) & 0x1FF);
                    if ((imm9 & 0x100) != 0) imm9 -= 0x200;
                    baseReg = rn;
                    offset = imm9;
                    return true;
                }
            }

            return false;
        }
    }
}
