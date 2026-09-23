# Windows 生产环境安装部署指南

适用：在另一台 Windows 电脑上正式运行 LS 库存轻量版。PostgreSQL 已安装，端口 **5433**，管理员用户名 **postgres**，密码沿用你已配置的数据库密码。本指南不记录真实密码，也不连接或修改现有开发库。

默认假设 PostgreSQL 和网站安装在新电脑的同一台机器，因此数据库地址使用 **127.0.0.1**。如果数据库实际位于另一台机器，只修改数据库 Host 为那台服务器的内网地址；不要把开发服务器地址直接复制到生产配置。生产项目库统一建议使用 **ls_inventory**，业务 schema 为 **lite**，不是 PostgreSQL 自带的 postgres 验证库。

## 一、部署方式与准备

采用一个 IIS 网站同时提供前端和 API：浏览器通过 HTTPS 访问 IIS，IIS 托管 .NET API，API 连接 PostgreSQL。前端已编译，不再运行 Vite。生产不使用 5173/5180 开发端口。

| 位置 | 需要准备 |
| --- | --- |
| 构建电脑（可以是当前开发电脑） | 源码、Git、global.json 对应的 .NET SDK、兼容 Node.js（22.12+）、网络依赖下载 |
| 生产电脑 | 受支持的 64 位 Windows（例如 Windows 11 专业版或 Windows Server 2022/2025）、IIS、.NET 10 Hosting Bundle、已安装的 PostgreSQL |
| 网站访问 | 固定局域网地址、访问名称和受信任 HTTPS 证书；例子使用 inventory.example.com，必须替换为实际名称 |

Windows 家庭版不提供本指南要求的完整 IIS 部署方式。安装需本机管理员权限。新电脑不需要安装源码编译工具或 Node.js。不要把开发电脑的数据库测试数据自动迁入生产。

## 二、下载或构建 Windows x64 部署包

优先从 [GitHub Release](https://github.com/liligit1815/Ls.Inventory/releases) 下载 `LS.Inventory-0.2.0-win-x64.zip`，核对旁边的 SHA-256 校验文件后解压。包内 `README-Windows.md` 提供新版 IIS 安装与保留数据升级步骤；已有站点使用 `tools/Prepare-Update.ps1` 准备独立版本目录，不要直接覆盖旧站点。该包包含 win-x64 运行时，仍需 IIS Hosting Bundle 托管模块。

以下是从源码自行构建的方法：

1. 获取项目。若在正在开发的电脑打包，先用“停止系统.cmd”停止开发服务，或使用独立源码副本；否则 npm ci 可能因文件占用报 EPERM：

   ```powershell
   git clone git@github.com:liligit1815/Ls.Inventory.git
   cd Ls.Inventory
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
   ```

   没有配置 SSH 的电脑可改用 `https://github.com/liligit1815/Ls.Inventory.git` 克隆。

2. 等待显示 `Package`。ZIP 和校验文件位于 `.artifacts/windows-时间-标识/`，实际网站文件在包内 `site/`。每次生成新目录，不覆盖生产文件。前端测试失败或构建失败时应停止部署。
3. 将包内 `site/` 中的**全部内容**复制至新电脑 `C:\LSInventory\site`。不要只复制 exe，也不要复制 node_modules、源码或旧配置。
4. 发布目录应包含 `web.config`、`Ls.Inventory.Api.exe`、`Ls.Inventory.Api.dll`、内置运行时及相关依赖、`wwwroot/index.html`、`wwwroot/assets/` 和 `Templates/商品信息及现有库存模板.xlsx`。

发布包不包含生产配置或数据库数据。GitHub 源码不是可直接双击运行的安装程序。

## 三、安装 IIS 和 .NET 托管组件

1. Windows“启用或关闭 Windows 功能”中启用 Internet Information Services，至少包含 Web 管理工具中的 IIS 管理控制台，以及万维网服务中的常见 HTTP 功能（静态内容、默认文档、HTTP 错误）。Windows Server 使用“添加角色和功能 → Web 服务器（IIS）”。
2. 从微软 [.NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) 安装 **ASP.NET Core Runtime 下的 Windows Hosting Bundle**，不是只有桌面运行时的安装包。
3. 先安装 IIS 再安装 Hosting Bundle；若顺序相反，修复安装 Hosting Bundle，必要时重启电脑。IIS 创建的应用池使用“无托管代码”，.NET 应用由 ASP.NET Core Module 托管。[微软 IIS 部署说明](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
4. 用 PowerShell 执行 `dotnet --list-runtimes`，确认包含 `Microsoft.NETCore.App 10.0.x` 和 `Microsoft.AspNetCore.App 10.0.x`。

## 四、准备生产数据库

使用 PostgreSQL SQL Shell（psql）或 pgAdmin 连接新电脑：服务器 127.0.0.1，端口 5433，维护数据库 postgres，用户名 postgres，输入现有密码。以下数据库创建命令只在**确认同名项目库尚不存在**时执行；如果已经有数据，先备份，不删除重建。

在 psql 中运行：

```sql
CREATE DATABASE ls_inventory WITH ENCODING 'UTF8' TEMPLATE template0;
\connect ls_inventory
CREATE SCHEMA IF NOT EXISTS lite;
```

`\connect` 是 psql 命令，不是 SQL；使用 pgAdmin 时创建数据库后，在 ls_inventory 中重新打开查询工具，再执行 CREATE SCHEMA。不要在一个事务块中执行 CREATE DATABASE。

同机部署不需向局域网开放数据库端口。数据库在另一台服务器时，只允许网站服务器的固定 IP 访问 5433，并在 pg_hba.conf 配置对应数据库、用户和精确来源范围，使用密码认证，不能使用 trust 或对整个公网放行。[PostgreSQL 客户端认证说明](https://www.postgresql.org/docs/current/auth-pg-hba-conf.html)

## 五、填写生产配置

1. 将部署包的 `config/appsettings.Production.example.json`（或源码的 `deploy/appsettings.Production.example.json`） 复制到 `C:\LSInventory\site\appsettings.Production.json`。发布目录以外保存一份受保护的配置备份。
2. 修改以下值：

   | 配置 | 填写方法 |
   | --- | --- |
   | AllowedHosts | 实际访问域名／主机名，不写 https:// 或端口；多个名称用分号分隔 |
   | ConnectionStrings:Inventory | 本机 Host=127.0.0.1，Port=5433，Database=ls_inventory，Username=postgres，填写当前数据库密码，Search Path=lite |
   | Jwt:SigningKey | 新生成的随机密钥，不使用开发密钥，不保留 REPLACE 占位内容 |
   | DatabaseInitialization:Initialize | 正常运行保持 false；首次初始化另用命令执行 |

3. PowerShell 生成密钥：

   ```powershell
   $inventoryKeyBytes = New-Object byte[] 48
   $inventoryRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
   $inventoryRng.GetBytes($inventoryKeyBytes)
   $inventoryRng.Dispose()
   [Convert]::ToBase64String($inventoryKeyBytes)
   ```

   将结果填写到 Jwt:SigningKey。不要发送给他人，不上传 GitHub。
4. JSON 的反斜杠须写成 `\\`，双引号须转义；数据库密码中的分号等连接字符串特殊字符还需遵循 Npgsql 连接字符串的引号规则。不要把 Markdown 的转义字符误当作密码的一部分。
5. 示例按你的要求使用 postgres，能够部署，但它是高权限管理员账号。正式长期运行建议按第八节更换低权限网站账号；不会更改 postgres 原密码。

生产不会读取开发电脑的 User Secrets。仅复制源码或发布文件，无法自动获得原电脑的连接密码。禁止把真实生产配置复制到 wwwroot，它是公开网页目录。

## 六、首次初始化（网站暂不启动）

在生产电脑 PowerShell 中执行，输入一个至少 12 位的**系统初始登录密码**，它与 PostgreSQL 密码是两回事：

```powershell
Set-Location C:\LSInventory\site
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DOTNET_ENVIRONMENT = 'Production'
$inventoryInitialPassword = Read-Host '输入至少12位的系统初始登录密码' -AsSecureString
$env:BootstrapAdmin__Password = [System.Net.NetworkCredential]::new('', $inventoryInitialPassword).Password
try {
    .\Ls.Inventory.Api.exe --initialize-database
    if ($LASTEXITCODE -ne 0) { throw '初始化失败，请检查配置与数据库，不要继续启动网站' }
} finally {
    Remove-Item Env:\BootstrapAdmin__Password -ErrorAction SilentlyContinue
    Remove-Variable inventoryInitialPassword -ErrorAction SilentlyContinue
}
```

成功显示“数据库结构与初始账号已检查完成”。此命令完成后退出，不占用网站端口。首次创建表及 admin 账号；已有表执行增量升级，已有账号不会重设密码。必须使用专用项目数据库；如果同名库已有其它系统表，先核实，不要强行删除或混用。

禁止使用 `--reset-structure`、DROP DATABASE 或删除 lite schema 作为安装排错步骤。升级发现小数／越界数量会停止，应先核实，不自动取整。

## 七、创建并启动 IIS 网站

1. IIS 管理器 → 应用程序池 → 添加 `LSInventory`：.NET CLR 版本“无托管代码”，托管管道“集成”；高级设置“启用 32 位应用程序=False”，“加载用户配置文件=True”。
2. 网站 → 添加网站：名称 `LSInventory`，应用池选上述专用池，物理路径 `C:\LSInventory\site`，HTTPS 端口 443，主机名使用第五节填写的实际名称，选择匹配的服务器证书。已有 443 网站时使用不同主机名和 SNI，不能删除已有网站来腾端口。
3. 匿名身份验证开启，Windows 身份验证关闭（系统有自己的登录页面）。IIS 管理网站进程，不再启动开发 CMD。
4. 为应用池身份授予站点目录读取执行权限：

   ```powershell
   icacls 'C:\LSInventory\site' /grant 'IIS AppPool\LSInventory:(OI)(CI)(RX)'
   ```

   配置文件继承此读取权限。通过目录“安全 → 高级”检查并限制：仅管理员、SYSTEM 和该应用池有需要的权限，不允许普通用户读取生产密码，不给 Everyone 完全控制。网站通常不需要写发布目录。加载用户配置文件有助于框架密钥持久化。
5. 发布生成的 web.config 默认托管 .NET 进程，不能随意删除。生产默认环境为 Production；检查没有机器级 `ASPNETCORE_ENVIRONMENT=Development` 或 `DOTNET_ENVIRONMENT=Development` 覆盖。需要显式固定时，在 web.config 的 aspNetCore 节内配置：

   ```xml
   <environmentVariables>
     <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     <environmentVariable name="DOTNET_ENVIRONMENT" value="Production" />
   </environmentVariables>
   ```

   保留该节点其它属性及生成的处理程序配置，修改前备份。不要重复定义同名环境变量。
6. 配置 DNS 或每台客户端的 hosts，使网站名称解析到新电脑的固定 IP。证书名称须匹配；内网使用企业 CA 签发的证书，并在客户端信任该 CA。生产不以“忽略证书警告”作为解决方案。
7. Windows 防火墙只允许需要的内网访问 HTTPS 443。例如管理员 PowerShell：

   ```powershell
   New-NetFirewallRule -DisplayName 'LS Inventory HTTPS' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow -Profile Domain,Private -RemoteAddress LocalSubnet
   ```

   只在需要其它电脑访问时添加；由管理员按实际网络范围调整。不要开放 5173/5180；同机数据库也不开放 5433。
8. 启动网站，在浏览器访问 `https://实际网站名称/`。登录 admin 并强制修改初始密码。密码支持 6～128 位和纯数字，但建议生产使用较长随机密码。仅 HTTPS 可可靠使用生产的 Secure 登录和防伪 Cookie。

本指南按**站点根路径 /** 部署，不配置 `/inventory` 子目录，也不额外叠加反向代理。直接刷新 `/stocktakes` 等页面应正常；缺失 API 仍返回 404，不返回网页。

## 八、推荐：改用低权限数据库运行账号

按前面步骤初始化完成后，由 postgres 在项目库中创建独立登录角色。下面在 psql 中执行，密码通过交互设置，不写进脚本：

```sql
CREATE ROLE ls_inventory_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
\password ls_inventory_app
GRANT CONNECT ON DATABASE ls_inventory TO ls_inventory_app;
GRANT USAGE ON SCHEMA lite TO ls_inventory_app;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA lite TO ls_inventory_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA lite TO ls_inventory_app;
REVOKE UPDATE ON lite.movements, lite.audit_logs FROM ls_inventory_app;
```

角色已存在时不要重复创建或擅自改其密码。将生产配置 Inventory 改为该账号及其新密码，回收应用池后复验。网站不保存 postgres 管理连接。初次部署不要求执行此可选步骤，但高权限账号风险应知悉。

升级结构时，在管理员 PowerShell 中临时提供同一项目库的 postgres 连接，完成后清除。下面的隐藏输入要求填写**完整连接字符串**（不是单独密码），格式与第五节相同但 Username=postgres；执行前核对 Host、Port、Database 均与网站运行连接一致。

```powershell
Set-Location C:\LSInventory\site
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DOTNET_ENVIRONMENT = 'Production'
$inventoryAdminConnection = Read-Host '输入同一项目库的完整结构管理连接字符串' -AsSecureString
$env:ConnectionStrings__InventoryMigration = [System.Net.NetworkCredential]::new('', $inventoryAdminConnection).Password
try {
    .\Ls.Inventory.Api.exe --initialize-database
    if ($LASTEXITCODE -ne 0) { throw '升级失败，不要启动新版本' }
} finally {
    Remove-Item Env:\ConnectionStrings__InventoryMigration -ErrorAction SilentlyContinue
    Remove-Variable inventoryAdminConnection -ErrorAction SilentlyContinue
}
```

代码会给不同于结构管理账号的运行账号补充现有表和序列权限。这是已有账号的结构升级流程；首次创建空库账号仍使用第六节。

## 九、上线验收

- HTTPS 证书可信；访问 `/health/live` 和 `/health/ready` 返回 200。ready 只证明数据库能连通，不替代下面的业务检查。
- 未登录访问商品接口返回 401；登录后六个业务入口可打开，刷新盘库和看板不会 404。
- 初始密码必须修改；退出后原会话失效。
- 用明确命名的验收商品测试入库 10、出库 3、结存 7；重复提交不重复记账，库存不足不能出库。
- 测试导入备注及现有库存、已有商品跳过、整数校验。
- 测试预警阈值、批量设置、看板日期与商品多选、盘库取消和确认返回列表。
- 检查操作日志、停用商品、重启网站后数据仍保留。
- 正式录入前完成一次数据库备份和到独立库的恢复演练。

测试商品会留下真实流水，生产不提供删除已过账记录；优先在独立验收库完成业务验收，正式库最后只做少量必要检查。不能把开发集成测试直接运行在生产库上。

## 十、备份、迁移、更新和回退

### 数据库备份

使用 PostgreSQL 安装目录中与服务器版本兼容的 pg_dump，备份目录限制为管理员可读。示例假设工具已经加入 PATH：

```powershell
New-Item -ItemType Directory -Path C:\LSInventory\backup -Force
$inventoryBackup = 'C:\LSInventory\backup\ls_inventory-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dump'
pg_dump -h 127.0.0.1 -p 5433 -U postgres -W -d ls_inventory -Fc -f $inventoryBackup
if ($LASTEXITCODE -ne 0) { throw '备份失败' }
```

`-W` 交互输入密码，`-Fc` 生成适合 pg_restore 的归档。建议每日备份及升级前备份，保留多天版本，并另存一份到受控的其它磁盘／设备。无人值守任务使用受权限保护的 pgpass 文件或凭据管理，不把密码写在命令行；不要把备份上传到公开仓库。备份网站配置和证书私钥时也应独立加密保管。[pg_dump 官方说明](https://www.postgresql.org/docs/current/app-pgdump.html)

### 恢复演练／迁移

先创建一个全新的专用恢复数据库，例如 ls_inventory_restorecheck，不覆盖当前库：

```powershell
createdb -h 127.0.0.1 -p 5433 -U postgres -W ls_inventory_restorecheck
pg_restore -h 127.0.0.1 -p 5433 -U postgres -W --exit-on-error --no-owner --no-privileges -d ls_inventory_restorecheck C:\LSInventory\backup\选定备份.dump
```

每条命令检查退出码，恢复后检查商品、数量、流水、盘库和账号；为低权限账号重新授权。切换真实生产库前停止网站、确认恢复目标和数据时间点，并备份当前状态。本指南不提供覆盖式删除命令。备份恢复也会恢复当时的账号密码和会话，必要时改密撤销旧会话；不要把开发账号密码和测试数据直接用于生产。[pg_restore 官方说明](https://www.postgresql.org/docs/current/app-pgrestore.html)

### 更新与回退

1. 记录当前版本，停止写入并备份数据库、生产配置及当前发布目录。
2. 将新发布包复制到新的版本目录，保留原目录；把受保护的生产配置复制到新目录，核对连接、密钥和权限。
3. 若有结构升级，在停站期间从新目录执行 `--initialize-database`，不能运行重建命令。使用低权限账号时按第八节临时提供结构管理连接。
4. 初始化成功后修改 IIS 物理路径到新目录并启动，完成健康与业务验收。不要把 `.artifacts` 中的示例配置覆盖真实配置。
5. 失败时保持停止写入，先判断数据库结构是否兼容旧版本。没有结构变化可切回旧目录；有不兼容变化需经确认恢复升级前数据库，恢复会丢失备份之后的数据，不能自动回退覆盖。

## 十一、常见问题

| 现象 | 排查方向 |
| --- | --- |
| 浏览器拒绝连接 | IIS 网站／应用池是否启动、443 绑定、DNS、内网防火墙；生产不是 5173 |
| 500.19 | web.config XML、IIS 模块及权限，不能把整个配置删除 |
| 500.30／500.31 | Hosting Bundle／10.0 运行时、配置占位符、数据库连接，查看事件查看器 Application 日志 |
| 登录反复失效或防伪错误 | HTTPS／证书、主机名是否一致、Production 环境，刷新页面取得新令牌 |
| 连接数据库失败 | Host 是否误指开发电脑、5433、密码转义、项目库是否创建、数据库服务状态和 pg_hba.conf |
| relation does not exist／权限不足 | 是否完成初始化、Search Path=lite、是否误用 postgres 验证库、是否授权 |
| 首次密码不生效 | 只有空用户表才建立初始账号；已有密码不会被 BootstrapAdmin 配置重置 |
| 刷新子页面 404 | 是否发布新后端及完整 wwwroot，是否错误部署为子目录 |
| Excel 上传 413 | 应用允许最大 10 MB，检查文件大小和 IIS 请求大小配置，不能无限扩大 |

临时启用 web.config 的 stdoutLogEnabled 排查启动问题时，为单独日志目录授予应用池写权限；排查结束立即关闭，日志可能含敏感错误信息，不上传公开仓库。目标生产电脑尚未由本次工作远程安装或验收，本指南和本机发布验证不能代替现场上线检查。
