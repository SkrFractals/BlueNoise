
#define NULLABLE

using RgbFractalGenCs.Content.Basic;
using System.Drawing;


namespace BlueNoise;
public partial class MainForm : Form {
	private readonly DoubleBufferedPanel
	screenPanel;                // Display panel
	private Bitmap currentBitmap;
	private NoiseTask[] tasks;
	public MainForm() {
		InitializeComponent();

		screenPanel = new() {
			Location = new(0, 0), // old = (239,13)
			Name = "screenPanel",
			Size = new Size(80, 80),
			TabIndex = 0,
			TabStop = false
			//Anchor = AnchorStyles.Left | AnchorStyles.Bottom
		};
		screenPanel.Paint += ScreenPanel_Paint;
		//screenPanel.Click += AnimateButton_Click;
		Controls.Add(screenPanel);
		currentBitmap = new(1, 1);
	}




	#region Events
	private void ScreenPanel_Paint(object sender, PaintEventArgs e) {
		if (currentBitmap == null)
			return;
		// Faster rendering with crisp pixels
		e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
		// some safety code to ensure no crashes
		byte attempt = 0;
		while (attempt < 10) {
			try {
				e.Graphics.DrawImage(currentBitmap, new Rectangle(0, 0, screenPanel.Width, screenPanel.Height));
				attempt = 10;
			} catch (Exception) {
				++attempt;
				Thread.Sleep(10 + 10 * attempt * attempt);
			}
		}
	}
	#endregion
}
