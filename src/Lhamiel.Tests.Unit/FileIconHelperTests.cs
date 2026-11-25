using System.Runtime.InteropServices;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// アイコンの画素読み出し（向き・α）の回帰テスト。実際の Win32 アイコン API を使う。
/// 16x16 のアイコン: 上半分が赤、下半分が青、左上 1 画素だけ AND マスクで透明。
/// </summary>
public sealed class FileIconHelperTests
{
    private const int Size = 16;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte[] presbits, uint dwResSize, bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [Theory]
    [InlineData(24, 0)]   // 24bpp の旧形式アイコン（変換後の hbmColor は α が全 0）
    [InlineData(32, 0)]   // α が全 0 の 32bpp アイコン
    [InlineData(32, 255)] // α を持つ通常の 32bpp アイコン（マスクは α に影響しない）
    public void ReadIconPixels_ReturnsTopDownPixelsWithAlphaRecoveredFromMask(int bitsPerPixel, byte sourceAlpha)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "GDI アイコン API は Windows 限定");
        var image = BuildIconImage(bitsPerPixel, sourceAlpha);
        var hIcon = CreateIconFromResourceEx(image, (uint)image.Length, true, 0x00030000, Size, Size, 0);
        Assert.NotEqual(IntPtr.Zero, hIcon);

        byte[]? pixels;
        int width;
        int height;
        try
        {
            pixels = FileIconHelper.ReadIconPixels(hIcon, out width, out height);
        }
        finally
        {
            DestroyIcon(hIcon);
        }

        Assert.NotNull(pixels);
        Assert.Equal(Size, width);
        Assert.Equal(Size, height);

        // 上半分は赤（BGRA = 0,0,255,255）、下半分は青（255,0,0,255）: トップダウンで返る。
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(pixels, 1, 0));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(pixels, 1, Size - 1));
        // マスクで透明にした左上は、α が全 0 のアイコンでも 0。α を持つ 32bpp アイコンはソースの値のまま。
        Assert.Equal(bitsPerPixel == 32 && sourceAlpha == 255 ? new byte[] { 0, 0, 255, 255 } : new byte[] { 0, 0, 0, 0 },
            Pixel(pixels, 0, 0));
    }

    private static byte[] Pixel(byte[] pixels, int x, int y)
    {
        var offset = (y * Size + x) * 4;
        return pixels[offset..(offset + 4)];
    }

    /// <summary>ICO 内の 1 画像（BITMAPINFOHEADER + XOR + AND）を組み立てる。行は下から上の順で格納する。</summary>
    private static byte[] BuildIconImage(int bitsPerPixel, byte sourceAlpha)
    {
        var bytesPerPixel = bitsPerPixel / 8;
        var xorRow = (Size * bytesPerPixel + 3) / 4 * 4;
        const int andRow = 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(Size);
        writer.Write(Size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)bitsPerPixel);
        writer.Write(0);
        writer.Write((xorRow + andRow) * Size);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (var storedRow = 0; storedRow < Size; storedRow++)
        {
            var y = Size - 1 - storedRow;
            for (var x = 0; x < Size; x++)
            {
                var red = y < Size / 2;
                writer.Write((byte)(red ? 0 : 255));
                writer.Write((byte)0);
                writer.Write((byte)(red ? 255 : 0));
                if (bitsPerPixel == 32)
                    writer.Write(sourceAlpha);
            }
            for (var pad = Size * bytesPerPixel; pad < xorRow; pad++)
                writer.Write((byte)0);
        }

        for (var storedRow = 0; storedRow < Size; storedRow++)
        {
            // AND マスク（1 = 透明）。トップダウンの y=0 の左端（先頭バイトの最上位ビット）だけ透明にする。
            var isTopRow = storedRow == Size - 1;
            writer.Write(isTopRow ? 0x80 : 0);
        }
        writer.Flush();
        return stream.ToArray();
    }
}
