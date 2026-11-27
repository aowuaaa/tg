# ThingsGateway 学习路线（精细化版：每阶段读哪里、看什么、理解什么）

> 配套笔记：`code-inventory.md`（文件职责清单）、`learning-path.md`（概览版），属本地学习笔记，未纳入仓库。
> 行号基于当前分支，代码改动后可能偏移，以成员名检索为准。

---

## 阶段 0 技术栈（半天）

| 要学 | 与本项目的连接点 | 看这里的代码验证 |
|---|---|---|
| Generic Host / DI | `Serve.RunAsync`、`IServiceCollection`、`BackgroundService` | `Server\Program\Program.cs:78-145` |
| AppStartup 模块化启动 | `[AppStartup(order)]` + `Configure/Use` 两段式 | `Gateway.Application\Startup.cs:20` |
| Blazor Server | `.razor` + `.razor.cs` + `@inject` | `Gateway.Razor\Page\GatewayMonitorPage.razor.cs` |
| TouchSocket | `TcpClient/TcpService/Dmtp` | `DeviceThreadManage.cs` 里 `ChannelObject`、`ManagementRpcServer.cs` |
| SqlOrm | `DbContext.GetDB<T>()`、`Queryable`、`CodeFirstUtils` | `Startup.cs:110-150`、各 `Entity/*.cs` |
| Mapperly | `[Mapper] partial` 生成 `AdaptXxx` | `Mapper\GatewayMapper.cs` |

---

## 阶段 1 项目地图 + 依赖注入（1 天，重点）

### 1.1 先看“程序怎么被拉起来”

阅读顺序：
1. `src\ThingsGateway.Server\Program\Program.cs`
   - `:48` `Directory.SetCurrentDirectory(AppContext.BaseDirectory)` —— 决定了后面所有相对路径（配置、插件目录）的基准。
   - `:52` `Runtime.CreateConfigOnMissing = true` —— 配置文件缺失时自动生成，读配置逻辑在 Foundation。
   - `:78` `if (Runtime.WebEnable)` —— **Web 模式 / 纯采集模式** 的分水岭。
   - `:81-127` `Serve.RunAsync(RunOptions...)`：`UseWindowsService/UseSystemd`（`:87-91`）、Kestrel 超时与上传限制、`MapRazorComponents<BlazorApp>().AddAdditionalAssemblies(App.RazorAssemblies)`。
     - `AddAdditionalAssemblies` 是关键：Blazor 组件在 `Gateway.Razor`、`Foundation.Razor` 等**别的程序集**里，必须显式加入路由扫描。
   - `:141` `MiniRunOptions` —— 无 Web 时只起最小宿主，所有页面服务仍注册但不渲染。
2. `src\ThingsGateway.Server\Program\Startup.cs`（387 行）
   - `:46-78` 编码器、HostOptions、事件总线、任务调度（`AddSchedule` + `JobPersistence`）、CORS、MVC/Controller、审计过滤器。
   - `:131-157` Blazor Server 参数（`DisconnectedCircuitRetentionPeriod`、Hub 选项）。
   - `:174-200` 本地化 + `BlazorAppContext`。
   - `:203-239` Cookie 认证、OAuth（Gitee/GitHub）、授权、`AuthenticationStateProvider`。
   - `:251-254` DataProtection（`ThingsGateway.pfx` + `Keys` 目录）。
   - `:264-364` `Use(...)`：中间件顺序（异常 → 压缩 → 静态文件 → 路由 → CORS → 认证 → 授权 → ScheduleUI → Antiforgery → MapControllers/MapHubs）。
   - **记一条**：`UseRouting` 之后、`UseAuthentication` 之前必须放 `UseCorsAccessor`（`:339-343`）。

### 1.2 理解 AppStartup：本项目的“模块化 DI”

- 基类与特性来自 NuGet 包 `ThingsGateway.Foundation.Common`（`ThingsGateway.AppStartup`、`AppStartupAttribute`），**不在本仓库**；XML 文档明确写了 `Order` “优先调用数值较大的”。
- 执行模型：框架扫描所有 `AppStartup` 子类 → 按 `Order` 排序 → 依次调用 `Configure(IServiceCollection)`（注册）→ 再由宿主调用各类 `Use(IServiceProvider)`（初始化/建库）。

本仓库的 order 表（检索 `\[AppStartup` 得到）：

| 项目 | Order | 作用 |
|---|---|---|
| `Foundation.Razor` | 100000000 | 通用 UI 服务（最先） |
| `Gateway.Application` | -100 | 网关核心服务、后台任务 |
| `Gateway.Razor` | -1000 | 网关页面服务 |
| `Server` | -99999 | 宿主：Blazor/认证/中间件 |
| `Management.Application` | -100000 | 管理端 RPC 客户端服务 |
| `Management.Razor` | -1000 | 管理端页面服务 |
| `ManagementServer` | -99999 | 管理端宿主 |
| `Photino` / `ManagementPhotino` | -999999 / -99999 | 桌面壳 |

理解点：**后注册覆盖先注册**（DI 单服务解析取最后一个）。所以宿主层（Server/ManagementServer）排在应用层之后，才能覆盖认证、Blazor 等默认实现；管理端把 `I*Service` 全量覆盖成 RPC 代理（见阶段 8）。

### 1.3 读 `Gateway.Application\Startup.cs`（222 行）——服务注册索引页

按行号分块读，一块一块对上“谁在用”：
- `:27-43` 规则引擎、冗余、`GatewayMonitorHostedService`、`ManagementHostedService`、`WebApiHostedService`、`GatewayRedundantSerivce`。
- `:45-55` `AddConfigurableOptions<T>()`：把 `Configuration/*.json` 的节绑定成 Options（`ChannelThreadOptions`、`WebApiOptions`、`RemoteServerManagementOptions`…）。**这是“配置 → 强类型对象”的唯一通道**。
- `:58` `AddDatabaseLogging<BackendLogDatabaseLoggingWriter>`：把 ILogger 输出接到数据库。
- `:71-95` 核心服务三连（以 Channel 为例）：
  ```csharp
  services.AddSingleton<IChannelService, ChannelService>();                       // 纯 DB CRUD
  services.AddSingleton<IChannelRuntimeService, ChannelRuntimeService>();          // DB + 运行时同步
  services.AddSingleton<IChannelPageService>(a => a.GetService<IChannelRuntimeService>()); // 页面契约 = 运行时契约子集
  services.AddSingleton<IChannelModelPageService>(a => a.GetService<IChannelRuntimeService>());
  ```
  → **一个实现，三个接口**；页面只依赖 `I*PageService`，插件/内部只依赖 `I*RuntimeService`。
- `:101-109` 内存变量、报警的后台服务。
- `:110-222` `Use(IServiceProvider)`：**不是 DI**，是启动初始化：
  - 检查 `DbContext.DbConfigs` 的 `ConfigId` 重复；
  - `CreateDatabase()`；
  - 删历史唯一索引/历史列（兼容升级）；
  - `CodeFirstUtils.CodeFirst(程序集全名)` 建表；
  - SQLite 下建 `strftime` 表达式索引（`backend_log`、`rpc_log`）；
  - 注册 `ApplicationStarted/Stopping` 日志。

### 1.4 三个必须看懂的 DI 手法

1. **`AddGatewayHostedService<TService, THostedService>`**
   文件：`src\ThingsGateway.Gateway.Application\Extensions\ServiceCollectionHostedServiceExtensions.cs:25`
   ```csharp
   services.AddSingleton<THostedService>();
   services.AddHostedService<THostedService>(a => a.GetService<THostedService>());
   services.AddSingleton<TService>(a => a.GetService<THostedService>());
   ```
   作用：**同一个实例**既是 `IHostedService`（宿主会 Start/Stop），又能以接口 `TService` 注入给别的服务。
   对比 `services.AddHostedService<X>()`：那种写法你拿不到 `X` 实例。`AlarmHostedService`、`GatewayMonitorHostedService`、`RulesEngineHostedService` 都靠它被 `GlobalData.AlarmHostedService` 等静态属性取到。
2. **别名注册** `AddSingleton<IFoo>(a => a.GetService<Bar>())`
   用于“接口收窄”（PageService ⊂ RuntimeService）和“远程代理替换”（阶段 8）。
3. **Options 绑定** `AddConfigurableOptions<T>()`
   之后在构造函数注入 `IOptions<T>` / `IOptionsMonitor<T>`；日志、WebAPI、线程参数都走这条。

### 1.5 服务被谁消费（三条路径）

| 路径 | 代码位置 | 注意 |
|---|---|---|
| 构造函数注入 | `DeviceThreadManage.cs:35`、各 `*Service` | 单例之间互相注入，禁止注入 Scoped |
| `[Inject]` | 所有 `.razor`（如 `GatewayMonitorPage.razor`） | Blazor 组件是 Scoped 作用域 |
| 静态门面 `GlobalData` | `GlobalData\GlobalData.cs:439` 起 | 懒加载 `App.RootServices.GetRequiredService<T>()`，见下 |

`GlobalData` 的写法值得单独理解：
```csharp
private static IVariableRuntimeService variableRuntimeService;
public static IVariableRuntimeService VariableRuntimeService
{
    get
    {
        if (variableRuntimeService == null)
            variableRuntimeService = App.RootServices.GetRequiredService<IVariableRuntimeService>();
        return variableRuntimeService;
    }
}
```
→ 这是 Service Locator：静态属性被插件/规则引擎/UI 到处使用，所以**对应服务必须是 Singleton**，且 `App.RootServices` 必须在宿主启动后才可用（不要在 `Configure` 阶段访问 `GlobalData`）。

### 1.6 自检 / 坑
- 说出“加一个新服务”要改哪几处（接口、实现、Startup 注册、页面是否需要别名）。
- 为什么 `GlobalData` 里取不到 Scoped 服务？（根容器无作用域，`ValidateScopes` 会抛异常）
- `Management.Application\Startup.cs` 把几乎所有接口注册成 `AddScoped<IXxx>(a => a.GetService<ManagementRpcServerService>())` —— 这是**代理模式**，同一接口在两端含义不同。

---

## 阶段 2 数据模型（1 天）

阅读顺序：
1. `Entity\Channel.cs` → `Device.cs` → `Variable.cs` → `AlarmPropertys.cs`
   - 看 `[OrmTable]/[OrmColumn]`、导航属性、`Device.ChannelId`、`Variable.DeviceId`。
   - `Variable` 重点字段：`RegisterAddress`、`DataType`、`ProtectType`、`OtherMethod`、`ReadExpressions`、报警阈值、`IntervalTime`。
2. `Model\ChannelRuntime.cs` → `DeviceRunTime.cs` → `VariableRunTime.cs`
   - 对比基类：Runtime 继承实体并加运行状态（`Driver`、`VariableRuntimes`、`ReadVariableMethods`、`Value/RawValue`、`CollectTime/ChangeTime`、`IsOnline`、`PerSecondCounter`、`DeviceStatus`）。
   - `VariableRunTime.cs:281 SetValue`、`:330 ApplyOutlierFilter` —— 数据质量总闸门。
3. `Model\VariableSourceRead.cs`、`VariableScriptRead.cs`、`VariableMethod.cs` —— 三种采集单元（批量地址 / 脚本 / 驱动方法）。
4. `Model\DataModel.cs` —— 对外轻量快照（`DeviceBasicData`、`VariableBasicData`），北向与 UI 共用。
5. `Mapper\GatewayMapper.cs` —— Mapperly 生成的 `AdaptXxx`，看 `AdaptListChannelRuntime` 就知道实体怎么变运行时。

自检：为什么 Runtime 要继承实体而不是组合？`VariableSourceRead` 的 `MaxPackCount/GroupInterval` 影响什么？

---

## 阶段 3 插件体系（1-2 天）

阅读顺序：
1. `Driver\DriverBase.cs`
   - `:282 StartAsync`（初始化 → `InitChannelAsync` → `GetTasks` → 启动调度循环）
   - `:341 GetTasks` / `:356 ProtectedGetTasks`（子类只提供任务列表）
   - `:449 ProtectedInitDevice`、`:456 IdVariableRuntimes`、`:468 InitChannelAsync`
   - `:496 AfterVariablesChangedAsync`（抽象：变量变了怎么办）
2. `Driver\Collect\CollectBase.cs`
   - `:62 AfterVariablesChangedAsync`：筛变量 → 建 `FrozenDictionary` → 预热表达式 → 分组打包 → 特殊地址转脚本读 → `RefreshVariableTasks`
   - `:175 RefreshVariableTasks`、`:213 ProtectedGetTasks`（设备状态 10s、在线检测 30s、每包/每方法/每脚本任务）
   - `:264 SetDeviceStatus`、`:496 TestOnline`、`:501 ScriptVariableRun`
   - `:529 ProtectedLoadSourceReadAsync`（**具体协议插件唯一必写的打包逻辑**）
   - `:536 ReadSourceAsync`、`:545 WriteValuesAsync`（读写实现）
   - `:613 InvokeMethodAsync` / `:686 InVokeWriteAsync`（RPC 与写入入口）
   - `:531 ReadWriteLock` + `:361 LinkedCancellationTokenSourceCache`（读写互斥、取消联动）
3. `Driver\Business\BusinessBase.cs`
   - `:61 AfterVariablesChangedAsync`（收集要上报的变量）
   - `:102 ProtectedGetTasks`（把变量分组任务化）
   - `:124 ProtectedExecuteAsync`（**北向插件唯一必写的方法**）
   - 再看 `Driver\Business\Cache\BusinessBaseWithCache*.cs` 里队列/批量/补偿实现。
4. `Services\Plugin\PluginService.cs`
   - `:107 GetDriver`（反射 new 驱动，注入属性）
   - `:273 GetPluginsAsync` / `:295 GetPluginList` / `:752 PrivateGetList`（扫描 `GatewayPlugins`）
   - `:356 ReloadPluginAsync` / `:384 SavePluginByPathAsync`（热更新：`.save`/`.del` 标记文件）
   - `:80 GetLoadContextAssemblyList`、`:665 GetAssembly`（`AssemblyLoadContext` 隔离加载）
5. 特性：`Attributes\*`（`DynamicMethod`、`DynamicProperty`、`EducationPlugin` 等）——决定 UI 如何展示驱动属性与方法。

自检：写一个采集插件必须实现哪 3 个成员？为什么 `ProtectedLoadSourceReadAsync` 只负责“打包”不负责“读”？

---

## 阶段 4 采集数据流（2 天，主干）

阅读顺序：
1. `Services\GatewayMonitor\GatewayMonitorHostedService.cs`
   - `ExecuteAsync` → `RestartAll()`：从 DB 取通道/设备/变量 → `Init()` → `GlobalData` 派发 → `ChannelThreadManage.RestartChannelAsync`
2. `Services\GatewayMonitor\ChannelManage\ChannelThreadManage.cs`
   - `:26 DeviceThreadManages`（通道 → 设备线程管理器字典）
   - `:89 PrivateRestartChannelAsync`（重建/启动/停止）、`:141/:157` 公开重载、`:57-75` 删除
3. `Services\GatewayMonitor\DeviceManage\DeviceThreadManage.cs`
   - `:35` 构造函数（建 `ChannelObject`、日志）
   - `:189/:205/:218 RestartDeviceAsync`（按设备启停，含 `deleteCache`）
   - `:523 DriverStart`（`driver.StartAsync`）
   - `:566 GlobalData_DeviceStatusChangeEvent`（设备状态联动）
   - `:612 DeviceRedundantThreadAsync`、`:714 CheckRedundantAsync`、`:763 CheckThreadAsync`（看门狗：断线重连/线程卡死重启）
4. 回到 `VariableRunTime.SetValue`：表达式 → 异常值过滤 → 更新值 → 事件（`GlobalData.VariableValueChangeEvent`）→ 报警登记。
5. `GlobalData.cs`：`:72-88` 四个事件、`:591-680` 只读字典（`IdChannels/IdDevices/IdVariables/RealAlarmIdVariables`）、`:403/:414` 两个 DispatchService（UI 增量刷新）。

关键概念：
- **打包连读**：同一批连续地址一次报文读回，按 `MaxPackCount` 与地址间隔分组；只读变量才能进包。
- **读写锁**：采集持读锁，写入持写锁，保证报文不交错。
- **重试**：`CollectProperties.RetryCount`；失败后变量置 `IsOnline=false` 并写 `null`。
- **看门狗**：`CheckThreadAsync` 检测任务长时间无进展 → 重启设备线程。

自检：画出“报文→SetValue→GlobalData→Business 插件/UI”的链路；解释设备离线与变量离线的关系。

---

## 阶段 5 配置服务（1 天）

阅读顺序（每个对象都是同一套模板）：
1. `Services\Channel\ChannelService.cs`（CRUD、事务、`IChannelService`）
2. `Services\Channel\ChannelRuntimeService.cs`（`:187 InsertAsync`、`:219 UpdateAsync`、`:277 DeleteChannelAsync`、`:80 RestartChannelAsync`）——**改配置后同步内存 + 重启采集**
3. `Services\Channel\ChannelServiceHelpers*.cs`（校验/命名/批量）
4. `Services\Device\*`、`Services\Variable\*` 同构；`VariableRuntimeService.cs:62 OnWriteVariableAsync`（页面下发写值）、`:469 SaveVariableAsync`
5. `Services\ManageHelper.cs`、`Project\Controller\*`、`GatewayExportService`（Excel 导入导出）

理解点：`restart` 参数 = 是否立刻重建线程；批量导入走“预览 → 校验 → 落库 → 一次性重启”。

---

## 阶段 6 边缘能力（2 天）

1. `MemoryVariable\`：`Driver\MemoryDriver.cs`（虚拟驱动）→ `Model\MemoryChannelRuntime.cs`/`MemoryDeviceRuntime.cs`（预置单例，`GlobalData.cs:663`）→ `Model\MemoryVariableRuntime.cs` → `Services\MemoryMonitorHostedService.cs`（定时执行表达式）→ `Mapper\MemoryVariableMapper.cs`。
2. 表达式/脚本：`Model\MemoryExpressionEvaluatorExtension.cs`、`CSScript\*`；变量侧入口 `VariableRunTime.SetValue` 的 `ReadExpressions`。
3. 报警：`Services\Alarm\AlarmHostedService.cs`（生命周期）→ `AlarmTask.cs`（扫描 `GlobalData.AlarmEnableIdVariables`，产生/恢复，写 `RealAlarmIdVariables`）→ `RealAlarmService.cs`（查询/确认）→ `Model\AlarmVariable.cs`、`AlarmRuntimePropertys.cs`。
4. 规则引擎：`Services\RulesEngine\Entity\Rules.cs`（图）→ `Node\`（`Start/Trigger/Expression/Actuator` 四类）→ `Services\RulesEngineHostedService.cs`（订阅事件）→ `Services\RulesService.cs`（CRUD/执行）→ `Helps\DefaultDiagram.cs`（默认图）。

自检：报警状态机；一条规则从变量变化到执行脚本的调用栈。

---

## 阶段 7 北向输出与内置服务（1 天）

1. `Driver\Business\BusinessBase.cs` + `Driver\Business\Cache\*`：队列 → 批量 → 失败补偿。
2. `Services\Management\WebApi\WebApiHostedService.cs` / `WebApiTask.cs`：网关自带 HTTP 服务端（TouchSocket），对外提供 RPC/接口。
3. `Services\Rpc\RpcService.cs`：内部 RPC 聚合（变量读写、设备方法）。
4. `Project\Controller\*`（ASP.NET WebAPI）与上面的 WebApi 是两条通道，注意区分。

---

## 阶段 8 运维与远程管理（1-2 天）

1. 网关侧暴露：`Services\Management\Management\ManagementRpcServer.cs`（把 `IChannelPageService` 等全部转发）、`ManagementHostedService.cs`、`ManagementTask.cs`、`ManagementOptions.cs`。
2. 管理端消费：`Management.Application\Services\ManagementRpcServerService.cs`（RPC 客户端）、`ManagementConfigService.cs` + `Model\ManagementConfig.cs`（对端地址/令牌/心跳）。
3. 冗余：`Services\Management\Redundant\`（`RedundancyTask` 主循环、`RedundancyService`、`RedundantRpcServer`、`GatewayRedundantSerivce` 开关）。
4. 升级：`Services\Management\Management\Update\`（`UpgradeRpcServer`、`FileServerHelpers`、`UpdateZipFile`、`RestartServerHelper`）。
5. 重启：`Services\Management\Restart\RestartService.cs`。
6. 日志：`Logging\BackendLogDatabaseLoggingWriter.cs`、`Job\LogJob.cs`、`Services\GatewayLogs\*`。
7. 指标：`Services\GatewayReport\*`、`Entity\MetricDefinition.cs`、测试 `test\...\MetricCalculatorTest.cs`。

自检：管理端点“重启”后的完整 RPC 调用链；冗余切换触发条件与数据同步范围。

---

## 阶段 9 UI（2 天）

1. `Gateway.Razor\Page\GatewayMonitorPage.razor` + `.razor.cs`（`:19 TreeChangedAsync`、`:116 OnAfterRenderAsync`）——左树右表的骨架。
2. `Page\ChannelDeviceTree.razor(.cs)`、`ChannelDeviceHelpers.cs`、`ChannelDeviceTreeItem.cs`（树节点模型与权限过滤）。
3. `Pages\GatewayMonitorPage\{Channel,Device,Variable,MemoryVariable}\*`：
   - `*Table.razor`（列表 + 批量操作）、`*EditComponent.razor`（编辑表单）、`*RuntimeInfo.razor`（实时值/统计）、`*CopyComponent.razor`。
4. `Gateway.Razor.Common\Components\PropertyComponent.razor` —— 反射生成插件属性表单，**插件无需写 UI**。
5. `Pages\RulesEngine\*`（拖拽画布）、`Pages\RealAlarmPage\*`、`Pages\LogPage\*`、`Pages\PluginPage\*`、`Page\TopologyPage\*`、`Page\ReportPage\*`。
6. `Gateway.Razor\Startup.cs`（仅注册 `IGatewayExportService`）+ `Locales\*.json`（EmbeddedResource）。
7. **改界面改这里**；`Management.Razor\Pages\*` 是构建时拷贝的副本。

自检：点“保存设备”后的调用链；插件配置表单是哪个组件生成的。

---

## 阶段 10 宿主/配置/部署（1 天）

1. `Server\Program\Program.cs`、`Server\Program\Startup.cs`（见阶段 1）。
2. `Server\Configuration\*.json`：`App.json`（`WebEnable`）、`Database*.json`、`ChannelThread.json`（采集线程/锁参数）、`GatewayLog.json`、`RpcLog.json`、`Management*.json`、`WebApi*.json`、`Website*.json`（认证/端口）、`JWT.json`。
3. `Server\targets\*.targets` + `Directory.Build.targets`：构建时解压/复制插件包（`Gateway.targets`、`Plugin*.targets`、`Pro2~Pro8.targets`）。
4. `ManagementServer\Program\*`、`ManagementServer\Configuration\*`、`Photino\Startup.cs`、`ManagementPhotino\Startup.cs`。
5. 部署产物：`Dockerfile`、`docker\*`、`WindowsServiceCreate.bat`、`*.service`、`pm2.json`。

自检：`WebEnable=false` 时哪些服务不启动？插件 dll 从哪来？

---

## 阶段 11 动手练习（持续）

1. 订阅 `GlobalData.VariableValueChangeEvent` 打印变化（验证事件流）。
2. 给变量加 `ReadExpressions`，观察异常值过滤（参考 `docs\异常值过滤功能说明.md`）。
3. 仿 `ScriptDebug\Test\TestCollectPlugin.cs` 写最小采集插件，本机跑通。
4. 跑测试：`dotnet test test\ThingsGateway.Gateway.Test`（异步锁、指标、报表）。
