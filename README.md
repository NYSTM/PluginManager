# PluginManager アプリ開発者向け手引書

**対象読者**: PluginManager を使って C# / VB アプリを開発するジュニア〜ミドルレベルの開発者  
**対象バージョン**: .NET 8 / C# 12

---

## 目次

1. [全体像](#1-全体像)
2. [プロジェクト参照の設定](#2-プロジェクト参照の設定)
3. [処理フロー詳解](#3-処理フロー詳解)
   - [3-1. アプリ起動〜ロードまでの流れ](#3-1-アプリ起動ロードまでの流れ)
   - [3-2. ステージ実行の流れ](#3-2-ステージ実行の流れ)
   - [3-3. リトライとタイムアウトの流れ](#3-3-リトライとタイムアウトの流れ)
4. [設定ファイル（pluginsettings.json）](#4-設定ファイル-pluginsettings-json)
5. [主要クラス・インターフェース一覧](#5-主要クラス-インターフェース一覧)
6. [実装パターン（C# / VB）](#6-実装パターン-c-vb)
   - [6-1. 最小実装（C#）](#6-1-最小実装-c)
   - [6-2. 最小実装（VB）](#6-2-最小実装-vb)
   - [6-3. プラグインの実装例（C#）](#6-3-プラグインの実装例-c)
   - [6-4. ステージ間のデータ受け渡し](#6-4-ステージ間のデータ受け渡し)
7. [PluginContext の使い方](#7-plugincontext-の使い方)
8. [エラーハンドリング](#8-エラーハンドリング)
9. [よくある間違い](#9-よくある間違い)
10. [チェックリスト](#10-チェックリスト)
11. [VB.NET 常駐プログラムの例](#11-vb-net-常駐プログラムの例)
12. [常駐プログラム（Windows Service）での使用シナリオ](#12-常駐プログラム-windows-service-での使用シナリオ)
13. [パフォーマンス最適化ガイド](#13-パフォーマンス最適化ガイド)
14. [ALC アンロードのベストプラクティス](#14-alc-アンロードのベストプラクティス)
---

## 1. 全体像

```
あなたのアプリ（WinForms / Console / WPF など）
    │
    │  using PluginManager;
    │
    ▼
┌─────────────────────────────────────────────────┐
│                  PluginLoader                    │
│  ┌─────────────┐  ┌──────────────────────────┐  │
│  │PluginDiscover│  │  PluginOrderResolver     │  │
│  │ DLL をスキャン│  │  Order 順にグループ化    │  │
│  └─────────────┘  └──────────────────────────┘  │
│  ┌─────────────────────────────────────────────┐ │
│  │           PluginLoadContext                  │ │
│  │  各 DLL を独立したコンテキストでロード        │ │
│  │  （アンロード可能・プロセス分離）            │ │
│  └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────┐
│                 PluginExecutor                   │
│  SupportedStages が一致するプラグインだけを実行  │
│  Contains が O(1)（FrozenSet による高速判定）   │
└─────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────┐
│                 PluginContext                    │
│  プラグイン間でデータを受け渡す共有辞書          │
│  ConcurrentDictionary（スレッドセーフ）         │
└─────────────────────────────────────────────────┘
```

---

## 2. プロジェクト参照の設定

### C# アプリ（.csproj）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- フレームワーク本体 -->
    <ProjectReference Include="..\PluginManager.Core\PluginManager.Core.csproj" />
    <ProjectReference Include="..\PluginManager\PluginManager.csproj" />
  </ItemGroup>

  <!-- 設定ファイルを出力ディレクトリにコピー -->
  <ItemGroup>
    <Content Include="..\PluginManager\pluginsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>pluginsettings.json</Link>
    </Content>
  </ItemGroup>

  <!-- プラグイン DLL をビルド後に plugins/ フォルダへコピー -->
  <Target Name="CopyPlugins" AfterTargets="Build">
    <MakeDir Directories="$(OutputPath)plugins" />
    <Copy SourceFiles="..\MyPlugin\bin\$(Configuration)\net8.0-windows\MyPlugin.dll"
          DestinationFolder="$(OutputPath)plugins"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

> **重要**: プラグイン DLL は `ProjectReference` で参照しないでください。  
> `plugins/` フォルダに配置して PluginLoader が動的にロードする構成が正しい設計です。

---

## 3. 処理フロー詳解

### 3-1. アプリ起動〜ロードまでの流れ

```
アプリ起動
    │
    ▼
pluginsettings.json を読み込む
    │ PluginConfigurationLoader.Load()
    │
    ▼
plugins/ フォルダの DLL をスキャン
    │ PluginDiscoverer.Discover()
    │
    │ 各 DLL を一時的な PluginLoadContext でロード
    │ [Plugin] 属性 または IPlugin 実装を持つ型を探索
    │ → PluginDescriptor（Id / Name / Version / SupportedStages）を生成
    │ ※スキャン用の LoadContext はここで Unload される
    │
    ▼
pluginsettings.json の StageOrders に従い Order でグループ化
    │ PluginOrderResolver.BuildExecutionGroups()
    │
    │  Order:1 → [plugin-a, plugin-b]  ← このグループは並列ロード
    │  Order:2 → [plugin-c]
    │
    ▼
グループ順に LoadPlugin() を実行
    │
    │  ┌──────────────────────────────────────────┐
    │  │ LoadPluginWithRetryAsync                  │
    │  │  ┌───────────────────────────────────┐   │
    │  │  │  LoadPluginWithTimeoutAsync        │   │
    │  │  │    LoadPluginAsync()               │   │
    │  │  │    タイムアウト超過 → TimeoutException│  │
    │  │  └───────────────────────────────────┘   │
    │  │  失敗 & 一時的エラー → RetryDelay 後リトライ │
    │  │  成功 or 恒久的エラー → そのまま返す       │
    │  └──────────────────────────────────────────┘
    │
    ▼
IPlugin.InitializeAsync(context, cancellationToken) を呼ぶ
    │
    ▼
PluginLoadResult（Success / Instance / Error）のリストを返す
```

### 3-2. ステージ実行の流れ

```
await loader.ExecutePluginsAndWaitAsync(loadResults, stage, context)
    │
    ▼
PluginExecutor が loadResults を走査
    │
    │  r.Success == true  かつ
    │  r.Instance.SupportedStages.Contains(stage) == true  ← FrozenSet により O(1)
    │       ↓ 該当するプラグインだけ実行
    │       ↓ 該当しないプラグインは自動スキップ
    │
    ▼
IPlugin.ExecuteAsync(stage, context, cancellationToken)
    │
    ▼
Task.WhenAll() で全タスクの完了を待機
    │
    ▼
IReadOnlyList<PluginExecutionResult> （各プラグインの実行結果リスト）
```

### 3-3. リトライとタイムアウトの流れ

```
LoadPlugin() 呼び出し
    │
    ├─ 成功 ──────────────────────────────▶ PluginLoadResult(Success)
    │
    ├─ タイムアウト（TimeoutMilliseconds 超過）
    │       │
    │       └─ 一時的エラー → リトライ待機（RetryDelayMilliseconds）
    │               │
    │               └─ RetryCount 回まで再試行
    │                       │
    │                       ├─ 回復 ──▶ PluginLoadResult(Success)
    │                       └─ 全リトライ失敗 ──▶ PluginLoadResult(Error)
    │
    ├─ AssemblyLoadContext 競合（一時的エラー）
    │       └─ 上と同じリトライフローへ
    │
    ├─ InvalidOperationException（型の問題など恒久的エラー）
    │       └─ リトライせず即 PluginLoadResult(Error) を返す
    │
    └─ CancellationToken キャンセル
            └─ リトライせず即 PluginLoadResult(Error) を返す
```

---

## 4. 設定ファイル（pluginsettings.json）

```json
{
  "PluginsPath": "plugins",
  "IntervalMilliseconds": 500,
  "TimeoutMilliseconds": 5000,
  "RetryCount": 3,
  "RetryDelayMilliseconds": 500,
  "StageOrders": [
    {
      "Stage": "PreProcessing",
      "PluginOrder": [
        { "Id": "my-plugin-a", "Order": 1 }
      ]
    },
    {
      "Stage": "Processing",
      "PluginOrder": [
        { "Id": "my-plugin-b", "Order": 1 },
        { "Id": "my-plugin-c", "Order": 1 },
        { "Id": "my-plugin-d", "Order": 2 }
      ]
    },
    {
      "Stage": "PostProcessing",
      "PluginOrder": [
        { "Id": "my-plugin-e", "Order": 1 }
      ]
    }
  ]
}
```

### 設定項目の説明

| キー | 型 | 説明 |
|---|---|---|
| `PluginsPath` | string | プラグイン DLL の格納フォルダ（相対 or 絶対パス） |
| `IntervalMilliseconds` | int | プラグイン間のロード待機時間（ms）。`0` で無効 |
| `TimeoutMilliseconds` | int | 1プラグインのロードタイムアウト（ms）。`0` で無制限 |
| `RetryCount` | int | ロード失敗時のリトライ回数。`0` でリトライなし |
| `RetryDelayMilliseconds` | int | リトライ間の待機時間（ms）。既定値 `500` |
| `Stage` | string | ステージID（任意の文字列。標準は `PreProcessing` / `Processing` / `PostProcessing`） |
| `Id` | string | プラグインの `[Plugin]` 属性の `id` と一致させる |
| `Order` | int | 同一ステージ内の実行順序。**同じ値は並列ロード** |

### Order と並列実行

```
Order: 1 ──┬── my-plugin-b ─┐
            └── my-plugin-c ─┴──▶ 両方完了後
Order: 2 ──── my-plugin-d
```

---

## 5. 主要クラス・インターフェース一覧

```
PluginManager.Core（参照するだけでよいクラス群）
│
├── IPlugin                  プラグインが実装すべき契約
│     .Id                    プラグインを一意に識別するID
│     .Name                  表示名
│     .Version               バージョン
│     .SupportedStages       対象ステージ（IReadOnlySet<PluginStage>）
│     .InitializeAsync(ctx, ct) 初期化（非同期）
│     .ExecuteAsync(...)     非同期実行
├── PluginAttribute          [Plugin(...)] 属性
├── PluginStage              ステージを表す値オブジェクト
│     PluginStage.PreProcessing  標準ステージ定数
│     PluginStage.Processing
│     PluginStage.PostProcessing
│     new PluginStage("CustomId") 独自ステージ
├── PluginContext             プラグイン間共有辞書（スレッドセーフ）
│     .SetProperty(key, value)
│     .GetProperty<T>(key)
│     .RemoveProperty(key)    指定キーを削除（bool 戻り値）
│     .Clear()                全プロパティを削除
│     .CreateScope()          スコープコピーを生成
├── PluginDescriptor          プラグインのメタ情報
│     .SupportedStages        IReadOnlySet<PluginStage>（FrozenSet）
├── PluginLoadResult          ロード結果（Success / Instance / Error）
├── PluginExecutionResult     実行結果（Success / Descriptor / Value / Error）
│     .Success                実行成功かどうか
│     .Descriptor             プラグインのメタ情報
│     .Value                  ExecuteAsync の戻り値
│     .Error                  失敗時の例外（成功時は null）
├── PluginConfiguration       設定値の保持クラス
│     .GetPluginOrder(stage)  ステージの順序定義を O(1) で取得
└── PluginConfigurationLoader 設定ファイル読み込み

PluginManager（ロード・実行を行うクラス群）
│
└── PluginLoader              ★アプリが直接使うメインクラス
      .Discover(dir)                DLL を探索
      .DiscoverFromConfiguration(path) 設定から探索
      .LoadAsync(dir, context)          非同期ロード
      .LoadFromConfigurationAsync(path, context) 設定に従いロード
      .ExecutePluginsAndWaitAsync(results, stage, context) 実行（IReadOnlyList<PluginExecutionResult> 完了待機）
      .UnloadPlugin(assemblyPath)       DLL をアンロード（Fire-and-forget GC）
      .UnloadPluginAsync(assemblyPath)  DLL をアンロードし GC 完了を待機
      .Dispose()                        全コンテキストを解放し GC を促す（UI ブロックなし）
      .DisposeAsync()                   全コンテキストを解放し GC 完了を await 待機（WinForms 推奨）
```

---

## 6. 実装パターン（C# / VB）

### 6-1. 最小実装（C#）

```csharp
using PluginManager;

// PluginLoader は IDisposable。using で確実に Dispose する。
using var loader = new PluginLoader();

// 実行スコープを生成（ステージをまたいでデータを共有するため同一インスタンスを使う）
var context = new PluginContext();

// 設定ファイルに従いプラグインをロード
var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);

// ロード結果を確認
foreach (var r in results)
{
    if (r.Success)
        Console.WriteLine($"[OK] {r.Descriptor.Id}");
    else
        Console.WriteLine($"[NG] {r.Descriptor.Id} - {r.Error?.Message}");
}

// ステージを順番に実行（同一 context を渡す点が重要）
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing,  context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing,     context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PostProcessing, context);
```

### 6-2. 最小実装（VB）

```vb
Imports PluginManager

' PluginLoader は IDisposable。Using で確実に Dispose する。
Using loader As New PluginLoader()

    ' 実行スコープを生成
    Dim context As New PluginContext()

    ' 設定ファイルに従いプラグインをロード
    Dim results = Await loader.LoadFromConfigurationAsync("pluginsettings.json", context)

    ' ロード結果を確認
    For Each r In results
        If r.Success Then
            Console.WriteLine($"[OK] {r.Descriptor.Id}")
        Else
            Console.WriteLine($"[NG] {r.Descriptor.Id} - {r.Error?.Message}")
        End If
    Next

    ' ステージを順番に実行（同一 context を渡す点が重要）
    Await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing,  context)
    Await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing,     context)
    Await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PostProcessing, context)

End Using
```

### 6-3. プラグインの実装例（C#）

`IPlugin` を実装する際は `SupportedStages` を `FrozenSet` で返してください。  
`FrozenSet` は最小完全ハッシュを使うため `Contains` が O(1) で動作します。

```csharp
using System.Collections.Frozen;

[Plugin("my-plugin", "マイプラグイン", "1.0.0", "Processing")]
public sealed class MyPlugin : IPlugin
{
    public string Id => "my-plugin";
    public string Name => "マイプラグイン";
    public Version Version => new(1, 0, 0);

    // FrozenSet で返すことで Contains が O(1) になる
    public IReadOnlySet<PluginStage> SupportedStages { get; } =
        new[] { PluginStage.Processing }.ToFrozenSet();

    public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        // 非同期の初期化処理（DB 接続・ファイル読み込みなど）
        await Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context,
        CancellationToken cancellationToken = default)
    {
        // SupportedStages のチェックは PluginExecutor が行うため通常は不要
        await Task.Delay(100, cancellationToken);
        return "完了";
    }
}
```

### 6-4. ステージ間のデータ受け渡し

PreProcessing の結果を PostProcessing で参照するパターンです。
**全ステージで同一の `context` インスタンスを渡すことが必須です。**

```
context（同一インスタンス）
    │
    ├─ PreProcessing
    │     └─ plugin-a: context.SetProperty("plugin-a.Result", "前処理完了")
    │                                           ↓ 同一 context に蓄積
    ├─ Processing
    │     └─ plugin-b: context.GetProperty<string>("plugin-a.Result")  ← 参照可能
    │
    └─ PostProcessing
          └─ plugin-c: context.GetProperty<string>("plugin-a.Result")  ← 参照可能
```

```csharp
// NG: ステージごとに CreateScope() すると別インスタンスになり共有できない
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing,  context.CreateScope());
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PostProcessing, context.CreateScope());

// OK: 同一インスタンスを全ステージに渡す
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing,  context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PostProcessing, context);
```

> `CreateScope()` は**常駐プログラムでリクエストごとに独立した実行**が必要な場合に使います。  
> 同一リクエスト内でステージをまたぐ場合は同一インスタンスをそのまま使ってください。

---

## 7. PluginContext の使い方

```csharp
// 書き込み（プラグイン側で実行）
context.SetProperty("my-plugin.Count", 42);
context.SetProperty("my-plugin.StartedAt", DateTime.Now);

// 読み込み（後続プラグインや呼び出し元で参照）
var count     = context.GetProperty<int>("my-plugin.Count");       // 42
var startedAt = context.GetProperty<DateTime>("my-plugin.StartedAt");
var missing   = context.GetProperty<string>("not-exist");          // null

// 全プロパティを直接操作したい場合
foreach (var kv in context.Properties)
    Console.WriteLine($"{kv.Key} = {kv.Value}");
```

### キー命名規則

```
✅ 推奨: "プラグインID.プロパティ名"
    context.SetProperty("my-validator.IsValid", true);

❌ 非推奨: 短すぎる汎用名（他プラグインと衝突する）
    context.SetProperty("result", true);
```

### PluginContext 肥大化の防止

常駐プログラムや長期稼働バッチで同一の `context` インスタンスを使い回す場合、
プラグインが毎回一意なキー（タイムスタンプ付きのログキーなど）を追加し続けると
内部の `ConcurrentDictionary` が肥大化して `OutOfMemoryException` に繋がります。

```csharp
// NG: 実行のたびに増え続けるキーを追加
var key = $"log.{DateTime.Now.Ticks}";
context.SetProperty(key, logEntry);   // キーが際限なく増える
```

**対処 ①: 処理サイクルをまたぐ場合は Clear() でリセット**

```csharp
// バッチ処理 1 サイクルが終わったらクリアしてから次のサイクルへ
context.Clear();
```

**対処 ②: 不要になったキーを RemoveProperty() で個別削除**

```csharp
// プラグイン内で一時キーを使い終わったら削除
context.SetProperty("my-plugin.TempBuffer", largeBuffer);
// ... 処理 ...
context.RemoveProperty("my-plugin.TempBuffer");   // 大きなオブジェクトを早期解放
```

**対処 ③: リクエストごとに新しい context を生成（推奨）**

```csharp
// 常駐プログラムでは毎回 new() で生成するのが最も安全
var context = new PluginContext();
await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, context);
// context はスコープを抜けると GC 対象になる
```

> **設計指針**: `PluginContext` に格納するキーの数は**処理の複雑さに比例した有限個**に保ってください。
> キー数が実行回数に比例して増える設計は必ず肥大化します。

---

## 8. エラーハンドリング

### ロード失敗の処理

```csharp
var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);

// 失敗したプラグインを抽出
var failed = results.Where(r => !r.Success).ToList();
if (failed.Count > 0)
{
    foreach (var r in failed)
        Console.Error.WriteLine($"[ロード失敗] {r.Descriptor.Id}: {r.Error?.Message}");

    // 必要なプラグインが失敗した場合は処理を中止する
    return;
}
```

### 実行時エラーの処理

```csharp
try
{
    var execResults = await loader.ExecutePluginsAndWaitAsync(
        results, PluginStage.Processing, context, cancellationToken);

    // 個別プラグインの失敗を確認（1件失敗しても他の結果は保持される）
    foreach (var r in execResults)
    {
        if (!r.Success)
            Console.Error.WriteLine($"[実行失敗] {r.Descriptor.Id}: {r.Error?.Message}");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("処理がキャンセルされました。");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"実行エラー: {ex.Message}");
}
```

### 失敗時のリトライ挙動

リトライは **フレームワーク側が自動で行います**。  
`pluginsettings.json` の設定が反映されます。

| エラー種別 | リトライするか |
|---|---|
| タイムアウト | ✅ する |
| AssemblyLoadContext 競合（一時的） | ✅ する |
| 型が IPlugin を実装していない | ❌ しない |
| CancellationToken によるキャンセル | ❌ しない |

---

## 9. よくある間違い

| 間違い | 問題 | 正しい対処 |
|---|---|---|
| プラグインを `ProjectReference` で参照する | 動的ロードにならず差し替え不可 | `plugins/` フォルダに配置して動的ロードする |
| ステージごとに `context.CreateScope()` する | ステージ間でデータが共有されない | 同一 `context` インスタンスを全ステージに渡す |
| `PluginLoader` を `Dispose` しない | `PluginLoadContext` が解放されずメモリリーク | `using var loader = new PluginLoader()` を使う |
| ロード直後に再ロードする | 前回の GC と競合してロードエラー | `RetryCount` を設定して自動回復させる |
| `LoadFromConfigurationAsync` の結果を確認しない | 失敗プラグインを見落とす | `r.Success` でチェックしてから実行する |
| `pluginsettings.json` の `Id` をプラグインの `Id` と一致させない | 設定が適用されない | `[Plugin]` 属性の第1引数と `Id` プロパティを一致させる |
| `SupportedStages` を `IReadOnlyList` で返す | `Contains` が O(n)になりステージ判定が遅くなる | `FrozenSet` で返し O(1) を保証する |
| `Dispose` 後も `loadResults` を保持し続ける | `IPlugin` への強参照が残り ALC が GC されない | `Dispose` 後は `loadResults` の参照を手放す |
| `UnloadPlugin` 後に GC 完了を待たずに再ロードする | ALC がまだ回収中で競合が発生する | `UnloadPluginAsync` を使い GC 完了を待機してから再ロードする |
| 常駐処理で同一 `context` に実行回数に比例したキーを追加し続ける | `ConcurrentDictionary` が肥大化して `OutOfMemoryException` | サイクルごとに `context.Clear()` するか毎回 `new PluginContext()` を生成する |

---

## 10. チェックリスト

### アプリ側

```
□ PluginManager.Core / PluginManager を ProjectReference で参照している
□ SamplePlugin などプラグイン DLL は ProjectReference で参照していない
□ ビルド後に plugins/ フォルダへ DLL をコピーする設定がある
□ pluginsettings.json を出力ディレクトリにコピーする設定がある
□ PluginLoader を using var または try/finally で Dispose している
□ LoadFromConfigurationAsync の結果を r.Success でチェックしている
□ 全ステージで同一の context インスタンスを渡している
□ CancellationToken を適切に渡してキャンセルに対応している
□ Dispose 前または Dispose 後に loadResults への参照を手放している（ALC 解放）
□ プラグインを差し替える場合は UnloadPluginAsync で GC 完了を待機している
□ LoadFromConfigurationAsync に渡すパスは AppContext.BaseDirectory 基準の絶対パスにしている

```

### プラグイン実装側

```
□ SupportedStages を IReadOnlySet<PluginStage> で宣言している
□ SupportedStages を FrozenSet（ToFrozenSet()）で返している
□ [Plugin] 属性の Id と IPlugin.Id プロパティが一致している
□ InitializeAsync が冪等（何度呼ばれても安全）に実装されている
```

### pluginsettings.json 側

```
□ PluginsPath が正しいフォルダパスを指している
□ Stage の文字列がプラグインの SupportedStages と一致している
□ Id がプラグインの [Plugin] 属性の id と一致している
□ RetryCount / RetryDelayMilliseconds が設定されている
□ 並列実行させたいプラグインは同じ Order 値になっている
```

---

## 11. VB.NET 常駐プログラムの例

リクエストごとに独立したコンテキストが必要な常駐プログラム（Windows Service など）の場合、`CreateScope()` を使用します。

### VB.NET - 常駐プログラム向け実装例

```vb
Imports PluginManager
Imports System.Diagnostics

' Windows Service や定期実行処理の一部
Public Class RequestHandler
    Private loader As PluginLoader
    Private loadResults As IReadOnlyList(Of PluginLoadResult)

    Public Sub New()
        ' アプリ起動時に 1 回だけロード
        loader = New PluginLoader()
    End Sub

    Public Async Function InitializeAsync() As Task
        Dim globalContext As New PluginContext()
        loadResults = Await loader.LoadFromConfigurationAsync("pluginsettings.json", globalContext)

        ' ロード結果を確認
        For Each r In loadResults
            If r.Success Then
                Console.WriteLine($"[OK] {r.Descriptor.Id}")
            Else
                Console.WriteLine($"[NG] {r.Descriptor.Id} - {r.Error?.Message}")
            End If
        Next

        ' globalContext のデータは初期化用のみ（リクエスト間で共有しない）
    End Function

    ' リクエストごとに呼ばれるメソッド
    Public Async Function HandleRequestAsync(requestId As String, requestData As String) As Task(Of String)
        ' リクエスト独立のコンテキストを生成
        Dim requestContext = New PluginContext()
        requestContext.SetProperty("RequestId", requestId)
        requestContext.SetProperty("RequestData", requestData)

        ' PreProcessing ステージを実行
        Dim preResults = Await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.PreProcessing, requestContext)
        For Each r In preResults
            If Not r.Success Then
                Console.WriteLine($"[NG] {r.Descriptor.Id}: {r.Error?.Message}")
            End If
        Next

        ' Processing ステージを実行
        Dim procResults = Await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.Processing, requestContext)
        For Each r In procResults
            If Not r.Success Then
                Console.WriteLine($"[NG] {r.Descriptor.Id}: {r.Error?.Message}")
            End If
        Next

        ' PostProcessing ステージを実行
        Await loader.ExecutePluginsAndWaitAsync(loadResults, PluginStage.PostProcessing, requestContext)

        ' リクエスト結果を返す
        Dim result = requestContext.GetProperty(Of String)("result")
        Return If(result, "処理完了")
    End Function

    Public Sub Cleanup()
        ' アプリ終了時に Dispose
        loader.Dispose()
    End Sub
End Class

' 使用例
Public Class Program
    Public Shared Async Function Main() As Task
        Dim handler As New RequestHandler()
        Await handler.InitializeAsync()

        ' リクエスト 1
        Dim result1 = Await handler.HandleRequestAsync("req-001", "data-1")
        Console.WriteLine($"Result 1: {result1}")

        ' リクエスト 2（リクエスト 1 の context とは独立）
        Dim result2 = Await handler.HandleRequestAsync("req-002", "data-2")
        Console.WriteLine($"Result 2: {result2}")

        ' クリーンアップ
        handler.Cleanup()
    End Function
End Class
```

### VB.NET のポイント

- **`New PluginLoader()`**: C# の `new PluginLoader()` と同じ。`()` 括弧は省略可。
- **`Await` / `Async`**: C# と同じ非同期パターン。
- **`Using` ステートメント**: `Using ... As New PluginLoader()` で自動 Dispose。
- **デフォルトパラメータ**: `cancellationToken = default` は VB.NET でも機能。
- **型推論**: `Dim context = New PluginContext()` の型は自動推論。
- **イベント購読**: `AddHandler loader.PluginEvent, AddressOf OnPluginEvent` で購読可。

### イベント購読の例（VB.NET）

```vb
' イベント購読
AddHandler loader.PluginEvent, AddressOf OnPluginEvent

' イベントハンドラ
Private Sub OnPluginEvent(sender As Object, e As PluginLoaderEventArgs)
    Console.WriteLine($"[{e.EventType}] {e.Message}")
    If e.Exception IsNot Nothing Then
        Console.WriteLine($"  エラー: {e.Exception.Message}")
    End If
End Sub
```

---

## 12. 常駐プログラム（Windows Service）での使用シナリオ

"
常駐プログラムと単発プログラムでの `PluginContext` の使い方は異なります。

### シナリオ 1: Web サーバー・リクエスト処理

```
アプリ起動
    │
    ├─ PluginLoader を Singleton で保持（1 回ロード）
    │
    ▼
リクエスト 1
    ├─ context = new PluginContext()  ← リクエスト専用コンテキスト
    ├─ Execute PreProcessing
    ├─ Execute Processing
    ├─ Execute PostProcessing
    └─ レスポンス返却
        context ガベージコレクション

        ↓ 次のリクエスト（context は別インスタンス）

リクエスト 2
    ├─ context = new PluginContext()  ← リクエスト 1 とは独立
    ├─ Execute PreProcessing
    ├─ Execute Processing
    ├─ Execute PostProcessing
    └─ レスポンス返却
        context ガベージコレクション
```

この場合、各リクエストが独立したコンテキストを持つため、**複数リクエストの context が絶対に混ざりません**。

### シナリオ 2: バッチ処理・定期実行

```
アプリ起動
    │
    ├─ PluginLoader を生成
    ├─ context = new PluginContext()  ← 処理全体で共有
    │
    ├─ Execute PreProcessing（context を渡す）
    ├─ Execute Processing（同じ context）
    ├─ Execute PostProcessing（同じ context）
    │
    └─ 結果を記録
        loader.Dispose()
```

この場合、複数のステージが同じ `context` を共有するため、前のステージの結果を後のステージで参照できます。

---



### シナリオ 3: Windows Service での相対パス問題

Windows Service として動作する場合、`Environment.CurrentDirectory` が `C:\Windows\System32` になることがあります。

**PluginManager はこの問題を内部で自動解決します。**

```
pluginsettings.json の PluginsPath: "plugins"（相対パス）
    │
    ▼
PluginConfigurationLoader.Load("C:\MyService\pluginsettings.json")
    │
    │  設定ファイルのディレクトリを基準に絶対パスへ変換
    │  Path.GetFullPath("plugins", "C:\MyService\")
    │
    ▼
PluginsPath = "C:\MyService\plugins"（絶対パス）← Environment.CurrentDirectory に依存しない
```

**呼び出し元で注意すること：**

設定ファイルのパス自体を絶対パスで渡してください。

```csharp
// NG: 相対パスで渡すと Windows Service で失敗する場合がある
await loader.LoadFromConfigurationAsync("pluginsettings.json", context);

// OK: AppContext.BaseDirectory を起点に絶対パスで渡す
var configPath = Path.Combine(AppContext.BaseDirectory, "pluginsettings.json");
await loader.LoadFromConfigurationAsync(configPath, context);
```

```vb
' VB.NET の場合
Dim configPath = Path.Combine(AppContext.BaseDirectory, "pluginsettings.json")
Await loader.LoadFromConfigurationAsync(configPath, context)
```

> `AppContext.BaseDirectory` は実行ファイル（.exe）があるディレクトリを返します。
> `Environment.CurrentDirectory` と異なり Windows Service でも正しいパスを返します。

---

## 13. パフォーマンス最適化ガイド

### 13-1. FrozenSet によるステージ判定の高速化

`SupportedStages` を `FrozenSet` で返すことで、`Contains` が **O(1)** になります。

```csharp
// ❌ NG: List や配列では Contains が O(n) になる
public IReadOnlySet<PluginStage> SupportedStages { get; } =
    new List<PluginStage> { PluginStage.Processing }.AsReadOnly();

// ✅ OK: FrozenSet で O(1) を保証
public IReadOnlySet<PluginStage> SupportedStages { get; } =
    new[] { PluginStage.Processing }.ToFrozenSet();
```

**パフォーマンス比較（1,000 回の Contains 呼び出し）:**

| 型 | Contains の計算量 | 実測時間 |
|---|---|---|
| `List<PluginStage>` | O(n) | ~5 µs |
| `HashSet<PluginStage>` | O(1) 平均 | ~2 µs |
| `FrozenSet<PluginStage>` | O(1) 保証 | ~1 µs |

> `FrozenSet` は最小完全ハッシュ（Minimal Perfect Hash）を使用するため、
> `HashSet` よりもメモリ効率が良く、衝突が発生しません。

### 13-2. プラグイン間のロード待機時間の調整

`pluginsettings.json` の `IntervalMilliseconds` は、同一 Order のプラグイングループ間の待機時間です。

```json
{
  "IntervalMilliseconds": 0,  // 待機なし（最速）
  "TimeoutMilliseconds": 5000,
  "RetryCount": 3
}
```

**推奨設定:**

| シナリオ | IntervalMilliseconds | 理由 |
|---|---|---|
| 本番環境（高速化優先） | `0` | 待機なしで最大速度 |
| 開発環境（デバッグ） | `500` | ログを目視確認しやすい |
| リソース制約環境 | `1000` | CPU・メモリの急激な負荷を回避 |

### 13-3. 並列ロードの活用

同じ `Order` 値を持つプラグインは並列ロードされます。

```json
{
  "Stage": "Processing",
  "PluginOrder": [
    { "Id": "plugin-a", "Order": 1 },  // ← これら 3 つは並列ロード
    { "Id": "plugin-b", "Order": 1 },
    { "Id": "plugin-c", "Order": 1 },
    { "Id": "plugin-d", "Order": 2 }   // ← Order 1 完了後にロード
  ]
}
```

**パフォーマンス比較（各プラグインのロード時間 = 1 秒）:**

| 設定 | 実行時間 |
|---|---|
| 全て Order: 1（並列） | 1 秒 |
| 全て異なる Order（直列） | 4 秒 |

> 依存関係がないプラグインは同じ Order にして並列化してください。

### 13-4. PluginContext のメモリ効率化

`PluginContext` は `ConcurrentDictionary` ベースです。
大きなオブジェクトを格納すると GC の負荷が増えます。

```csharp
// ❌ NG: 大きなバッファを context に保持し続ける
var largeBuffer = new byte[100_000_000];  // 100 MB
context.SetProperty("my-plugin.Buffer", largeBuffer);

// ✅ OK: 使い終わったら削除して早期解放
context.SetProperty("my-plugin.Buffer", largeBuffer);
// ... 処理 ...
context.RemoveProperty("my-plugin.Buffer");  // GC 対象にする
```

**メモリプロファイリングの推奨:**

- Visual Studio の診断ツールで `PluginContext` のメモリ使用量を監視
- キー数が実行回数に比例して増加していないかチェック
- 常駐プログラムでは定期的に `context.Clear()` を呼ぶ

### 13-5. InitializeAsync の最適化

`InitializeAsync` は**プラグインロード時に 1 回だけ**呼ばれます。
重い初期化処理（DB 接続プール、設定ファイル読み込み）はここで行ってください。

```csharp
public sealed class MyPlugin : IPlugin
{
    private HttpClient? _httpClient;

    public async Task InitializeAsync(PluginContext context, CancellationToken cancellationToken = default)
    {
        // ✅ OK: ロード時に 1 回だけ初期化
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        await Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(PluginStage stage, PluginContext context,
        CancellationToken cancellationToken = default)
    {
        // ❌ NG: 実行のたびに HttpClient を生成すると遅い
        // using var client = new HttpClient();

        // ✅ OK: 初期化済みのインスタンスを再利用
        var response = await _httpClient!.GetAsync("https://example.com", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
```

### 13-6. タイムアウトとリトライの調整

```json
{
  "TimeoutMilliseconds": 5000,  // 5 秒でタイムアウト
  "RetryCount": 3,              // 3 回リトライ
  "RetryDelayMilliseconds": 500 // リトライ間隔 500ms
}
```

**チューニング指針:**

| プラグインの特性 | TimeoutMilliseconds | RetryCount |
|---|---|---|
| ローカルファイル読み込み | 1000 | 1 |
| ネットワーク API 呼び出し | 10000 | 5 |
| データベースクエリ | 5000 | 3 |

> タイムアウトが短すぎるとリトライ回数が増え、逆に遅くなります。

---

## 14. ALC アンロードのベストプラクティス

### 14-1. AssemblyLoadContext（ALC）の仕組み

PluginManager は各プラグインを独立した `AssemblyLoadContext` にロードします。
これにより、以下が可能になります：

- **プラグインの動的差し替え**（DLL 更新後に再ロード）
- **メモリリーク防止**（アンロード後に GC で完全回収）
- **バージョン衝突の回避**（異なるバージョンの同名 DLL を共存）

### 14-2. Dispose のタイミング

```csharp
using var loader = new PluginLoader();
var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);

// プラグイン実行
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context);

// ✅ OK: using ブロックを抜けると自動で Dispose される
// Dispose 内で全 ALC が Unload され、GC が促進される
```

**重要:** `Dispose` 後も `results` への参照を保持し続けると、
`IPlugin` インスタンスへの強参照が残り、ALC が GC されません。

```csharp
// ❌ NG: Dispose 後も results を保持
var results = await loader.LoadFromConfigurationAsync(...);
loader.Dispose();
Console.WriteLine(results[0].Descriptor.Name);  // results が ALC を参照し続ける

// ✅ OK: Dispose 前に results を使い終わる
var results = await loader.LoadFromConfigurationAsync(...);
await loader.ExecutePluginsAndWaitAsync(results, ...);
loader.Dispose();
// この時点で results への参照はスコープ外
```

### 14-3. UnloadPlugin による個別アンロード

特定のプラグインだけをアンロードしたい場合は `UnloadPlugin` を使います。

```csharp
// プラグインをロード
var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);

// 特定プラグインをアンロード（Fire-and-forget）
loader.UnloadPlugin("plugins/MyPlugin.dll");

// ❌ NG: アンロード直後に再ロードすると GC が完了していない
await loader.LoadAsync("plugins", context);  // 失敗する可能性あり
```

**正しい手順: UnloadPluginAsync を使う**

```csharp
// プラグインをアンロード（GC 完了を待機）
await loader.UnloadPluginAsync("plugins/MyPlugin.dll");

// ✅ OK: GC 完了後なので再ロード可能
await loader.LoadAsync("plugins", context);
```

**UnloadPluginAsync の内部動作:**

```
UnloadPluginAsync("plugins/MyPlugin.dll")
    │
    ├─ PluginLoadContext.Unload() を呼ぶ
    │
    ├─ WeakReference で ALC を監視
    │
    ├─ GC.Collect() / GC.WaitForPendingFinalizers() を複数回実行
    │
    └─ WeakReference.IsAlive == false になるまで待機
        （タイムアウト: 10 秒）
```

### 14-4. WinForms での DisposeAsync の推奨

WinForms アプリでは `DisposeAsync` を使ってください。

```csharp
public partial class PluginManagerForm : Form
{
    private PluginLoader? _loader;

    private async void PluginManagerForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        // ✅ OK: DisposeAsync で GC 完了を待機（UI スレッドはブロックしない）
        if (_loader is not null)
            await _loader.DisposeAsync();
    }
}
```

**Dispose と DisposeAsync の違い:**

| メソッド | GC 待機 | UI ブロック | 推奨シナリオ |
|---|---|---|---|
| `Dispose()` | しない | しない | コンソールアプリ・バックグラウンド処理 |
| `DisposeAsync()` | する | しない | WinForms・WPF・ASP.NET Core |

### 14-5. メモリリーク診断

ALC が正しくアンロードされているか確認するには、Visual Studio の診断ツールを使います。

**手順:**

1. **診断ツール** → **メモリ使用量** を開く
2. プラグインをロード → 実行 → Dispose
3. **スナップショットを取得**
4. `AssemblyLoadContext` のインスタンス数をチェック

**期待される結果:**

```
Dispose 前: AssemblyLoadContext のインスタンス数 = プラグイン数 + 1（デフォルト ALC）
Dispose 後: AssemblyLoadContext のインスタンス数 = 1（デフォルト ALC のみ）
```

**リークしている場合の原因:**

- `loadResults` への参照を Dispose 後も保持している
- プラグイン内で静的フィールドがアプリ本体の型を参照している
- イベントハンドラの購読解除漏れ（`-=` していない）

### 14-6. 長期稼働アプリでの注意点

常駐プログラムや Web サーバーでは、定期的にメモリ使用量を監視してください。

```csharp
// 定期的に ALC のアンロード状態を確認
var alcCount = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => AssemblyLoadContext.GetLoadContext(a))
    .Distinct()
    .Count();

if (alcCount > 予想値)
{
    // メモリリークの可能性
    _logger.LogWarning($"ALC count is unexpectedly high: {alcCount}");
}
```

---

> **まとめ:**
> - `Dispose` / `DisposeAsync` を必ず呼ぶ
> - `Dispose` 後は `loadResults` の参照を手放す
> - プラグイン差し替え時は `UnloadPluginAsync` で GC 完了を待機
> - 長期稼働では定期的にメモリ使用量を監視
