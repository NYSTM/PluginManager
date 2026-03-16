# PluginManager テスト項目一覧

**対象バージョン**: .NET 8 / C# 12  
**対象プロジェクト**: `PluginManager`, `PluginManager.Core`, `PluginManager.Ipc`, `PluginHost`, `PluginManagerTest`

> この資料は、現在のワークスペースに存在する主要なテスト観点を整理したものです。  
> 単体テスト・統合テスト・非機能テストの観点を、実装責務ごとに俯瞰できるようにまとめています。

## 1. この資料の目的

この資料では、次の内容を整理します。

1. どの責務に対してどの種別のテストがあるか
2. 正常系・異常系・境界値・非機能のどこを見ているか
3. 今後追加すると有効な観点は何か

## 2. テストの分類

本ワークスペースのテストは、概ね次の 4 種類に分かれます。

- **単体テスト**: クラス単位でロジックを検証する
- **コンポーネントテスト**: 複数クラスの連携を小さな範囲で検証する
- **統合テスト**: 実際のファイル・DLL・ALC・IPC を含めて検証する
- **非機能テスト**: 応答性、並列性、解放健全性などを退行検知する

## 3. 実装領域ごとのテスト項目

### 3-1. `PluginManager.Core`

#### `PluginContext`
- 型付き取得 (`GetProperty`, `TryGetProperty`, `GetPropertyOrDefault`, `GetPropertyOrThrow`)
- 参照型 / 值型 / `null` の扱い
- `ContextKey<T>` を使った取得・設定
- `RemoveProperty`, `Clear`, `CreateScope`
- `ToJsonDictionary`, `ApplyJsonDictionary`
- スレッドセーフ前提の共有データ利用

#### `PluginStage`, `PluginStageRegistry`, `PluginStageJsonConverter`
- 既定ステージの解決
- 文字列変換
- JSON シリアライズ / デシリアライズ
- 未知ステージや境界値の扱い

#### `PluginErrorInfo`
- エラーカテゴリ分類
- 例外情報の保持
- 表示用メッセージ整形

#### `PluginConfiguration`, `PluginConfigurationLoader`
- 設定ファイルの読み込み
- 欠損値の既定値補完
- 不正 JSON / 不正値の扱い
- `StageOrders`, `PluginDependencies`, `RetryCount`, `TimeoutMilliseconds` などの解釈

### 3-2. `PluginManager` 本体

#### `PluginLoader`
- 設定ファイル経由のロード
- ディレクトリ指定のロード
- ロード結果の集約
- 実行時のステージ通知
- 実行グループ分割
- `Dispose` / `DisposeAsync`
- タイムアウト、リトライ、インターバル処理
- 内部ワークフロー (`LoadPluginWithTimeoutAsync`, `LoadPluginWithRetryAsync`, `LoadPluginWithIntervalAsync`, `CompleteExecuteAsync`)

#### `PluginExecutor`
- フラット実行
- グループ実行
- 同一グループ並列実行
- グループ間逐次実行
- スキップ条件（ロード失敗・対象外ステージ）
- 実行例外の捕捉
- `MaxDegreeOfParallelism` 制限
- キャンセル時の挙動

#### `PluginDiscoverer`
- DLL 列挙
- `IPlugin` 実装の発見
- `PluginAttribute` 付き型の解釈
- 属性なし型のフォールバック
- 既定ステージ適用
- 無効アセンブリのスキップ
- 重複 ID 検出

#### `PluginDependencyResolver`, `PluginOrderResolver`, `DependencyGraph`
- 依存グラフ構築
- トポロジカルソート
- 循環依存検出
- 手動 `Order` との統合
- ステージ別実行順序解決

#### `InProcessPluginRuntime`
- ALC を使った同一プロセスロード
- 初期化成功 / 失敗
- 不正型の扱い
- 個別アンロード / 全体アンロード
- コンテキスト除去の整合性

#### `OutOfProcessPluginRuntime`
- ランタイム生成
- プロキシ登録 / 解除
- `Unload`, `UnloadAsync`, `UnloadAll`
- shutdown 送信失敗の吸収
- 通知キュー publish
- `PluginHost.exe` 探索
- エラー応答から例外へのマッピング
- 内部状態解放の健全性

#### `PluginLoadContext`
- 共有アセンブリ委譲
- 解決可能アセンブリのロード
- 解決不能アセンブリの `null`
- native DLL 解決成功 / 失敗

#### `RetryHelper`
- 初回成功
- リトライ成功
- 恒久失敗
- リトライ上限到達
- キャンセル
- タイムアウト
- 遅延キャンセル吸収

#### `UnloadVerifier`
- 強参照がある場合のアンロード失敗
- キャンセル
- タイムアウト
- 診断ログ出力
- 実プラグインでの解放確認

#### 通知 Publisher 群
- `PluginLoaderNotificationPublisher`
- `PluginExecutorNotificationPublisher`
- `PluginProcessNotificationPublisher`

観点:
- 通常通知
- 必須値不足時の分岐
- コールバック例外の握りつぶし
- ログ出力分岐

### 3-3. `PluginManager.Ipc`

#### `PluginHostClient`
- 接続成功 / タイムアウト
- 未接続送信エラー
- 切断時エラー
- 不正応答エラー
- request/response ID の対応
- Dispose 後利用禁止
- プロセス終了処理
- 例外吸収
- 通常時レスポンス
- 高負荷同時要求

#### `MemoryMappedNotificationQueue`
- enqueue / drain
- 空キュー
- JSON 復元
- 容量超過
- 破棄後利用禁止

#### IPC 契約 (`PluginHostRequest`, `PluginHostResponse`, `PluginProcessNotification`)
- シリアライズ / デシリアライズ整合性

### 3-4. `PluginHost`

#### `PluginRequestHandler`
- `Ping`
- `Load`
- `Initialize`
- `Execute`
- `Unload`
- `Shutdown`
- 不明コマンド
- Load/Unload の成功通知 / 失敗通知
- 初期化失敗、実行失敗、キャンセル、タイムアウト

#### `PluginRegistry`
- ロード成功 / 重複ロード
- 不正型ロード失敗
- アンロード成功 / 失敗
- `TryGet`
- `UnloadAll`
- `Dispose`

#### `PluginHost.PluginLoadContext`
- マネージド依存解決 成功 / 失敗
- アンマネージド依存解決 成功 / 失敗

#### `PipeServer`, `Program`, `PluginHostNotifier`
- パイプ起動
- 通知送出
- プロセス起動経路

## 4. 統合テスト項目

`PluginManagerTest\Integration\IntegrationTests.cs` では、次のような統合観点を見ています。

- エンドツーエンドのロード・実行
- 複数プラグインの並列ロード
- `Order` による順序制御
- 短タイムアウト設定時の挙動
- 成功・失敗混在時の継続処理
- コンテキスト共有
- ALC 解放後の GC 回収

## 5. 非機能テスト項目

### 5-1. 通常時レスポンス
- `PluginHostClient.SendRequestAsync` の単発応答が通常時に安定していること
- `PluginLoader.LoadFromConfigurationAsync` が通常構成で極端に遅くならないこと
- `RetryHelper` のタイムアウト応答性

### 5-2. 高負荷レスポンス
- IPC 同時要求で応答破損が起きないこと
- `PluginHostClient.SendRequestAsync` が多数同時要求でも応答の対応関係を維持できること
- `PluginExecutor.ExecutePluginsInGroupsAsync` で高並列実行が逐次実行より高速であること
- 並列実行上限が守られること
- 同時ロード時に極端な遅延が発生しないこと

### 5-3. リソース健全性
- `OutOfProcessPluginRuntime` の内部辞書（クライアント・通知キュー・プロキシ）が解放されること
- 特定アセンブリだけをアンロードして、他方の状態が維持されること
- ALC が最終的に GC 回収されること
- shutdown / unload 時の例外で状態が壊れないこと
- ロード前後の `GC.GetTotalMemory` 差分が異常に増え続けないこと
- out-of-process 経路のロード / アンロード後に `PluginHost` プロセス残骸が残らないこと
- 反復的なロード / 実行 / アンロードで状態が破綻しないこと
- 連続 20 回のロード / アンロードで例外が蓄積しないこと
- 連続 20 回のロード / 実行 / アンロードで内部状態が壊れないこと

## 6. 関連資料

- `README.md`
- `docs/developer-guide.md`
- `docs/advanced-guide.md`
