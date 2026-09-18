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

脚本还会在本地打上 `v<版本>` 标签（已存在且指向别的提交就报错），这样 GitHub 与 Gitee 用的是同一个标签对象。

若只需公开包，省略 `-PrivateTarget`。公开发行应上传生成的文件，不能从正在使用的同步文件夹重新打包。每个正式 Release 挂四样：`PaperFlow-<版本>-win-x64.zip`、`PaperFlow-<版本>-source.zip`、`update.json`、`build-info.json`。**不再单独发布可执行文件**：程序内的更新就是下载这个压缩包。程序读两份清单——先 Gitee 镜像（`https://gitee.com/pan-wang-yuang/paperflow/releases/download/latest/update.json`），再 GitHub（`https://github.com/pwya/paperflow/releases/latest/download/update.json`）——两份都读、按下面的规矩取一份。少传 `update.json` 时这一边不会被发现。

清单里的 `sha256`/`length` 描述压缩包本身，`exeSha256`/`exeLength` 描述压缩包里那个 `versions/<版本>/PaperFlow.exe`；发布脚本会真的把那个文件从压缩包里抠出来算一遍哈希，对不上就拒绝出包。压缩包内部的路径必须正好是 `versions/<版本>/PaperFlow.exe`（`Compress-Archive` 写的是反斜杠，比较时要归一化——应用侧就是这么做的）。

清单里还有一个 `notes`：**程序里"这次改了什么"显示的就是它**。这段文字只有一份来源——`CHANGELOG.md` 里这一版的小节，发布脚本自动抽出来写进 `update.json`，同时生成 `release-notes.md` 放在 `artifacts/release-<版本>/` 里供 Release 正文使用。所以程序里看到的和 Release 页面上看到的永远是同一段字。这一版在 CHANGELOG 里没有小节、或者小节超过 4000 字，脚本会直接拒绝出包（更新说明面板放不下）。英文说明可以另写一份放进 `notesEn`，目前留空，英文界面就显示中文那份。

## 两份清单怎么取舍

- **镜像不许抢先**：Gitee 报的版本比 GitHub 高时忽略它（镜像只允许落后）。
- **同版本必须完全一致**：SHA-256 与长度都对得上才安装，对不上就这次不更新（防的是有人只改了一边）。
- **只有 GitHub 连不上时**（国内常见），才单凭镜像更新；这一条是刻意的取舍，写在 `SECURITY.md` 与交接文档里。

## 公众号配图（开发用）

```powershell
# 用指定图片当背景，渲染一张 2880×1920 的海报（界面部分仍是代码生成的虚构论文）
PaperFlow.exe --article-poster <输出目录> --background <图片路径> --name <文件名> [--backdrop]
             [--scrim <0-95>] [--height <像素，默认 760>] [--theme <主题名，默认 极简 · 白>]
             [--title <文案>] [--description <文案>] [--detail <文案>]
```

不加 `--backdrop` 时，那张图作为**小组件自己的背景图**；加了 `--backdrop`，那张图铺满**整张海报**（会自动盖一层浅色遮罩保证文字可读）。同时还会输出一张 2 倍像素的 `-界面原图.png`。

配图当背景时：`--scrim` 是图片遮罩浓度（默认 35，数字越大字越清楚、图片越淡），`--height` 是小组件高度（默认 760，接近日常挂件的比例；调大到 1000 上下能更完整地露出方形背景图），`--theme` 决定取色（默认 `极简 · 白`，暖色可用 `柔光 · 陶土`，深色可用 `极简 · 夜`）。三次导出都不会读取任何真实资料。

## 镜像到 Gitee

发布脚本可以直接把这一版镜像过去：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Release.ps1 `
  -PrivateTarget <你的自用安装目录> `
  -MirrorToGitee -GiteeOwner pan-wang-yuang -NotesFile <发布说明.md>
```

它调用 `scripts/Mirror-Gitee.ps1`：缺仓库就建、改成公开、推分支与标签、建版本发行版（五个附件）、删除并重建固定的 `latest` 发行版（只放 `update.json`，指向 Gitee 自己的下载地址），最后自检镜像出的清单与本地一致、下载地址可访问。令牌从 `GITEE_TOKEN` 环境变量或 `%USERPROFILE%\.gitee-token.txt` 读，不写进 `.git/config`。跑之前先设好 `HTTPS_PROXY`（脚本内部要 `git fetch --tags` 去 GitHub 取标签）。

## 程序内自动更新

设置里“更新提示”三档（每次都提示 / 每天一次 / 不提示，默认每天一次）。检查是一次普通 GET，只读清单；只有用户点了“下载并安装”才会下载程序 exe，落本机缓存并校验长度与 SHA-256，校验不过就删掉并报错。安装方式与发布脚本一致：写 `versions/<版本>/PaperFlow.exe` 和新的 `channel.json`，旧清单留一份 `channel.json.previous`。装好后提示“重启并生效”，重启时新进程先等旧进程退出（`--wait-for`），避免单实例锁把挂件弄丢。

开发与自动化验证用 `--update-url <清单地址>` 把清单指到本地服务器，整条下载安装链可以在不联网的情况下端到端跑一遍。

所有自用电脑收到新版后，从托盘菜单完全退出，再打开原启动器。文件尚未到齐时继续使用已缓存的旧版；初次使用则等待完整同步。保持旧版本目录可供回退。

## GitHub

源码仓库可在审查后添加自己的远程地址并推送。首次公开也可解压脚本生成的源码 ZIP，在全新仓库初始化，从而只包含审查过的源码快照。以后应让这个仓库成为唯一公开上游，所有开发电脑从它 clone/pull；不要长期复制两份源码分别修改。

仓库带 Windows CI，每次 push/PR 运行审计、测试、构建并上传干净发布 ZIP 作为 Actions artifact。它不会自动创建公开 Release，也没有读取个人同步目录的权限。正式 Release 可在审查制品后由维护者发布。

GitHub push 和个人安装部署是两个明确动作，由同一提交串联；程序自身不持有 GitHub 令牌，也不自动推送。某一动作暂时失败时，凭提交编号和版本号重试即可，不需要复制论文数据。

CI 参考 [GitHub 的 .NET 构建文档](https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net)。
