# PluginManager アプリ開発者向け手引書

**対象読者**: PluginManager を使って C# / VB アプリを開発する開発者  
**対象バージョン**: .NET 8 / C# 12

> この手引きは、**まず動かすための入口** に絞った Quick Start です。日常開発の詳細は `docs/developer-guide.md`、上級者向け情報は `docs/advanced-guide.md` を参照してください。

---

## 1. この資料の位置づけ

この `README.md` では、最初の 1 本を動かすために必要な内容だけを扱います。

- `PluginManager` の全体像をつかむ
- 参照設定を行う
- `pluginsettings.json` を最小構成で作る
- `PluginBase` で最初のプラグインを書く
- アプリ側からロードして実行する

より詳しい内容は次の資料に分離しています。

- **日常開発の詳細**: `docs/developer-guide.md`
- **上級者向け補足**: `docs/advanced-guide.md`

## 2. 目次

1. [最初に読む順番](#3-最初に読む順番)
2. [全体像](#4-全体像)
3. [プロジェクト参照の設定](#5-プロジェクト参照の設定)
4. [設定ファイル（pluginsettingsjson）](#6-設定ファイル-pluginsettingsjson)
5. [最小実装（c--vb）](#7-最小実装-c--vb)
6. [ステージ間のデータ受け渡し](#8-ステージ間のデータ受け渡し)
7. [チェックリスト](#9-チェックリスト)
8. [次に読む資料](#10-次に読む資料)

## 3. 最初に読む順番

初めて使う場合は、次の順で読めば動かせます。

1. `3. 全体像`
2. `4. プロジェクト参照の設定`
3. `5. 設定ファイル（pluginsettings.json）`
4. `6. 最小実装（C# / VB）`
5. `7. ステージ間のデータ受け渡し`
6. `8. チェックリスト`

## 4. 全体像

```
あなたのアプリ（WinForms / Console / WPF など）
    │
    │  using PluginManager;
    │
    ▼
┌─────────────────────────────────────────────────┐
│                  PluginLoader                    │
│  DLL をスキャンし、設定に従ってロード            │
└─────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────┐
│                 PluginExecutor                   │
│  対象ステージのプラグインを実行                  │
│  同一 Order → 並列 / 異なる Order → 逐次        │
└─────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────┐
│                 PluginContext                    │
│  プラグイン間でデータを受け渡す共有辞書          │
└─────────────────────────────────────────────────┘
```

最初に覚えること:

- アプリ側の入口は `PluginLoader`
- プラグイン側の入口は `PluginBase`
- 実行順序は `pluginsettings.json` の `StageOrders` で決める
- データ受け渡しは `PluginContext` を使う

## 5. プロジェクト参照の設定

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

> **重要**: プラグイン DLL は `ProjectReference` で参照しないでください。  
> `plugins/` フォルダに配置して `PluginLoader` が動的にロードする構成にします。

## 6. 設定ファイル（pluginsettings.json）

最初は次の構成で十分です。

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
      "PluginOrder": [
        { "Id": "my-plugin", "Order": 1 }
      ]
    }
  ]
}
```

最初に押さえる 4 点:

1. `PluginsPath` に DLL 配置先を書く
2. `TimeoutMilliseconds` にタイムアウトを書く
3. `RetryCount` に再試行回数を書く
4. `StageOrders` にステージと順序を書く

`Id` はプラグイン側の ID と一致している必要があります。

## 7. 最小実装（C# / VB）

### 7-1. 最小実装（C#）

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

### 7-2. 最小実装（VB）

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

### 7-3. 最小プラグイン実装（C#）

```csharp
using PluginManager;

[Plugin("my-plugin", "マイプラグイン", "1.0.0", "Processing")]
public sealed class MyPlugin : PluginBase
{
    public MyPlugin()
        : base("my-plugin", "マイプラグイン", new Version(1, 0, 0), PluginStage.Processing)
    {
    }

    protected override Task<object?> OnExecuteAsync(
        PluginStage stage,
        PluginContext context,
        CancellationToken cancellationToken)
        => Task.FromResult<object?>("完了");
}
```

最初は次だけ守れば十分です。

- まずは `PluginBase` を使う
- ステージは `PluginStage.Processing` から始める
- `pluginsettings.json` の `Id` と一致させる

## 8. ステージ間のデータ受け渡し

前段プラグインで値を保存します。

```csharp
context.SetProperty("sample.Message", "Hello, PluginManager!");
context.SetProperty("sample.CreatedAt", DateTime.Now);
```

後段プラグインで値を読みます。

```csharp
var message = context.GetProperty<string>("sample.Message");
var createdAt = context.GetProperty<DateTime>("sample.CreatedAt");
```

呼び出し側では、**全ステージで同じ `PluginContext` インスタンス** を渡してください。

```csharp
var context = new PluginContext();

await loader.ExecutePluginsAndWaitAsync(results, PluginStage.PreProcessing, context);
await loader.ExecutePluginsAndWaitAsync(results, PluginStage.Processing, context);
```

## 9. チェックリスト

### アプリ側

```
□ `PluginManager.Core` / `PluginManager` を `ProjectReference` で参照している
□ プラグイン DLL は `ProjectReference` で参照していない
□ ビルド後に `plugins/` へ DLL をコピーしている
□ `pluginsettings.json` を出力ディレクトリへコピーしている
□ `PluginLoader` を `using var` または `try/finally` で `Dispose` している
□ `LoadFromConfigurationAsync` の結果を確認している
□ 全ステージで同じ `context` を渡している
□ Windows Service などでは設定ファイルパスを絶対パスで渡している
```

### プラグイン実装側

```
□ `SupportedStages` を `IReadOnlySet<PluginStage>` で宣言している
□ `[Plugin]` 属性の `Id` と `IPlugin.Id` が一致している
□ 初期化が必要な場合だけ `OnInitializeAsync` を使っている
```

## 10. 次に読む資料

### 日常開発で必要になったら

- `docs/developer-guide.md`
  - 詳細な設定方法
  - 実装パターン
  - `PluginContext` の詳細 API
  - エラーハンドリング
  - 常駐アプリでの使い分け

### より高度な構成や診断が必要になったら

- `docs/advanced-guide.md`
