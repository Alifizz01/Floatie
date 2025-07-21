using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Floatie
{
    public partial class MainWindow : Window
    {
        private bool isDragging = false;
        private Point clickPosition;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void FencePanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isDragging = true;
            clickPosition = e.GetPosition(MainCanvas);
            FencePanel.CaptureMouse();
        }

        private void FencePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPosition = e.GetPosition(MainCanvas);

                double offsetX = currentPosition.X - clickPosition.X;
                double offsetY = currentPosition.Y - clickPosition.Y;

                double newLeft = Canvas.GetLeft(FencePanel) + offsetX;
                double newTop = Canvas.GetTop(FencePanel) + offsetY;

                Canvas.SetLeft(FencePanel, newLeft);
                Canvas.SetTop(FencePanel, newTop);

                clickPosition = currentPosition;
            }
        }

        private void FencePanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            FencePanel.ReleaseMouseCapture();
        }
    }
}
