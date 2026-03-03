using Org.BouncyCastle.Asn1.Crmf;
using System.Windows.Forms;

namespace BSH_Import_Utility
{
    partial class SelectableMessageForm
    {
        private System.ComponentModel.IContainer components = null!;
        private TextBox txtMessage = null!;
        private Button btnClose = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            txtMessage = new TextBox();
            btnClose = new Button();

            SuspendLayout();

            // txtMessage
            txtMessage.Multiline = true;
            txtMessage.ReadOnly = true;
            txtMessage.ScrollBars = ScrollBars.Vertical;
            txtMessage.Dock = DockStyle.Fill;
            txtMessage.Font = new System.Drawing.Font("Segoe UI", 9F);
            txtMessage.BackColor = System.Drawing.SystemColors.Window;

            // btnClose
            btnClose.Text = "Close";
            btnClose.Dock = DockStyle.Bottom;
            btnClose.Height = 40;
            btnClose.Click += (_, _) => Close();

            // SelectableMessageForm
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(700, 400);
            Controls.Add(txtMessage);
            Controls.Add(btnClose);
            StartPosition = FormStartPosition.CenterParent;
            Name = "SelectableMessageForm";

            ResumeLayout(false);
        }
    }
}