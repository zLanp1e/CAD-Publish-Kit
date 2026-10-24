using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CadPublishKit
{
    internal static class DesktopProgram
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PublishForm(args));
        }
    }
    public sealed partial class PublishForm : Form
    {
        private readonly Color navy = Color.FromArgb(20, 36, 59), teal = Color.FromArgb(0, 119, 112), muted = Color.FromArgb(105, 119, 139);
        private readonly BlockingCollection<Action> jobs = new BlockingCollection<Action>();
        private readonly DesktopWorkflow workflow = new DesktopWorkflow();
        private readonly Thread worker;
        private readonly Label fileLabel = new Label(), status = new Label(), summary = new Label(), format = new Label();
        private readonly TextBox output = new TextBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Button choose = new Button(), browse = new Button(), export = new Button(), open = new Button(), templateButton = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Panel drop = new Panel();
        private readonly string[] arguments;
        private string templateOverride, sourcePath, lastOutput;
        private bool busy, ready, closing;
        private int heartbeat;
        private readonly System.Windows.Forms.Timer pulse = new System.Windows.Forms.Timer();

        public PublishForm(string[] args)
        {
            arguments = args; Text = "CAD Publish Kit v0.1 Preview — STEP 交付工具";
            Size = new Size(1240, 960); MinimumSize = new Size(1100, 820);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(242, 245, 249);
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildLayout();
            worker = new Thread(() => { foreach (var job in jobs.GetConsumingEnumerable()) job(); });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
            pulse.Interval = 250; pulse.Tick += (s, e) => heartbeat++; pulse.Start();
            FormClosing += OnClosing;
            Shown += (s, e) => {
                if (IsSmoke) {
                    output.Text = args[2];
                    DropFile(drop, new DragEventArgs(new DataObject(DataFormats.FileDrop, new[] { args[1] }), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy));
                }
                if (args.Length == 4 && args[0] == "--batch-smoke") {
                    tabs.SelectedIndex = 1; batchRoot.Text = args[1]; batchVerify.Checked = Boolean.Parse(args[2]); StartBatch();
                }
            };
        }
        private Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, Color color)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), AutoEllipsis = true };
            parent.Controls.Add(label); return label;
        }
        private void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(212, 221, 231);
            button.BackColor = primary ? teal : Color.White; button.ForeColor = primary ? Color.White : navy;
            button.Cursor = Cursors.Hand; button.Height = 38;
        }
        private void BuildLayout()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 95, BackColor = navy };
            Controls.Add(header);
            LabelAt(header, "CAD Publish Kit", 30, 12, 550, 40, 24, Color.White);
            LabelAt(header, "从 STEP 模型到清晰的交付文件", 33, 59, 700, 25, 10, Color.FromArgb(173, 193, 214));
            var tag = LabelAt(header, "LOCAL  /  本地处理", 800, 35, 255, 25, 10, Color.FromArgb(112, 219, 199));
            tag.Anchor = AnchorStyles.Top | AnchorStyles.Right; tag.TextAlign = ContentAlignment.MiddleRight;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 18, 26, 18), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 113));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            tabs.Dock = DockStyle.Fill;
            var singleTab = new TabPage("单文件预览 / 导出") { BackColor = BackColor };
            singleTab.Controls.Add(layout); tabs.TabPages.Add(singleTab); tabs.TabPages.Add(BuildBatchTab());
            ConfigureNavigation();
            Controls.Add(tabs); tabs.BringToFront();

            drop.Dock = DockStyle.Fill; drop.BackColor = Color.White; drop.Margin = new Padding(0, 0, 0, 14); drop.AllowDrop = true;
            drop.Paint += (s, e) => { using (var pen = new Pen(Color.FromArgb(181, 205, 211))) { pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; e.Graphics.DrawRectangle(pen, 0, 0, drop.Width - 1, drop.Height - 1); } };
            LabelAt(drop, "01  /  导入模型", 20, 8, 250, 24, 10, teal);
            var title = LabelAt(drop, "将 STEP 文件拖到这里", 20, 31, 710, 35, 19, navy); title.Anchor |= AnchorStyles.Right;
            var hint = LabelAt(drop, "支持 .step / .stp  ·  放入后自动分析，无需先手动打开模型", 22, 70, 730, 23, 10, muted); hint.Anchor |= AnchorStyles.Right;
            fileLabel.SetBounds(22, 97, 740, 24); fileLabel.AutoEllipsis = true; fileLabel.ForeColor = muted; fileLabel.Text = "尚未选择文件"; fileLabel.Anchor |= AnchorStyles.Right; drop.Controls.Add(fileLabel);
            StyleButton(choose, "选择 STEP 文件", true); choose.SetBounds(800, 43, 210, 42); choose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            choose.Click += (s, e) => { using (var dialog = new OpenFileDialog { Filter = "STEP 文件|*.step;*.stp", Multiselect = false }) { if (dialog.ShowDialog(this) == DialogResult.OK) StartAnalyze(dialog.FileName); } };
            drop.Controls.Add(choose);
            drop.SizeChanged += (s, e) => {
                choose.Left = drop.ClientSize.Width - 232; choose.Top = 43;
                title.Width = hint.Width = Math.Max(200, drop.ClientSize.Width - 280);
                fileLabel.Width = drop.ClientSize.Width - 44;
            };
            drop.DragEnter += DragEnterFile; drop.DragDrop += DropFile;
            foreach (Control c in drop.Controls) { c.AllowDrop = true; c.DragEnter += DragEnterFile; c.DragDrop += DropFile; }
            layout.Controls.Add(drop, 0, 0);

            var list = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 0, 14), Padding = new Padding(18, 86, 18, 14) };
            summary.Text = "02  /  实体预览"; summary.SetBounds(20, 16, 700, 27); summary.ForeColor = navy; summary.Font = new Font(Font, FontStyle.Bold); list.Controls.Add(summary);
            grid.Dock = DockStyle.Fill; grid.ReadOnly = false; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 243, 246); grid.ColumnHeadersDefaultCellStyle.ForeColor = navy;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold); grid.ColumnHeadersHeight = 37;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 242, 239); grid.DefaultCellStyle.SelectionForeColor = navy;
            grid.DefaultCellStyle.ForeColor = navy; grid.DefaultCellStyle.Padding = new Padding(7, 0, 0, 0); grid.RowTemplate.Height = 34;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252); grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(234, 239, 244); grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            BuildPreviewControls(list); layout.Controls.Add(list, 0, 1);

            var destination = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 0, 14) };
            LabelAt(destination, "03  /  保存位置", 20, 10, 300, 25, 10, teal);
            output.SetBounds(22, 42, 780, 31); output.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; output.BorderStyle = BorderStyle.FixedSingle; destination.Controls.Add(output);
            StyleButton(browse, "浏览文件夹…", false); browse.SetBounds(822, 39, 188, 35); browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { Description = "选择保存位置：将在这里生成原始 STEP、BOM.csv 和 parts 文件夹" }) { if (Directory.Exists(output.Text)) dialog.SelectedPath = output.Text; if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath; } };
            destination.Controls.Add(browse);
            format.SetBounds(22, 77, 720, 20); format.ForeColor = muted; format.Text = "交付内容：原始 STEP  +  BOM.csv  +  parts/"; destination.Controls.Add(format);
            StyleButton(templateButton, "零件模板（自动）", false); templateButton.SetBounds(822, 75, 188, 24); templateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; templateButton.Font = new Font(Font.FontFamily, 9);
            templateButton.Click += (s, e) => { using (var d = new OpenFileDialog { Filter = "SolidWorks 零件模板|*.prtdot" }) { if (d.ShowDialog(this) == DialogResult.OK) { templateOverride = d.FileName; templateButton.Text = "已选择零件模板"; } } }; destination.Controls.Add(templateButton);
            destination.SizeChanged += (s, e) => {
                browse.Left = templateButton.Left = destination.ClientSize.Width - 210;
                output.Width = destination.ClientSize.Width - 254;
                format.Width = destination.ClientSize.Width - 250;
            };
            layout.Controls.Add(destination, 0, 2);

            var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            status.SetBounds(0, 1, 650, 39); status.ForeColor = muted; status.Text = "准备就绪。拖入一个 STEP 开始。"; status.Anchor |= AnchorStyles.Right; footer.Controls.Add(status);
            progress.SetBounds(0, 49, 650, 6); progress.Anchor |= AnchorStyles.Right; progress.Style = ProgressBarStyle.Continuous; footer.Controls.Add(progress);
            StyleButton(open, "打开输出文件夹", false); open.SetBounds(670, 10, 170, 44); open.Anchor = AnchorStyles.Top | AnchorStyles.Right; open.Enabled = false;
            open.Click += (s, e) => { if (Directory.Exists(lastOutput)) Process.Start(new ProcessStartInfo(lastOutput) { UseShellExecute = true }); }; footer.Controls.Add(open);
            StyleButton(export, "导出交付包  →", true); export.SetBounds(854, 10, 174, 44); export.Anchor = AnchorStyles.Top | AnchorStyles.Right; export.Enabled = false;
            export.Click += (s, e) => StartExport(); footer.Controls.Add(export); layout.Controls.Add(footer, 0, 3);
            footer.SizeChanged += (s, e) => {
                export.Left = footer.ClientSize.Width - 174; open.Left = footer.ClientSize.Width - 358;
                status.Width = progress.Width = Math.Max(100, footer.ClientSize.Width - 380);
            };
            // Trigger final positioning after every parent has its actual layout size.
            Shown += (s, e) => {
                choose.Left = drop.ClientSize.Width - 232;
                browse.Left = templateButton.Left = destination.ClientSize.Width - 210;
                output.Width = destination.ClientSize.Width - 254;
                export.Left = footer.ClientSize.Width - 174; open.Left = footer.ClientSize.Width - 358;
                status.Width = progress.Width = footer.ClientSize.Width - 380;
            };
        }
        private void UI(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
        private void Report(string text) { UI(() => { status.Text = text; batchStatus.Text = text; }); }
        private void SetBusy(bool value)
        {
            busy = value; choose.Enabled = browse.Enabled = output.Enabled = templateButton.Enabled = !value;
            export.Enabled = !value && ready; drop.AllowDrop = !value;
            grid.Enabled = !value; selectAll.Enabled = selectNone.Enabled = !value && preview != null && preview.Ready;
            expandDetails.Enabled = !value && preview != null;
            SetBatchBusy(value);
            progress.Style = value ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progress.Value = value ? 0 : 100;
        }
        private void DragEnterFile(object sender, DragEventArgs e)
        { e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }
        private void DropFile(object sender, DragEventArgs e)
        {
            if (busy) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) { status.Text = "请一次放入一个 STEP 文件。"; return; }
            StartAnalyze(files[0]);
        }
        private void StartAnalyze(string path)
        {
            if (busy) return;
            ready = false; grid.Rows.Clear(); open.Enabled = false; sourcePath = path;
            preview = null; details.Text = "正在分析…"; analysisReport.Enabled = false;
            fileLabel.Text = path;
            if (String.IsNullOrWhiteSpace(output.Text) || output.Text == autoDestination) {
                autoDestination = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_delivery"); output.Text = autoDestination;
            }
            SetBusy(true); status.Text = "正在准备分析…";
            string chosenTemplate = templateOverride;
            jobs.Add(() => {
                try {
                    var report = workflow.Analyze(path, Report, chosenTemplate);
                    UI(() => {
                        RenderPreview(report);
                        SetBusy(false);
                        if (IsSmoke) {
                            if (arguments[0] == "--preview-smoke") SmokeFinish(preview.Ready);
                            else {
                                if (arguments[0] == "--selection-smoke") {
                                    SelectPreview(false);
                                    if (grid.Rows.Count > 0) grid.Rows[0].Cells["selected"].Value = true;
                                    if (grid.Rows.Count > 2) grid.Rows[2].Cells["selected"].Value = true;
                                }
                                if (ready) StartExport(); else SmokeFinish(false);
                            }
                        }
                    });
                }
                catch (Exception ex) { Failure(ex); }
            });
        }
        private void StartExport()
        {
            if (busy || !ready) return;
            if (String.IsNullOrWhiteSpace(output.Text)) { status.Text = "请选择或填写保存位置。"; return; }
            string destination = output.Text.Trim(), selectedTemplate = templateOverride;
            string[] selectedFiles = SelectedFiles();
            SetBusy(true); status.Text = "正在准备导出…";
            jobs.Add(() => {
                try {
                    string result = workflow.Export(destination, selectedTemplate, Report, selectedFiles);
                    UI(() => {
                        lastOutput = result; SetBusy(false); open.Enabled = true;
                        foreach (DataGridViewRow row in grid.Rows) {
                            bool included = selectedFiles.Contains(((PreviewItem)row.Tag).FileName);
                            row.Cells["state"].Value = included ? "已验证" : "未选择";
                            row.Cells["state"].ToolTipText = included ? "本次导出及重新打开检查通过。" : "本次未导出此项。";
                        }
                        ShowPreviewDetails();
                        status.Text = "导出完成：" + selectedFiles.Length + " 项通过验证。" + (selectedFiles.Length < preview.Items.Count ? " BOM 仅包含所选项目。" : "");
                        if (IsSmoke) SmokeFinish(true);
                    });
                } catch (Exception ex) { Failure(ex); }
            });
        }
        private bool IsSmoke { get { return arguments.Length == 4 && (arguments[0] == "--smoke" || arguments[0] == "--preview-smoke" || arguments[0] == "--selection-smoke"); } }
        private void Failure(Exception ex)
        {
            if (ex is System.Runtime.InteropServices.COMException || ex.Message.Contains("RPC") || ex.Message.Contains("远程过程")) {
                workflow.ForgetDisconnectedSession();
                UI(() => ready = false);
            }
            UI(() => { SetBusy(false); status.Text = ex.Message; ShowFailureDetails(ex); if (IsSmoke) SmokeFinish(false); });
        }
        private void SmokeFinish(bool success)
        {
            string capture = Path.GetFullPath(arguments[3]); Directory.CreateDirectory(Path.GetDirectoryName(capture));
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            if (Width < MinimumSize.Width || Height < MinimumSize.Height) Size = new Size(1240, 960);
            PerformLayout(); Refresh();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(capture); }
            File.WriteAllText(capture + ".txt", "success=" + success + "\r\nui_heartbeat_ticks=" + heartbeat + "\r\nrows=" + grid.Rows.Count + "\r\nstatus=" + status.Text);
            Close();
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (closing) return;
            e.Cancel = true;
            if (busy) { status.Text = "正在处理模型，请等待本次操作结束后再关闭。"; return; }
            SetBusy(true); status.Text = "正在关闭分析临时文档…";
            jobs.Add(() => {
                try { workflow.CloseImported(); UI(() => { closing = true; pulse.Stop(); jobs.CompleteAdding(); Close(); }); }
                catch (Exception ex) { UI(() => { SetBusy(false); status.Text = "临时文档关闭失败：" + ex.Message; closing = true; }); }
            });
        }
    }
}
