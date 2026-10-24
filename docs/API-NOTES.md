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

## 2026-10-24：装配体 STEP 唯一零件导出

新增 AssemblyExportPlan，递归读取组件，按规范化路径和引用配置分组，通过 IComponent2.GetBodies3 获取组件实例的实体，避免依赖 GetModelDoc2 当前激活配置或切换用户配置。官方文档指出该方法支持零件组件的 solid/surface，BodiesInfo 区分普通与装配级修改实体。当前只接受 normal solid，拒绝特殊/不可用状态；临时引用路径无需实际落盘，但必须有已加载组件和几何。每类零件全部 solid 复制到独立临时零件，重复实例数量写入 BOM。单项失败记录并继续后续项，全部通过且状态恢复才发布。

参考：
- https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetBodies3.html
- https://help.solidworks.com/2020/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModeler~CheckInterferenceBetweenTwoBodies.html
- https://help.solidworks.com/2016/english/api/sldworksapi/Set_Components_and_Transforms_for_Interference_Detection_Example_CSharp.htm

实例保存 Transform2.ArrayData，旋转 0–8，平移 9–11（米），缩放 12，13–15 未使用；相对于根装配体。导出零件不应用位置变换，完整装配体使用输入 STEP 原样副本；没有重新生成装配结构。通过复制前后 SHA-256 核对完整文件不变。变换数组前后逐项相等，未宣称第三方展示重建已验证。
