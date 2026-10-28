using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace EasyLabel.UI
{
    public sealed class ProgressForm : Form
    {
        private readonly Label _message;
        private readonly ProgressBar _bar;
        private readonly Button _cancel;
        private readonly CancellationTokenSource _cts;
        private bool _completed;

        public CancellationToken Token { get { return _cts.Token; } }

        public ProgressForm(string title, int maximum)
        {
            _cts = new CancellationTokenSource();
            Text = title ?? "请稍候";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false;
            BackColor = Color.White;
            ClientSize = new Size(420, 142);
            Font = Theme.Ui(9f);

            _message = new Label
            {
                Text = "正在处理…",
                AutoSize = false,
                Location = new Point(22, 20),
                Size = new Size(376, 24),
                Font = Theme.Ui(9.5f),
                ForeColor = Theme.Text
            };
            _bar = new ProgressBar
            {
                Location = new Point(22, 52),
                Size = new Size(376, 16),
                Minimum = 0,
                Maximum = Math.Max(1, maximum),
                Style = ProgressBarStyle.Continuous
            };
            _cancel = UiKit.SecondaryButton("取消");
            _cancel.Location = new Point(314, 88);
            _cancel.Size = new Size(84, 34);
            _cancel.Click += (s, e) =>
            {
                _cancel.Enabled = false;
                _message.Text = "正在取消…";
                try { _cts.Cancel(); } catch { }
            };

            Controls.Add(_message);
            Controls.Add(_bar);
            Controls.Add(_cancel);

            Theme.ApplyScale(this, new Size(420, 142));
        }

        public void Report(int value, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<int, string>(Report), value, message); } catch { }
                return;
            }
            if (value < _bar.Minimum) value = _bar.Minimum;
            if (value > _bar.Maximum) value = _bar.Maximum;
            _bar.Value = value;
            if (!string.IsNullOrEmpty(message)) _message.Text = message;
        }

        public void Finish()
        {
            _completed = true;
            try { _cts.Dispose(); } catch { }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_completed)
            {
                try { _cts.Cancel(); } catch { }
            }
            base.OnFormClosing(e);
        }
    }
}
