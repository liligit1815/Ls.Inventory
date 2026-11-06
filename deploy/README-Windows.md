# LS 库存 0.3.3 · Windows x64 IIS 部署包

本包用于 Windows 11 专业版／企业版及 Windows Server 2022/2025 64 位的 IIS 正式部署，包含已编译网页、API 和 .NET 运行时。新装和从本仓库上一版轻量版升级均使用同一份包。**不包含数据库数据、真实密码或开发环境配置。**

## 准备

- Windows 11 专业版／企业版或 Windows Server 2022/2025，启用 IIS，使用 64 位应用池。
- 启用 IIS，安装 .NET 10 Hosting Bundle（提供 IIS 的 ASP.NET Core Module）。即使本包包含运行时，IIS 托管模块仍须安装。
- 在目标设备安装 PostgreSQL，监听端口设为 **15432**；本包配置示例使用 `127.0.0.1:15432/ls_inventory`、用户 `postgres`、业务 schema `lite`。密码在目标机填写；数据库和网站位于同一台目标设备。
- 正确的主机名、可信 HTTPS 证书和网站目录权限。

微软参考：[IIS 托管](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)、[.NET 发布方式](https://learn.microsoft.com/en-us/dotnet/core/deploying/)。

## 包内文件

| 路径 | 用途 |
| --- | --- |
| `site/` | IIS 网站物理目录的全部程序文件，包括 `web.config` 和 `wwwroot` |
| `config/appsettings.Production.example.json` | 无密码配置示例，仅新安装时使用 |
| `tools/upgrade-0.3.1-to-0.3.2.sql` | 0.3.1 升级至本版的可选 pgAdmin 脚本；历史表结构沿用 0.3.2 |
| `tools/Prepare-Update.ps1` | 校验发布文件，准备新版本目录，并沿用旧站点生产配置和 IIS 配置 |
| `site-manifest.json` | 程序文件 SHA-256 清单，升级脚本会验证 |
| `release.json` | 版本、源码提交、平台和构建时间 |
| `Windows生产环境安装部署指南.md` | 完整安装、备份、权限、验收及排错步骤 |

下载 ZIP 旁的 `.sha256` 文件后，用 `Get-FileHash .\LS.Inventory-0.3.3-win-x64.zip -Algorithm SHA256` 对比校验值。解压到独立目录；不要直接解压覆盖运行中的站点。

## 新安装

1. 复制 `site` 的全部内容到 `C:\LSInventory\site-0.3.3`。
2. 复制 `config/appsettings.Production.example.json` 到该目录并改名为 `appsettings.Production.json`，填入目标机数据库连接、随机 JWT 密钥和网站主机名。配置文件不能放进 `wwwroot`。
3. 按完整指南创建专用数据库及 `lite` schema；已有数据库不要重建。
4. 网站启动前，在 PowerShell 中初始化（不是双击 EXE）：

   ```powershell
   Set-Location C:\LSInventory\site-0.3.3
   $env:ASPNETCORE_ENVIRONMENT = 'Production'
   $env:DOTNET_ENVIRONMENT = 'Production'
   $inventoryPassword = Read-Host '输入至少12位的初始管理员密码' -AsSecureString
   $env:BootstrapAdmin__Password = [System.Net.NetworkCredential]::new('', $inventoryPassword).Password
   try {
       .\Ls.Inventory.Api.exe --initialize-database
       if ($LASTEXITCODE -ne 0) { throw '初始化失败，请停止部署并检查配置' }
   } finally {
       Remove-Item Env:\BootstrapAdmin__Password -ErrorAction SilentlyContinue
       Remove-Variable inventoryPassword -ErrorAction SilentlyContinue
   }
   ```

5. IIS 新建专用应用池：无托管代码、集成管道、启用 32 位应用程序=False、加载用户配置文件=True。网站物理路径指向 `C:\LSInventory\site-0.3.3`，配置 HTTPS 绑定和证书。给 `IIS AppPool\实际应用池名称` 读取执行权限，保留程序附带的 `web.config`。
6. 启动网站，访问 `/health/live`、`/health/ready`，然后登录、修改初始密码并检查工作台。

生产机不需要 Node.js、Git、SDK 或开发端口 5173/5180；不使用源码里的“启动系统.cmd”。

## 从旧版轻量版升级（IIS）

从 0.3.1 或更早版本升级时，需要增量初始化数据库，以创建 `product_histories` 并导入旧日志中的完整商品快照。更新保留商品、库存、历史流水、盘库、用户、密码及预警配置。数据库连接、端口、JWT 密钥和 `Search Path` 必须沿用当前正常运行的配置。若现有表在 `public`，保留 `Search Path=public`。已完成 0.3.2 历史表升级的服务器，本次不增加数据库结构，可跳过第 3 步；重复执行该检查也不会重置数据或密码。

1. 记录旧站点物理路径、应用池、HTTPS 绑定。停止 IIS 网站及其专用应用池，停止所有写入；按完整指南备份数据库、原生产配置和旧目录。
2. 保留解压包原样，不先把生产配置放进包内 `site`。在管理员 PowerShell 中执行下列命令，路径替换为实际路径：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Downloads\LS.Inventory-0.3.3-win-x64\tools\Prepare-Update.ps1" -ExistingSite "C:\LSInventory\site" -NewSite "C:\LSInventory\site-0.3.3"
   ```

   脚本只准备新目录，不启动／停止网站、不切换 IIS、不连接数据库。新目录必须不存在，不能是旧目录或解压包的子目录。旧目录不会删除或修改。

   脚本原样沿用旧 `appsettings.Production.json`，继承旧站点及配置文件权限，并保留旧 `web.config` 中的自定义设置和环境变量，仅将启动程序调整为本包的 EXE。迁移目录后请核对其中的绝对路径、证书／日志路径。不复制旧网页资源，避免新旧文件混用。

3. 在新目录核对生产配置及权限，备份后以 **Production** 执行增量初始化。

   ```powershell
   Set-Location C:\LSInventory\site-0.3.3
   $env:ASPNETCORE_ENVIRONMENT = 'Production'
   $env:DOTNET_ENVIRONMENT = 'Production'
   .\Ls.Inventory.Api.exe --initialize-database
   if ($LASTEXITCODE -ne 0) { throw '升级检查失败，不要切换 IIS 或启动新版本' }
   ```

   已有用户不需要重新设置初始密码，也不会重置密码。运行连接权限不足时，按完整指南第八节临时提供 `ConnectionStrings__InventoryMigration`。命令行进程不自动读取 IIS `web.config` 环境变量；若连接或密钥仅配置在 IIS 中，需要在本次 PowerShell 进程临时提供相同配置，再执行命令。不能运行 `--reset-structure`。

   如果选择在 pgAdmin 完成历史表升级，可用包内 `tools/upgrade-0.3.1-to-0.3.2.sql` 代替上面的初始化命令；文件名表示该结构引入的版本，0.3.3 沿用此结构。整份执行前按脚本注释填写原业务 schema 和网站数据库账号，并确认事务提交成功。两种方式选择一种即可。

4. 在 IIS 管理器把站点“基本设置 → 物理路径”改成 `C:\LSInventory\site-0.3.3`，保留原应用池、域名、HTTPS 绑定和证书。确认应用池为 64 位，启动应用池和网站。
5. 检查 HTTPS、`/health/ready`、原账号登录、原商品库存和历史记录、每日出入库交叉表及商品搜索。检查商品档案的启用／停用筛选、表格列宽和显示列设置，以及快捷出入库保存成功后关闭面板；完成后才恢复业务录入。浏览器可按 Ctrl+F5 刷新新版网页。请保留旧目录和备份。

若旧配置仅在环境变量／IIS 中，没有 `appsettings.Production.json`，升级工具会停止；改按完整指南“更新与回退”手工准备新目录，沿用原配置，不拿示例替换真实配置。

若此前只是通过“启动系统.cmd”运行源码版，先完成 IIS 首次配置，并在目标机的生产配置中使用原项目数据库和原 JWT 密钥；不要新建空库代替原库，不要把 User Secrets 上传或放入 `wwwroot`。

## 失败与回退

准备目录失败时，旧站点和数据库未被脚本修改；修正原因后选择另一个不存在的新目录重试。数据库增量检查失败时保持停站，先排查，不强行启动。

回退前请确认旧程序与升级后的数据库兼容。旧程序运行期间的资料修改，应在重新启用新版本前核对。恢复备份会丢失备份之后的数据。

0.3.3 更新工作台：左右固定商品、库存和出入库列，固定表头，自定义展示天数，数量列自动加宽且零值留空，同名商品按单位数字排序。保留 0.3.2 的商品资料对照和变更历史；从 0.3.1 或更早版本升级时需先执行第 3 步。
