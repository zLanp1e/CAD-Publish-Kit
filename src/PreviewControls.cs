using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CadPublishKit
{
    public sealed partial class PublishForm
    {
        private readonly TabControl tabs = new TabControl();
        private readonly Button selectAll = new Button(), selectNone = new Button(), analysisReport = new Button(), expandDetails = new Button();
        private readonly Label selectionSummary = new Label();
        private readonly TextBox details = new TextBox();
        private PreviewModel preview;
        private bool fillingPreview;
        private string autoDestination, analysisDirectory;

        private void ConfigureNavigation()
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(330, 64);
            tabs.TabPages[0].Text = "单文件预览与导出";
            tabs.TabPages[1].Text = "批量检查文件夹";
            tabs.AccessibleName = "选择工作模式";
            tabs.DrawItem += (s, e) => {
                bool active = e.Index == tabs.SelectedIndex;
                Rectangle card = e.Bounds; card.Inflate(-4, -4);
                Color background = active ? teal : Color.White;
                Color foreground = active ? Color.White : navy;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, card);
                using (var pen = new Pen(active ? teal : Color.FromArgb(208, 221, 228))) e.Graphics.DrawRectangle(pen, card);
                int x = card.Left + 16, y = card.Top + 17;
                using (var pen = new Pen(active ? Color.White : teal, 2)) {
                    if (e.Index == 0) {
                        e.Graphics.DrawRectangle(pen, x + 2, y - 2, 18, 24);
                        e.Graphics.DrawLine(pen, x + 6, y + 5, x + 16, y + 5);
                        e.Graphics.DrawLine(pen, x + 6, y + 11, x + 16, y + 11);
                    } else {
                        e.Graphics.DrawLines(pen, new[] { new Point(x, y + 3), new Point(x, y - 2), new Point(x + 10, y - 2), new Point(x + 14, y + 3), new Point(x + 25, y + 3), new Point(x + 25, y + 21), new Point(x, y + 21), new Point(x, y + 3) });
                        e.Graphics.DrawLine(pen, x, y + 7, x + 25, y + 7);
                    }
                }
                using (var titleFont = new Font(Font.FontFamily, 12, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, titleFont, new Rectangle(card.Left + 55, card.Top + 6, card.Width - 65, 25), foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using (var subtitleFont = new Font(Font.FontFamily, 9))
                    TextRenderer.DrawText(e.Graphics, e.Index == 0 ? "拖入 STEP · 预览、勾选与导出" : "选择文件夹 · 递归 10 层检查", subtitleFont, new Rectangle(card.Left + 55, card.Top + 32, card.Width - 65, 20), active ? Color.FromArgb(218, 244, 238) : muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                if (active && tabs.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(card, -3, -3), foreground, background);
            };
            tabs.SelectedIndexChanged += (s, e) => tabs.Invalidate();
            tabs.GotFocus += (s, e) => tabs.Invalidate();
            tabs.LostFocus += (s, e) => tabs.Invalidate();
        }

        private void BuildPreviewControls(Panel list)
        {
            StyleButton(selectAll, "全选", false); selectAll.SetBounds(20, 46, 80, 29);
            StyleButton(selectNone, "清空选择", false); selectNone.SetBounds(108, 46, 100, 29);
            StyleButton(analysisReport, "打开分析报告", false); analysisReport.SetBounds(216, 46, 135, 29);
            selectAll.Enabled = selectNone.Enabled = analysisReport.Enabled = false;
            selectAll.Click += (s, e) => SelectPreview(true); selectNone.Click += (s, e) => SelectPreview(false);
            analysisReport.Click += (s, e) => OpenFolder(analysisDirectory);
            StyleButton(expandDetails, "展开详情", false); expandDetails.SetBounds(359, 46, 100, 29); expandDetails.Enabled = false;
            expandDetails.Click += (s, e) => ShowDetailsWindow("项目详情", details.Text);
            selectionSummary.SetBounds(477, 49, 600, 25); selectionSummary.ForeColor = muted; selectionSummary.AutoEllipsis = true;
            list.Controls.AddRange(new Control[] { selectAll, selectNone, analysisReport, expandDetails, selectionSummary });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "selected", HeaderText = "导出", FillWeight = 40 });
            grid.Columns.Add("name", "名称"); grid.Columns.Add("configuration", "配置"); grid.Columns.Add("quantity", "数量");
            grid.Columns.Add("solids", "实体数"); grid.Columns.Add("faces", "面数"); grid.Columns.Add("file", "预计输出文件"); grid.Columns.Add("state", "状态");
            float[] weights = { 40, 160, 65, 45, 50, 55, 190, 85 };
            for (int i = 0; i < grid.Columns.Count; i++) {
                grid.Columns[i].FillWeight = weights[i]; grid.Columns[i].ReadOnly = i != 0; grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += (s, e) => { if (!fillingPreview && e.RowIndex >= 0 && e.ColumnIndex == 0) UpdateSelection(); };
            grid.SelectionChanged += (s, e) => ShowPreviewDetails();
            var inner = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0) };
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            inner.Controls.Add(grid, 0, 0);
            details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical;
            details.BackColor = Color.FromArgb(247, 250, 252); details.ForeColor = navy; details.BorderStyle = BorderStyle.FixedSingle;
            details.Text = "选择一行可查看完整文件名、来源和检查原因。"; details.Margin = new Padding(0, 8, 0, 0);
            inner.Controls.Add(details, 0, 1); list.Controls.Add(inner);
        }
        private void RenderPreview(ScanReport scan)
        {
            preview = PreviewModel.FromScan(scan); fillingPreview = true;
            try {
                grid.Rows.Clear();
                foreach (var item in preview.Items) {
                    int n = grid.Rows.Add(preview.Ready && item.Selectable, item.Name, item.Configuration, item.Quantity, item.SolidCount,
                        item.Faces.HasValue ? item.Faces.Value.ToString() : "—", item.FileName, item.Selectable ? "可导出" : "需检查");
                    var row = grid.Rows[n]; row.Tag = item;
                    row.Cells["selected"].ReadOnly = !preview.Ready || !item.Selectable;
                    row.Cells["file"].ToolTipText = item.FileName;
                    row.Cells["state"].ToolTipText = item.Error ?? "导出时将逐项重新打开检查。";
                    if (!item.Selectable) row.DefaultCellStyle.ForeColor = Color.Firebrick;
                }
            } finally { fillingPreview = false; }
            summary.Text = preview.Assembly ? "02  /  装配体预览     " + preview.Items.Count + " 类 · " + preview.Items.Sum(p => p.Quantity) + " 个实例" : "02  /  实体预览     " + preview.Items.Count + " 个实体";
            analysisDirectory = workflow.LastAnalysisDirectory; analysisReport.Enabled = Directory.Exists(analysisDirectory);
            UpdateSelection(); ShowPreviewDetails();
            status.Text = preview.Ready ? "分析完成。勾选需要导出的项目，核对保存位置。" : "存在需检查项目，详情见下方信息区。";
        }
        private string[] SelectedFiles()
        {
            return grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is PreviewItem && Convert.ToBoolean(r.Cells["selected"].Value))
                .Select(r => ((PreviewItem)r.Tag).FileName).ToArray();
        }
        private void SelectPreview(bool selected)
        {
            if (busy || preview == null || !preview.Ready) return;
            fillingPreview = true;
            try { foreach (DataGridViewRow row in grid.Rows) row.Cells["selected"].Value = selected && ((PreviewItem)row.Tag).Selectable; }
            finally { fillingPreview = false; }
            UpdateSelection();
        }
        private void UpdateSelection()
        {
            int count = SelectedFiles().Length;
            ready = preview != null && preview.Ready && count > 0;
            export.Enabled = !busy && ready;
            selectionSummary.Text = preview == null ? "" : "已选 " + count + " / " + preview.Items.Count + " 项" +
                (count > 0 && count < preview.Items.Count ? "  ·  BOM 将标注为所选项目清单" : "");
        }
        private void ShowPreviewDetails()
        {
            if (fillingPreview || preview == null) return;
            var item = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as PreviewItem;
            string global = preview.Issues.Count == 0 ? "" : "整体检查：" + String.Join("；", preview.Issues) + "\r\n";
            details.Text = global + (item == null ? "选择一行可查看完整信息。" :
                "名称：" + item.Name + "    配置：" + item.Configuration + "    数量：" + item.Quantity + "\r\n" +
                "预计文件：" + item.FileName + "\r\n源 STEP：" + preview.SourceFile + "\r\n" +
                (preview.Assembly ? "导入组件标识（临时路径）：" + item.SourcePath + "\r\n" : "") +
                (item.Error ?? "结构可读取；导出后将检查几何范围和重新打开结果。") + "\r\n状态：" + grid.CurrentRow.Cells["state"].Value + "；" + grid.CurrentRow.Cells["state"].ToolTipText);
        }
        private void ShowFailureDetails(Exception ex)
        {
            details.Text = ex.ToString();
            var report = workflow.LastExportReport;
            if (report == null) return;
            foreach (DataGridViewRow row in grid.Rows) {
                var item = row.Tag as PreviewItem;
                var result = report.bodies.FirstOrDefault(b => item != null && Path.GetFileName(b.export_path) == item.FileName);
                if (result == null) continue;
                row.Cells["state"].Value = result.status == "verified" ? "已验证 / 未发布" : "失败";
                row.Cells["state"].ToolTipText = result.error ?? "未发布交付包，请查看报告。";
            }
            analysisDirectory = workflow.LastExportDirectory; analysisReport.Enabled = Directory.Exists(analysisDirectory);
        }
        private void OpenFolder(string path)
        {
            try { if (!String.IsNullOrEmpty(path) && Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开文件夹", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        private void ShowDetailsWindow(string title, string text)
        {
            using (var dialog = new Form { Text = title, Size = new Size(920, 450), MinimumSize = new Size(600, 300), StartPosition = FormStartPosition.CenterParent, Font = Font }) {
                dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = text, BackColor = Color.White });
                dialog.ShowDialog(this);
            }
        }
    }
}
