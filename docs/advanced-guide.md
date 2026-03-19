# 発展ガイド

**対象読者**: `PluginManager` の通常利用に慣れたあとで、より高度な構成・設計・診断・検証を扱いたい開発者  
**位置づけ**: 上級者向け補足資料

## 1. この資料の位置づけ

この資料は、Quick Start 用の `README.md` と、日常開発向けの `docs/developer-guide.md` から切り離した発展項目をまとめたものです。  
通常利用では、まず `PluginLoader`、`pluginsettings.json`、`PluginBase`、`PluginContext` を理解すれば十分です。

通常は次の順で学ぶことをおすすめします。

1. `README.md` の Quick Start を理解する
2. `docs/developer-guide.md` の日常開発向けガイドを読む
3. `PluginBase` を使って通常のプラグインを作る
4. `PluginContext` によるデータ受け渡しを理解する
5. 必要が明確になった項目だけ、この資料で確認する

## 2. 目次

1. [PluginHost](#3-pluginhost)
2. [ALC アンロードと解放確認](#4-alc-アンロードと解放確認)
3. [プラグインインターフェース設計](#5-プラグインインターフェース設計)
4. [実行モデルの詳細](#6-実行モデルの詳細)
5. [テストとデバッグ](#7-テストとデバッグ)
6. [セキュリティと信頼境界](#8-セキュリティと信頼境界)
7. [運用監視と保守](#9-運用監視と保守)
8. [迷ったときの判断基準](#10-迷ったときの判断基準)
9. [結論](#11-結論)

## 3. PluginHost

### 3-1. どういうときに検討するか

`PluginHost` は、通常のアプリ開発者が最初に触るものではありません。  
次のような場面で検討します。

- プラグイン実行環境をより明確に分離したい
- ホスト側の責務を整理したい
- 実行モデルを拡張したい
- 高度な運用・デバッグ・ホスティング構成を扱いたい

### 3-2. まず覚えること

- 通常利用の入口は `PluginLoader`
- 通常のプラグイン実装の入口は `PluginBase`
- `PluginHost` は発展的な構成要素

### 3-3. `PluginLoader` のままでよいケース

- プラグインをロードして実行したいだけ
- `pluginsettings.json` で順序制御できれば十分
- `PluginContext` でデータ共有できれば十分
- 標準利用を優先したい

### 3-4. PluginHost のクラス構成

`PluginHost` は Named Pipe を介してメインプロセスからの要求を受け取り、別プロセスでプラグインを実行します。  
責務ごとに次の 4 クラスに分割されています。

| クラス | 責務 |
|---|---|
| `PluginRegistry` | プラグインの Load / Unload / 状態管理 |
| `PluginRequestHandler` | Ping / Initialize / Execute / Unload コマンドの処理 |
| `PipeServer` | Named Pipe 接続受付・送受信ループ・並列数制御 |
| `PluginHostNotifier` | `PluginHost` 内部イベントを process 通知へ変換 |

`PluginHost` の運用情報は通常の Console 出力ではなく、メモリマップドファイル経由の process 通知として `PluginManager` 側へ渡します。
そのため、通常運用で `PluginHost` の標準出力を監視する前提は不要です。

### 3-5. JSON ベースのプロセス間コンテキスト転送

`OutOfProcess` モードでは、`PluginContext` をメインプロセスと `PluginHost` プロセスの間で転送します。  
現在は `Dictionary<string, JsonElement>` を使うことで、**JSON 表現を保持したまま転送**できます。
復元時は `JsonElement.ValueKind` に基づいて一部の基本型を復元しますが、元の CLR 型情報そのものを厳密に保持するわけではありません。

- `.ToJsonDictionary()` — 全プロパティを `Dictionary<string, JsonElement>` に変換
- `.ApplyJsonDictionary(data)` — JSON 表現からコンテキストへ適用

### 3-6. `PluginLoader` と `PluginHost` の採用判断

| 観点 | `PluginLoader` | `PluginHost` |
|---|---|---|
| 導入のしやすさ | 高い | 追加構成が必要 |
| 実行オーバーヘッド | 小さい | IPC / シリアライズのコストあり |
| 障害分離 | 同一プロセス内 | 別プロセスに分離しやすい |
| デバッグ | 比較的簡単 | 複数プロセスを追う必要がある |
| 配置・運用 | 単純 | プロセス管理が増える |

### 3-7. `OutOfProcess` を選ぶ際のトレードオフ

- 呼び出しごとにシリアライズと IPC が発生する
- ログの追跡先が増える
- 例外・タイムアウト・キャンセルの発生箇所が見えにくくなる
- 障害影響は分離しやすくなる

### 3-8. `PluginHostShutdownTimeoutMilliseconds` の考え方

`PluginHostShutdownTimeoutMilliseconds` は、`OutOfProcess` 実行時に `PluginHost` へ `Shutdown` を送ったあと、応答を待つ上限時間です。
通常の `PluginLoader` 利用では最初に意識する項目ではなく、別プロセス運用で終了待機を調整したい場合だけ設定します。

```json
{
  "PluginHostShutdownTimeoutMilliseconds": 3000
}
```

この値の意味は次のとおりです。

1. `Shutdown` を送る
2. 指定ミリ秒だけ応答を待つ
3. 間に合えば正常終了する
4. 間に合わなければ切断し、必要に応じて強制終了へ進む

設定を検討する場面:

- `PluginHost` が重い終了処理を持つ
- サービス停止時間の上限を短くしたい
- テストや運用で終了待ち時間を明示的に管理したい

通常は既定値のままで十分です。

### 3-9. Pipe 通信プロトコル（長さプレフィックス）

`PluginManager` と `PluginHost` 間の IPC は、`JSONL`（改行区切り）ではなく **長さプレフィックス** 方式です。  
フレームは次の形式で送受信します。

- `int32 little-endian length`
- `UTF-8 payload`（JSON）

`length` は `payload` のバイト長を表し、ヘッダー 4 バイトは含みません。

エンコーディング方針:

- `payload` は UTF-8（BOM なし）を使用する
- 不正な UTF-8 バイト列は置換せずエラー扱いにする

payload の契約:

- 要求: `PluginHostRequest`
- 応答: `PluginHostResponse`

上限値:

- フレーム最大サイズ: `1,048,576 bytes`（`1024 * 1024`）
- `length <= 0` または上限超過は不正フレームとして扱う

エラー時方針:

- 未キャンセル `OperationCanceledException` が送受信で発生した場合:
  - 接続を不正状態とみなし即時破棄する
  - 呼び出し元へ `IOException` を返す
- 受信で `bytesRead == 0` の場合:
  - 切断とみなし接続を破棄し、`IOException` を返す
- 不正 UTF-8 / 不正フレーム長 / 不正 JSON の場合:
  - 受信失敗として例外を返し、同一接続で継続しない
- `RequestId` 不一致応答は読み飛ばすが、上限回数（10回）を超えたら:
  - 接続を破棄し、`IOException` を返す

運用上の注意:

- 呼び出し元は `CancellationToken` でタイムアウトを明示する
- 送受信エラー後は同一接続を再利用しない

## 4. ALC アンロードと解放確認

### 4-1. どういうときに必要か

- 長時間稼働でメモリ使用量が増え続ける
- プラグイン差し替え後にメモリが戻らない
- アンロード後の再ロードで問題が起きる
- 参照が残っている疑いがある

### 4-2. 関連する主な要素

- `PluginLoader.Dispose()`
- `PluginLoader.DisposeAsync()`
- `PluginLoader.UnloadPluginAsync(...)`
- `UnloadVerifier`
- `WeakReference`
- `AssemblyLoadContext`

### 4-3. まず覚えること

- 通常は `Dispose` / `DisposeAsync` を使う
- より厳密に確認したいときだけ `UnloadPluginAsync` を使う
- 参照残りを調べるときだけ `UnloadVerifier` を使う

### 4-4. スキャン用 ALC が確実にアンロードされる仕組み

`PluginDescriptor` は `Type` を保持せず、`PluginTypeName`（完全修飾型名の文字列）を保持します。  
この 2 フェーズ構造により、スキャン用の一時 ALC を GC 対象にできます。

### 4-5. ALC の仕組みと利点

- プラグインの動的差し替え
- アンロード後の GC 回収
- バージョン衝突の回避

### 4-6. Dispose のタイミングと参照管理

```csharp
using var loader = new PluginLoader();
var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context);
```

**重要**: `Dispose` 後も `results` への参照を保持すると、`IPlugin` への強参照が残り ALC が GC されません。

### 4-7. UnloadPlugin による個別アンロード

差し替え時は `UnloadPluginAsync` を使い、GC 完了まで待機してください。

### 4-8. WinForms / WPF での `DisposeAsync` の推奨

UI アプリでは `DisposeAsync()` を使うと、UI スレッドをブロックせずに GC 完了を待機できます。

### 4-9. メモリリーク診断と長期稼働での注意点

- `loadResults` への参照保持
- 静的フィールド参照
- イベント購読解除漏れ

この 3 点は最初に疑ってください。

## 5. プラグインインターフェース設計

### 5-1. どういうときに検討するか

- メタデータ公開と実行責務を分けたい
- 初期化処理を別責務として整理したい
- テストしやすさを優先したい
- フレームワーク拡張を前提にしたい

### 5-2. 関連する主な要素

- `IPlugin`
- `IPluginMetadata`
- `IPluginInitializer`
- `IStageExecutor`
- `PluginBase`

### 5-3. まず覚えること

- 普通は `PluginBase`
- 全部を明示したいときだけ `IPlugin`
- 特殊な設計が必要なときだけ個別インターフェース

### 5-4. インターフェース階層の全体像

```
IPluginMetadata    ← メタデータのみ公開したい場合
IPluginInitializer ← 初期化責務のみ分離したい場合
IStageExecutor     ← 実行責務のみ分離したい場合

IPlugin            ← 上記 3 つを合成した複合インターフェース
PluginBase         ← IPlugin の基底クラス（推奨）
```

### 5-5. 代表的な実装方針

- 通常は `PluginBase` のオーバーライドで十分
- ロード時 1 回の処理は `OnInitializeAsync`
- ステージ実行は `OnExecuteAsync`
- 外部リソース解放は `DisposeAsyncCore`

### 5-6. 互換性を意識した設計

上級者向けでは、`IPlugin` のシグネチャだけでなく **設定ファイル・ステージ・`PluginContext` キーも契約** として扱います。

- `Id` を変えると設定や依存関係が壊れる
- ステージ名を変えると実行順序の前提が壊れる
- `PluginContext` のキー名や型の変更は後続プラグインに影響する

## 6. 実行モデルの詳細

### 6-1. どういうときに検討するか

- `Order` と実際の実行順序が一致しない
- `PluginContext` の書き込みタイミングが問題になる
- 並列・逐次を意識してプラグイン設計したい
- `LoadAsync` と `LoadFromConfigurationAsync` の違いを理解したい

### 6-2. 同一 Order 並列・異なる Order 逐次

`LoadFromConfigurationAsync` では、同じ `Order` のプラグインは並列実行、異なる `Order` は逐次実行です。

### 6-3. `LoadAsync` との違い

| ロード方法 | 実行方式 |
|---|---|
| `LoadFromConfigurationAsync` | `Order` グループ単位で逐次 / グループ内は並列 |
| `LoadAsync` | 全プラグインを一括並列 |

### 6-4. まず覚えること

- `Order` が同じプラグインは並列実行される
- 同一キーへ同時書き込みする場合は注意する
- 書き込み → 読み込みを保証したいなら `Order` を分ける

### 6-5. `Order` と `PluginDependencies` の役割分担

- `Order` は大まかな段階分け
- `PluginDependencies` は必須の前後関係表現
- 循環依存が出る場合は設計見直しのサイン

### 6-6. カスタムステージを増やす判断

カスタムステージは、業務上の意味が明確に分かれる場合だけ追加すると保守しやすくなります。

## 7. テストとデバッグ

### 7-1. どういうときに必要か

- リファクタリング前後で安全性を高めたい
- 統合テストを CI に組み込みたい
- DLL ロードやアンロードの不具合を調査したい
- 詳細ログやコールバックで挙動を追いたい

### 7-2. 関連する主な要素

- `PluginLoaderTests`
- `IntegrationTests`
- `PluginExecutorTests`
- `IPluginLoaderCallback`
- `UnloadVerifier`
- `Microsoft.Extensions.Logging`

### 7-3. まず覚えること

- 単体テストは日常的に使う
- 統合テストは DLL 配置やビルド順序も含めて考える
- 詳細なアンロード診断は必要になってから扱う

### 7-4. 単体テストの書き方

- 失敗したロード結果がスキップされること
- 対象外ステージがスキップされること
- `PluginContext` の受け渡しが成立すること

### 7-5. 統合テストの構成

- 実際の DLL をテスト用ディレクトリに配置する
- テストごとに独立ディレクトリを使う
- CI では DLL 不在時の扱いを明確にする

### 7-6. callback によるデバッグ

通常のロード・実行通知は `IPluginLoaderCallback`、`PluginExecutor` の進捗通知は `IPluginExecutorCallback` を使います。
`OutOfProcess` の詳細通知は `IPluginProcessCallback` を使うと、`PluginHost` 起動、別プロセス側のロード・初期化・実行・アンロード・シャットダウン通知を型安全に追跡できます。

`OnNotification(PluginProcessNotification notification)` を実装すると、次のようなホスト運用通知もまとめて受け取れます。

- `PipeServerStarted` / `PipeServerStopped`
- `ClientConnectionWaiting` / `ClientConnected`
- `HostShutdownRequested` / `HostFatalError`
- `ConnectionProcessingFailed` / `RequestProcessingFailed` / `ServerInstanceFailed`
- `UnloadAllStarted` / `UnloadAllCompleted`

標準出力の読み取りではなく、process 通知を診断の入口として扱うのが現在の前提です。

### 7-7. `UnloadVerifier` によるアンロード確認

アンロードが完了しない場合は、参照残りや静的参照を疑ってください。

### 7-8. 障害切り分けの進め方

1. `pluginsettings.json` の `Id`・`Stage`・パスを確認する
2. ロード時の問題か実行時の問題かを分ける
3. コールバックやログで失敗したプラグイン ID・試行回数・ステージを確認する
4. 再ロード絡みなら `Dispose` / `UnloadPluginAsync` の呼び忘れを確認する
5. `OutOfProcess` の場合は通信とシリアライズ復元を疑う

### 7-9. 変更前後で最低限守りたいテスト

- ロード成功 / 失敗判定
- 対象外ステージのスキップ
- `PluginContext` のキー受け渡し
- 差し替え後の再ロード
- 型付きコンテキスト転送

## 8. セキュリティと信頼境界

### 8-1. どういうときに検討するか

- 外部配布されたプラグインを読み込む
- 複数チームで別々にプラグインを開発する
- サーバーやサービスで長期運用する
- 権限分離まで意識したい

### 8-2. まず覚えること

- プラグインは任意の .NET コードである
- `OutOfProcess` は完全なサンドボックスではない
- 信頼できないプラグインをそのまま読み込む設計は避ける

### 8-3. 信頼できるプラグインだけを配置する

- 配置元が信頼できるプラグインだけを `plugins/` に置く
- 配布経路を固定する
- 必要なら署名・ハッシュ・配布元検証を導入する
- `plugins/` への書き込み権限を最小化する

### 8-4. 強い隔離が必要な場合の考え方

必要に応じて、サービスアカウント分離、ファイル権限分離、OS / コンテナ制御と組み合わせてください。

## 9. 運用監視と保守

### 9-1. どういうときに必要か

- 長期稼働する
- プラグインを差し替えながら運用する
- 障害時に短時間で原因を特定したい
- バージョン混在や段階的リリースを行いたい

### 9-2. 監視したい主な項目

| 項目 | 理由 |
|---|---|
| ロード失敗数 | 配置ミス・互換性崩れを検知しやすい |
| リトライ回数 / タイムアウト数 | 一時障害か恒久障害かを見分けやすい |
| ステージ別実行時間 | 遅いプラグインを特定しやすい |
| `AssemblyLoadContext` 数 | アンロード漏れを追いやすい |
| メモリ使用量 | 長期運用時のリーク傾向を確認しやすい |
| 実行中プラグインのバージョン | 差し替え後の不整合を追いやすい |

### 9-3. プラグイン差し替え時の進め方

1. 新規処理投入を抑える
2. `UnloadPluginAsync` でアンロードする
3. DLL を差し替える
4. 再ロードして疎通確認する
5. ロールバック用に直前版を保持する

### 9-4. ログに残したい情報

- プラグイン ID
- ステージ ID
- 試行回数
- DLL パス
- 例外メッセージ
- どのプロセスで失敗したか

## 10. 迷ったときの判断基準

### 10-1. 本編だけで十分なケース

- プラグインをロードして実行できればよい
- `pluginsettings.json` と `PluginBase` で要件を満たせる
- `PluginContext` によるデータ共有で足りる
- 高度な運用や内部設計までは不要

### 10-2. 発展ガイドを読むべきケース

- 一段複雑なホスト構成が必要
- 解放失敗やメモリ調査が必要
- 責務分割や設計整理をしたい
- 統合テストや詳細デバッグを導入したい
- 信頼境界や運用監視まで含めて設計したい

## 11. 結論

- 最初はこの資料を読まなくてよい
- まず `README.md` の Quick Start を優先する
- 設計判断・運用判断・信頼境界まで必要になったときだけ、この資料を参照する
