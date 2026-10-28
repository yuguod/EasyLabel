using System;
using System.Drawing;
using System.Windows.Forms;

namespace EasyLabel.UI
{
    /// <summary>一个通用的“输入一行文字”小对话框，用于保存/重命名模版。</summary>
    public sealed class TextPromptDialog : Form
    {
        private readonly TextBox _box;
        private readonly Label _error;
        private readonly Func<string, string> _validate;

        public string Value
        {
            get { return (_box.Text ?? "").Trim(); }
        }

        public TextPromptDialog(string title, string prompt, string value, string okText,
            Func<string, string> validate = null)
        {
            _validate = validate;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 190);
            BackColor = Theme.Card;
            Font = Theme.Ui(9f);
            AutoScaleMode = AutoScaleMode.None;

            var promptLabel = Theme.Label(prompt, 9.2f, Theme.Muted);
            promptLabel.Location = new Point(20, 18);

            _box = new TextBox
            {
                Location = new Point(22, 46),
                Width = 416,
                Text = value ?? "",
                MaxLength = 60
            };
            Theme.StyleTextBox(_box);
            _box.Font = Theme.Ui(10f);

            _error = Theme.Label("", 8.4f, Theme.Error);
            _error.AutoSize = false;
            _error.Location = new Point(22, 82);
            _error.Size = new Size(416, 22);

            var hint = Theme.Label("同名模版会被覆盖；文件名中的特殊字符会自动替换为下划线。", 8.3f, Theme.Muted);
            hint.AutoSize = false;
            hint.Location = new Point(22, 104);
            hint.Size = new Size(416, 22);

            var ok = UiKit.PrimaryButton(string.IsNullOrEmpty(okText) ? "确定" : okText);
            ok.Size = new Size(96, 34);
            ok.Location = new Point(342, 138);
            ok.Click += (s, e) => Commit();

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Size = new Size(84, 34);
            cancel.Location = new Point(250, 138);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(promptLabel);
            Controls.Add(_box);
            Controls.Add(_error);
            Controls.Add(hint);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Theme.ApplyScale(this, new Size(460, 190));
            Load += (s, e) =>
            {
                _box.Focus();
                _box.SelectAll();
            };
        }

        private void Commit()
        {
            string value = Value;
            if (value.Length == 0)
            {
                _error.Text = "名称不能为空。";
                _box.Focus();
                return;
            }
            if (_validate != null)
            {
                string message = _validate(value);
                if (!string.IsNullOrEmpty(message))
                {
                    _error.Text = message;
                    _box.Focus();
                    return;
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
