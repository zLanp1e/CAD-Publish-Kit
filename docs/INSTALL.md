# V0.1 Preview: installation / 安装

## 中文

1. 安装并确认 SolidWorks 能正常打开模型。当前仅在 Windows 11 x64、
   SolidWorks 2026 SP0.0、.NET Framework 4.8 上验证。
2. 下载 `CAD-Publish-Kit-0.1.0-preview.1-win-x64.zip`，完整解压到可写目录，
   如 `D:\CADPublishKit`。不要在压缩包中直接运行，也不要放到 Program Files。
3. 双击根目录 `Start CAD Publish Kit.cmd`。首次启动自动从本机 SolidWorks
   安装目录复制两个接口 DLL 到本地 bin，不需要编译，不需登录或上传模型。
4. 拖入 STEP，查看清单，选择输出目录并导出；批量检查入口位于顶部文件夹标签。
   批量默认会实际导出并重新打开验证，较慢；取消验证后只是结构判断。

保留一个 SolidWorks 实例，与工具使用同一 Windows 账户和权限。运行期间不要编辑
CAD 文档。模板提示出现时选择有效的空白 `.prtdot`。输出有 BOM.csv 或 parts 时请选择
新目录；工具不覆盖已有交付包。内部副本和日志在工具的 output/desktop-runs 中。

### 故障排查

- 缺失接口库或 COM 注册失败：先运行一次已安装的 SolidWorks。若仍失败，
  在解压目录运行下面命令（修改为自己的安装目录），然后再双击启动器。
- 模型导入失败：先确认它能在 SolidWorks 中打开，查看界面详情和内部记录。
- 单实体显示不可拆分：表示当前导入结果只有一个实体，不等于文件损坏。
- 断开连接：完成当前错误处理后重启工具和 SolidWorks，再分析。
- 此版本没有安装器和代码签名。若组织策略禁止执行，请联系本机管理员，勿关闭系统防护。
- 升级时解压到新目录。不要覆盖正在运行的旧程序；需要的历史记录可保留在旧目录。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-runtime.ps1 -SolidWorksDirectory 'D:\SOLIDWORKS Corp\SOLIDWORKS'
```

## English

Install and license SOLIDWORKS first. Verified environment: Windows 11 x64,
SOLIDWORKS 2026 SP0.0 and .NET Framework 4.8. Other versions are unverified.

Extract the entire Windows ZIP into a writable local directory. Double-click
`Start CAD Publish Kit.cmd`. The first launch copies the two interface libraries
from your own SOLIDWORKS installation; the ZIP does not redistribute them.
No build, account or upload is required. The interface is currently Chinese.

Drop a STEP file, review/select rows, choose a destination, then export. Use the
folder-icon tab for recursive inspection through ten descendant levels. Real
export/reopen checks are enabled by default; disabling them yields structural
candidates only. Batch inspection does not publish delivery packages.

Keep one SOLIDWORKS instance under the same account/elevation. Do not edit CAD
documents while processing. Choose an empty part template when prompted. Existing
BOM.csv/parts are refused. Logs and working copies remain in output/desktop-runs.
The application is unsigned and has no installer. Follow your organization's
execution policy. Install updates in a new folder. For registry discovery
failures, run the command above with your installation directory, then relaunch.
