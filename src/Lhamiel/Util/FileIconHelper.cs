using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Lhamiel.Util;

/// <summary>
/// Windows Shell API を使用してファイルの関連付けアイコンを取得するヘルパー
/// </summary>
public static class FileIconHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int nCount, ref BITMAP lpObject);

    [DllImport("gdi32.dll")]
    private static extern int GetBitmapBits(IntPtr hbmp, int cbBuffer, byte[] lpvBits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// ファイルパスからアイコンを Avalonia Bitmap として取得する。
    /// ファイルが存在しない場合は拡張子ベースでジェネリックアイコンを取得する。
    /// </summary>
    public static Bitmap? GetFileIcon(string filePath, bool largeIcon = true)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            var flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);

            var fileAttr = Directory.Exists(filePath) ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
                flags |= SHGFI_USEFILEATTRIBUTES;

            var result = SHGetFileInfo(filePath, fileAttr, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                return HIconToBitmap(shinfo.hIcon);
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ファイルアイコン取得失敗: {filePath}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// HICON → Avalonia Bitmap 変換（System.Drawing 不要）
    /// </summary>
    private static Bitmap? HIconToBitmap(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
            return null;

        try
        {
            var hbmp = iconInfo.hbmColor != IntPtr.Zero ? iconInfo.hbmColor : iconInfo.hbmMask;
            if (hbmp == IntPtr.Zero)
                return null;

            var bmpStruct = new BITMAP();
            if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), ref bmpStruct) == 0)
                return null;

            var width = bmpStruct.bmWidth;
            var height = bmpStruct.bmHeight;
            // 32bpp BGRA を期待
            var stride = width * 4;
            var bufferSize = stride * height;
            var pixels = new byte[bufferSize];

            if (bmpStruct.bmBitsPixel == 32)
            {
                GetBitmapBits(hbmp, bufferSize, pixels);
            }
            else
            {
                // 32bpp でない場合はフォールバック
                return null;
            }

            // GDI のビットマップはボトムアップなので上下反転
            var flipped = new byte[bufferSize];
            for (var y = 0; y < height; y++)
            {
                Array.Copy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);
            }

            // WriteableBitmap に書き込み
            var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = wb.Lock())
            {
                Marshal.Copy(flipped, 0, fb.Address, bufferSize);
            }
            return wb;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }
}
