using System;
using System.Drawing;
using System.Windows.Forms;
using EasyLabel.Core;

namespace EasyLabel.UI
{
    public sealed class CustomSizeDialog : Form
    {
        private readonly NumericUpDown _width;
        private readonly NumericUpDown _height;
        private readonly Label _preview;

        public LabelSpec Spec { get; private set; }

        public CustomSizeDialog(LabelSpec current)
        {
            Text = "自定义标签尺寸";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(390, 258);
            BackColor = Color.White;
            Font = Theme.Ui(9f);

            var title = Theme.Label("输入标签的物理尺寸（宽 × 高）", 10.5f, Theme.Text, FontStyle.Bold);
            title.Location = new Point(20, 16);

            var widthLabel = Theme.Label("宽度 mm", 9.2f, Theme.Muted);
            widthLabel.Location = new Point(24, 62);
            _width = UiKit.Numeric(5, 300, current != null ? (decimal)current.WidthMm : 30m, 1, 1m);
            _width.Location = new Point(105, 58);
            _width.Width = 100;

            var heightLabel = Theme.Label("高度 mm", 9.2f, Theme.Muted);
            heightLabel.Location = new Point(24, 102);
            _height = UiKit.Numeric(5, 300, current != null ? (decimal)current.HeightMm : 20m, 1, 1m);
            _height.Location = new Point(105, 98);
            _height.Width = 100;

            _preview = Theme.Label("", 10f, Theme.Accent, FontStyle.Bold);
            _preview.AutoSize = false;
            _preview.Location = new Point(24, 138);
            _preview.Size = new Size(340, 24);

            var hint = Theme.Label("常见 4 英寸机型的最大打印宽度约 104 mm；自定义宽度建议不超过该值。", 8.4f, Theme.Muted);
            hint.AutoSize = false;
            hint.Location = new Point(24, 164);
            // 一行放不下时要能折行，否则末尾会被裁掉。
            hint.Size = new Size(340, 40);

            var cancel = UiKit.SecondaryButton("取消");
            cancel.Location = new Point(190, 212);
            cancel.Size = new Size(80, 32);
            cancel.DialogResult = DialogResult.Cancel;
            var ok = UiKit.PrimaryButton("确定");
            ok.Location = new Point(280, 212);
            ok.Size = new Size(84, 32);
            ok.Click += (s, e) => Apply();

            Controls.Add(title);
            Controls.Add(widthLabel);
            Controls.Add(_width);
            Controls.Add(heightLabel);
            Controls.Add(_height);
            Controls.Add(_preview);
            Controls.Add(hint);
            Controls.Add(cancel);
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            _width.ValueChanged += (s, e) => UpdatePreview();
            _height.ValueChanged += (s, e) => UpdatePreview();
            UpdatePreview();

            Theme.ApplyScale(this, new Size(390, 258));
        }

        private void UpdatePreview()
        {
            _preview.Text = "当前尺寸：" + _width.Value.ToString("0.#") + " × " +
                            _height.Value.ToString("0.#") + " mm";
        }

        private void Apply()
        {
            try
            {
                if (_width.Value > 104m)
                    throw new ArgumentException("最大打印宽度约 104 mm，请减小宽度");
                Spec = LabelSpec.CreateCustom((double)_width.Value, (double)_height.Value);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "尺寸无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
