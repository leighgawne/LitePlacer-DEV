namespace LitePlacer
{
    partial class CameraUC
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.Cam_pictureBox = new Terpsichore.Machine.Sensors.ProtectedPictureBox();
            this.FeatureDetails = new System.Windows.Forms.Label();
            this.UpdateRate = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.Cam_pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // Cam_pictureBox
            // 
            this.Cam_pictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Cam_pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Cam_pictureBox.Location = new System.Drawing.Point(0, 0);
            this.Cam_pictureBox.Name = "Cam_pictureBox";
            this.Cam_pictureBox.Size = new System.Drawing.Size(723, 562);
            this.Cam_pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.Cam_pictureBox.TabIndex = 12;
            this.Cam_pictureBox.TabStop = false;
            // 
            // FeatureDetails
            // 
            this.FeatureDetails.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.FeatureDetails.AutoSize = true;
            this.FeatureDetails.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(0)))));
            this.FeatureDetails.Font = new System.Drawing.Font("Microsoft Sans Serif", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FeatureDetails.ForeColor = System.Drawing.Color.White;
            this.FeatureDetails.Location = new System.Drawing.Point(512, 523);
            this.FeatureDetails.Name = "FeatureDetails";
            this.FeatureDetails.Size = new System.Drawing.Size(163, 25);
            this.FeatureDetails.TabIndex = 13;
            this.FeatureDetails.Text = "X: ??? Y: ??? ";
            // 
            // UpdateRate
            // 
            this.UpdateRate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.UpdateRate.AutoSize = true;
            this.UpdateRate.BackColor = System.Drawing.Color.Blue;
            this.UpdateRate.Font = new System.Drawing.Font("Microsoft Sans Serif", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.UpdateRate.ForeColor = System.Drawing.Color.White;
            this.UpdateRate.Location = new System.Drawing.Point(13, 523);
            this.UpdateRate.Name = "UpdateRate";
            this.UpdateRate.Size = new System.Drawing.Size(102, 25);
            this.UpdateRate.TabIndex = 14;
            this.UpdateRate.Text = "??? FPS";
            // 
            // CameraUC
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.UpdateRate);
            this.Controls.Add(this.FeatureDetails);
            this.Controls.Add(this.Cam_pictureBox);
            this.Name = "CameraUC";
            this.Size = new System.Drawing.Size(723, 562);
            ((System.ComponentModel.ISupportInitialize)(this.Cam_pictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public Terpsichore.Machine.Sensors.ProtectedPictureBox Cam_pictureBox;
        public System.Windows.Forms.Label FeatureDetails;
        public System.Windows.Forms.Label UpdateRate;
    }
}
