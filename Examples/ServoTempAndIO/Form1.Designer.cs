namespace SerialLedBtnLibEx
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            checkBox1 = new CheckBox();
            label1 = new Label();
            checkBox2 = new CheckBox();
            checkBox3 = new CheckBox();
            checkBox4 = new CheckBox();
            checkBox5 = new CheckBox();
            checkBox6 = new CheckBox();
            checkBox7 = new CheckBox();
            trackBar1 = new TrackBar();
            label2 = new Label();
            textBox1 = new TextBox();
            groupBox1 = new GroupBox();
            checkBox8 = new CheckBox();
            formsPlot1 = new ScottPlot.WinForms.FormsPlot();
            label4 = new Label();
            groupBox2 = new GroupBox();
            ((System.ComponentModel.ISupportInitialize)trackBar1).BeginInit();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(365, 78);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(82, 19);
            checkBox1.TabIndex = 0;
            checkBox1.Text = "checkBox1";
            checkBox1.UseVisualStyleBackColor = true;
            checkBox1.CheckedChanged += checkBox1_CheckedChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Britannic Bold", 20F);
            label1.Location = new Point(24, 9);
            label1.Name = "label1";
            label1.Size = new Size(127, 30);
            label1.TabIndex = 1;
            label1.Text = "I/O Demo";
            // 
            // checkBox2
            // 
            checkBox2.AutoSize = true;
            checkBox2.Enabled = false;
            checkBox2.Location = new Point(60, 138);
            checkBox2.Name = "checkBox2";
            checkBox2.Size = new Size(82, 19);
            checkBox2.TabIndex = 2;
            checkBox2.Text = "checkBox2";
            checkBox2.UseVisualStyleBackColor = true;
            // 
            // checkBox3
            // 
            checkBox3.AutoSize = true;
            checkBox3.Location = new Point(263, 78);
            checkBox3.Name = "checkBox3";
            checkBox3.Size = new Size(82, 19);
            checkBox3.TabIndex = 4;
            checkBox3.Text = "checkBox3";
            checkBox3.UseVisualStyleBackColor = true;
            checkBox3.CheckedChanged += checkBox3_CheckedChanged;
            // 
            // checkBox4
            // 
            checkBox4.AutoSize = true;
            checkBox4.Enabled = false;
            checkBox4.Location = new Point(165, 138);
            checkBox4.Name = "checkBox4";
            checkBox4.Size = new Size(82, 19);
            checkBox4.TabIndex = 3;
            checkBox4.Text = "checkBox4";
            checkBox4.UseVisualStyleBackColor = true;
            // 
            // checkBox5
            // 
            checkBox5.AutoSize = true;
            checkBox5.Location = new Point(165, 78);
            checkBox5.Name = "checkBox5";
            checkBox5.Size = new Size(82, 19);
            checkBox5.TabIndex = 6;
            checkBox5.Text = "checkBox5";
            checkBox5.UseVisualStyleBackColor = true;
            checkBox5.CheckedChanged += checkBox5_CheckedChanged;
            // 
            // checkBox6
            // 
            checkBox6.AutoSize = true;
            checkBox6.Enabled = false;
            checkBox6.Location = new Point(263, 138);
            checkBox6.Name = "checkBox6";
            checkBox6.Size = new Size(82, 19);
            checkBox6.TabIndex = 5;
            checkBox6.Text = "checkBox6";
            checkBox6.UseVisualStyleBackColor = true;
            // 
            // checkBox7
            // 
            checkBox7.AutoSize = true;
            checkBox7.Location = new Point(60, 78);
            checkBox7.Name = "checkBox7";
            checkBox7.Size = new Size(82, 19);
            checkBox7.TabIndex = 8;
            checkBox7.Text = "checkBox7";
            checkBox7.UseVisualStyleBackColor = true;
            checkBox7.CheckedChanged += checkBox7_CheckedChanged;
            // 
            // trackBar1
            // 
            trackBar1.Location = new Point(42, 68);
            trackBar1.Maximum = 180;
            trackBar1.Name = "trackBar1";
            trackBar1.Size = new Size(298, 45);
            trackBar1.TabIndex = 9;
            trackBar1.TickFrequency = 10;
            trackBar1.Scroll += trackBar1_Scroll;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(95, 39);
            label2.Name = "label2";
            label2.Size = new Size(38, 15);
            label2.TabIndex = 10;
            label2.Text = "Angle";
            // 
            // textBox1
            // 
            textBox1.Enabled = false;
            textBox1.Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Pixel);
            textBox1.Location = new Point(150, 31);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(100, 29);
            textBox1.TabIndex = 11;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(textBox1);
            groupBox1.Controls.Add(trackBar1);
            groupBox1.Location = new Point(490, 65);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(395, 122);
            groupBox1.TabIndex = 12;
            groupBox1.TabStop = false;
            groupBox1.Text = "Servo SV1 Control";
            groupBox1.Enter += groupBox1_Enter;
            // 
            // checkBox8
            // 
            checkBox8.AutoSize = true;
            checkBox8.Enabled = false;
            checkBox8.Location = new Point(365, 138);
            checkBox8.Name = "checkBox8";
            checkBox8.Size = new Size(82, 19);
            checkBox8.TabIndex = 13;
            checkBox8.Text = "checkBox8";
            checkBox8.UseVisualStyleBackColor = true;
            // 
            // formsPlot1
            // 
            formsPlot1.Location = new Point(12, 16);
            formsPlot1.Name = "formsPlot1";
            formsPlot1.Size = new Size(924, 222);
            formsPlot1.TabIndex = 14;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 25F);
            label4.Location = new Point(942, 87);
            label4.Name = "label4";
            label4.Size = new Size(109, 46);
            label4.TabIndex = 16;
            label4.Text = "label4";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(formsPlot1);
            groupBox2.Location = new Point(12, 193);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(1071, 241);
            groupBox2.TabIndex = 17;
            groupBox2.TabStop = false;
            groupBox2.Text = "Temperature";
            groupBox2.Enter += groupBox2_Enter;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1095, 510);
            Controls.Add(checkBox8);
            Controls.Add(checkBox7);
            Controls.Add(checkBox5);
            Controls.Add(checkBox6);
            Controls.Add(checkBox3);
            Controls.Add(checkBox4);
            Controls.Add(checkBox2);
            Controls.Add(label1);
            Controls.Add(checkBox1);
            Controls.Add(groupBox1);
            Controls.Add(groupBox2);
            Name = "Form1";
            Text = "Form1";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)trackBar1).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CheckBox checkBox1;
        private Label label1;
        private CheckBox checkBox2;
        private CheckBox checkBox3;
        private CheckBox checkBox4;
        private CheckBox checkBox5;
        private CheckBox checkBox6;
        private CheckBox checkBox7;
        private TrackBar trackBar1;
        private Label label2;
        private TextBox textBox1;
        private GroupBox groupBox1;
        private CheckBox checkBox8;
        private ScottPlot.WinForms.FormsPlot formsPlot1;
        private Label label4;
        private GroupBox groupBox2;
    }
}
