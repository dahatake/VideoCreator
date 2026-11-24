# VideoCreator ドキュメントサイト（GitHub Pages）

公開先: <https://dahatake.github.io/VideoCreator/>

このサイトは **リポジトリの Markdown から生成** されます。手で書いたページはなく、内容の正本は常に次のファイルです。

| サイトの場所 | 生成元 |
| --- | --- |
| トップ / 使う人へ / カスタマイズする人へ | `site/src/i18n.mjs`（導線の文言）＋各ガイドの見出し |
| ガイド（`guide/…`） | `README.md`、`users-guide/*.md`（日本語）、`users-guide/{en,ko,zh-CN}/*.md` |
| 仕様（`docs/…`） | `docs/PRD.md`、`docs/IMPLEMENTATION.md`、`docs/ANALYTICS.md` |
| 編集パターン | `presets/P*.json` と `src/VideoCreator.App/wwwroot/js/server-resources.js` |
| バージョン表記 | `Directory.Build.props` の `VersionPrefix` |

`main` でこれらのどれかが変わると [.github/workflows/pages.yml](../.github/workflows/pages.yml) が自動でビルド・検査・公開します（手動実行は Actions の **Pages → Run workflow**）。

## ローカルで確認する

```powershell
cd site
npm ci
node build.mjs            # site/dist に生成
node check.mjs            # SEO・リンク・アンカー・hreflang・sitemap の検査
node verify-browser.mjs "$env:TEMP\vc-site-shots"   # Edge で axe・操作・reduced motion を検査し、スクリーンショットを保存
```

`verify-browser.mjs` はインストール済みの Edge を使います（`BROWSER_CHANNEL=chrome` で Chrome）。ブラウザーの同梱ダウンロードは不要です。

## 公開前の検査（CI と同じ）

- `scripts/verify-localized-docs.mjs` — 翻訳版が日本語版と同じ構造か
- `check.mjs` — 全ページの `title`/`description` が一意で長さが適切、`canonical`・`hreflang`（4言語＋`x-default`）・OGP・JSON-LD がある、内部リンクとアンカーが全部解決する、`sitemap.xml` が全ページを含む、ビルド警告がない
- `verify-browser.mjs` — axe-core（WCAG 2.x A/AA）違反 0、コンソールエラー 0、検索・言語切替・テーマ・ステップ・グラフ並べ替え・Mermaid・モバイル幅・`prefers-reduced-motion`・JavaScript 無効時の表示

どれかが失敗すると公開されません。

## 構成

| パス | 役割 |
| --- | --- |
| `build.mjs` | 生成器（marked で Markdown を解析し、リンクをサイト内 URL に書き換え） |
| `check.mjs` / `verify-browser.mjs` | 上記の検査 |
| `src/i18n.mjs` | UI とハブページの4言語の文言 |
| `src/assets/site.css` / `site.js` | デザインと動き（JavaScript なしでも全文が読めます） |
| `static/` | アイコンと OGP 画像 |
| `tools/make-og.mjs` | OGP 画像（`static/og/og-*.png`）の再生成。Edge と Windows のフォントを使うため手元で実行してコミットします |

## SEO について

- URL は言語ごとに分かれ（日本語はルート、`en/`・`ko/`・`zh-CN/`）、相互に `hreflang` で結ばれます。
- `sitemap.xml` の `lastmod` は各ソースファイルの最終コミット日時です。
- プロジェクトサイト（`/VideoCreator/`）の `robots.txt` は検索エンジンに読まれません。サイトマップは Google Search Console / Bing Webmaster Tools にリポジトリー所有者が登録してください。所有確認のメタタグは、環境変数 `GOOGLE_SITE_VERIFICATION` / `BING_SITE_VERIFICATION` を設定してビルドすると出力されます。
