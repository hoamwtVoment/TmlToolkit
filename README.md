# Terraria / tModLoader 全物品修改器

Windows 平台的 Terraria 原版与 tModLoader 双兼容修改器。项目采用独立启动器、原生注入引导和游戏内 WinForms 界面，不需要安装 tModLoader Mod。

## 主要特性

### 启动与注入

- 一个 EXE 同时支持 Terraria 原版和 tModLoader。
- 游戏未启动时可以先打开完整功能预览界面。
- 后台检测到游戏进程后自动选择客户端并注入。
- 游戏内界面启动成功后自动关闭预览启动器。
- 阻止同一游戏进程重复注入。

### 生存与防护

- 无敌、生命锁定、无限魔力、无限氧气。
- 秒重生、无药水冷却、清除负面状态。
- 免疫击退、摔落伤害和熔岩伤害。

### 移动与物理

- 可调移动速度和跳跃高度。
- 无限段跳、无限飞行。
- 可调自身重力倍率。
- 可调最大下落速度，支持解除下落速度上限。
- 可调 Step 高度，允许直接走上多个完整方块。

### 容量与召唤

- 修改最大仆从容量。
- 在召唤清理判定前应用容量，避免新仆从错误驱逐已有仆从。
- 修改单人世界中的 Buff 栏容量。

### 世界、视野与建造

- Fullbright 场景全亮。
- 分批点亮整个地图。
- 解除游戏缩放按键限制，并按实际视野扩展渲染目标（tModLoader）。
- 可设置最远缩放倍率（默认 0.5）：越小视野越大，渲染和光照开销也按可见面积增加。
- 忽略玩家软边界和摄像机世界边界。
- 解除 ImproveGame 液体魔杖、建筑和油漆工具的选择范围限制。
- 无限放置、挖掘和交互距离。

### 战斗与特殊状态

- 并发攻击：提前触发新攻击，同时保留已有弹射物。
- 微光穿墙状态下仍可使用物品和操作。
- 移除灾厄通用 Parry/格挡冷却，包括方舟系右键格挡。

### 物品与背包

- 获取游戏中的全部物品 ID、名称和内部名。
- 按 ID、中文名、英文名或内部名搜索。
- 查看并编辑玩家背包槽位。
- 原版背包图标直接从游戏 XNB 内容文件进行 CPU 解码，不依赖显卡贴图回读。
- 修改数量、伤害、暴击、击退、使用时间、动画、射弹速度、尺寸倍率等属性。
- 根据当前物品列出实际可用的原版与模组前缀。
- 应用前缀、清除前缀和随机重铸。
- 查看多人游戏中的其他玩家背包并将选中物品给予自己。
- 记录玩家曾经拿出的物品，支持搜索、清除和导出。

## 使用方式

1. 运行 `Terraria全物品修改器_原版TML双兼容.exe`。
2. 没有运行游戏时，可以在预览界面查看现有功能。
3. 启动 Terraria 原版或 tModLoader。
4. 启动器检测到游戏后会自动注入，并打开可操作的游戏内修改器。

已经注入旧版本时，需要重启游戏进程后再加载新版本。

## 构建

### 环境

- Windows 10/11
- Visual Studio 2022（Roslyn C# 编译器）
- .NET Framework 4.x 编译器
- tModLoader 对应的 Terraria、MonoMod 和 Mono.Cecil 编译引用

`ui.rsp` 保存 tModLoader UI 的编译引用。如果本机 tModLoader 安装位置不同，需要更新其中的编译引用路径；成品运行时不会依赖写死的 tModLoader 安装目录。

### 重新编译 tModLoader UI

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe' `
  /noconfig '@ui.rsp'
```

### 重新编译原版托管界面

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$root = (Get-Location).Path
& $csc /nologo /target:library /platform:anycpu /optimize+ `
  "/out:$root\Vanilla1456Toolkit\Terraria1456Toolkit.Managed.dll" `
  /reference:System.dll /reference:System.Core.dll `
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  "/resource:$root\Vanilla1456Toolkit\items.tsv,Terraria1456Toolkit.items.tsv" `
  "$root\Vanilla1456Toolkit\EntryPoint.cs" `
  "$root\Vanilla1456Toolkit\TrainerForm.cs" `
  "$root\Vanilla1456Toolkit\InventorySlotGrid.cs" `
  "$root\Vanilla1456Toolkit\VanillaGameApi.cs" `
  "$root\Vanilla1456Toolkit\VanillaXnbTextureDecoder.cs"
```

### 生成双兼容整合包

```powershell
.\BUILD_COMBINED.ps1
```

脚本会把原版和 tModLoader 两套注入组件作为资源嵌入：

`Terraria全物品修改器_原版TML双兼容.exe`

## 代码结构

| 路径 | 用途 |
| --- | --- |
| `CombinedToolkit/CombinedLauncher.cs` | 离线功能预览、游戏检测、自动注入和双后端选择 |
| `TmlInjector.cs` | tModLoader x64 注入器 |
| `TmlBootstrap.cpp` | tModLoader 原生引导 |
| `ManagedBootstrap.cs` | .NET 托管引导 |
| `UiEntry.cs` | 游戏内 UI 线程入口 |
| `TmlTrainerForm.cs` | tModLoader 修改器主界面与功能 Hook |
| `TmlInventorySlotGrid.cs` | 背包槽位和物品图标控件 |
| `TmlViewZoom.cs` | 扩展视野的渲染目标尺寸规则（离线测试：`test/TmlViewZoomTest.cs`） |
| `Vanilla1456Toolkit/` | Terraria 1.4.5.6 原版后端 |
| `Vanilla1456Toolkit/VanillaXnbTextureDecoder.cs` | 原版物品 XNB 的 CPU 解压与贴图转换 |
| `Vanilla1456Toolkit/test/ReflectionHarness.cs` | 原版后端差分测试：用桩类型模拟 Terraria，输出可逐行对比的运行轨迹（构建方法见文件头） |
| `BUILD_COMBINED.ps1` | 双兼容 EXE 打包脚本 |
| `CODE_FILES.md` | 代码文件清单 |

## 兼容说明

- 原版后端目录名为 `Vanilla1456Toolkit`，目标版本为 Terraria 1.4.5.6。
- tModLoader 后端在运行时从目标进程识别 Terraria 与 tModLoader 类型，不写死游戏启动路径。
- 灾厄和 ImproveGame 的专用功能会在对应模组存在时安装 Hook；未加载对应模组时不会启用这些专用逻辑。
