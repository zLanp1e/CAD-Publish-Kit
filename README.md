# CAD Publish Kit

SolidWorks 装配体交付工具，当前仅实现阶段一的命令行读取原型。未实现 STEP 导出、桌面 UI、三维查看器或爆炸图。

## 当前验证状态

- 本机 Windows 11 x64：编译成功，9 项核心测试通过。
- 已通过运行对象表连接到真实 SolidWorks；实际扫描因活动文档不是装配体而拒绝执行。
- **真实装配体遍历与数量验收尚未通过，不应视为阶段一完成。**
- 候选支持环境：Windows 11 x64、SolidWorks 2026、.NET Framework 4.8 或更高版本。其他 Windows / SolidWorks 版本未验证；尚无正式 CAD 兼容性承诺。

## 构建和运行

在项目目录使用 Windows PowerShell：

```powershell
.\scripts\build.ps1
.\bin\CoreTests.exe
.\bin\CadPublishKit.exe .\output
```

构建脚本使用 Windows 自带的 .NET Framework C# 编译器，不依赖未安装的 .NET SDK。从本机 COM 注册寻找 SolidWorks 接口库，也可指定安装目录：

```powershell
.\scripts\build.ps1 -SolidWorksDirectory 'D:\SOLIDWORKS Corp\SOLIDWORKS'
```

运行前手动启动 SolidWorks，打开真实 `.SLDASM` 并激活所需配置。程序与 SolidWorks 应使用同一 Windows 用户及相同权限级别。多实例选择尚未支持，请只保留一个 SolidWorks 实例。扫描期间请勿切换文档、配置或编辑模型。

每次成功扫描建立新的 `output/scan_时间_GUID/` 目录，包含：

- `manifest.json`：原始路径、当前配置、组件父子关系、唯一零件映射、警告。
- `parts.csv`：按规范化 Windows 绝对路径和引用配置统计的唯一零件及观测次数。
- `instances.csv`：每个组件实例、状态与异常，包含子装配体及无法识别的组件。

CSV 使用 UTF-8 BOM，双引号转义，并保护可能被 Excel 当作公式的文本；JSON 保留原始文本。输出采用 CreateNew，禁止覆盖。报告可能含私人文件路径，请勿直接提交公开仓库。输出失败可能留下部分报告，保留供诊断，不自动删除。

退出码：0=完成扫描且没有额外组件异常（仍有覆盖限制）；1=连接、文档类型或写出失败；2=报告已写出但存在待检查项。命令行错误可通过 `2> scan-error.txt` 保存。

## 数据和安全边界

仅连接现有实例，读取活动配置；不启动 CAD、不打开文件、不切换配置、不解析轻量组件、不改变选择、不重建、不保存、不关闭文档。不创建或清理 CAD 临时文件。

扫描前后比较配置名及保存标志，发生变化则记录警告，不声称恢复成功。该检查不等价于完整状态快照，用户并发操作可能使结果失效。

路径比较不区分大小写，配置名保守地按大小写区分。不会解析符号链接、网络共享别名或按几何去重。实例 ID 和零件 ID 只在单份报告内有效。

子装配体保留在实例树中，不计作零件。抑制组件仍可能出现在观测计数中，因此该计数不是采购 BOM；抑制或未加载子装配体的后代可能缺失。未知、虚拟组件不参与唯一零件合并，异常写入报告。文件存在不等于几何已验证。变换矩阵与几何验证暂未实现。

## 代码结构

- `src/Models.cs`：数据结构和去重。
- `src/SolidWorksScanner.cs`：COM 连接及递归扫描。
- `src/ManifestWriter.cs`：JSON / CSV 写出。
- `src/Program.cs`：命令行入口。
- `tests/CoreTests.cs`：独立于 CAD 的核心测试。
- `docs/DEVELOPMENT.md`：实际验证记录和下一步。
- `docs/API-NOTES.md`：官方接口依据。

本地 SolidWorks 接口库只复制到忽略的 bin 目录，不提交第三方 DLL 或个人 CAD 文件。开源许可证及第三方再分发审查留待发布阶段，当前未选择许可证。
