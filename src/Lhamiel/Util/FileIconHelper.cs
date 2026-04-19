using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Lhamiel.Util;

/// <summary>
/// サムネイル取得対象の拡張子セット
/// </summary>
file static class ThumbnailExtensions
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff", ".svg", ".avif", ".heic", ".heif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp"
    };

    public static bool IsThumbnailable(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);
    }
}

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

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    // IShellItemImageFactory GUID
    private static readonly Guid CLSID_ShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    // IShellItemImageFactory::GetImage は vtable のインデックス 3（IUnknown の3メソッド後）
    private delegate int GetImageDelegate(IntPtr pThis, SIZE size, int flags, out IntPtr phbm);

    private const int SIIGBF_BIGGERSIZEOK = 0x00;

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
    /// 画像・動画ファイルのサムネイルを取得する。
    /// Windows Shell の IShellItemImageFactory を使用。
    /// ファイルが存在しない、またはサムネイル取得に失敗した場合は null を返す。
    /// </summary>
    public static Bitmap? GetThumbnail(string filePath, int size = 32)
    {
        try
        {
            if (!File.Exists(filePath) || !ThumbnailExtensions.IsThumbnailable(filePath))
                return null;

            var iid = IID_IShellItemImageFactory;
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out var pShellItem);
            if (pShellItem == IntPtr.Zero)
                return null;

            try
            {
                // vtable[3] = GetImage (IUnknown: 0=QI, 1=AddRef, 2=Release, 3=GetImage)
                var vtable = Marshal.ReadIntPtr(pShellItem);
                var getImagePtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var getImage = Marshal.GetDelegateForFunctionPointer<GetImageDelegate>(getImagePtr);

                var requestedSize = new SIZE { cx = size, cy = size };
                var hr = getImage(pShellItem, requestedSize, SIIGBF_BIGGERSIZEOK, out var hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return null;

                try
                {
                    return HBitmapToBitmap(hBitmap);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                Marshal.Release(pShellItem);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"サムネイル取得失敗: {filePath}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ファイルのサムネイルまたはアイコンを取得する。
    /// 画像・動画ファイルはサムネイルを優先し、取得できなければアイコンにフォールバック。
    /// </summary>
    public static Bitmap? GetThumbnailOrIcon(string filePath, bool largeIcon = true, int thumbnailSize = 32)
    {
        if (File.Exists(filePath) && ThumbnailExtensions.IsThumbnailable(filePath))
        {
            var thumb = GetThumbnail(filePath, thumbnailSize);
            if (thumb is not null)
                return thumb;
        }
        return GetFileIcon(filePath, largeIcon);
    }

    /// <summary>
    /// 拡張子→Bitmap の共有キャッシュ（ジェネリックアイコン用）。
    /// SHGetFileInfo の P/Invoke コストは 1〜5ms と高く、同一拡張子のファイルが 100 件並ぶと
    /// UI スレッド上で 500ms オーダーのブロックになるため拡張子単位でメモ化する。
    /// 悪意ある/未知の拡張子多数ケースでメモリが無制限に増えないよう上限を設ける。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Bitmap> _extensionIconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>キャッシュの最大エントリ数。越えた場合は単純に全クリアする（LRU は Avalonia 依存を増やさないため非採用）。</summary>
    private const int MaxExtensionIconCacheEntries = 256;

    /// <summary>
    /// ファイルパスからアイコンを Avalonia Bitmap として取得する。
    /// ファイルが存在しない場合は拡張子ベースでジェネリックアイコンを取得し、拡張子単位でキャッシュする。
    /// </summary>
    public static Bitmap? GetFileIcon(string filePath, bool largeIcon = true)
    {
        // 実ファイルが存在しない場合は拡張子キャッシュを利用して P/Invoke を省略する。
        // 存在する実ファイル自体のアイコンは埋め込みアイコンが優先されるためキャッシュ対象外。
        var shouldCache = !File.Exists(filePath) && !Directory.Exists(filePath);
        if (shouldCache)
        {
            var ext = Path.GetExtension(filePath);
            var cacheKey = $"{(largeIcon ? "L:" : "S:")}{ext}";
            if (_extensionIconCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var loaded = LoadFileIcon(filePath, largeIcon);
            // 失敗（null）はキャッシュしない。後で別条件で成功するかもしれないため毎回再試行する。
            if (loaded is not null)
            {
                // 上限到達時はエントリを半数ずつ破棄する（全クリアだと頻繁に上限到達する環境で
                // P/Invoke のスパイクが再発しやすい。ConcurrentDictionary の Keys は順序
                // 保証がないため厳密な LRU ではないが、全クリアよりは UI のブロック周期が長くなる）。
                // Bitmap は unmanaged リソース（GDI+ ハンドル等）を持つので、evict 時は必ず
                // Dispose する。キャッシュ上限（256）到達はユースケース上まれで、その時点で
                // 衝突ダイアログは既に閉じられて UI 側の参照も切れている想定。
                if (_extensionIconCache.Count >= MaxExtensionIconCacheEntries)
                {
                    var targetRemove = _extensionIconCache.Count / 2;
                    var removed = 0;
                    foreach (var key in _extensionIconCache.Keys)
                    {
                        if (removed >= targetRemove) break;
                        if (_extensionIconCache.TryRemove(key, out var evicted))
                        {
                            evicted?.Dispose();
                            removed++;
                        }
                    }
                }

                // 並行スレッドが同じキーで先に設定していたら、自分の loaded を Dispose して
                // 既存を返す。TryAdd で検査することで race に負けた側の Bitmap がリークしない。
                if (!_extensionIconCache.TryAdd(cacheKey, loaded))
                {
                    loaded.Dispose();
                    return _extensionIconCache.TryGetValue(cacheKey, out var winner) ? winner : null;
                }
            }
            return loaded;
        }
        return LoadFileIcon(filePath, largeIcon);
    }

    /// <summary>
    /// 実際に SHGetFileInfo を呼び出してアイコンを読み込む。
    /// </summary>
    private static Bitmap? LoadFileIcon(string filePath, bool largeIcon)
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
    /// HBITMAP → Avalonia Bitmap 変換（サムネイル用）
    /// </summary>
    private static Bitmap? HBitmapToBitmap(IntPtr hBitmap)
    {
        var bmpStruct = new BITMAP();
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bmpStruct) == 0)
            return null;

        var width = bmpStruct.bmWidth;
        var height = Math.Abs(bmpStruct.bmHeight);
        var isBottomUp = bmpStruct.bmHeight > 0;
        if (width <= 0 || height <= 0)
            return null;

        var stride = width * 4;
        var bufferSize = stride * height;
        var pixels = new byte[bufferSize];

        if (bmpStruct.bmBitsPixel == 32)
        {
            GetBitmapBits(hBitmap, bufferSize, pixels);
        }
        else
        {
            return null;
        }

        // ボトムアップ（bmHeight > 0）の場合のみ上下反転
        byte[] finalPixels;
        if (isBottomUp)
        {
            finalPixels = new byte[bufferSize];
            for (var y = 0; y < height; y++)
                Array.Copy(pixels, (height - 1 - y) * stride, finalPixels, y * stride, stride);
        }
        else
        {
            finalPixels = pixels;
        }

        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            Marshal.Copy(finalPixels, 0, fb.Address, bufferSize);
        }
        return wb;
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
