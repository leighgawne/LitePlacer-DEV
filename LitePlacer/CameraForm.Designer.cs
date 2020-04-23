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
            this.Cam_pictureBox = new LitePlacer.Camera.ProtectedPictureBox();
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
            // CameraForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.Controls.Add(this.Cam_pictureBox);
            this.Name = "CameraForm";
            this.Text = "Down Cam";
            ((System.ComponentModel.ISupportInitialize)(this.Cam_pictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        public Camera.ProtectedPictureBox Cam_pictureBox;
    }
}