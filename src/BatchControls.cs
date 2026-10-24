using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CadPublishKit
{
    public sealed partial class PublishForm
    {
        private readonly TextBox batchRoot = new TextBox(), batchDetails = new TextBox();
        private readonly CheckBox batchVerify = new CheckBox(), batchProblemsOnly = new CheckBox();
        private readonly Button batchBrowse = new Button(), batchStart = new Button(), batchStop = new Button(), batchFolder = new Button(), batchPreview = new Button(), batchReports = new Button();
        private readonly DataGridView batchGrid = new DataGridView();
        private readonly Label batchSummary = new Label(), batchStatus = new Label();
        private readonly ProgressBar batchProgress = new ProgressBar();
        private readonly Dictionary<string, DataGridViewRow> batchRows = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private BatchReport batchReport;
        private volatile bool batchCancel;
        private bool batchRunning;

        private TabPage BuildBatchTab()
        {
            var page = new TabPage("批量检查文件夹") { BackColor = BackColor };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            page.Controls.Add(layout);
            var input = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            input.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); input.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); input.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            input.Controls.Add(new Label { Text = "扫描 STEP / STP  ·  包含所选目录及下方 10 层子目录  ·  原文件和 PDF 保留原位", Dock = DockStyle.Fill, ForeColor = navy }, 0, 0);
            var pathLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); pathLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            batchRoot.Dock = DockStyle.Fill; batchRoot.Margin = new Padding(0, 7, 12, 0);
            StyleButton(batchBrowse, "选择文件夹…", false); batchBrowse.Dock = DockStyle.Fill;
            StyleButton(batchStart, "开始批量检查", true); batchStart.Dock = DockStyle.Fill;
            pathLine.Controls.Add(batchRoot, 0, 0); pathLine.Controls.Add(batchBrowse, 1, 0); pathLine.Controls.Add(batchStart, 2, 0); input.Controls.Add(pathLine, 0, 1);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            batchVerify.Text = "实际导出并重新打开验证（较慢）"; batchVerify.Checked = true; batchVerify.AutoSize = true; batchVerify.Margin = new Padding(0, 9, 20, 0);
            StyleButton(batchStop, "本文件完成后停止", false); batchStop.Width = 170; batchStop.Height = 31; batchStop.Enabled = false;
            var batchTemplate = new Button(); StyleButton(batchTemplate, "选择零件模板…", false); batchTemplate.Width = 150; batchTemplate.Height = 31;
            batchTemplate.Click += (s, e) => { if (busy) return; using (var dialog = new OpenFileDialog { Filter = "SolidWorks 零件模板|*.prtdot" }) if (dialog.ShowDialog(this) == DialogResult.OK) { templateOverride = dialog.FileName; templateButton.Text = "已选择零件模板"; } };
            options.Controls.Add(batchVerify); options.Controls.Add(batchStop); options.Controls.Add(batchTemplate); input.Controls.Add(options, 0, 2); layout.Controls.Add(input, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            batchProblemsOnly.Text = "只看 × / !"; batchProblemsOnly.AutoSize = true; batchProblemsOnly.Margin = new Padding(3, 10, 18, 0);
            StyleButton(batchFolder, "打开所在文件夹", false); batchFolder.Width = 155; batchFolder.Height = 32; batchFolder.Enabled = false;
            StyleButton(batchPreview, "在单文件预览中打开", false); batchPreview.Width = 185; batchPreview.Height = 32; batchPreview.Enabled = false;
            StyleButton(batchReports, "查看本次记录", false); batchReports.Width = 145; batchReports.Height = 32; batchReports.Enabled = false;
            actions.Controls.AddRange(new Control[] { batchProblemsOnly, batchFolder, batchPreview, batchReports }); layout.Controls.Add(actions, 0, 1);
            batchSummary.Dock = DockStyle.Fill; batchSummary.Text = "输入资料文件夹，开始检查。"; batchSummary.ForeColor = navy; layout.Controls.Add(batchSummary, 0, 2);
            batchGrid.Dock = DockStyle.Fill; batchGrid.ReadOnly = true; batchGrid.AllowUserToAddRows = false; batchGrid.AllowUserToDeleteRows = false;
            batchGrid.RowHeadersVisible = false; batchGrid.BackgroundColor = Color.White; batchGrid.BorderStyle = BorderStyle.None;
            batchGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; batchGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; batchGrid.MultiSelect = false;
            batchGrid.EnableHeadersVisualStyles = false; batchGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 243, 246);
            batchGrid.ColumnHeadersHeight = 36; batchGrid.RowTemplate.Height = 34;
            batchGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 242, 239); batchGrid.DefaultCellStyle.SelectionForeColor = navy;
            batchGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            batchGrid.Columns.Add("file", "STEP 文件"); batchGrid.Columns.Add("folder", "相对目录"); batchGrid.Columns.Add("result", "检查结果"); batchGrid.Columns.Add("count", "种类 / 实例"); batchGrid.Columns.Add("reason", "原因");
            float[] weights = { 180, 160, 150, 80, 250 };
            for (int i = 0; i < weights.Length; i++) { batchGrid.Columns[i].FillWeight = weights[i]; batchGrid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable; }
            layout.Controls.Add(batchGrid, 0, 3);
            batchDetails.Dock = DockStyle.Fill; batchDetails.Multiline = true; batchDetails.ReadOnly = true; batchDetails.ScrollBars = ScrollBars.Vertical;
            batchDetails.BackColor = Color.White; batchDetails.Text = "✓ 表示可拆分，× 表示当前工具无法拆成多个零件，! 表示需检查。\r\n未验证的结构检查会单独注明；不提取或移动原始资料，PDF 保留原位。";
            layout.Controls.Add(batchDetails, 0, 4);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 7));
            batchStatus.Dock = DockStyle.Fill; batchStatus.ForeColor = muted; batchStatus.Text = "默认会实际验证导出；验证临时文件和记录保留在工具内部 output 目录。";
            batchProgress.Dock = DockStyle.Fill; footer.Controls.Add(batchStatus, 0, 0); footer.Controls.Add(batchProgress, 0, 1); layout.Controls.Add(footer, 0, 5);
            batchBrowse.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { Description = "选择原始 STEP 资料目录" }) { if (Directory.Exists(batchRoot.Text)) dialog.SelectedPath = batchRoot.Text; if (dialog.ShowDialog(this) == DialogResult.OK) batchRoot.Text = dialog.SelectedPath; } };
            batchStart.Click += (s, e) => StartBatch();
            batchStop.Click += (s, e) => { batchCancel = true; batchStop.Enabled = false; batchStatus.Text = "已请求停止，当前文件处理完后结束；未处理文件不会标成失败。"; };
            batchProblemsOnly.CheckedChanged += (s, e) => ApplyBatchFilter();
            batchGrid.SelectionChanged += (s, e) => ShowBatchDetails();
            batchGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowDetailsWindow("批量检查详情", batchDetails.Text); };
            batchFolder.Click += (s, e) => { var item = CurrentBatchItem(); if (item != null) OpenFolder(Path.GetDirectoryName(item.source_path)); };
            batchPreview.Click += (s, e) => { var item = CurrentBatchItem(); if (!busy && item != null) { tabs.SelectedIndex = 0; StartAnalyze(item.source_path); } };
            batchReports.Click += (s, e) => { if (batchReport != null) OpenFolder(batchReport.report_directory); };
            return page;
        }
        private void SetBatchBusy(bool value)
        {
            batchRoot.Enabled = batchBrowse.Enabled = batchStart.Enabled = batchVerify.Enabled = !value;
            batchStop.Enabled = value && batchRunning && !batchCancel;
            batchPreview.Enabled = !value && CurrentBatchItem() != null;
            batchProgress.Style = value && batchRunning ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        }
        private void StartBatch()
        {
            if (busy) return;
            string root = batchRoot.Text.Trim().Trim('"');
            if (!Directory.Exists(root)) { batchStatus.Text = "请选择或输入一个存在的文件夹。"; return; }
            bool verify = batchVerify.Checked; string chosenTemplate = templateOverride;
            batchRunning = true; batchCancel = false; batchReport = null; batchRows.Clear(); batchGrid.Rows.Clear();
            batchSummary.Text = "正在查找文件…"; batchDetails.Text = "扫描目录：" + root; batchReports.Enabled = false;
            preview = null; ready = false; grid.Rows.Clear(); details.Text = "批量检查结束后，可选一行重新打开单文件预览。"; analysisReport.Enabled = false;
            summary.Text = "02  /  等待单文件预览"; selectionSummary.Text = "";
            SetBusy(true);
            jobs.Add(() => {
                try {
                    var result = BatchProcessor.Run(workflow, root, verify, chosenTemplate, () => batchCancel,
                        text => UI(() => batchStatus.Text = text + (batchCancel ? "（本文件完成后停止）" : "")),
                        report => UI(() => {
                            batchReport = report;
                            foreach (var item in report.files) {
                                int n = batchGrid.Rows.Add(Path.GetFileName(item.source_path), Path.GetDirectoryName(item.relative_path), SplitAssessment.Label(item.status), "—", item.reason);
                                var row = batchGrid.Rows[n]; row.Tag = item; row.Cells["file"].ToolTipText = item.source_path; batchRows[item.source_path] = row;
                            }
                            batchReports.Enabled = true; UpdateBatchSummary(); ApplyBatchFilter();
                        }), item => UI(() => UpdateBatchRow(item)));
                    UI(() => {
                        batchReport = result; batchRunning = false; SetBusy(false); UpdateBatchSummary(); ShowBatchDetails();
                        string finish = result.stop_reason ?? (result.cancelled ? "已停止。剩余文件保持“未检查”。" : "检查结束。资料仍在原位，可选择问题文件打开所在文件夹。");
                        batchStatus.Text = finish + (result.directory_issues.Count > 0 ? " 有目录跳过或无法访问，详见下方记录。" : "");
                        if (arguments.Length == 4 && arguments[0] == "--batch-smoke") {
                            if (!verify) batchProblemsOnly.Checked = true;
                            status.Text = batchSummary.Text; SmokeFinish(!result.cancelled && result.stop_reason == null);
                            File.AppendAllText(arguments[3] + ".txt", "\r\nbatch_total=" + result.files.Count + "\r\nbatch_verified=" + result.files.Count(f => f.status == "verified") + "\r\nvisible_rows=" + batchGrid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible) + "\r\nreport=" + result.report_directory);
                        }
                    });
                } catch (Exception ex) {
                    UI(() => { batchRunning = false; SetBusy(false); batchStatus.Text = "批量检查未完成：" + ex.Message; batchDetails.Text = ex.ToString();
                        if (arguments.Length == 4 && arguments[0] == "--batch-smoke") { status.Text = batchStatus.Text; SmokeFinish(false); }
                    });
                }
            });
        }
        private BatchItem CurrentBatchItem() { return batchGrid.CurrentRow == null ? null : batchGrid.CurrentRow.Tag as BatchItem; }
        private void UpdateBatchRow(BatchItem item)
        {
            DataGridViewRow row;
            if (!batchRows.TryGetValue(item.source_path, out row)) return;
            row.Cells["result"].Value = SplitAssessment.Label(item.status); row.Cells["count"].Value = item.unique_parts + " / " + item.instance_count; row.Cells["reason"].Value = item.reason;
            row.Cells["reason"].ToolTipText = item.reason;
            row.DefaultCellStyle.ForeColor = item.status == "verified" || item.status == "splittable" ? teal : item.status == "error" || item.status == "review" ? Color.Firebrick : navy;
            if (batchProblemsOnly.Checked) ApplyBatchFilter();
            UpdateBatchSummary(); ShowBatchDetails();
        }
        private void UpdateBatchSummary()
        {
            if (batchReport == null) return;
            var files = batchReport.files;
            int yes = files.Count(f => f.status == "verified" || f.status == "splittable"), no = files.Count(f => f.status == "unsplittable"), review = files.Count(f => f.status == "error" || f.status == "review");
            batchSummary.Text = "共 " + files.Count + " 个文件  ·  已检查 " + (yes + no + review) + "  ·  ✓ " + yes + "  × " + no + "  ! " + review +
                "  ·  目录提示 " + batchReport.directory_issues.Count;
        }
        private void ApplyBatchFilter()
        {
            batchGrid.CurrentCell = null;
            foreach (DataGridViewRow row in batchGrid.Rows) {
                var item = row.Tag as BatchItem;
                row.Visible = !batchProblemsOnly.Checked || (item != null && (item.status == "unsplittable" || item.status == "review" || item.status == "error"));
            }
            var first = batchGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible);
            if (first != null) batchGrid.CurrentCell = first.Cells[0];
            ShowBatchDetails();
        }
        private void ShowBatchDetails()
        {
            var item = CurrentBatchItem(); batchFolder.Enabled = item != null; batchPreview.Enabled = item != null && !busy;
            string directoryIssues = batchReport == null || batchReport.directory_issues.Count == 0 ? "" : "\r\n目录范围提示：\r\n" + String.Join("\r\n", batchReport.directory_issues);
            batchDetails.Text = (item == null ? "选择一行查看完整路径及原因。" :
                "文件：" + item.source_path + "\r\n结果：" + SplitAssessment.Label(item.status) + "    用时：" + item.seconds.ToString("F1") + " 秒\r\n原因：" + item.reason +
                "\r\n分析记录：" + item.analysis_directory + "\r\n验证记录：" + item.export_directory) + directoryIssues;
        }
    }
}
