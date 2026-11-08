# 資料の検証と保守

[アプリREADME](../README.md) / [ガイド目次](README.md) / [技術リファレンス](technical-reference.md) / [制作方針](documentation-brief.md)

## 一次資料と検証範囲

このガイドは2026-11-08の作業ツリーを調査したものです。作業中に存在したアプリ変更を取り消さず、実装・設定・テストを根拠に説明しました。Gitに未コミットの変更も含むため、特定リリースタグに対する仕様保証ではありません。

| 説明対象 | 確認方法 |
|---|---|
| 起動・準備 | [run.cmd](../run.cmd)、[setup.ps1](../scripts/setup.ps1)、[AppOptions](../src/VideoCreator.App/AppOptions.cs) |
| 画面・入力・操作 | [app.js](../src/VideoCreator.App/wwwroot/js/app.js)と実ブラウザー |
| API・承認・処理条件 | [Api.cs](../src/VideoCreator.App/Api.cs)、[JobQueue](../src/VideoCreator.App/JobQueue.cs)、既存[HTTP例](../src/VideoCreator.App/exports.http) |
| モデル・処理・出力 | [Models](../src/VideoCreator.Core/Models/Models.cs)、[PipelineRunner](../src/VideoCreator.Core/Pipeline/PipelineRunner.cs)、Renderer、QaEngine |
| Azureの前提 | Microsoft Learnの[Fast Transcription](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create)と、実装内の接続・認証コード |
| 画面SVG | 実アプリをEdgeで表示し、PNGをSVG内へ埋め込む。模式図とは区別 |
| 説明・図の整合性 | ローカルリンク・見出しアンカー・XML・画像埋め込み・文字領域・ブラウザー描画の機械検証 |

### 今回の実行確認

- 通常のRelease出力先が起動済みプロセスにロックされていたため、そのプロセスを停止せず**独立したartifacts出力先**でビルド。成功（警告0、エラー0）。
- 専用DataDir・ProjectsDir・portで起動し、health応答と必要フィルタの検出を確認。
- 合成動画＋書き起こしキャッシュを用い、実UIの権利確認→P4候補生成→確認画面→直接最終書き出しを実行。
- 最終QAのハード結果がすべてpassであることを確認。
- `mp4` / `srt` / `ass` / `editplan` / `metadata` / `thumb1` / `thumb2` / `thumb3` の8キーがあり、対応APIで空でないファイルを取得できることを確認。
- 実際のファイルをffprobeで確認：H.264、1080×1920、AAC、48kHz、2ch。P4の出力は27.20秒、QA測定値は−14.0 LUFS、−12.5 dBTP（正弦波fixtureの結果であり実発話の音質評価ではない）。
- 同じ素材から2本のシリーズ候補を生成して、シリーズの構造と画面を確認。
- ガイドの全SVGとローカルリンクを[検証スクリプト](tools/validate.mjs)で確認。

最終検証結果：**Markdown 9ファイル（ルートREADMEと[制作方針](documentation-brief.md)を含む）、ローカルリンク・アンカー211件、SVG 20点（模式図5点・実画面15点）**。XML、埋め込みPNG、図の文字領域、Edgeでの描画、ヘルパースクリプトの構文、差分の空白チェックがすべて成功しました。最終キャプチャではSRTの時刻書式、ASSのEvents、編集計画のCanvas/Timeline、サムネイルのPNG形式、MP4のコーデック・解像度・音声形式・P4尺も検証しています。

**今回実行していないこと**：実Azure/Copilotへの素材送信、実発話の認識精度試験、全P1〜P10の生成、すべてのOSダイアログ・保存先移動・削除・一括承認の操作、リポジトリ全テストの再実行、SNSへの投稿、第三者の初心者によるユーザビリティ評価。これらの機能説明はソースと既存テストを根拠とし、今回の実行済み結果とは区別します。

音量等のQAが通る合成データを使ったことは、本物の音声との内容一致を検証した証拠ではありません。実利用では、自分の許諾済み素材で最初の1本を確認してください。

## スクリーンショットの再作成

### 1. 前提

.NET SDK 10、FFmpeg・フォント・モデル、Node.js、OSにインストールされたMicrosoft Edgeを使います。アプリ利用者はNode.js不要ですが、**画面を再作成する開発者には必要**です。Playwrightは[ui/package.json](../ui/package.json)の既存依存を利用します。依存がない場合は `ui` で `npm ci` を実行します。ブラウザーの追加ダウンロードは不要です。

コマンドはリポジトリ直下のPowerShellで実行します。例のパスは自分の作業用パスに変更してください。

### 2. 隔離fixtureを準備

[seed.cs](tools/seed.cs)は既存データのあるフォルダーを拒否します。新しい空のディレクトリを指定します。

```powershell
dotnet run --file users-guide\tools\seed.cs -- C:\Temp\VideoCreatorGuideCapture
```

生成するのは260秒のテストパターン動画と正弦波、プロジェクト／素材のDB記録、[talk_transcript.json](../tests/VideoCreator.Tests/Fixtures/talk_transcript.json)を130秒ずらして2回並べた書き起こしキャッシュです。字幕は**この正弦波を認識した結果ではありません**。初回の130秒fixtureは無音カット後に2本分の最小尺を満たさなかったため、延長しました。

このfixtureキーは現在のASRキャッシュ方式と空辞書に対応します。キャッシュ実装・辞書・ASR Nameを変更した場合はseedも更新してください。検証用エンドポイントを実利用に転用しないでください。

### 3. 専用ポートで起動

別のPowerShellで次を実行します。起動中のアプリや本物のプロジェクトのDataDirは指定しません。追加のDictionary環境変数など、実利用の上書き設定が残っていない新しいターミナルを使います。

```powershell
$env:VideoCreator__DataDir = 'C:\Temp\VideoCreatorGuideCapture\data'
$env:VideoCreator__ProjectsDir = 'C:\Temp\VideoCreatorGuideCapture\projects'
$env:VideoCreator__Port = '18765'
$env:VideoCreator__OpenBrowser = 'false'
$env:VideoCreator__LocalOnly = 'true'
$env:VideoCreator__Asr__Provider = 'azure'
$env:VideoCreator__Asr__Endpoint = 'http://127.0.0.1:1/'
dotnet run --project src\VideoCreator.App -c Release
```

portが使用中なら別portを指定し、以後のURLもそろえます。LLMは無効、ASRはfixtureキャッシュを読みます。万一キャッシュが一致しない場合も外部サービスではなくloopbackの未使用ポートへの失敗になる構成です。

起動済みバイナリと競合する場合は、独立した出力先でビルドして起動します。

```powershell
dotnet build src\VideoCreator.App\VideoCreator.App.csproj -c Release `
  --artifacts-path C:\Temp\VideoCreatorGuideBuild
dotnet C:\Temp\VideoCreatorGuideBuild\bin\VideoCreator.App\release\VideoCreator.App.dll `
  --contentRoot "$PWD\src\VideoCreator.App"
```

これも上記の環境変数を指定した同じPowerShellから実行します。

### 4. 取得して検証

起動の応答を確認してから、もう1つのターミナルで実行します。

```powershell
Invoke-RestMethod http://127.0.0.1:18765/api/health
node users-guide\tools\capture.mjs http://127.0.0.1:18765
node users-guide\tools\validate.mjs
git diff --check -- README.md users-guide
```

[capture.mjs](tools/capture.mjs)は `guide-demo` だけの専用環境、LLM不使用、ASR loopback endpointであることをチェックしてから操作します。実アプリに新しい生成・書き出しを残し、[images](images)内の画面SVGを更新します。個人名を含み得るCLIパスは画面上でマスクします。画像取得後、起動用ターミナルでCtrl+Cを押し、不要になった専用作業ディレクトリだけを整理してください。

## 更新時のチェックリスト

1. UIラベル・初期値・表示条件が変わったら[画面説明](screens.md)とキャプチャを更新する。
2. 設定・API record・エラー・状態が変わったら[設定・API](configuration-api.md)と操作例を更新する。
3. 出力キー・QA・キャッシュ・時間軸が変わったら[技術資料](technical-reference.md)とseed/captureを更新する。
4. 初回の権限・料金・送信先が変わったら[クイックスタート](quickstart.md)と[README](../README.md)を更新する。
5. SVGを機械検証し、主要図を目視して読めるか確認する。リンク検証は外部URLの可用性や説明の真偽までは証明しない。
6. 開発者以外の初回利用者に、ガイドだけで1本を書き出してもらい、迷った欄と失敗条件を記録する。今回この第三者評価は未実施。
