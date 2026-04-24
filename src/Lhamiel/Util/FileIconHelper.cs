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

    /// <summary>
    /// 挿入順を保持するキュー。eviction 時に <see cref="ConcurrentDictionary{TKey,TValue}.Keys"/> の
    /// O(N) スナップショット生成を避け、先頭から O(1) で取り出して FIFO 削除する。
    /// 途中で削除されたキーは <see cref="_extensionIconCache"/> 側で TryRemove が失敗するだけなので
    /// 両構造の厳密同期は不要（近似 LRU ≒ FIFO）。
    /// </summary>
    private static readonly ConcurrentQueue<string> _insertionOrder = new();

    /// <summary>キャッシュの最大エントリ数。</summary>
    private const int MaxExtensionIconCacheEntries = 256;

    /// <summary>
    /// eviction 実行中フラグ（0 = idle, 1 = running）。
    /// 複数スレッドが同時に <see cref="GetFileIcon"/> を呼んで上限到達と判断した場合、
    /// それぞれが半数削除を実行すると合計で N/2 以上が消えてキャッシュヒット率が崩壊する。
    /// また <see cref="ConcurrentDictionary{TKey,TValue}.Keys"/> はスナップショット生成で
    /// O(n) のロックを取るため、多重実行は競合コストも高い。
    /// よって <see cref="Interlocked.CompareExchange(ref int, int, int)"/> で 1 スレッドだけが
    /// eviction を実行し、他スレッドはスキップ（次の追加タイミングで再評価される）。
    /// </summary>
    private static int _evictionInProgress;

    /// <summary>
    /// キャッシュ件数の独立カウンタ。
    /// <see cref="ConcurrentDictionary{TKey,TValue}.Count"/> は内部で全バケットをロックするため
    /// ホットパス（ファイルリスト表示など短時間に大量呼び出しされる経路）での参照コストが O(N) に
    /// 近づき、キャッシュが大きくなるほどボトルネックになる。
    /// TryAdd 成功時 <see cref="Interlocked.Increment(ref int)"/>、TryRemove 成功時 Decrement で
    /// 管理する近似カウンタ（Dictionary 本体と一瞬ずれることはあるが、eviction トリガの判定には十分）。
    /// </summary>
    private static int _cacheCount;

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
                //
                // evict した Bitmap は **ここで Dispose しない**。ConflictCellViewModel 等が
                // GetFileIcon の戻り値を強参照として保持しており、ダイアログ表示中にも
                // 256 以上のユニーク拡張子で evict が走りうる。Dispose するとまだ画面に
                // 表示中の行でレンダリング失敗 / 白抜きアイコンが起きる。
                // 参照が UI から切れたあとは .NET の GC + Bitmap のファイナライザが
                // unmanaged ハンドル（GDI+）を回収するのに任せる（多少遅れるが安全側）。
                // 一度に 1 スレッドだけ eviction を実行。他スレッドは超過分を次回呼び出しに任せる。
                // CompareExchange は 0→1 に成功したスレッドだけが本体に入り、失敗した側は単に
                // TryAdd に進む（上限を数エントリ超過する可能性はあるが許容範囲）。
                // _cacheCount は独立カウンタ（ConcurrentDictionary.Count は O(N) ロックのため回避）。
                if (_cacheCount >= MaxExtensionIconCacheEntries &&
                    Interlocked.CompareExchange(ref _evictionInProgress, 1, 0) == 0)
                {
                    try
                    {
                        // 挿入順キューから FIFO で古い半数を削除。_extensionIconCache.Keys の
                        // O(N) スナップショット生成を回避し、TryDequeue は O(1)。
                        var targetRemove = _cacheCount / 2;
                        var removed = 0;
                        while (removed < targetRemove && _insertionOrder.TryDequeue(out var oldKey))
                        {
                            if (_extensionIconCache.TryRemove(oldKey, out _))
                            {
                                Interlocked.Decrement(ref _cacheCount);
                                removed++;
                            }
                            // TryRemove 失敗 = 既に誰かが削除済み。カウンタは他スレッドで
                            // 減算済みなのでスキップして次のキーを試す。
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _evictionInProgress, 0);
                    }
                }

                // 並行スレッドが同じキーで先に設定していたら、自分の loaded を Dispose して
                // 既存を返す。この loaded はまだ UI にバインドされておらず呼び出し元にも
                // 返していないので、Dispose しても安全（race 敗者だけのローカル参照）。
                if (!_extensionIconCache.TryAdd(cacheKey, loaded))
                {
                    loaded.Dispose();
                    return _extensionIconCache.TryGetValue(cacheKey, out var winner) ? winner : null;
                }
                Interlocked.Increment(ref _cacheCount);
                _insertionOrder.Enqueue(cacheKey);
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
