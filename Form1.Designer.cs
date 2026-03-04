using System.Drawing;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace BSH_Import_Utility
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.ImportOrderForm = new System.Windows.Forms.Button();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.menuStrip2 = new System.Windows.Forms.MenuStrip();
            this.menuToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.accessDatabaseLocationToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ignoredColumnsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.viewImportLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.refreshToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.progressBarImport = new System.Windows.Forms.ProgressBar();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.menuStrip2.SuspendLayout();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // ImportOrderForm
            // 
            this.ImportOrderForm.Location = new System.Drawing.Point(3, 3);
            this.ImportOrderForm.Name = "ImportOrderForm";
            this.ImportOrderForm.Size = new System.Drawing.Size(109, 41);
            this.ImportOrderForm.TabIndex = 0;
            this.ImportOrderForm.Text = "Import Order Form";
            this.ImportOrderForm.UseVisualStyleBackColor = true;
            this.ImportOrderForm.Click += new System.EventHandler(this.GetDataFromPDF_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.MultiSelect = false;
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.ReadOnly = true;
            this.dataGridView1.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridView1.Size = new System.Drawing.Size(1540, 575);
            this.dataGridView1.TabIndex = 1;
            // 
            // menuStrip2
            // 
            this.menuStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuToolStripMenuItem});
            this.menuStrip2.Location = new System.Drawing.Point(0, 0);
            this.menuStrip2.Name = "menuStrip2";
            this.menuStrip2.Padding = new System.Windows.Forms.Padding(5, 2, 0, 2);
            this.menuStrip2.Size = new System.Drawing.Size(1674, 24);
            this.menuStrip2.TabIndex = 3;
            this.menuStrip2.Text = "menuStrip2";
            // 
            // menuToolStripMenuItem
            // 
            this.menuToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.accessDatabaseLocationToolStripMenuItem,
            this.ignoredColumnsToolStripMenuItem,
            this.refreshToolStripMenuItem,
            this.viewImportLogToolStripMenuItem,
            this.exitToolStripMenuItem});
            this.menuToolStripMenuItem.Name = "menuToolStripMenuItem";
            this.menuToolStripMenuItem.Size = new System.Drawing.Size(50, 20);
            this.menuToolStripMenuItem.Text = "Menu";
            // 
            // accessDatabaseLocationToolStripMenuItem
            // 
            this.accessDatabaseLocationToolStripMenuItem.Name = "accessDatabaseLocationToolStripMenuItem";
            this.accessDatabaseLocationToolStripMenuItem.Size = new System.Drawing.Size(210, 22);
            this.accessDatabaseLocationToolStripMenuItem.Text = "Access Database Location";
            this.accessDatabaseLocationToolStripMenuItem.Click += new System.EventHandler(this.accessDatabaseLocationToolStripMenuItem_Click);
            // 
            // ignoredColumnsToolStripMenuItem
            // 
            this.ignoredColumnsToolStripMenuItem.Name = "ignoredColumnsToolStripMenuItem";
            this.ignoredColumnsToolStripMenuItem.Size = new System.Drawing.Size(210, 22);
            this.ignoredColumnsToolStripMenuItem.Text = "Ignored Columns";
            this.ignoredColumnsToolStripMenuItem.Click += new System.EventHandler(this.btnShowIgnoredColumns_Click);
            // 
            // viewImportLogStripMenuItem
            // 
            this.viewImportLogToolStripMenuItem.Name = "viewImportLogToolStripMenuItem";
            this.viewImportLogToolStripMenuItem.Size = new System.Drawing.Size(210, 22);
            this.viewImportLogToolStripMenuItem.Text = "View Import Log";
            this.viewImportLogToolStripMenuItem.Click += new System.EventHandler(this.viewImportLogToolStripMenuItem_Click);
            //
            // refreshToolStringMenuItem
            //
            this.refreshToolStripMenuItem.Name = "refreshToolStripMenuItem";
            this.refreshToolStripMenuItem.Size = new System.Drawing.Size(210, 22);
            this.refreshToolStripMenuItem.Text = "Refresh Grid - F5";
            this.refreshToolStripMenuItem.Click += new System.EventHandler(this.refreshToolStripMenuItem_Click);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(210, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.ImportOrderForm);
            this.panel1.Location = new System.Drawing.Point(10, 23);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(118, 607);
            this.panel1.TabIndex = 4;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.dataGridView1);
            this.panel2.Location = new System.Drawing.Point(134, 26);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(1540, 575);
            this.panel2.TabIndex = 1;
            // 
            // progressBarImport
            // 
            this.progressBarImport.Location = new System.Drawing.Point(417, 1);
            this.progressBarImport.Name = "progressBarImport";
            this.progressBarImport.Size = new System.Drawing.Size(1007, 23);
            this.progressBarImport.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.progressBarImport.TabIndex = 2;
            this.progressBarImport.Visible = false;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1674, 601);
            this.Controls.Add(this.progressBarImport);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.menuStrip2);
            this.Name = "Form1";
            this.Text = "Bishop Storehouse Import Application";
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.menuStrip2.ResumeLayout(false);
            this.menuStrip2.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Button ImportOrderForm;
        private DataGridView dataGridView1;
        private MenuStrip menuStrip2;
        private ToolStripMenuItem menuToolStripMenuItem;
        private ToolStripMenuItem accessDatabaseLocationToolStripMenuItem;
        private ToolStripMenuItem ignoredColumnsToolStripMenuItem;
        private ToolStripMenuItem viewImportLogToolStripMenuItem;
        private ToolStripMenuItem refreshToolStripMenuItem;
        private Panel panel1;
        private Panel panel2;
        private ToolStripMenuItem exitToolStripMenuItem;
        private ProgressBar progressBarImport;
    }
}