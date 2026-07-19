namespace TachoGraphStudio.Core.Imaging;

// WriteableBitmap 等が要求する premultiplied BGRA への変換。
// 本合成(FR-19)にはストレートアルファを使い、表示用にのみこの変換を適用する
public static class PremultipliedAlpha
{
    public static byte[] FromStraightBgra(byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.Length % 4 != 0)
        {
            throw new ArgumentException($"BGRA バッファ長が 4 の倍数ではありません: {bgra.Length}", nameof(bgra));
        }

        byte[] result = new byte[bgra.Length];
        for (int offset = 0; offset < bgra.Length; offset += 4)
        {
            byte alpha = bgra[offset + 3];
            result[offset] = (byte)((bgra[offset] * alpha + 127) / 255);
            result[offset + 1] = (byte)((bgra[offset + 1] * alpha + 127) / 255);
            result[offset + 2] = (byte)((bgra[offset + 2] * alpha + 127) / 255);
            result[offset + 3] = alpha;
        }

        return result;
    }
}
