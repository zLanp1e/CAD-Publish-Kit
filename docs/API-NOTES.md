# 阶段一 API 依据

日期：2026-10-24。使用本机 SolidWorks 2026 提供的强类型 Interop 程序集编译，不凭方法名动态猜测接口。

- [IConfiguration.GetRootComponent3](https://help.solidworks.com/2019/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IConfiguration~GetRootComponent3.html)：当前激活配置可使用 false，避免主动加载附加组件。根对象仅用于 GetChildren。
- [IComponent2.GetChildren](https://help.solidworks.com/2018/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetChildren.html)：只返回直接子级，需递归；抑制子装配体可能不返回子项；返回顺序不保证稳定；遍历受引用配置影响。
- [IComponent2.GetSuppression2](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetSuppression2.html)：本机接口库验证方法可编译，保存原始数值及枚举名；在线正文未成功取得，状态边界行为仍待真实组件测试。

本机官方 api/redist 的 sldworks 和 swconst 接口库验证了使用的方法签名与枚举。签名通过编译不代表真实装配体行为已验证。

本阶段不调用 STEP 导出，不定义未经实测的变换矩阵或坐标约定。
