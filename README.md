# LS 库存 · 轻量版

面向个体户和单店的进销存工具：选商品、填数量，即时入库或出库；随时查看库存、处理预警、完成盘库。采用冰蓝色界面，无采购销售订单、审批流或多仓库配置。

## 功能

| 模块 | 内容 |
| --- | --- |
| 登录与安全 | JWT 身份验证、登录退出、首次改密、会话撤销、登录失败锁定 |
| 工作台 | 近日出入库整合快捷录入：顶部或商品旁直接入库／出库，右侧面板自动带入商品、预览操作后库存，成功后刷新并可定位今天记录；提供日历／表格／每日汇总卡片三种视图（默认含今天的近 7 天，可自定义 1～367 天）；表格上方为日期、左侧为商品，日历和卡片采用统一高度的日期摘要，点击日期在下方查看每页8条商品明细，按单位汇总可展开查看，完整规格和备注可点击商品名称查看；支持多商品筛选、当前库存、即时出入库及库存告急提醒 |
| 数据看板 | 日期及商品多选、指标卡、出入库趋势、库存状态环图、商品卡片与明细、历史流水 |
| 盘库 | 账面库存快照、填写实盘数量、差异确认、取消、历史查看 |
| 商品档案 | 名称、产品规格、原料规格、单位、编码、备注、启停用、Excel 商品及现有库存导入 |
| 库存预警 | 单商品或多商品批量设置预警值；低于阈值时工作台提醒 |
| 操作日志 | 操作人、时间、动作和来源地址，只读查看 |

当前仅维护轻量版，不提供版本切换。全量版功能范围仅记录为：角色权限、多仓库库位、分类及单位换算、供应商客户、复杂出入库单与审批、调拨、月度 Excel 历史流水导入、期间结账与冲销；这些功能不包含在当前实现中。

## 技术与目录

- 后端：.NET 10、ASP.NET Core、EF Core、Npgsql。
- 前端：Vue 3、TypeScript、Vite、Element Plus、ECharts、Pinia。
- 数据库：PostgreSQL，项目库建议命名为 `ls_inventory`，业务表位于 `lite` schema。
- 生产运行：IIS + .NET 10 Hosting Bundle + HTTPS，前端静态资源和 API 同站点。

```text
src/Ls.Inventory.Api/                 后端、数据库模型、Excel 读取
src/Ls.Inventory.Web/                 前端页面、公共样式与前端测试
tests/Ls.Inventory.IntegrationTests/  后端测试（数据库部分使用隔离 schema）
scripts/                             开发启停与 Windows 发布脚本
deploy/                              无密码的生产配置示例
docs/Windows生产环境安装部署指南.md  安装、初始化、备份、更新及排错
商品信息及现有库存模板.xlsx           当前导入模板
```

## 本机开发

准备 Git、`global.json` 指定的 .NET SDK 10.0.301（允许同特性带较新补丁）、Node.js 22.12 或更高的兼容版本及 PostgreSQL。首次安装前端依赖运行 `npm ci`。具体版本以项目锁文件为准。

```powershell
git clone git@github.com:liligit1815/Ls.Inventory.git
cd Ls.Inventory
dotnet restore Ls.Inventory.slnx
cd src/Ls.Inventory.Web
npm ci
cd ../..
```

在 PostgreSQL 中先创建项目数据库和 `lite` schema，再配置以下 .NET User Secrets。以下均为占位示例，真实密码只保存在本机，不提交 Git：

```powershell
dotnet user-secrets set 'ConnectionStrings:Inventory' 'Host=127.0.0.1;Port=5433;Database=ls_inventory;Username=postgres;Password=<本机数据库密码>;Search Path=lite' --project src/Ls.Inventory.Api
dotnet user-secrets set 'Jwt:SigningKey' '<至少32字节的随机密钥>' --project src/Ls.Inventory.Api
dotnet user-secrets set 'BootstrapAdmin:Password' '<至少12位的初始登录密码>' --project src/Ls.Inventory.Api
```

使用独立低权限运行账号时，另配置 `ConnectionStrings:InventoryMigration` 为同一项目库的结构管理连接。开发环境正常启动会创建缺失的表并执行增量升级，不清空已有业务数据。

双击 `启动系统.cmd` 后访问：

- 前端：`http://127.0.0.1:5173`
- API 就绪检查：`http://127.0.0.1:5180/health/ready`
- 默认登录名：`admin`；首次登录必须修改初始密码。
- `查看初始管理员密码.cmd` 只查看当前电脑开发配置中的初始密码，不能查询已修改后的密码。
- 使用 `停止系统.cmd` 停止本机开发服务。

CMD 的英文脚本入口是为避免中文批处理编码问题保留的，不应只因存在中文脚本就删除。

## Windows 11 x64 · IIS 部署与升级

从 [GitHub Releases](https://github.com/liligit1815/Ls.Inventory/releases) 下载 `LS.Inventory-0.2.0-win-x64.zip` 和对应 SHA-256 校验文件。仓库只维护源码、测试、配置示例和文档；编译后的部署包仅作为 Release 附件，不提交数据库、真实配置、日志或本机运行数据。

本版本包含近日出入库的日历／表格／每日汇总卡片，日期按先后排列，商品明细分页展示；快捷出入库使用大按钮，桌面商品列表在面板左侧展开，可一次查看更多商品。页面统一使用右上角“刷新数据”。

部署包的 `site/` 已包含前端、后端和 win-x64 独立运行时，适用于 Windows 11 专业版／企业版等具备完整 IIS 功能的版本。IIS 仍需安装 .NET 10 Hosting Bundle 以提供托管模块，并配置 PostgreSQL 和 HTTPS；不需要 Node.js、Git 或 SDK。

旧版轻量版升级：先停站并备份数据库，使用包内 `tools/Prepare-Update.ps1` 将新版本放入新的目录，沿用旧 `appsettings.Production.json`、目录权限和 IIS 自定义配置，保留原目录；完成数据库增量检查后切换 IIS 物理路径。此版本未改变业务表结构，不重置账号或库存。工具不自动切换站点或修改数据库；不支持直接迁移其他系统或旧全量版结构。

详细步骤见 [部署包说明（新装／旧版升级）](deploy/README-Windows.md)。

### 从源码构建部署包

请按 [Windows 生产环境安装部署指南](docs/Windows生产环境安装部署指南.md) 操作。目标电脑已安装 PostgreSQL 时不必重装，端口使用 5433，用户名和密码在目标机配置。

先停止开发服务（或使用独立源码副本），在构建电脑执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
```

脚本还原前端依赖、执行前端测试、构建前端，并以 `win-x64 --self-contained true` 发布后端，生成 ZIP、文件清单、版本信息和 SHA-256 校验文件。输出位于 `.artifacts/windows-时间-标识/`；包内含升级工具和部署文档，不包含真实配置。开发服务正在运行且已按锁文件安装依赖时，可加 `-UseInstalledDependencies` 避免重新安装依赖。

生产电脑只需要发布文件、IIS 和 .NET 10 Hosting Bundle，不需要 Node.js、Git 或开发用的 5173/5180 服务。生产配置必须使用 `Production` 和 HTTPS，不能用 `启动系统.cmd` 代替正式部署。

## 业务规则与使用注意

1. 同一商品由“商品名称＋产品规格＋原料规格＋单位”四项识别，仅去除首尾空格；任一项不同就是不同商品。备注不参与判重。
2. 数量均为整数，范围 0～2,147,483,647；出入库数量必须大于零，库存不能为负数，不做单位换算。
3. Excel 仅导入商品与现有结存，不读取旧版月度历史流水。支持 .xlsx，最大 10 MB；模板识别名称、两项规格、单位、现有库存、备注。名称、单位及库存必填；空规格需补全或明确确认“无规格”。空库存不等于零。
4. 已存在商品（包括停用商品）自动跳过，不覆盖库存、备注或状态；新商品的备注与库存一同导入。同一新商品重复行需用户保留一条，不累加库存。
5. 初始库存导入及盘库差异作为库存调整，不计入正常入库或出库。看板的净变化＝入库－出库＋库存调整；当前结存为此刻库存，不是查询期末余额。不同单位不能混合相加。
6. 库存严格低于预警值时提醒；等于预警值、未配置或停用商品不提醒。
7. 盘库确认前的实盘输入只保留在当前页面，离开可能丢失。期间商品或库存变化时拒绝覆盖，需取消后重新盘库；确认或取消成功返回记录列表。
8. 网络结果不确定时使用同一请求重试；不能自行重复录入，否则可能被当作新业务。
9. 修改密码支持 6～128 位，包括纯数字；生产使用仍建议更长的随机密码。初始管理员密码至少 12 位，新密码不能与当前密码相同。
10. 已过账流水和日志不提供编辑删除功能；不包含数据导出、金额、利润或财务功能。

## 测试

```powershell
cd src/Ls.Inventory.Web
npm test
npm run build
cd ../..
dotnet test Ls.Inventory.slnx -c Release --filter FullyQualifiedName~ApplicationSmokeTests
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-windows-update.ps1
```

完整后端测试需要设置 `LS_TEST_ADMIN_CONNECTION` 环境变量，指向允许创建 schema 的专用开发／测试数据库，然后运行 `dotnet test Ls.Inventory.slnx -c Release`。测试创建随机命名的 `ls_lite_test_*` schema，结束后删除该测试结构；**不要指向生产数据库**。正在运行的开发程序占用 Debug 文件时，使用 Release 构建验证。

## 配置与安全边界

- GitHub 仓库和发布包均不存放真实数据库密码、JWT 密钥、证书私钥、数据库备份或开发运行日志。
- JWT 存在 HttpOnly Cookie 中，写请求校验防伪令牌；生产使用 HTTPS 和独立随机密钥。退出、改密会撤销旧会话。
- 出入库、盘库、日志在事务中提交，使用锁和版本检查防止重复过账及并发覆盖。
- 数据库增量升级、历史导入回执兼容逻辑、看板 GET 兼容查询仍有用途，不作为无用代码删除。
- `--initialize-database` 用于首次建表、增量升级与初始账号检查，完成后退出；已有用户不会重置密码。执行前应备份。
- `--reset-structure` 是开发环境专用的破坏性命令，生产部署禁止使用。
- 本项目尚未在目标生产电脑完成验收；发布前须完成 HTTPS、备份恢复、权限和实际业务检查。

常见部署故障及日常备份说明见 [安装部署指南](docs/Windows生产环境安装部署指南.md)。
