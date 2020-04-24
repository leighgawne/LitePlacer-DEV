namespace LitePlacer
{
    partial class CameraForm
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.Cam_pictureBox = new Terpsichore.Machine.Sensors.ProtectedPictureBox();
            this.FeatureDetails = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.Cam_pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // Cam_pictureBox
            // 
            this.Cam_pictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Cam_pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Cam_pictureBox.Location = new System.Drawing.Point(0, 0);
            this.Cam_pictureBox.Name = "Cam_pictureBox";
            this.Cam_pictureBox.Size = new System.Drawing.Size(784, 561);
            this.Cam_pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.Cam_pictureBox.TabIndex = 11;
            this.Cam_pictureBox.TabStop = false;
            // 
            // FeatureDetails
            // 
            this.FeatureDetails.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.FeatureDetails.AutoSize = true;
            this.FeatureDetails.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(0)))));
            this.FeatureDetails.Font = new System.Drawing.Font("Microsoft Sans Serif", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FeatureDetails.ForeColor = System.Drawing.Color.White;
            this.FeatureDetails.Location = new System.Drawing.Point(470, 518);
            this.FeatureDetails.Name = "FeatureDetails";
            this.FeatureDetails.Size = new System.Drawing.Size(163, 25);
            this.FeatureDetails.TabIndex = 12;
            this.FeatureDetails.Text = "X: ??? Y: ??? ";
            // 
            // CameraForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.Controls.Add(this.FeatureDetails);
            this.Controls.Add(this.Cam_pictureBox);
            this.Name = "CameraForm";
            this.Text = "Down Cam";
            ((System.ComponentModel.ISupportInitialize)(this.Cam_pictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public Terpsichore.Machine.Sensors.ProtectedPictureBox Cam_pictureBox;
        public System.Windows.Forms.Label FeatureDetails;
    }
}