# PluginManager 開発ガイド

**対象読者**: `PluginManager` を使って日常的にアプリ開発を行う C# / VB 開発者  
**対象バージョン**: .NET 8 / C# 12

> この資料は、`README.md` の Quick Start から一歩進んで、日常開発で必要になる設定・実装・エラーハンドリング・運用上の注意点をまとめたものです。

## 1. この資料の位置づけ

最初に最短で動かしたい場合は、先に `README.md` を読んでください。  
この資料では、日常開発でよく参照する詳細事項を整理しています。

通常は次の順で読むことをおすすめします。

1. `README.md` の Quick Start を読む
2. この資料の `4. プロジェクト参照と設定ファイル` を読む
3. この資料の `5. 実装パターン` と `6. PluginContext` を読む
4. 必要になったら `docs/advanced-guide.md` を読む

## 2. 目次

1. [まず押さえる全体像](#3-まず押さえる全体像)
2. [プロジェクト参照と設定ファイル](#4-プロジェクト参照と設定ファイル)
3. [実装パターン](#5-実装パターン)
4. [PluginContext の使い方](#6-plugincontext-の使い方)
5. [エラーハンドリング](#7-エラーハンドリング)
6. [よくある間違い](#8-よくある間違い)
7. [チェックリスト](#9-チェックリスト)
8. [常駐プログラムでの使い分け](#10-常駐プログラムでの使い分け)
9. [関連資料](#11-関連資料)

## 3. まず押さえる全体像

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
│  │  （アンロード可能・アセンブリ分離）          │ │
│  └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────┐
│                 PluginExecutor                   │
│  SupportedStages が一致するプラグインだけを実行  │
│  同一 Order → 並列実行 / 異なる Order → 逐次実行│
└─────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────┐
│                 PluginContext                    │
│  プラグイン間でデータを受け渡す共有辞書          │
│  ConcurrentDictionary（スレッドセーフ）         │
└─────────────────────────────────────────────────┘
```

日常利用で最も重要なのは次の 4 点です。

- アプリ側の入口は `PluginLoader`
- プラグイン側の入口は `PluginBase`
- 実行順序は `pluginsettings.json` の `StageOrders` で制御する
- ステージ間のデータは `PluginContext` で受け渡す

## 4. プロジェクト参照と設定ファイル

### 4-1. プロジェクト参照の基本

### C# アプリ（`.csproj`）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\PluginManager.Core\PluginManager.Core.csproj" />
    <ProjectReference Include="..\PluginManager\PluginManager.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="..\PluginManager\pluginsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <Link>pluginsettings.json</Link>
    </Content>
  </ItemGroup>

  <Target Name="CopyPlugins" AfterTargets="Build">
    <MakeDir Directories="$(OutputPath)plugins" />
    <Copy SourceFiles="..\MyPlugin\bin\$(Configuration)\net8.0-windows\MyPlugin.dll"
          DestinationFolder="$(OutputPath)plugins"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

> **重要**: プラグイン DLL は `ProjectReference` で参照しません。  
> `plugins/` フォルダに配置して、`PluginLoader` が動的にロードする構成にします。

### 4-2. `pluginsettings.json` の最小構成

```json
{
  "PluginsPath": "plugins",
  "IntervalMilliseconds": 0,
  "TimeoutMilliseconds": 5000,
  "RetryCount": 3,
  "RetryDelayMilliseconds": 500,
  "StageOrders": [
    {
      "Stage": "Processing",
      "MaxDegreeOfParallelism": 2,
      "PluginOrder": [
        { "Id": "my-plugin", "Order": 1 }
      ]
    }
  ]
}
```

最初は次の 4 つを押さえれば十分です。

1. `PluginsPath` に DLL 配置先を書く
2. `TimeoutMilliseconds` にタイムアウトを書く
3. `RetryCount` に再試行回数を書く
4. `StageOrders` に実行ステージと順序を書く

`MaxDegreeOfParallelism` は任意の調整項目です。
同一ステージ内の同時実行上限を制御したい場合だけ指定してください。
未指定時は既定の実行方式が使われ、指定時もホスト側の安全上限内で適用されます。

### 4-3. `Order` の考え方

- 同じ `Order` は並列実行
- 異なる `Order` は順番実行

```json
{
  "Stage": "Processing",
  "PluginOrder": [
    { "Id": "writer-plugin", "Order": 1 },
    { "Id": "reader-plugin", "Order": 2 }
  ]
}
```

書き込み → 読み込みの順序保証が必要なら、`Order` を分けてください。

### 4-4. 呼び出し側で注意すること

Windows Service などでは、設定ファイルのパスは相対ではなく絶対パスで渡すことを推奨します。

```csharp
var configPath = Path.Combine(AppContext.BaseDirectory, "pluginsettings.json");
var results = await loader.LoadFromConfigurationAsync(configPath, context);
```

## 5. 実装パターン

### 5-1. 最小実装（C#）

```csharp
using PluginManager;

using var loader = new PluginLoader();
var context = new PluginContext();

var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);

foreach (var r in results)
{
    if (r.Success)
        Console.WriteLine($"[OK] {r.Descriptor.Id}");
    else
        Console.WriteLine($"[NG] {r.Descriptor.Id} - {r.Error?.Message}");
}

await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing, context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PostProcessing, context);
```

### 5-2. 最小実装（VB）

```vb
Imports PluginManager

Using loader As New PluginLoader()

    Dim context As New PluginContext()
    Dim results = Await loader.LoadFromConfigurationAsync("pluginsettings.json", context)

    For Each r In results
        If r.Success Then
            Console.WriteLine($"[OK] {r.Descriptor.Id}")
        Else
            Console.WriteLine($"[NG] {r.Descriptor.Id} - {r.Error?.Message}")
        End If
    Next

    Await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing, context)
    Await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context)
    Await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PostProcessing, context)

End Using
```

### 5-3. プラグイン実装の基本

通常は `PluginBase` を継承します。

```csharp
using PluginManager;

[Plugin("simple-plugin", "シンプルプラグイン", "1.0.0", "Processing")]
public sealed class SimplePlugin : PluginBase
{
    public SimplePlugin()
        : base("simple-plugin", "シンプルプラグイン", new Version(1, 0, 0), PluginStage.Processing)
    {
    }

    protected override async Task<object?> OnExecuteAsync(
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        context.SetProperty("simple-plugin.Result", "完了");
        return "完了";
    }
}
```

### 5-4. 実装時の判断

- まずは `PluginBase`
- 実行処理は `OnExecuteAsync`
- 初期化が必要なときだけ `OnInitializeAsync`
- 外部リソースを持つときだけ `DisposeAsyncCore`

```csharp
public sealed class CustomInitPlugin : PluginBase
{
    private HttpClient? _client;

    public CustomInitPlugin()
        : base("custom-init", "カスタム初期化", new Version(1, 0, 0), PluginStage.Processing)
    {
    }

    protected override Task OnInitializeAsync(
        PluginContext context,
        CancellationToken cancellationToken)
    {
        _client = new HttpClient();
        return Task.CompletedTask;
    }

    protected override async Task<object?> OnExecuteAsync(
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        var response = await _client!.GetStringAsync("https://example.com", cancellationToken);
        return response;
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _client?.Dispose();
        return base.DisposeAsyncCore();
    }
}
```

### 5-5. コールバック方式の利用

`IPluginLoaderCallback` を実装すると、ロードや実行の通知を受け取れます。
通常の通知メソッドに加えて、`OnNotification` を実装すると `ExecutionId` を含む通知オブジェクト全体を受け取れます。

```csharp
public class MyCallback : IPluginLoaderCallback
{
    public void OnLoadStart(string configPath)
        => Console.WriteLine($"ロード開始: {configPath}");

    public void OnPluginLoadSuccess(string pluginId, int attempt)
        => Console.WriteLine($"[{pluginId}] ロード成功");

    public void OnPluginLoadFailed(string pluginId, int attempt, Exception? error)
        => Console.WriteLine($"[{pluginId}] ロード失敗: {error?.Message}");

    public void OnExecuteStart(string stageId)
        => Console.WriteLine($"ステージ [{stageId}] 実行開始");
}
```

### 5-6. `OnNotification` と `ExecutionId` によるログ相関

`OnNotification` を使うと、通知種別ごとの簡易メソッドでは受け取れない `ExecutionId` を利用できます。
同じロードサイクル、または同じステージ実行サイクルの通知を同一 ID で追跡したい場合に有効です。

```csharp
public sealed class TraceableCallback : IPluginLoaderCallback
{
    public void OnNotification(PluginLoaderNotification notification)
    {
        Console.WriteLine(
            $"[ExecutionId={notification.ExecutionId}] " +
            $"Type={notification.NotificationType}, " +
            $"PluginId={notification.PluginId}, " +
            $"StageId={notification.StageId}, " +
            $"Message={notification.Message}");
    }
}
```

たとえば、`LoadStart` と `LoadCompleted` が同じ `ExecutionId` を持っていれば、
その 2 つが同じロード処理に属する通知だと判断できます。
プラグイン単位の `PluginLoadStart` / `PluginLoadRetry` / `PluginLoadFailed` も同じ `ExecutionId` でまとめて追跡できます。

### 5-7. `PluginExecutor` の callback 利用

実行中の進捗や失敗を細かく追跡したい場合は、`IPluginExecutorCallback` を `SetExecutorCallback` で登録します。
グループ開始・個別プラグイン実行開始・成功・失敗・スキップを受け取れます。

```csharp
public sealed class MyExecutorCallback : IPluginExecutorCallback
{
    public void OnPluginExecuteCompleted(string stageId, string pluginId, int groupIndex)
        => Console.WriteLine($"[{stageId}] Group={groupIndex} Plugin={pluginId} 実行完了");

    public void OnPluginExecuteFailed(string stageId, string pluginId, int groupIndex, Exception error)
        => Console.WriteLine($"[{stageId}] Group={groupIndex} Plugin={pluginId} 実行失敗: {error.Message}");

    public void OnPluginSkipped(string stageId, string pluginId, int groupIndex, string skipReason)
        => Console.WriteLine($"[{stageId}] Group={groupIndex} Plugin={pluginId} スキップ: {skipReason}");
}

using var loader = new PluginLoader();
loader.SetExecutorCallback(new MyExecutorCallback());
```

`IPluginLoaderCallback` はロード中心の通知、`IPluginExecutorCallback` は実行中心の通知として使い分けると整理しやすくなります。

## 6. PluginContext の使い方

### 6-1. 基本

```csharp
context.SetProperty("my-plugin.Count", 42);
context.SetProperty("my-plugin.StartedAt", DateTime.Now);

var count = context.GetProperty<int>("my-plugin.Count");
var startedAt = context.GetProperty<DateTime>("my-plugin.StartedAt");
```

### 6-2. 安全な取得方法

```csharp
if (context.TryGetProperty<int>("count", out var count))
{
    Console.WriteLine($"count = {count}");
}

var name = context.GetPropertyOrDefault("name", "Unknown");
var strictValue = context.GetPropertyOrThrow<int>("count");
```

使い分け:

| メソッド | 用途 |
|---|---|
| `GetProperty<T>` | 既存互換 |
| `TryGetProperty<T>` | 推奨 |
| `GetPropertyOrDefault<T>` | デフォルト値が必要な場合 |
| `GetPropertyOrThrow<T>` | 厳密な型チェックが必要な場合 |

### 6-3. ステージ間データ受け渡し

前段プラグイン:

```csharp
context.SetProperty("sample.Message", "Hello, PluginManager!");
context.SetProperty("sample.CreatedAt", DateTime.UtcNow);
```

後段プラグイン:

```csharp
var message = context.GetProperty<string>("sample.Message");
var createdAt = context.GetProperty<DateTime>("sample.CreatedAt");
```

呼び出し側は、**全ステージで同じ `PluginContext` インスタンス** を渡してください。

### 6-4. キー命名規則

- 推奨: `プラグインID.プロパティ名`
- 非推奨: `result` のような短い汎用名

### 6-5. 肥大化を防ぐ

長期稼働では、実行回数に比例してキーが増え続ける設計を避けてください。

```csharp
context.Clear();
context.RemoveProperty("my-plugin.TempBuffer");
```

常駐アプリでは、リクエストごとに `new PluginContext()` を生成するのが最も安全です。

## 7. エラーハンドリング

### 7-1. ロード失敗

```csharp
var results = await loader.LoadFromConfigurationAsync("pluginsettings.json", context);
var failed = results.Where(r => !r.Success).ToList();

if (failed.Count > 0)
{
    foreach (var r in failed)
        Console.Error.WriteLine($"[ロード失敗] {r.Descriptor.Id}: {r.Error?.Message}");

    return;
}
```

### 7-2. 実行結果の見方

```csharp
var execResults = await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context);

foreach (var r in execResults)
{
    if (r.Skipped)
        Console.WriteLine($"[スキップ] {r.Descriptor.Id}: {r.SkipReason}");
    else if (!r.Success)
        Console.Error.WriteLine($"[実行失敗] {r.Descriptor.Id}: {r.Error?.Message}");
    else
        Console.WriteLine($"[OK] {r.Descriptor.Id}");
}
```

**重要**: `Success = true` はスキップも含みます。  
実際に実行されたかどうかは `Skipped` で判定してください。

### 7-3. リトライ挙動

| エラー種別 | リトライするか |
|---|---|
| タイムアウト | する |
| `AssemblyLoadContext` 競合（一時的） | する |
| 型が `IPlugin` を実装していない | しない |
| `CancellationToken` によるキャンセル | しない |

### 7-4. 重複 ID の扱い

- 設定ファイル内で同一ステージに同じ `Id` を重複させない
- ディレクトリ内に同じプラグイン ID の DLL を複数置かない

## 8. よくある間違い

| 間違い | 問題 | 正しい対処 |
|---|---|---|
| プラグインを `ProjectReference` で参照する | 動的ロードにならない | `plugins/` に配置して動的ロードする |
| ステージごとに別の `context` を使う | データが共有されない | 同一 `context` を全ステージに渡す |
| `PluginLoader` を `Dispose` しない | ALC が解放されない | `using var loader = new PluginLoader()` を使う |
| `LoadFromConfigurationAsync` の結果を確認しない | 失敗を見落とす | `r.Success` を確認してから実行する |
| `Dispose` 後も `loadResults` を保持する | ALC が GC されない | 参照を手放す |
| 常駐処理で同一 `context` にキーを追加し続ける | 辞書が肥大化する | `Clear()` するか毎回新しい `context` を使う |

## 9. チェックリスト

### アプリ側

```
□ `PluginManager.Core` / `PluginManager` を `ProjectReference` で参照している
□ プラグイン DLL は `ProjectReference` で参照していない
□ ビルド後に `plugins/` へ DLL をコピーする設定がある
□ `pluginsettings.json` を出力ディレクトリにコピーしている
□ `PluginLoader` を確実に `Dispose` している
□ `LoadFromConfigurationAsync` の結果を確認している
□ 全ステージで同一の `context` を渡している
□ 設定ファイルのパスは必要に応じて絶対パスで渡している
```

### プラグイン実装側

```
□ `SupportedStages` を `IReadOnlySet<PluginStage>` で宣言している
□ `SupportedStages` を `FrozenSet` で返している
□ `[Plugin]` 属性の `Id` と `IPlugin.Id` が一致している
□ `InitializeAsync` が冪等に実装されている
```

## 10. 常駐プログラムでの使い分け

### 10-1. リクエスト処理型

Web サーバーやサービスでは、アプリ起動時に `PluginLoader` を 1 回ロードし、リクエストごとに新しい `PluginContext` を生成します。

### 10-2. バッチ処理型

単発処理やバッチでは、同じ `PluginContext` を複数ステージに渡して前段結果を後段で参照します。

### 10-3. 相対パス問題

Windows Service では `Environment.CurrentDirectory` が `C:\Windows\System32` になることがあります。  
設定ファイルパスは `AppContext.BaseDirectory` 基準の絶対パスにしてください。

## 11. 関連資料

- 入口 / Quick Start: `README.md`
- 日常開発の詳細: `docs/developer-guide.md`
- 上級者向け: `docs/advanced-guide.md`
