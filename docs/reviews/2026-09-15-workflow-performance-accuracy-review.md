# VS C# 开发工作流审查与增强建议

日期：2026-09-15。代码基线：`5d55a95`，产品版本 `0.2.0`。

## 审查进度

- [x] 核对当前源码、README、现有架构文档与工具契约快照。
- [x] 核对已安装共享技能的默认调用流程。
- [x] 执行一次只读 VS 实例发现，确认发现链路可用。
- [x] 调研微软、Anthropic 和 MCP 官方资料。
- [x] 区分代码问题、改进建议、验证缺口与外部方案适用边界。

本报告是审查结果，不代表以下整改已经实施。本轮未修改产品代码，未安装或更新 VSIX、MCP、插件或技能，未操作业务工程的调试状态。

## 结论

下一轮重点应从“增加工具数量”转向“让已有工具提供可验证的新鲜证据，并减少完成任务所需的调用与计算”。

当前最值得先处理的不是更换 MCP，而是四项准确性问题：修改预览的源码版本校验、诊断基线、短期缓存失效、上下文租约的目标一致性。提速方面，文件诊断和批量源码读取存在可直接定位的优化点。调试增强应围绕可重复执行的场景闭环建设，而不是继续增加孤立按钮。

## 证据范围

- 当前契约快照包含 83 个工具、15 个资源模板。对快照中 `tools` 数组重新压缩序列化，得到 112,174 个 UTF-8 字节；这不是模型实际输入 token 数，也不意味着所有宿主每次都会加载全部定义。
- `CodeNavigationTools.cs` 为 11,072 行，任务内核仍依赖这一实现类。文件长度不是独立缺陷，但说明能力外壳拆分尚未消除内部集中耦合。
- 已安装共享技能为 212 行、27,271 字节。它与仓库运行时是不同核对对象，不能据此推断所有宿主技能都完全相同。
- 本轮 VS 实例发现成功，调用约 0.212 秒；发现的活动解决方案属于其他业务工程，未对其深入调用。该结果只证明发现链路可用，不证明全部查询、调试、修改能力通过验证。
- `HANDOFF.md` 中 2026-09-07 的 251/251 测试通过属于历史证据。本轮未运行自动化测试或性能基准。
- 当前公开树没有历史交接中提到的 `docs/architecture.md`、`docs/implementation-plan.md`、`docs/current-session-dogfood-plan.md`。本次采用当前实际存在的架构文档，没有把文件缺失推断为产品能力缺失。

基线文件：[README](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/README.md:15)、[工具快照](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/docs/agentic-tool-schema.snapshot.json:1)。

## 一、确认的问题

以下问题按优先级排列。“确认”指调用路径和契约问题可以从源码确认；未声称已在用户的真实业务工程中复现。

### F1 / P1：修改预览的版本号不覆盖源码修改

代码：[版本构造](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Vsix/Workspace/VisualStudioWorkspaceQueryService.RefactoringMutation.cs:1633)、[应用前比较](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Vsix/Workspace/VisualStudioWorkspaceQueryService.RefactoringMutation.cs:655)、[预览会话标识](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Vsix/Workspace/VisualStudioWorkspaceQueryService.RefactoringMutation.cs:2455)。

- `CreateWorkspaceVersion` 只组合解决方案路径、项目数量和各 `project.Version`。
- Roslyn 官方定义指出，`Project.Version` 对应项目文件版本，不是全部源码版本。[S1]
- 会话标识由修改种类、诊断编号和候选稳定键生成，没有绑定这一次预览的实际差异。
- 服务端会话记录比较版本、候选和阻断项，也没有保存实际差异摘要：[MutationSessionStore](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/MutationSessionStore.cs:23)。

触发场景：先预览项目级 Fix All，再在同一项目新增一个同类源码问题，保留相同候选后应用。重新计算出的修复范围可能已经变化，但现有版本和会话比较不足以保证拒绝。风险是“实际应用超出已审阅的预览”，不应夸大为一定覆盖用户编辑。

建议：预览绑定目标身份、源码快照、候选与差异摘要；应用前验证同一快照和差异，变化后要求重新预览。快照应覆盖文档文本，以及影响解析的项目选项、引用、分析器配置等；不能仅换成语义版本，因为方法体修改也必须被识别。

验收：预览后修改方法体、新增同类诊断、切换条件编译配置或另一个任务编辑文件，旧预览都应被拒绝；未变化预览仍能正常应用。

### F2 / P1：诊断“本次引入”和“原有问题”是启发式，却当作事实输出

代码：[构建问题分类](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs:5319)、[Roslyn 诊断分类](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs:5361)。

- 改动文件中的问题直接标记为 `IntroducedByCurrentChange`，其他问题标为 `PreExisting`。
- 相应证据通常标记为 `Fact`。
- 一个原有错误可能恰好在改动文件中；一个新错误也可能由 API 修改引起，却出现在未改动的调用方。两种情况都会被错误归类。

建议：将“与当前任务相关程度”和“是否新增”拆成独立字段。没有可比的历史诊断快照时，新增状态必须是未知；有基线时，才在相同配置、框架和分析范围下比较诊断指纹。指纹不能只依赖行号，需处理代码插入造成的位置变化。

验收：未修改文件中出现的新增调用方错误可识别；已修改文件中的原有错误不变成新增；基线缺失、分析范围或配置变化时不输出虚假的确定结论。

另一个相关问题：[噪声策略](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Protocol/DiagnosticsNoisePolicy.cs:43)仍把 `acadplugins`、`tzdata_src` 写进全局规则。建议迁移为仓库配置；生成代码分类、项目既有问题基线和用户排除规则应分开，不能用业务目录名替代诊断证据。

### F3 / P1：短期缓存没有源码版本失效机制

代码：[缓存实现](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/ShortLivedQueryCache.cs:17)、[源码读取缓存](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs:2600)、[缓存键](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs:11056)。

- 默认缓存 5 秒，键是操作名和请求参数，没有文档版本或工作区代次。
- 源码、符号、诊断、项目图都使用该缓存；缓存类没有主动失效接口。
- 非异常形式的失败结果也可被缓存。

触发场景：读取代码后马上编辑，再用相同参数读取源码或诊断，可能拿到旧结果。`ServerCacheHit` 虽然标记了命中，但不能证明内容仍对应当前代码。

建议：缓存按具体 VS 实例、解决方案代次、文档或项目版本组织；修改成功和工作区事件驱动失效，TTL 仅作为兜底。提供修改后强制新鲜读取，失败与不完整结果采用独立策略。不要为了算缓存键，每次先完整扫描全解决方案。

验收：VS 内未保存编辑、外部文件编辑、工具重命名和重新加载项目之后，查询均不得返回旧版本内容。

### F4 / P1：有效上下文租约会覆盖本次显式指定的目标

代码：[目标解析](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/WorkflowKernel.cs:423)、[租约存储](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/WorkspaceContextLeaseStore.cs:30)、[管道路由](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Bridge/NamedPipeVisualStudioWorkspaceBridge.cs:675)。

- `ResolveTargetAsync` 先返回有效租约目标，之后才处理 `requestedTarget`。
- 租约有效性只检查存在和过期，没有校验本次显式目标是否冲突。
- 管道路由优先使用 `PipeName`，不能仅靠附带的旧解决方案路径保证目标没有改变。

触发场景：任务保留了工程 A 的租约，下一次明确指定工程 B，结果仍查询 A。这会污染后续定位和修改决策；本报告不声称该任务级接口本身会直接修改错误工程。

建议：显式目标与租约冲突时明确拒绝或重建租约，不能静默覆盖；租约绑定解决方案代次，VS 更换解决方案后失效。

验收：双 VS 实例切换、同一 VS 更换解决方案、旧租约与新目标同时传入，均有确定且可解释的行为。

### F5 / P2：查询范围变小，没有同步缩小底层计算

代码：[诊断执行](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Vsix/Workspace/VisualStudioWorkspaceQueryService.Diagnostics.cs:101)、[分析器执行](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Vsix/Workspace/VisualStudioWorkspaceQueryService.Diagnostics.cs:238)、[批量源码读取](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs:2640)。

- 文件参数会把诊断范围缩到所属项目，但仍先计算该项目的全部编译器和分析器诊断，再按文件筛选。
- `MaxResults` 限制最终返回数量，不限制前面的分析工作量。
- 批量源码读取只是一个 MCP 调用内部串行调用 N 次桥接；每次桥接重新建立管道，并非一次 VS 快照批量查询。

建议：分成文档快速检查、受影响项目检查、完整项目检查三个明确等级。文档级检查不能冒充完整构建；需要全项目分析的规则按等级执行，并暴露分析覆盖范围。批量源码读取下沉到 VSIX，在同一解决方案快照内读取并去重；并发只用于彼此独立的后台计算，不把依赖 VS 主线程的调用无界并行化。

验收：记录实际分析项目数、文档数、分析器耗时和桥接次数；读取 20 个位置应走一次批量桥接。比较冷启动和热缓存性能，不先承诺固定提速比例。

### F6 / P2：输出预算和端到端耗时预算没有成为硬约束

代码：[输出裁剪](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/WorkflowKernel.cs:464)、[预算定义](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/WorkflowBudgetPolicy.cs:7)、[计时结束位置](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/WorkflowKernel.cs:73)。

- 超过字符预算后只再裁剪一次，不检查裁剪后的结果是否仍超限；留下的单条长字符串仍可能很大。
- 计量对象是内部结果，不包含最终外层响应的全部字段。
- `MaxToolCalls` 和 `MaxElapsedMilliseconds` 在任务内核中作为预算元数据构造，没有形成统一的执行约束。
- 编辑任务在证据包构造、资源序列化之前停止计时，漏记部分成本。

建议：统一在执行入口建立截止时间和计算预算，逐层传递；最终响应经过硬上限检查，超大正文转资源，必要时返回固定小型错误响应。区分字符、UTF-8 字节、实际或估算 token，不能混称。必须保留错误、截断状态和安全阻断信息，不能靠裁掉这些字段满足预算。

验收：长诊断、长路径、大字符串和中文混合内容，最终响应均符合明确上限；统计覆盖序列化与资源处理。

### F7 / P2：延迟读取资源，尚未实现有界存储

代码：[资源写入](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/EvidenceStore.cs:27)、[JSON 序列化](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/EvidenceStore.cs:55)、[过期处理](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Agentic/EvidenceStore.cs:110)。

- 资源保留 30 分钟，但过期清理仅由列举或读取触发；持续写入、不读取资源时，过期数据仍留在内存。
- 没有条目数、单资源大小或总字节容量限制。
- 资源在创建时就完整序列化；“按需返回”并未同时做到“按需计算”。
- 资源过期被返回为普通文本内容，不利于客户端区分证据正文与获取失败；官方规范提供资源不存在的错误形式。[S9]

建议：写入时清理、容量淘汰、按内容摘要去重、延迟序列化及超大正文按段读取；资源过期应是机器可识别的状态，并给出重新获取动作。缓存应有明确隐私和磁盘保留策略。

验收：持续生成证据而不调用资源读取，内存占用仍受上限约束；已过期资源不能被误当作正常证据正文。

### F8 / P2：取消没有贯通请求合并与 VS 执行

代码：[同键请求合并](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/ShortLivedQueryCache.cs:61)、[客户端等待](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Bridge/NamedPipeVisualStudioWorkspaceBridge.cs:613)、[VS 请求生命周期](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Vsix/Bridge/VisualStudioBridgeServer.cs:130)。

- 合并请求等待共享任务时，没有使用各等待方自己的取消令牌；工厂捕获的是创建共享请求时的调用方令牌。
- 每个等待方都会按键移除共享任务，缺少“只移除自己观察到的那一项”的身份比较，失败重试时存在竞态窗口。
- 客户端取消等待不等于 VS 收到取消指令；VS 端令牌来自服务生命周期和本地超时，缺少按请求编号的跨进程取消。

建议：区分共享计算生命周期与单个等待方生命周期；请求带编号、截止时间和可查询终态，显式传播取消。修改开始后的取消需说明“未开始、已完成或结果未知”，不得自动重试非幂等操作。

验收：一个等待方取消不影响其他等待方；取消后后台工作及时收敛；修改结果不确定时先查询操作状态，不盲目重放。

## 二、技能与结构优化

### 技能默认路径应更短、更一致

当前共享技能既要求先枚举实例，又列出工作区准备、健康检查、任务入口的默认链。任务入口自身也收集就绪信息，因此规则容易引导重复准备。

同时，Code Fix 章节已经说明支持提供程序驱动的预览和应用，复杂重构章节仍把 Code Fix / Fix All 描述为等待后续版本的仅计划能力。这是指令漂移，不是这些功能完全未实现。

建议将常用技能正文缩为决策路径：已验证目标直接使用任务入口；首次进入或目标变化时准备工作区；只有故障时展开健康和安装流程。写操作仍需明确目标与预览，不得为了减少调用跳过安全检查。

### 保留传输层，按能力拆内部实现

不建议现在再做一次大爆炸式重写。保留 MCP 协议、VSIX 宿主和兼容工具门面，逐步提取：

1. 工作区身份与快照服务：管理实例、解决方案代次、文档版本和租约。
2. 查询服务：导航、文档批量读取、诊断分级和缓存失效。
3. 修改协调服务：预览、差异绑定、应用互斥、幂等状态和修改后验证。
4. 调试场景执行服务：事件等待、一致快照、执行所有权和资源清理。
5. 验证与证据服务：构建/测试/性能采集、诊断基线、输出预算和资源存储。

已有工具类应调用这些服务，不再把新能力继续放进 `CodeNavigationTools`。迁移以行为等价测试保护，每次迁移一条工作流。

## 三、官方方案与可落地的增强

下述是设计建议，不是已经实现的能力，也不意味着可直接调用其他产品的内部智能体。

| 方向 | 官方依据 | 本工具建议 | 限制 |
| --- | --- | --- | --- |
| 调试闭环 | 微软 Debugger Agent 描述复现、设置观测、运行验证、针对性修复与人工确认。[S2] | 在现有调试计划与控制工具之上增加场景执行器，保存假设、观测点、证据、结论和复验结果。 | 官方 Copilot 产品功能不等于公开 SDK；需独立验证 VS 接口。 |
| 失败测试驱动调试 | Test Explorer 支持解释失败、调试测试；官方描述修复后在调试器下重跑。[S3] | 增加“失败测试证据包”：测试唯一标识、框架、配置、参数、堆栈、源码映射和受影响调用链，再允许显式重跑单项。 | 当前验证入口主要输出计划，不能把计划当作已执行测试。 |
| 性能诊断闭环 | Profiler Agent 将 CPU、分配分析和基准前后对比结合；.NET 有成熟采集工具。[S4][S5] | 根据症状选择计数器、短时跟踪、线程堆栈或转储分析，返回热点与修复前后对比，不把原始大日志塞给模型。 | 按目标运行时和能力检测选后端；不能默认现代 .NET 工具适用于全部 .NET Framework 或混合原生工程。 |
| 按需工具和服务端组合 | Anthropic 建议延迟发现工具、在执行环境筛选中间结果，减少模型往返。[S6][S7] | 对外优先少量任务入口，兼容保留原始工具；支持宿主能力感知的工具发现。稳定步骤在服务端编排，模型只处理需要判断的分支。 | 不同宿主不一定支持相同延迟加载机制；厂商案例的节省比例不能作为本工具的预测。 |
| 可恢复长任务 | MCP 2025-11-25 的 Tasks 描述可查询状态、延迟取结果、取消和生命周期。[S8] | 为构建、测试和调试场景建立任务编号、事件游标、截止时间、取消和终态；宿主断开后能判断任务是否仍在执行。 | 该规范版本把 Tasks 标为实验性；协商支持后采用，不支持的宿主提供普通工具适配。 |

### 调试场景的建议契约

当前 [调试场景计划](D:/WorkCodes/VisualStudio.CSharpNavigatorMcp/src/VisualStudio.CSharpNavigator.Server/Tools/CodeNavigationTools.cs:9728)返回步骤和推荐动作，仍要求调用方逐项执行和轮询。增强不应把它偷偷变成会启动进程的只读接口，而应新增显式执行入口。

- 输入绑定目标实例、解决方案、启动配置、进程身份、场景输入和预期结果。
- 收到真实停止事件后产生 `stopId`；堆栈、线程、变量和源码从同一次停止快照获取，避免跨次暂停混合数据。
- 临时断点、跟踪点和启动进程有任务所有权，结束时只清理本任务创建的资源。
- 复验重用相同输入，结果区分成功、失败、未执行和证据不足，不能用“进程启动了”替代业务验证。
- 表达式求值默认限制副作用。日志、局部变量、转储中的敏感数据需脱敏和明确保留期限。
- 应用特有的“打开文档、创建对象”等动作使用应用侧结构化适配器，并返回操作编号和结果证据；不要依赖临时拼接调试表达式作为业务自动化协议。

### 获取代码后避免重复阅读

推荐源码片段返回：文件/符号身份、文档版本、内容摘要、准确范围、截断状态、编码/换行信息及证据标识。

下一次用证据标识和期望版本请求校验，未变化时返回“仍有效”，变化时只返回差异或必要新片段。写入前还要校验实际编辑目标；若工具读取的是 VS 未保存缓冲区，而代理要改磁盘文件，两者必须明确区分，不能因为路径相同就跳过复读。

这是减少重复读文件的前提。单纯让技能命令模型“不要再读一次”，会把新鲜度问题转嫁给模型。

## 四、建议实施顺序与验收

这是一份待确认的整改次序，不是已开始的新实施计划。

| 批次 | 范围 | 验收重点 |
| --- | --- | --- |
| A：先准确 | F1-F4、业务噪声规则配置化 | 旧预览拒绝、无虚假诊断基线、修改后无旧缓存、目标冲突不静默跳转。 |
| B：再提速 | F5-F8、批量快照、增量源码、技能路由收敛 | 降低桥接与模型往返；输出和内存有硬上限；取消有效。 |
| C：调试闭环 | 场景执行器、单测试调试、结构化构建/测试证据、性能采集适配 | 同一场景复现及复验可追踪，任务结束能清理且不影响其他任务。 |

实施 A 前先建立可重复测量基线，之后每批增加对应回归测试。不要等所有增强写完才验证。

建议回放工作集包含：单方法修改、跨文件重命名、已有错误工程中的新错误、失败单测定位、启动到异常停止、长时间并发查询、MCP 重连、VS 切换解决方案。

指标必须覆盖：

- 任务总耗时及中位数/第 95 百分位耗时，区分冷启动、热缓存和模型思考时间。
- MCP 调用数、桥接调用数、VS 主线程等待、Roslyn 计算和序列化耗时。
- 工具定义和响应的字节数，以及宿主能提供时的真实 token 消耗。
- 陈旧证据、错误目标、虚假新增诊断、错误修改和结果未知的次数。
- 每项结果附分析覆盖范围；速度提升不得以漏报、隐藏错误或取消安全门槛换取。

不推荐优先做的事：为了“更快”全面改 CLI、继续无差别增加几十个工具、用大模型重新解释每一段原始日志、只测缓存命中场景，或直接声称可以套用别家公布的节省比例。

## 五、官方资料

查阅日期均为 2026-09-15。通用网页检索接口本轮没有返回有效内容，改用 Microsoft Learn 官方检索/全文工具，以及官方页面和规范仓库直接获取。以下均实际读取过；没有使用未验证的搜索摘要冒充事实。

- **S1 / Roslyn API**：`Project.Version` 对应项目文件版本。用于核实 F1，而非推测 Roslyn 行为。
  `https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.project.version?view=roslyn-dotnet-4.13.0`
- **S2 / Visual Studio 调试智能体**：复现、观测、运行验证与修复闭环。页面区分 Visual Studio 与 VS 2022 版本范围，不应混用。
  `https://learn.microsoft.com/visualstudio/debugger/debug-with-copilot?view=visualstudio`
- **S3 / Test Explorer 调试与分析**：失败测试调试和性能分析；其中 Profile with Copilot 标明 Visual Studio 2026 18.4 起支持，当前该命令支持 .NET 测试。
  `https://learn.microsoft.com/visualstudio/test/debug-unit-tests-with-test-explorer?view=visualstudio`
- **S4 / Profiler Agent**：CPU、内存分配、基准与前后验证。本报告借鉴其工作流，不承诺调用其内部能力。
  `https://learn.microsoft.com/visualstudio/profiling/profile-with-copilot-agent?view=visualstudio`
- **S5 / .NET 诊断工具**：计数器、跟踪、转储与线程堆栈的成熟工具分类。
  `https://learn.microsoft.com/dotnet/core/diagnostics/tools-overview`
- **S6 / Anthropic 高级工具使用**：2025-11-24，按需工具发现、程序化调用及示例。文章实验数据不等于本工具收益。
  `https://www.anthropic.com/engineering/advanced-tool-use`
- **S7 / Anthropic MCP 代码执行**：2025-11-04，减少中间结果进入模型上下文，同时明确沙箱、资源限制和监测成本。
  `https://www.anthropic.com/engineering/code-execution-with-mcp`
- **S8 / MCP Tasks 规范**：固定研究对象为 2025-11-25 版本；文档说明实验性、能力协商、状态查询、取消和延迟结果。
  `https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2025-11-25/basic/utilities/tasks.mdx`
- **S9 / MCP Resources 规范**：资源读取和错误处理，用于区分正常证据正文与资源缺失。
  `https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/docs/specification/2025-11-25/server/resources.mdx`
