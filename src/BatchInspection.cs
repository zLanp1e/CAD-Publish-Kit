using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CadPublishKit
{
    public sealed class BatchDiscovery
    {
        public string Root;
        public readonly List<string> Files = new List<string>();
        public readonly List<string> Issues = new List<string>();
        public bool Cancelled;
    }
    public static class BatchFileScanner
    {
        public static BatchDiscovery Discover(string root, string excludedDirectory = null, Func<bool> cancelled = null)
        {
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("扫描目录不存在：" + root);
            var result = new BatchDiscovery { Root = root };
            Visit(root, 0, excludedDirectory, cancelled ?? (() => false), result);
            return result;
        }
        public static bool IsWithin(string path, string parent)
        {
            string a = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string b = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        private static void Visit(string directory, int depth, string excluded, Func<bool> cancelled, BatchDiscovery result)
        {
            if (cancelled()) { result.Cancelled = true; return; }
            try {
                if (excluded != null && IsWithin(directory, excluded)) { result.Issues.Add("跳过工具内部工作目录：" + directory); return; }
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { result.Issues.Add("跳过目录链接，避免循环或离开扫描范围：" + directory); return; }
                foreach (string file in Directory.GetFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
                    if (cancelled()) { result.Cancelled = true; return; }
                    string ext = Path.GetExtension(file);
                    if (!ext.Equals(".step", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".stp", StringComparison.OrdinalIgnoreCase)) continue;
                    try {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) { result.Issues.Add("跳过文件链接：" + file); continue; }
                        result.Files.Add(file);
                    } catch (Exception ex) { result.Issues.Add(file + "：" + ex.Message); }
                }
                var children = Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
                // Root is depth zero. Files at ten descendant levels are included.
                if (depth == 10) { if (children.Length > 0) result.Issues.Add("达到 10 层上限，未进入 " + children.Length + " 个子目录：" + directory); return; }
                foreach (string child in children) { Visit(child, depth + 1, excluded, cancelled, result); if (result.Cancelled) return; }
            } catch (Exception ex) { result.Issues.Add("无法扫描目录 " + directory + "：" + ex.Message); }
        }
        public static string Relative(string path, string root)
        {
            if (!IsWithin(path, root)) throw new IOException("文件不在扫描目录内。");
            return Path.GetFullPath(path).Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length).TrimStart('\\', '/');
        }
    }
    public sealed class BatchItem
    {
        public string source_path, relative_path, status = "pending", reason = "等待检查", document_kind, analysis_directory, export_directory;
        public int unique_parts, instance_count;
        public double seconds;
    }
    public sealed class BatchReport
    {
        public string root, report_directory, started_at = DateTimeOffset.Now.ToString("o"), finished_at, stop_reason;
        public bool verify_exports, cancelled;
        public List<string> directory_issues = new List<string>();
        public List<BatchItem> files = new List<BatchItem>();
    }
    public static class SplitAssessment
    {
        public static void Apply(ScanReport scan, BatchItem item)
        {
            var preview = PreviewModel.FromScan(scan);
            item.document_kind = scan.document_kind; item.unique_parts = preview.Items.Count;
            item.instance_count = preview.Items.Sum(p => p.Quantity);
            if (!preview.Ready) {
                item.status = "review";
                item.reason = String.Join("; ", preview.Issues.Concat(preview.Items.Where(p => !p.Selectable).Select(p => p.Name + ": " + p.Error)));
                if (String.IsNullOrEmpty(item.reason)) item.reason = "没有可读取的 solid 实体，无法确定是否可拆分。";
            } else if (item.instance_count < 2) {
                item.status = "unsplittable";
                item.reason = preview.Assembly ? "仅有一个零件实例；当前按装配零件导出，不能拆成多个零件文件。" : "仅有一个 solid 实体，当前工具无法还原其内部独立零件。";
            } else {
                item.status = "splittable";
                item.reason = "结构检查可拆分；未验证实际导出。";
                if (preview.Assembly && item.unique_parts == 1) item.reason += "重复实例共享一份唯一零件 STEP。";
            }
        }
        public static string Label(string status)
        {
            switch (status) {
                case "verified": return "✓ 可拆分 · 已验证";
                case "splittable": return "✓ 可拆分 · 仅结构";
                case "unsplittable": return "× 不可拆分";
                case "review": return "! 需检查";
                case "error": return "! 检查失败";
                case "running": return "正在检查…";
                default: return "— 未检查";
            }
        }
    }
    public static class BatchProcessor
    {
        public static BatchReport Run(DesktopWorkflow workflow, string root, bool verify, string template,
            Func<bool> cancelled, Action<string> progress, Action<BatchReport> discovered, Action<BatchItem> changed)
        {
            workflow.CloseImported();
            progress("正在查找 STEP / STP，最多进入 10 层子目录…");
            var found = BatchFileScanner.Discover(root, workflow.WorkRoot, cancelled);
            string directory = Path.Combine(workflow.WorkRoot, "batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var report = new BatchReport { root = found.Root, report_directory = directory, verify_exports = verify, cancelled = found.Cancelled,
                directory_issues = found.Issues, files = found.Files.Select(f => new BatchItem { source_path = f, relative_path = BatchFileScanner.Relative(f, found.Root) }).ToList() };
            var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            using (var json = new FileStream(Path.Combine(directory, "batch_report.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var csv = new FileStream(Path.Combine(directory, "batch_results.csv"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                Action checkpoint = () => {
                    WriteOwned(json, serializer.Serialize(report));
                    var text = new StringBuilder("RelativePath,Result,Reason,UniqueParts,InstanceCount,Seconds,SourcePath,AnalysisReport,ExportReport\r\n");
                    foreach (var row in report.files) text.AppendLine(String.Join(",", new[] { row.relative_path, SplitAssessment.Label(row.status), row.reason,
                        row.unique_parts.ToString(), row.instance_count.ToString(), row.seconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), row.source_path, row.analysis_directory, row.export_directory }.Select(ManifestWriter.Csv)));
                    WriteOwned(csv, text.ToString());
                };
                checkpoint(); discovered(report);
                try {
                    for (int index = 0; index < report.files.Count; index++) {
                        if (cancelled()) { report.cancelled = true; break; }
                        var row = report.files[index]; var watch = System.Diagnostics.Stopwatch.StartNew();
                        row.status = "running"; row.reason = "正在导入和分析"; changed(row);
                        string prefix = "[" + (index + 1) + " / " + report.files.Count + "] " + Path.GetFileName(row.source_path) + " · ";
                        bool verifying = false;
                        try {
                            var scan = workflow.Analyze(row.source_path, message => progress(prefix + message), template);
                            row.analysis_directory = workflow.LastAnalysisDirectory;
                            SplitAssessment.Apply(scan, row);
                            if (row.status == "splittable" && verify) {
                                verifying = true;
                                workflow.Verify(template, message => progress(prefix + message));
                                row.status = "verified"; row.reason = "全部唯一零件导出并重新打开验证通过；原文件未保存。";
                            }
                        } catch (Exception ex) {
                            row.status = "error"; row.reason = (verifying ? "导出验证失败：" : "读取/分析失败：") + ex.Message;
                            if (ex is System.Runtime.InteropServices.COMException || ex.Message.Contains("RPC") || ex.Message.Contains("远程过程")) {
                                report.stop_reason = "SolidWorks 连接中断，本批次停止；剩余文件未检查。";
                                workflow.ForgetDisconnectedSession();
                            }
                        } finally {
                            row.analysis_directory = workflow.LastAnalysisDirectory; row.export_directory = workflow.LastExportDirectory;
                            try { workflow.CloseImported(); }
                            catch (Exception ex) { row.status = "error"; row.reason += "；临时文档清理失败：" + ex.Message; report.stop_reason = "无法安全关闭临时文档，本批次停止。"; }
                            watch.Stop(); row.seconds = watch.Elapsed.TotalSeconds; changed(row); checkpoint();
                        }
                        if (report.stop_reason != null) break;
                    }
                } finally {
                    report.cancelled = report.cancelled || cancelled(); report.finished_at = DateTimeOffset.Now.ToString("o"); checkpoint();
                }
            }
            return report;
        }
        private static void WriteOwned(FileStream stream, string text)
        {
            byte[] bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            stream.Position = 0; stream.Write(bytes, 0, bytes.Length); stream.SetLength(bytes.Length); stream.Flush();
        }
    }
}
