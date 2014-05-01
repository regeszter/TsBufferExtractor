using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using TvDatabase;
using TvLibrary.Log;

namespace SetupTv.Sections
{
  public partial class TsBufferExtractorSetup : SetupTv.SectionSettings
  {
    private RadioButton radioButton1;
    private RadioButton radioButton2;
    private RadioButton radioButton3;
    private GroupBox groupBox1;
    String tsBufferExtractorSetup;

    #region constructors

    public TsBufferExtractorSetup()
    {
      InitializeComponent();
    }

    #endregion

    public override void LoadSettings()
    {
      var layer = new TvBusinessLayer();
      tsBufferExtractorSetup = layer.GetSetting("TsBufferExtractorSetup", "A").Value;
      switch (tsBufferExtractorSetup)
      {
        case "A":
          radioButton1.Checked = true;
          break;
        case "B":
          radioButton2.Checked = true;
          break;
        case "C":
          radioButton3.Checked = true;
          break;
      }
    }

    public override void SaveSettings()
    {
      var layer = new TvBusinessLayer();
      Setting setting = layer.GetSetting("TsBufferExtractorSetup");
      
      if (radioButton1.Checked)
        setting.Value = "A";

      if (radioButton2.Checked)
        setting.Value = "B";

      if (radioButton3.Checked)
        setting.Value = "C";

      setting.Persist();
    }

    public override void OnSectionDeActivated()
    {
      Log.Info("TsBufferExtractor: Configuration deactivated");
      SaveSettings();
      base.OnSectionDeActivated();
    }

    public override void OnSectionActivated()
    {
      Log.Info("TsBufferExtractor: Configuration activated");
      LoadSettings();
      base.OnSectionActivated();
    }

    private void InitializeComponent()
    {
      this.radioButton1 = new System.Windows.Forms.RadioButton();
      this.radioButton2 = new System.Windows.Forms.RadioButton();
      this.radioButton3 = new System.Windows.Forms.RadioButton();
      this.groupBox1 = new System.Windows.Forms.GroupBox();
      this.groupBox1.SuspendLayout();
      this.SuspendLayout();
      // 
      // radioButton1
      // 
      this.radioButton1.AutoSize = true;
      this.radioButton1.Checked = true;
      this.radioButton1.Location = new System.Drawing.Point(18, 19);
      this.radioButton1.Name = "radioButton1";
      this.radioButton1.Size = new System.Drawing.Size(293, 17);
      this.radioButton1.TabIndex = 0;
      this.radioButton1.TabStop = true;
      this.radioButton1.Text = "from the beginning of the current program if it is available.";
      this.radioButton1.UseVisualStyleBackColor = true;
      // 
      // radioButton2
      // 
      this.radioButton2.AutoSize = true;
      this.radioButton2.Location = new System.Drawing.Point(18, 42);
      this.radioButton2.Name = "radioButton2";
      this.radioButton2.Size = new System.Drawing.Size(420, 17);
      this.radioButton2.TabIndex = 1;
      this.radioButton2.TabStop = true;
      this.radioButton2.Text = "from the last channel change if the beginning of the current program is not avail" +
    "able.";
      this.radioButton2.UseVisualStyleBackColor = true;
      // 
      // radioButton3
      // 
      this.radioButton3.AutoSize = true;
      this.radioButton3.Location = new System.Drawing.Point(18, 65);
      this.radioButton3.Name = "radioButton3";
      this.radioButton3.Size = new System.Drawing.Size(168, 17);
      this.radioButton3.TabIndex = 2;
      this.radioButton3.TabStop = true;
      this.radioButton3.Text = "from the last channel change. ";
      this.radioButton3.UseVisualStyleBackColor = true;
      // 
      // groupBox1
      // 
      this.groupBox1.Controls.Add(this.radioButton1);
      this.groupBox1.Controls.Add(this.radioButton3);
      this.groupBox1.Controls.Add(this.radioButton2);
      this.groupBox1.Location = new System.Drawing.Point(15, 39);
      this.groupBox1.Name = "groupBox1";
      this.groupBox1.Size = new System.Drawing.Size(441, 99);
      this.groupBox1.TabIndex = 3;
      this.groupBox1.TabStop = false;
      this.groupBox1.Text = "Save the puffer";
      // 
      // TsBufferExtractorSetup
      // 
      this.Controls.Add(this.groupBox1);
      this.Name = "TsBufferExtractorSetup";
      this.Size = new System.Drawing.Size(475, 305);
      this.groupBox1.ResumeLayout(false);
      this.groupBox1.PerformLayout();
      this.ResumeLayout(false);

    }
  }
}
