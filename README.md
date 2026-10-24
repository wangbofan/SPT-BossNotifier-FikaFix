# BossNotifier — Fika 无头主机修复版

[原版 BossNotifier](https://sp-mod.com/mod/2543/bossnotifier)（作者 **Mattexe**，v1.2.1）在 **Fika 无头主机（headless host）** 房间里完全不出提示：不播 boss 名单、不播 "No Bosses Located"、没有屏幕标记、按热键也没反应。

这个仓库是修复版，**替换原来的 `BossNotifier.dll` 即可**，配置文件与 `custom_bosses.json` 全部兼容，不需要重新配置。

修复版版本号 **`1.2.1.4`**（BepInEx 插件列表与日志里能看到；文件属性 `ProductVersion = 1.2.1-fika.4`），插件 GUID 仍是 `Mattexe.BossNotifier`，所以配置文件名不变。

下载：见 [Releases](https://github.com/wangbofan/SPT-BossNotifier-FikaFix/releases)，或直接取仓库里的 `dist/BossNotifier.dll`。

---

## 根因

修复前先要明白：无头房里，能看见屏幕的只有**加入房间的客户端**，而无头服务端本身没有玩家、没有摄像机、没有 HUD。原版恰好把这两者弄反了。

### 1. 开关反了：只有房主才会工作

```csharp
// 原版 BossNotifierMono.ShouldFunction()
if (BossNotifierPlugin.FikaClientTypeProperty == null) return true;
return (int)BossNotifierPlugin.FikaClientTypeProperty.GetValue(null) == 2;   // ← 2 = Host
```

Fika 的枚举是 `EClientType { None = 0, Client = 1, Host = 2 }`，所以这句的实际含义是「只有房主才工作」。无头主机 `ClientType == Host` → 它去做播报（但没屏）；客户端 `ClientType == Client` → 整个插件被静默。

### 2. 数据断了：客户端根本没有 boss 名单

原版两条唯一的 boss 数据来源，都只会在**主机侧**执行：

| 补丁 | 目标 | 客户端为什么不执行 |
|---|---|---|
| `BossLocationSpawnPatch` | `BossLocationSpawn.Init()` | 出生流程由房主的 `BossSpawnScenario` 驱动，客户端不跑 |
| `BotBossPatch` | `BotBoss` 构造函数 | 客户端上的 bot 是 `ObservedPlayer`，不会构造 `BotBoss` |

于是客户端即使认出了 boss，`bossesInRaid` 也是空的，`GenerateBossNotifications()` 只能产出空列表 → 连一句 “No Bosses Located” 都不会有。

### 3. 时序：开局 2 秒时数据还没到

客户端要靠房主广播的 `SendCharacterPacket` / `SpawnAIPacket` 才知道场上有什么。开局 2 秒时这些包通常刚到或还没到，原版此时已经播完并再也不播了。

---

## 修复内容

### `FikaCompat.cs`（新增，反射探测，软依赖）

没有装 Fika 时全部退化为单机语义，不引入对 `Fika.Core.dll` 的硬引用。

```csharp
public static bool CanRenderUi
{
    get
    {
        if (LocalHeadless) return false;   // 真·无头进程，永远不画 UI
        return HasLocalPlayer;             // 有没有本地玩家，才是"能不能显示"的本质条件
    }
}
```

> ⚠️ 关键点：**不能**用 `FikaBackendUtils.IsHeadlessGame` 判断。它的含义是「本局房主是无头服务端」（`FikaPingingClient.Init` 里 `IsHeadlessGame = host.IsHeadless`），加入无头房的正常客户端一样是 `true`。用它当开关会把正常客户端一起静默——这是中途踩过的坑。

### `BossZoneLocator.cs`（新增）

客户端没有 `BossLocationSpawn.BornZone`，改用 boss 坐标反查最近的 `BotZone`：

```csharp
// 首选：直接读场景字段
LocationScene.LoadedScenes[i].BotZones
// 兜底：直接扫已加载对象（含未激活）
Resources.FindObjectsOfTypeAll<BotZone>()
// 距离优先用 SpawnPointMarkers，退化到 CenterOfSpawnPoints，再退化到 transform.position
```

> ⚠️ 反例：**不要**用 `LocationScene.GetAllObjects<BotZone>()`。它查的是 `_typeToContainer`，而 `LocationScene.Init()` 里的 `Register(BotZones)` 可能在地形数组被 `FillArraysFrom` 填充之前执行，登记进去的是个空数组、之后不再更新 —— 实测在 Streets 上永远返回 0 个 zone，提示就退化成 “X has been located.”，说不出在哪。

区域名还会**延迟补解析**：开局那一刻 `BotZone` 可能还没加载完，`bossesInRaid` 里会先留下空区域名；之后每次生成提示（按 O、附近播报、boss 死亡）都会用 boss **首次被看到时的坐标**再查一次，补上就写回 `bossesInRaid`。

这样客户端也能给出区域名，而不是只播 “X has been located.”。

### `BossNotifierMono.cs`（改动）

**a. UI 闸门**：`ShouldFunction()` 改为走 `FikaCompat.CanRenderUi`；`Start()` 不再直接初始化 UI，而是 `TryBeginUi()` 轮询等本地玩家出现（最多 120 秒），无头进程直接停在被动跟踪状态。

**b. 客户端 boss 登记**：`OnPersonAdd` 收到 `ObservedPlayer` 后，按 `Profile.Info.Settings.Role` 判 boss，再用 `BossZoneLocator` 补区域名写进 `bossesInRaid`。

**c. 播报链路缩短**：原来固定等 90 秒（后改 15 秒）才下结论，现在改成**数据到货即播**：

```csharp
// OnPersonAdd：role 51 = pmcBEAR、52 = pmcUSEC，其余都算 AI
if (!val3.HasValue || ((int)val3.Value != 51 && (int)val3.Value != 52))
{
    _botObservations++;
    NotifyBotDataArrived();      // 0.5 秒防抖后立刻播报
}
```

`SendBossNotifications()` 只在「一个 AI 都没看到」时才重试，看到 AI 就直接定论 `No Bosses Located`。`BossDataWaitSeconds = 15` 仅作兜底。

**d. 客户端接近提示**：`vicinity` 判定不再限定房主，客户端也会播 `X has been detected in your vicinity.`（60 米内，每组一次）。

**e. 自检通知**：进图约 1 秒播一条 `BossNotifier: online`。看不到这条就说明问题在通知显示链路而非 boss 数据，能一口气把排查范围砍半。

---

## 安装

1. 关掉游戏。
2. 备份并替换：

```
<EscapeFromTarkov>\BepInEx\plugins\BossNotifier\BossNotifier.dll
```

3. 保留同目录下的 `custom_bosses.json` 和 `BepInEx\config\Mattexe.BossNotifier.cfg`，配置项完全兼容。

无头服务端**也要**放同一份 dll（它不播报，但保持数据路径一致，方便日志对照）。

## 时间线（进图后）

| 时刻 | 现象 |
|---|---|
| ~1 s | `BossNotifier: online` |
| 2 s | 第一次播报尝试，无数据则静默重试 |
| ~2–4 s | 第一个 AI 进入 `GameWorld` → 日志 `Bot data arrived (N observation(s))` |
| +0.5 s | boss 名单 或 `No Bosses Located` |
| 之后 | 新 boss 出现时各组补播一次；按 `O` 随时重播 |

## 排查日志

`BepInEx\LogOutput.log` 里搜 `BossNotifier`：

```
Fika detected - ClientType=1 localHeadless=False hostHeadless=True
BossNotifier UI ready (clientType=1, hostHeadless=True)
Scanned 5 existing player(s), found 0 boss(es)
Bot data arrived (3 observation(s)) - announcing boss list in 0.5s
Sending 1 boss notification(s): Reshala has been located near Dormitory.
```

| 最后停在 | 含义 |
|---|---|
| `Fika detected` 之后没了 | `TryBeginUi` 一直没等到本地玩家 |
| 有 `UI ready` 无 `Bot data arrived` | 整局一个 AI 都没收到（bot 全关？） |
| 有 `Sending ...` 但屏上没字 | 通知显示链路问题，不是数据问题 |
| 有 `Cached N BotZone(s)`，N≥1 | 区域反查可用，播报会带 `near <地名>` |
| `No BotZone found yet` | 场景里还没找到 BotZone，会每 5 秒重试；此时先播 “has been located.”，之后自动补上地名 |

`Late-resolved zone for <Boss>: <地名>` 表示开局没查到、后来补上了。

---

## 编译

需要 .NET SDK（netstandard2.1 目标）与本地 EFT + BepInEx + SPT 的托管库。

```powershell
dotnet build src\BossNotifier\BossNotifier.csproj -c Release -p:GameDir="E:\GAME"
```

`GameDir` 默认 `E:\GAME`，指向包含 `EscapeFromTarkov_Data\Managed` 与 `BepInEx\core` 的游戏根目录。
产物：`src\BossNotifier\bin\Release\BossNotifier.dll`（也可用 `build.ps1`，会自动复制到 `dist\`）。

引用的程序集：`BepInEx`、`0Harmony`、`Assembly-CSharp`、`spt-reflection`、`Comfort`、`Newtonsoft.Json`、`UnityEngine(.CoreModule/.TextRenderingModule/.PhysicsModule/.IMGUIModule)`。**不引用 `Fika.Core.dll`**——Fika 相关全部走反射。

## 兼容性

- 原版 v1.2.1 行为在单机下保持不变（`FikaCompat` 在无 Fika 时返回 `-1`，走原逻辑）。
- Fika 类型按名称反射（`Fika.Core.Main.Utils.FikaBackendUtils`），不绑定具体 Fika 版本；找不到类型就当作单机。
- 仅客户端 mod，不改服务端数据。

## 声明

源码由原版 v1.2.1 程序集反编译而来，**原始代码与设计归 Mattexe 所有**，本仓库只包含为适配 Fika 无头房间所做的修改。如果原作者要求下架，请提 issue。
