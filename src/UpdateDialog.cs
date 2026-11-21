using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class UpdateDialog : Form
    {
        private readonly Label status;
        private Uri page = ReleaseUpdate.ReleasesPage;
        private CancellationTokenSource request;

        internal UpdateDialog()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            DialogUi.Setup(this, "检查更新", new Size(476, 242));

            var root = DialogUi.Rows(38, -1, 54);
            Controls.Add(root);
            root.Controls.Add(DialogUi.Label("当前版本 " + Program.DisplayVersion, 15, UiTheme.Ink), 0, 0);
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 7, 0, 8) };
            status = DialogUi.Label("正在检查更新…", 10, UiTheme.Muted);
            card.Controls.Add(status);
            root.Controls.Add(card, 0, 1);

            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton open = GameQuestionDialog.MakeButton("打开发布页", 136, delegate { OpenReleasePage(); }, true);
            open.Glyph = UiGlyph.Arrow;
            SoftButton close = GameQuestionDialog.MakeButton("关闭", 88, delegate { Close(); }, false);
            actions.Controls.Add(open);
            actions.Controls.Add(close);
            root.Controls.Add(actions, 0, 2);
            CancelButton = close;
            ActiveControl = close;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            using (var cancellation = new CancellationTokenSource())
            {
                request = cancellation;
                try
                {
                    ReleaseUpdate result = await ReleaseUpdate.CheckAsync(Program.CurrentVersion, cancellation.Token);
                    if (cancellation.IsCancellationRequested || IsDisposed) return;
                    page = result.Page;
                    switch (result.Status)
                    {
                        case UpdateStatus.Available:
                            status.Text = "发现新版本 v" + result.LatestVersion.ToString(3) + "。\r\n打开发布页查看说明和下载文件。";
                            break;
                        case UpdateStatus.Current:
                            status.Text = "当前没有更新版本。";
                            break;
                        case UpdateStatus.NoReleaseInformation:
                            status.Text = "暂无可读取的正式发布。\r\n如果仓库是私有的，请在浏览器登录 GitHub 后查看发布页。";
                            break;
                    }
                }
                catch (Exception)
                {
                    if (!cancellation.IsCancellationRequested && !IsDisposed)
                        status.Text = "暂时无法检查更新。\r\n请检查网络后重试，或打开发布页手动查看。";
                }
                finally { request = null; }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (request != null) request.Cancel();
            base.OnFormClosed(e);
        }

        private void OpenReleasePage()
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })) { }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                AppMessageBox.Show(this, "无法打开浏览器。发布页地址如下。\r\n" + page.AbsoluteUri, "打开发布页失败");
            }
        }
    }
}
