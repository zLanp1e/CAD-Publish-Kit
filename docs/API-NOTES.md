# 阶段一 API 依据

日期：2026-10-24。使用本机 SolidWorks 2026 提供的强类型 Interop 程序集编译，不凭方法名动态猜测接口。

- [IConfiguration.GetRootComponent3](https://help.solidworks.com/2019/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IConfiguration~GetRootComponent3.html)：当前激活配置可使用 false，避免主动加载附加组件。根对象仅用于 GetChildren。
- [IComponent2.GetChildren](https://help.solidworks.com/2018/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetChildren.html)：只返回直接子级，需递归；抑制子装配体可能不返回子项；返回顺序不保证稳定；遍历受引用配置影响。
- [IComponent2.GetSuppression2](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetSuppression2.html)：本机接口库验证方法可编译，保存原始数值及枚举名；在线正文未成功取得，状态边界行为仍待真实组件测试。

本机官方 api/redist 的 sldworks 和 swconst 接口库验证了使用的方法签名与枚举。签名通过编译不代表真实装配体行为已验证。

本阶段不调用 STEP 导出，不定义未经实测的变换矩阵或坐标约定。

## 多实体分支

- [IPartDoc.GetBodies2](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ipartdoc~getbodies2.html)：按 swBodyType_e 过滤；VisibleOnly=false 包含隐藏实体。本项目分别枚举 solid 和 sheet。
- [IBody2.GetFaceCount](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ibody2~getfacecount.html)：获取实体面数，不能作为实体有效性或导出成功判据。

以上调用已在用户当前 STEP 导入文档中实际执行成功。

## 逐实体导出依据与实测

使用 IBody2.Copy 生成临时实体，IPartDoc.CreateFeatureFromBody3(copy, false, swCreateFeatureBodyCheck) 在新零件文档建立导入特征；本机官方 Interop 验证签名，用户五实体实测全部成功。

- [NewDocument](https://help.solidworks.com/2021/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISldWorks~NewDocument.html)：按零件模板建立新文档。
- [SaveAs2](https://help.solidworks.com/2025/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldocextension~saveas2.html)：STEP 需要目标文档激活；选中实体会影响范围，因此仅清空临时文档的选择。API 会覆盖文件，程序在独立运行目录中检查目标不存在。
- [LoadFile4](https://help.solidworks.com/2015/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~LoadFile4.html)：参数 r 导入到新文档。
- [ActivateDoc3](https://help.solidworks.com/2014/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~ActivateDoc3.html)：使用 false + swDontRebuildActiveDoc 恢复源文档，核对 COM 对象身份。
- [GetExtremePoint](https://help.solidworks.com/2014/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetExtremePoint.html)：分别取正负 X/Y/Z 方向的对应轴坐标，对比导出前后范围。GetBodyBox 是近似值，不用于验证。
