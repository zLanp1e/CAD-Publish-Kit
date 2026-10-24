# CAD Publish Kit

[English](README.md)

本地 Windows / SolidWorks 模型交付工具。当前已验证多实体 STEP 拆分导出，原生装配体批量导出尚未完成。

## 交付目录

```text
test-cases/MBNW02RZ-A-1-M1/
├── 原始文件.step
├── BOM.csv
└── parts/
    ├── body_001.step
    ├── body_002.step
    └── ...
```

原始 STEP、BOM 和零部件文件夹放在同一级。BOM 用 Excel 打开，包含实体名称、实体数量、对应文件、导出状态、前后面数和轴向偏差（毫米）。数量统计的是导出实体，不是恢复出的原始机械零件数量。原始文件名保留，内容不修改。

内部日志、扫描记录和历次验证报告保留在项目 `output/` 中，不放进交付目录。CAD 文件、BOM 和内部日志均不上传 GitHub。

## 构建和运行

在项目根目录运行：

```powershell
.\scripts\build.ps1
.\bin\CoreTests.exe
```

先在 SolidWorks 中打开对应模型并激活所需配置，再执行：

```powershell
# 扫描，内部清单写入 output/<case-name>/
.\scripts\run-case.ps1 -CaseName 'MBNW02RZ-A-1-M1'
# 导出并生成交付目录
.\scripts\run-case.ps1 -CaseName 'MBNW02RZ-A-1-M1' -ExportBodies -Template 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_part.prtdot'
```

模板路径按本机修改，默认模板有效时可以不传 Template。案例名称只决定输出位置，不会自动打开或核对源模型。运行时不要编辑或切换文档，SolidWorks 与程序应使用同一 Windows 用户及权限级别。

已有 BOM.csv 或 parts 时，脚本会在导出前停止，避免覆盖。需要重跑时先自行保留或移动上一份交付结果。存在失败或恢复异常时不自动发布新交付包，详见 output 下的报告。

## 实际验证及限制

- Windows 11 x64、SolidWorks 2026 SP0.0、.NET Framework 4.8 或更高版本；其他版本未验证。
- 11 项核心测试通过。用户的 5 个实体均导出并重新打开成功，面数一致，六个轴向极值差值为 0，错误码均为 0。
- 原始 STEP 校验值未变；源文档的配置、选择对象/标记、路径、保存标志及活动状态检查一致。
- 采用实体复制到独立临时零件文档的方式导出，不保存源文档。只关闭本次创建的临时文档，失败输出保留供诊断。
- 当前只导出 solid 实体，包含隐藏实体；sheet 暂不导出。STEP 使用现有设置，本机实测 AP203。
- 位置范围检查容差为 1e-6 米，不等于完整几何等价证明或第三方 CAD 验收。
- 装配体去重按文件路径和配置；抑制、未加载子装配体可能导致后代不可读。原生装配树数量验收仍待完成。
- CSV 为 UTF-8 BOM，包含必要转义及公式前缀保护；实例 ID 和实体 ID 只在单次运行内有效。
- 退出码 0 为当前检查通过，1 为前置/致命错误，2 为需要检查。扫描范围限制仍适用于退出码 0。

完整命令和边界见英文 README。桌面 UI、三维展示及爆炸图未实现。开源许可证及第三方再分发审查尚未完成。
