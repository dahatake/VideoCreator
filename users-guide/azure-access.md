# Azure の準備と共有利用者の認証

<!-- language-nav -->
[日本語](azure-access.md) | [English](en/azure-access.md) | [한국어](ko/azure-access.md) | [简体中文](zh-CN/azure-access.md)
<!-- /language-nav -->

[はじめに](../README.md) / [クイックスタート](quickstart.md) / [設定・API](configuration-api.md)

VideoCreator は利用者の PC で動かします。Azure へのアプリ公開、動画保存用の
Storage、Azure SQL は必要ありません。素材・SQLite・描画・顔検出・高画質化は
従来どおりローカルです。GitHub Copilot は任意の代替であり、Azure のサービスではありません。

## 必要なサービス

| 用途 | 必要なもの | Entra ID で利用するロール |
|---|---|---|
| 字幕の書き起こし（必須） | Azure AI Services / Speech、S0、カスタムサブドメイン。MAI-Transcribe-2 を使うには対応リージョン（下記）。それ以外のリージョンは標準モデル | Cognitive Services Speech User |
| AI 選定・テロップ・投稿文等 | Azure AI Services / Azure OpenAI、Structured Outputs 対応のチャットデプロイ | Cognitive Services OpenAI User |
| 意味ベースの話題分割 | 同リソースの埋め込みデプロイ（未設定なら語彙ベース） | Cognitive Services OpenAI User |

字幕の書き起こしは、既定で Microsoft の音声認識モデル **MAI-Transcribe-2** を使います。
Azure Speech の Fast Transcription API に `enhancedMode` を付けて呼ぶため、デプロイの作成は不要です。
MAI-Transcribe-2 が使えるリージョンは Central India、East US、North Europe、Southeast Asia、
West Europe、West US、West US 2 です（2026-11 時点。Japan East は未対応）。
対応外のリージョンや、2 時間・250 MB 以上の録音では、自動で標準モデルに切り替え、
接続テスト・設定画面・ジョブの警告にその旨を表示します。標準モデルに固定するには
`VideoCreator:Asr:Model` を `standard` にします。画像分析（Azure AI Vision）は使っていません。

複数用途に対応する **1つの Azure AI Services リソースを共用できます**。
Foundry のプロジェクト、Video Indexer、Event Grid、追加のマネージド ID は、
現在のアプリの実行経路には不要です。既存のものを削除する必要もありません。

### この環境の確認結果（2026-11-20）

- サブスクリプション: `dahatake Internal Subscription`
  (`0e930937-43f5-484d-ba9e-1dd1151cf7ba`)
- テナント: `16b3c013-d300-468d-ac64-7eda0820b6d3`
- リソースグループ: `rg-videocreator-jpe`
- リソース（1つだけ）: `videocreator-mai-9709`（AIServices / S0 / Southeast Asia）。
  チャット・埋め込み・画像生成・字幕（MAI-Transcribe-2）をすべてこの1つで賄います。
  以前の `videocreator-ai-40800` は削除し、purge も済ませました。
- Foundry / Speech の送信先: `https://videocreator-mai-9709.cognitiveservices.azure.com/`
- チャット: `gpt-chat-latest`
- 埋め込み: `text-embedding-3-large`
- Viva Engage 画像: `FLUX-1.1-pro`

必要なリソースとデプロイは作成済みでした。重複したサービス・モデル容量は追加しません。
追加の固定容量費用がないことと、利用が無料であることは別です。Speech の処理時間、
チャット・埋め込みのトークン、接続テストは従量課金です。共有利用者の利用分も
リソース所有側のサブスクリプションに請求されます。Azure の予算・利用量を管理してください。
既存のチャット・埋め込みデプロイは **GlobalStandard** です。リソースの場所が
Japan East でも推論が日本国内に限定される保証ではありません。組織のデータ所在地要件を確認します。

**検証結果（2026-11-18）:**

- [infra/main.bicep](../infra/main.bicep) の `what-if` は、既存アカウントのタグ・
  `armFeatures`・`associatedProjects`・`defaultProject` と、モデルの `currentCapacity` の差分を報告しました。
  タグを空に指定した再デプロイは `Succeeded` です（重複作成なし）。
- [scripts/grant-azure-access.ps1](../scripts/grant-azure-access.ps1) で、RG へのロールの付与・
  再実行（重複なし）・削除を実際に確認しました。所有者アカウントには RG スコープの2ロールを付与済みです。
- アプリの接続テストで、Entra ID による Speech・チャットの JSON Schema 応答・埋め込みが成功しました。
- キー認証は **組織ポリシーによって使えません**。管理グループに割り当てられた
  「AzTB 17 - Disable Local auth for all Cognitive Services」（effect: modify）が、
  すべての Cognitive Services に `disableLocalAuth=true` を強制します。
  `false` で更新しても要求時点で `true` に書き換えられることを確認しました。
  アプリのキー認証はキーを送信し、401 を「キー拒否」と診断できます。
- このテナントでは条件付きアクセスによりアプリ登録を作成できません。そのため、
  権限を持たない別の ID での検証はしていません。

## 管理者: Azure リソースを作成する

[infra/main.bicep](../infra/main.bicep) は、必要なものをまとめて作成します。対象は
AIServices（S0、カスタムサブドメイン、既定はキー認証無効）、チャットと埋め込みのデプロイ、
および任意で利用者ロールです。既存リソースの変更は `what-if` で確認してください。
`userObjectIds` は既存の同一スコープ・ロールの割り当てを自動検出しないため、
既に IAM やスクリプトで付与した利用者を指定すると競合する場合があります。
既存利用者にはこのパラメーターを使わず、下記のスクリプトを使用してください。

```powershell
az login --tenant <テナントID>
az group create -n rg-videocreator-jpe -l japaneast
az deployment group what-if -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<一意の名前>
az deployment group create  -g rg-videocreator-jpe -f infra/main.bicep -p accountName=<一意の名前>
```

出力の `speechEndpoint`、`foundryEndpoint`、`chatDeployment`、`embeddingDeployment`、`tenantId`
を設定画面に入力します。モデル名・バージョン・容量・リージョンは、
そのリージョンで利用できる値をパラメーターで指定します。

## 管理者: リソースグループに利用者を追加する

「アカウントを追加する」とは、そのリソースグループの **アクセス制御 (IAM) で
ロールを割り当てる**ことです。RG に Contributor や Owner を付けるだけでは、
Speech のトークンによる API 実行権限にはなりません。

1. 利用者がリソースと同じ Entra テナントのメンバー、または招待・承諾済みゲストであることを確認します。
2. 次のスクリプトを実行します（実行者には Owner、User Access Administrator、
   または RBAC 管理者の権限が必要です）。

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\grant-azure-access.ps1 -User alice@contoso.com
   ```

   `-User` には UPN、ゲストのメールアドレス、またはオブジェクト ID を指定します。
   メールで検索できない場合や Microsoft Graph の検索が制限されている場合は、
   対象テナントの Entra ID の「ユーザー」画面で確認したオブジェクト ID を指定します。
   Azure CLI の既定のテナントと対象サブスクリプションのテナントを一致させてください。
   書き起こしだけなら `-SpeechOnly`、取り消しは `-Remove` です。
   既にある RG 直下のロールは重複して作成せず、削除も RG 直下の割り当てだけが対象です。
3. ロールの反映を数分待ち、利用者の PC から接続テストします。

ポータルで行う場合は、対象 RG → **アクセス制御 (IAM)** → **ロールの割り当ての追加**で、
上表の2つの利用者ロールを付与します。

RG のスコープなら、その RG 内の該当リソースに権限が継承されます。アクセス範囲を
1リソースだけに限定したいときは、リソース単位で同じロールを付けます。
一般利用者に Owner、Contributor、Cognitive Services Contributor は不要です。
Speech User / OpenAI User はリソースのキーを取得するための権限ではありません。

利用を止めるときは IAM で対象のロール割り当てを削除します。グループ経由・
リソース単位・サブスクリプションからの継承が残っていないかも確認してください。

## 利用者: 設定画面で選ぶ

### Microsoft Entra ID（共有利用で推奨）

利用者の PC で Azure CLI をインストールし、リソースのテナントにサインインします。

```powershell
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3
```

アプリの **設定 → AI の接続** には、Azure の接続は **Microsoft Foundry の1つだけ**あります。
字幕（Azure Speech）も同じリソースを使い、別の Speech 接続はありません。

1. **Microsoft Foundry**: 認証方式（**Microsoft Entra ID** か **API キー**）を最初に選びます。
   選ばなかった方式の入力欄は隠れます。続けて、エンドポイント、チャット・埋め込み・Viva Engage 画像の
   デプロイ名、テナント ID を入力します。**接続テスト**で確認し、**テストして保存**します。
2. 接続テストはチャット・埋め込み・字幕の書き起こし（Speech）を確認します。
   字幕の書き起こしに失敗すると保存できません。MAI-Transcribe-2 の対応リージョン外では標準モデルになります。
3. 旧版で個別に保存した Speech 接続は使われず、次に設定を保存したときに消えます。

#### Azure から選ぶ（エンドポイント・デプロイ名を入力しない）

**Azure から選択** を押すと、サインイン中の Entra ID で見える
Microsoft Foundry のリソースを一覧して選べます（テナント ID 欄が空なら既定のサインイン）。

- Azure Resource Manager（`https://management.azure.com`）から、アクセスできる全
  サブスクリプションの Cognitive Services アカウントを読み、Speech（音声テキスト変換 API を
  持つもの）と Foundry（Azure OpenAI API を持つもの）だけを出します。Computer Vision などは出しません。
- Foundry では、選んだリソースのデプロイから **チャット**、**埋め込み**、**Viva Engage 画像** の候補を
  出します。Entra ID 認証で候補が取得できた項目は、コンボボックスから選択し、重複する手入力欄は表示しません。
  候補を取得できない項目や API キー認証では手入力もできます。
- リソースを選ぶとエンドポイントが設定されます。キー認証が無効
  （`disableLocalAuth`）のリソースでは Microsoft Entra ID に切り替えます。保存はしません。
  必ず **接続テスト** → **テストして保存** で確かめてください。
- 一覧に必要なのは、リソースの読み取り（管理プレーン）権限です。上の手順で付与する
  `Cognitive Services Speech User` / `Cognitive Services OpenAI User` にも含まれます。
  利用権限（データプレーン）は Azure の `permissions` API で確かめ、足りなければ
  「利用権限が見つかりません」と表示します。拒否割り当てや反映待ちで結果が変わることがあるため、
  最終判断は接続テストです。
- 一覧のときに **キーは読みません**（`listKeys` を呼びません）。キー認証で使う場合も、
  キーは管理者から受け取って入力します。
- 一部のサブスクリプションが読めない場合も、読めた分だけ一覧し、読めなかった
  サブスクリプション名を表示します。カスタムドメインのない Speech リソースは
  Entra ID では使えないため、警告を表示します。

Entra ID のパスワードやクライアントシークレットはアプリに入力しません。
バックエンドは Azure Identity の `DefaultAzureCredential` を使い、Azure CLI などの
既存のサインインから対象テナントのトークンを取得します。アプリ側からブラウザーの
ログインを自動起動する方式ではありません。環境変数等で別の資格情報を設定している
PC では、その資格情報も認証チェーンの対象になります。

### API キー

それぞれの接続で **API キー** を選び、管理者から安全な方法で受け取った
**そのリソースのキー**を入力して **テストして保存**します。異なるリソースのキーを
使い回さないでください。キーは Windows では DPAPI（その PC のその Windows ユーザー）
で暗号化します。他の PC・ユーザーに設定ファイルをコピーしても復号できません。
OS 暗号化を実装していない環境ではキーの保存を拒否しますが、接続テストはできます。

**現在の共用リソースでは、組織ポリシーによりキー認証が無効に固定されています。**
このサブスクリプションでは、キーを選ぶと 401（キー拒否）になります。キー認証を使うには、
組織のポリシーで許可された別のサブスクリプションに、`disableLocalAuth=false` で作成した
リソースが必要です（Bicep の `-p disableLocalAuth=false`）。セキュリティ統制を回避する
ポリシー除外を、利用者の判断で作成しないでください。この変更はアプリのラジオボタンでは行いません。
キーは個人別の権限ではなくリソース共通の資格情報です。Entra ID を使う利用者にはキー配布が不要です。

保存したキーは画面・設定取得 API に返しません。空欄のキーは同じ接続先
（HTTPS のホスト・ポート）に保存済みのものだけを再利用します。Entra ID に切り替えて
保存すると、その接続の保存済みキーを削除します。**認証失敗時に別方式へ自動で
フォールバックしません。**

## 保存・テスト・トラブル対応

- 画面設定は `DataDir` の `ai-settings.json` に保存され、起動設定より優先されます。
  再起動後も有効です。**保存した接続を削除**すると起動設定へ戻ります。
- 保存後に開始する処理から適用されます。処理中のジョブの接続は切り替えません。
- Speech のテストは、生成と同じ Fast Transcription API に **合成した1秒の無音**
  を送ります。利用者の音声は送りません。字幕生成と同じ認証・経路の疎通検証であり、
  実動画の認識精度の保証ではありません。結果には使うモデル（MAI-Transcribe-2 か標準）を表示します。
- 「標準モデルで書き起こします」と出る場合は、そのリソースのリージョンが MAI-Transcribe-2 に
  対応していません。**Azure から選択** の一覧にも対応の有無を表示します。
- Foundry はチャットの厳密な JSON Schema 応答と、指定時には埋め込みをテストします。
- LLM 不使用でも Speech は必要です。Speech の接続テスト・保存はこのモードでもできます。
- `401`: キーの間違い・キー認証無効、またはテナント・トークンを確認します。
  `403`: 上表の利用ロール・対象ユーザー・スコープ・反映待ち・ネットワーク制限を確認します。
  `404`: エンドポイントとデプロイ名、`429`: レート制限・共有クォータを確認します。
- エンドポイントは HTTPS のリソースルートです。Speech には `/openai/v1` や
  `speechtotext/...` を付けません。Entra ID ではリージョン共通アドレスでなく
  カスタムサブドメインを使います。
- `run.cmd --check` の Azure 診断は起動設定を参照します。画面で保存した接続を
  確認するには、この設定画面の接続テストを使います。
- キーやアクセストークンを文書・スクリーンショット・問い合わせに貼り付けないでください。

公式根拠:
[Speech RBAC](https://learn.microsoft.com/azure/ai-services/speech-service/role-based-access-control) /
[Fast Transcription](https://learn.microsoft.com/azure/ai-services/speech-service/fast-transcription-create) /
[MAI-Transcribe-2](https://learn.microsoft.com/azure/ai-services/speech-service/mai-transcribe) /
[Azure OpenAI RBAC](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control)。
