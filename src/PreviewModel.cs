using System;
using System.Collections.Generic;
using System.Linq;

namespace CadPublishKit
{
    public sealed class PreviewItem
    {
        public string FileName, Name, Configuration, SourcePath, Error;
        public int Quantity, SolidCount;
        public int? Faces;
        public bool Selectable { get { return String.IsNullOrEmpty(Error); } }
    }
    public sealed class PreviewModel
    {
        public readonly List<PreviewItem> Items = new List<PreviewItem>();
        public readonly List<string> Issues = new List<string>();
        public bool Assembly;
        public string SourceFile;
        public bool Ready { get { return Issues.Count == 0 && Items.Count > 0 && Items.All(x => x.Selectable); } }
        public static PreviewModel FromScan(ScanReport scan)
        {
            var result = new PreviewModel { Assembly = scan.document_kind == "assembly", SourceFile = scan.source_path };
            if (result.Assembly) {
                result.Issues.AddRange(scan.warnings);
                foreach (var p in scan.parts) result.Items.Add(new PreviewItem { FileName = p.export_filename, Name = p.part_name,
                    Configuration = p.configuration, Quantity = p.instance_count, SolidCount = p.solid_count, Faces = p.face_count,
                    SourcePath = p.source_path, Error = p.export_issue });
            } else if (scan.document_kind == "part_bodies") {
                result.Issues.AddRange(scan.warnings.Where(w => w.Contains("enumeration failed") || w.Contains("State check failed") || w.Contains("state changed")));
                int n = 0;
                foreach (var b in scan.bodies) {
                    n++;
                    result.Items.Add(new PreviewItem { FileName = "body_" + n.ToString("D3") + ".step", Name = b.body_name,
                        Configuration = scan.configuration, Quantity = 1, SolidCount = b.body_type == "solid" ? 1 : 0, Faces = b.face_count,
                        SourcePath = scan.source_path, Error = b.body_type != "solid" ? "曲面等非 solid 实体暂不支持导出。" : b.issues.Count > 0 ? String.Join("; ", b.issues) : null });
                }
            } else result.Issues.Add("不支持的文档类型。");
            if (scan.state_check_passed == false) result.Issues.Add("分析前后文档状态不一致，请检查 SolidWorks。");
            return result;
        }
        public string Fingerprint()
        {
            return String.Join("\n", Items.Select(p => String.Join("\0", p.FileName, p.Name, p.Configuration, p.Quantity.ToString(), p.SolidCount.ToString(), p.Faces.ToString(), p.Error)));
        }
    }
    public static class ExportSelection
    {
        public static int[] Indices(IEnumerable<string> availableFiles, IEnumerable<string> selectedFiles)
        {
            var available = availableFiles.ToArray();
            if (available.Distinct(StringComparer.OrdinalIgnoreCase).Count() != available.Length)
                throw new InvalidOperationException("输出文件名重复，拒绝导出。");
            if (selectedFiles == null) return Enumerable.Range(0, available.Length).ToArray();
            var selected = new HashSet<string>(selectedFiles, StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0) throw new InvalidOperationException("至少选择一个零件。");
            if (selected.Any(s => !available.Contains(s, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("选择清单与当前模型不一致，请重新分析。");
            return Enumerable.Range(0, available.Length).Where(i => selected.Contains(available[i])).ToArray();
        }
    }
}
