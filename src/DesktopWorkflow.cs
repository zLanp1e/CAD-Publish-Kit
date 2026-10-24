using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

namespace CadPublishKit
{
    // This session is used exclusively by one STA worker. No COM objects enter UI controls.
    public sealed class DesktopWorkflow
    {
        private ISldWorks app;
        private IModelDoc2 imported;
        private string input, inputHash, template;
        private string previewFingerprint, analyzedConfiguration;
        private bool analyzedDirty;
        public string LastAnalysisDirectory { get; private set; }
        public string LastExportDirectory { get; private set; }
        public BodyExportReport LastExportReport { get; private set; }
        public readonly string WorkRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "output", "desktop-runs"));
        public string Template { get { return template; } }
        public ScanReport Analyze(string path, Action<string> progress, string templateOverride = null)
        {
            LastAnalysisDirectory = null; LastExportDirectory = null; LastExportReport = null;
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !(Path.GetExtension(path).Equals(".step", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".stp", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("请选择一个存在的 STEP / STP 文件。");
            if (app == null) {
                progress("正在连接 SolidWorks…");
                try { app = SolidWorksConnector.Connect(); }
                catch (COMException) {
                    progress("正在启动 SolidWorks，请稍候…");
                    var type = Type.GetTypeFromProgID("SldWorks.Application");
                    if (type == null) throw new InvalidOperationException("未找到 SolidWorks。请先安装并完成授权。");
                    app = (ISldWorks)Activator.CreateInstance(type); app.Visible = true;
                }
            }
            CloseImported();
            var previous = app.ActiveDoc as IModelDoc2;
            var before = (app.GetDocuments() as object[]) ?? new object[0];
            template = String.IsNullOrWhiteSpace(templateOverride) ? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart) : templateOverride;
            if (!File.Exists(template)) {
                int revision;
                if (Int32.TryParse(app.RevisionNumber().Split('.')[0], out revision)) {
                    string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SOLIDWORKS", "SOLIDWORKS " + (revision + 1992), "templates", "gb_part.prtdot");
                    if (File.Exists(fallback)) template = fallback;
                }
            }
            string workspace = Path.Combine(WorkRoot, "import_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            // Unique filename avoids collisions with an unsaved user document of the same name.
            string copy = Path.Combine(workspace, "cpk_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".step");
            inputHash = Hash(path); File.Copy(path, copy, false);
            if (Hash(copy) != inputHash) throw new IOException("输入文件复制校验失败，请重新分析。");
            int errors = 0;
            try {
                progress("正在导入 STEP 并分析内部结构…");
                if (!File.Exists(template)) throw new IOException("未找到零件模板，请先在 SolidWorks 配置默认零件模板。");
                imported = app.LoadFile4(copy, "r", null, ref errors) as IModelDoc2;
                if (imported == null || before.Any(d => Object.ReferenceEquals(d, imported))) {
                    imported = null; throw new InvalidOperationException("无法建立独立的分析文档，错误码：" + errors);
                }
                if (errors != 0) throw new InvalidOperationException("STEP 导入返回错误码：" + errors);
                Activate(imported);
                var report = imported.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY ? AssemblyExportPlan.Build(imported).Scan : ModelScanner.Scan(app);
                report.source_path = path; report.document_title = Path.GetFileName(path);
                input = path;
                previewFingerprint = PreviewModel.FromScan(report).Fingerprint();
                analyzedDirty = imported.GetSaveFlag(); analyzedConfiguration = imported.ConfigurationManager.ActiveConfiguration.Name;
                LastAnalysisDirectory = Path.Combine(workspace, "analysis");
                ManifestWriter.Write(report, LastAnalysisDirectory);
                return report;
            }
            finally { if (previous != null) Activate(previous); }
        }
        public string Export(string destination, string templateOverride, Action<string> progress, IEnumerable<string> selectedFiles = null)
        {
            LastExportReport = null; LastExportDirectory = null;
            if (imported == null || String.IsNullOrEmpty(input)) throw new InvalidOperationException("请先分析 STEP 文件。");
            destination = Path.GetFullPath(destination);
            if (File.Exists(Path.Combine(destination, "BOM.csv")) || Directory.Exists(Path.Combine(destination, "parts")))
                throw new IOException("输出位置已有 BOM.csv 或 parts。请选择新的文件夹，现有文件不会被覆盖。");
            if (Hash(input) != inputHash) throw new IOException("源文件在分析后发生变化，请重新分析。");
            string sourceTarget = Path.Combine(destination, Path.GetFileName(input));
            if (File.Exists(sourceTarget) && Hash(sourceTarget) != inputHash) throw new IOException("输出位置有同名但内容不同的源文件，请选择其他文件夹。");
            string selectedTemplate = String.IsNullOrWhiteSpace(templateOverride) ? template : templateOverride;
            if (!File.Exists(selectedTemplate)) throw new IOException("未找到零件模板。请点击“零件模板”选择一个 .prtdot 文件。");
            Directory.CreateDirectory(destination);
            // Check write access before starting expensive CAD work.
            string probe = Path.Combine(destination, ".cpk_probe_" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            var report = Verify(templateOverride, progress, selectedFiles);
            Publish(report, input, destination);
            return destination;
        }
        public BodyExportReport Verify(string templateOverride, Action<string> progress, IEnumerable<string> selectedFiles = null)
        {
            LastExportDirectory = null; LastExportReport = null;
            if (imported == null || String.IsNullOrEmpty(input)) throw new InvalidOperationException("请先分析 STEP 文件。");
            if (Hash(input) != inputHash) throw new IOException("源文件在分析后发生变化，请重新分析。");
            if (imported.GetSaveFlag() != analyzedDirty || imported.ConfigurationManager.ActiveConfiguration.Name != analyzedConfiguration)
                throw new IOException("分析文档已发生变化，请重新分析。");
            string selectedTemplate = String.IsNullOrWhiteSpace(templateOverride) ? template : templateOverride;
            if (!File.Exists(selectedTemplate)) throw new IOException("未找到可用零件模板，请选择 .prtdot 文件。");
            var previous = app.ActiveDoc as IModelDoc2;
            string run = null;
            try {
                Activate(imported);
                var scan = imported.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY ? AssemblyExportPlan.Build(imported).Scan : ModelScanner.Scan(app);
                var preview = PreviewModel.FromScan(scan);
                if (!preview.Ready || preview.Fingerprint() != previewFingerprint) throw new IOException("当前模型与预览不一致或存在未解决问题，请重新分析。");
                int result = BodyStepExporter.Run(app, WorkRoot, selectedTemplate, progress, p => { run = p; LastExportDirectory = p; }, selectedFiles);
                LastExportReport = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Deserialize<BodyExportReport>(File.ReadAllText(Path.Combine(run, "export_report.json")));
                if (result != 0) throw new IOException("部分导出或状态检查未通过，未发布交付包。详细报告：" + run);
                if (Hash(input) != inputHash) throw new IOException("源文件发生变化，未发布交付包，请重新分析。");
                return LastExportReport;
            }
            finally {
                if (previous != null) {
                    try { Activate(previous); }
                    catch (Exception ex) { throw new IOException("无法恢复之前的 CAD 文档。导出报告保留在 " + (run ?? WorkRoot) + "。" + ex.Message); }
                }
            }
        }
        public static void Publish(BodyExportReport report, string source, string destination)
        {
            if (!report.source_state_preserved || report.warnings.Count != 0 || report.bodies.Count == 0 || report.bodies.Any(b => b.status != "verified" || !File.Exists(b.export_path)))
                throw new IOException("导出结果未全部通过验证。");
            string parts = Path.Combine(destination, "parts"), bom = Path.Combine(destination, "BOM.csv");
            if (Directory.Exists(parts) || File.Exists(bom)) throw new IOException("输出已存在，拒绝覆盖。");
            // Stage inside the destination so directory publication stays on the same volume.
            string stage = Path.Combine(destination, ".cpk_staging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            bool assembly = report.document_kind == "assembly";
            var csv = new StringBuilder(assembly ? "Item,PartName,InstanceCount,STEPFile,ExportStatus,OriginalFaces,ReopenedFaces,MaxAxisDeviation_mm,SourceSTEP,Notes,Configuration,ComponentSourcePath,SolidCount\r\n" : "Item,BodyName,BodyQuantity,STEPFile,ExportStatus,OriginalFaces,ReopenedFaces,MaxAxisDeviation_mm,SourceSTEP,Notes\r\n");
            int i = 0;
            foreach (var body in report.bodies) {
                string name = Path.GetFileName(body.export_path);
                string target = Path.Combine(stage, name);
                File.Copy(body.export_path, target, false);
                if (Hash(target) != Hash(body.export_path)) throw new IOException("交付文件校验失败。");
                var fields = new[] { (++i).ToString(), body.body_name, body.instance_count.ToString(), "parts/" + name, body.status,
                    body.original_faces.ToString(), body.reopened_faces.ToString(), (body.max_axis_delta.Value * 1000).ToString("G", CultureInfo.InvariantCulture), Path.GetFileName(source),
                    (report.export_scope == "selection" ? "SELECTED ITEMS ONLY; not a complete BOM. " : "") + (assembly ? "Unique component path and referenced configuration; geometry in component-local coordinates. Original STEP retains assembly placement." : "One exported solid body; not a reconstructed mechanical-parts BOM.") };
                if (assembly) fields = fields.Concat(new[] { body.configuration, body.component_source_path, body.original_solids.ToString() }).ToArray();
                csv.AppendLine(String.Join(",", fields.Select(ManifestWriter.Csv)));
            }
            string stagedBom = Path.Combine(destination, ".cpk_bom_" + Guid.NewGuid().ToString("N"));
            using (var stream = new FileStream(stagedBom, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(csv.ToString());
            string targetSource = Path.Combine(destination, Path.GetFileName(source));
            if (!File.Exists(targetSource)) File.Copy(source, targetSource, false);
            if (Hash(source) != Hash(targetSource)) throw new IOException("源文件复制校验失败。");
            Directory.Move(stage, parts); File.Move(stagedBom, bom);
        }
        public void CloseImported()
        {
            if (imported == null || app == null) return;
            string title = imported.GetTitle();
            var matches = ((app.GetDocuments() as object[]) ?? new object[0]).Cast<IModelDoc2>().Where(d => d.GetTitle() == title).ToArray();
            if (matches.Length != 1 || !Object.ReferenceEquals(matches[0], imported)) throw new InvalidOperationException("无法安全关闭分析临时文档，请检查 SolidWorks。");
            app.CloseDoc(title); imported = null; input = null;
        }
        public void ForgetDisconnectedSession()
        { imported = null; app = null; input = null; }
        private void Activate(IModelDoc2 doc)
        {
            int errors = 0;
            object active = app.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            if (!Object.ReferenceEquals(active, doc)) throw new InvalidOperationException("无法恢复指定 CAD 文档，错误码：" + errors);
        }
        private static string Hash(string path)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)); }
    }
}
