// nephilim.jp サブドメインのランディングページ配信 Worker。
//
// このホスト名は R2 バケットのカスタムドメインでもある（Velopack 自動更新ファイルの配信元）。
// Cloudflare では「同一ホスト名に張った Worker Route は Custom Domain より優先」される。
// そこで:
//   - "/" と "/index.html" : バンドルしたランディングページ HTML を返す
//   - それ以外のパス        : fetch(request) で R2 カスタムドメイン origin にそのまま委譲する
//
// Worker Route は「同一ゾーンの fetch() の対象にならない」仕様なので再帰ループせず、
// 更新ファイル (*.nupkg / releases.*.json / *-Setup.exe 等) は R2 がネイティブ配信する
// （Range / 条件付きリクエスト / キャッシュ / Content-Type をそのまま維持）。
import landingHtml from "./index.html";

export default {
  async fetch(request) {
    const { pathname } = new URL(request.url);

    if (pathname === "/" || pathname === "/index.html") {
      return new Response(landingHtml, {
        headers: {
          "content-type": "text/html; charset=utf-8",
          "cache-control": "public, max-age=300",
        },
      });
    }

    // 更新配信パスは一切加工せず R2 へそのまま委譲（リグレッション防止）。
    return fetch(request);
  },
};
