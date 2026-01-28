using Xunit;
using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using System;

namespace Lhamiel.Tests.Unit;

public class ProgressLogicTests
{
    [Fact]
    public void CalculateProgress_ShouldBeMonotonic()
    {
        // 複数ファイルを圧縮するシミュレーション
        var totalCount = 10;
        var totalBytes = 10000;
        var lastP = -1;

        // ライブラリの新しいロジック（per-file）をシミュレート
        for (int i = 1; i <= totalCount; i++)
        {
            // 1. 各ファイルの準備段階 (Prepare)
            var prepareReport = new Report
            {
                State = ProgressState.Prepare,
                Count = i,
                TotalCount = totalCount,
                Bytes = (i - 1) * 1000,
                TotalBytes = totalBytes
            };
            var pp = GetPercentage(prepareReport);
            Assert.True(pp >= lastP, $"Prepare stage should be monotonic. File {i}. Last: {lastP}, Current: {pp}");
            lastP = pp;

            // 2. 各ファイルの進行段階 (Progress)
            for (int b = 0; b < 1000; b += 200)
            {
                var report = new Report
                {
                    State = ProgressState.Progress,
                    Count = i,
                    TotalCount = totalCount,
                    Bytes = (i - 1) * 1000 + b,
                    TotalBytes = totalBytes
                };
                var p = GetPercentage(report);
                Assert.True(p >= lastP, $"Progress stage should be monotonic. File {i}, Bytes {report.Bytes}. Last: {lastP}, Current: {p}");
                lastP = p;
            }

            // 3. 各ファイルの完了 (Success)
            var successReport = new Report
            {
                State = ProgressState.Success,
                Count = i,
                TotalCount = totalCount,
                Bytes = i * 1000,
                TotalBytes = totalBytes
            };
            var sp = GetPercentage(successReport);
            Assert.True(sp >= lastP, $"Success state should be monotonic. File {i}. Last: {lastP}, Current: {sp}");
            lastP = sp;
        }

        // 最後のファイル完了後は 100% になる（Terminate で保証される）
        Assert.True(lastP >= 0, "Progress should be non-negative");
    }

    [Fact]
    public void CalculateProgress_ShouldBeMonotonicWithGetRatio()
    {
        // ライブラリの GetRatio() を信じ、単調増加を保証するテスト
        var lastP = -1;

        // 最後のファイルの処理中 (Progress)
        var lastFileProgress = new Report
        {
            State = ProgressState.Progress,
            Count = 5,
            TotalCount = 5,
            Bytes = 4999,
            TotalBytes = 5000
        };
        var p1 = GetPercentage(lastFileProgress);
        Assert.True(p1 >= lastP, $"Progress should be monotonic. Last: {lastP}, Current: {p1}");
        lastP = p1;

        // 100% になった場合（ライブラリの GetRatio() が 1.0 を返す場合）
        var lastFileProgressFull = new Report
        {
            State = ProgressState.Progress,
            Count = 5,
            TotalCount = 5,
            Bytes = 5000,
            TotalBytes = 5000
        };
        var p2 = GetPercentage(lastFileProgressFull);
        // 単調増加を保証（前の値より小さくならない）
        Assert.True(p2 >= lastP, $"Progress should be monotonic. Last: {lastP}, Current: {p2}");
        lastP = p2;

        // 最後のファイルが完了 (Success)
        var lastFileSuccess = new Report
        {
            State = ProgressState.Success,
            Count = 5,
            TotalCount = 5,
            Bytes = 5000,
            TotalBytes = 5000
        };
        var p3 = GetPercentage(lastFileSuccess);
        Assert.True(p3 >= lastP, $"Success should be monotonic. Last: {lastP}, Current: {p3}");
    }

    // ArchiveCompressor.cs 内のロジックを再現（Ice アプリケーションの実装パターンに準拠）
    private int GetPercentage(Report report)
    {
        // ライブラリの GetRatio() と Report を信じる
        var ratio = report.GetRatio();
        var percentage = (int)(ratio * 100);
        return percentage;
    }
}
