# 一份源码，同时维护自用与公开版本

## 一次性安排

- 把源码 Git clone 放在同步文件夹外。每台开发电脑各有自己的 clone，通过 Git 同步源码，不同步 `.git` 文件夹。
- 把个人安装放在同步文件夹内。整个安装结构由启动器、`channel.json`、`versions` 和私人 `data` 组成。
- 每台电脑从自己的同步路径启动同一个安装，启动器会找到旁边的资料目录。
- 设置 `git config core.hooksPath .githooks`。提交者身份由本地 Git 设置管理；公开前检查作者邮箱。

## 每次修 BUG

在源码目录修改、复现验证，并更新项目版本号与 CHANGELOG。暂存明确的源码文件后：

```powershell
git diff --cached
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-PublicTree.ps1 -Staged
git commit -m "Describe the fix"
```

从干净提交构建并部署。将下列 `$privateInstall` 设为自己的个人安装文件夹；不要把该值写入仓库文件。

```powershell
$privateInstall = Join-Path $syncRoot 'Apps/PaperFlow'   # $syncRoot 指向你自己的云盘同步文件夹
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Release.ps1 -PrivateTarget $privateInstall
```

脚本会测试、构建、记录提交编号与程序哈希，再生成 `artifacts/release-<版本>/` 下的源码 ZIP、Windows ZIP、单独的程序 exe 与 `update.json`。它们都不读取个人 `data`。部署只加入新版本和更新启动入口/清单。相同版本若已有不同内容则拒绝覆盖，必须升版本。

若只需公开包，省略 `-PrivateTarget`。公开发行应上传生成的文件，不能从正在使用的同步文件夹重新打包。每个正式 Release 必须同时挂上 `PaperFlow-<版本>-win-x64.exe` 和 `update.json`：程序只在 `https://github.com/pwya/paperflow/releases/latest/download/update.json` 读更新清单，清单再指向同一次发布的 exe。少传 `update.json` 时程序内的更新提示会安静地什么都不做（离线机器本来也不该联网），只是这一版不会被自动发现。

## 程序内自动更新

设置里“更新提示”三档（每次都提示 / 每天一次 / 不提示，默认每天一次）。检查是一次普通 GET，只读清单；只有用户点了“下载并安装”才会下载程序 exe，落本机缓存并校验长度与 SHA-256，校验不过就删掉并报错。安装方式与发布脚本一致：写 `versions/<版本>/PaperFlow.exe` 和新的 `channel.json`，旧清单留一份 `channel.json.previous`。装好后提示“重启并生效”，重启时新进程先等旧进程退出（`--wait-for`），避免单实例锁把挂件弄丢。

开发与自动化验证用 `--update-url <清单地址>` 把清单指到本地服务器，整条下载安装链可以在不联网的情况下端到端跑一遍。

所有自用电脑收到新版后，从托盘菜单完全退出，再打开原启动器。文件尚未到齐时继续使用已缓存的旧版；初次使用则等待完整同步。保持旧版本目录可供回退。

## GitHub

源码仓库可在审查后添加自己的远程地址并推送。首次公开也可解压脚本生成的源码 ZIP，在全新仓库初始化，从而只包含审查过的源码快照。以后应让这个仓库成为唯一公开上游，所有开发电脑从它 clone/pull；不要长期复制两份源码分别修改。

仓库带 Windows CI，每次 push/PR 运行审计、测试、构建并上传干净发布 ZIP 作为 Actions artifact。它不会自动创建公开 Release，也没有读取个人同步目录的权限。正式 Release 可在审查制品后由维护者发布。

GitHub push 和个人安装部署是两个明确动作，由同一提交串联；程序自身不持有 GitHub 令牌，也不自动推送。某一动作暂时失败时，凭提交编号和版本号重试即可，不需要复制论文数据。

CI 参考 [GitHub 的 .NET 构建文档](https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net)。
