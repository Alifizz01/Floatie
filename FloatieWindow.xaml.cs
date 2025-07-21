using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Floatie
{
    public partial class FloatieWindow : Window
    {
        private bool isDragging = false;
        private Point clickPosition;

        public FloatieWindow(string typeName, string iconPath)
        {
            InitializeComponent();
            HeaderText.Text = $"🧩 {typeName} Files";

            // Add a sample image (optional, can be replaced)
            var image = new Image
            {
                Source = new BitmapImage(new Uri(iconPath, UriKind.Relative)),
                Width = 40,
                Height = 40,
                Margin = new Thickness(5)
            };

            Grid.SetRow(image, 0);
            Grid.SetColumn(image, 0);
            ContentGrid.Children.Add(image);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Close the floatie window
        }

        private void FencePanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isDragging = true;
            clickPosition = e.GetPosition(this);
            FencePanel.CaptureMouse();
        }

        private void FencePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPos = e.GetPosition(this);
                this.Left += currentPos.X - clickPosition.X;
                this.Top += currentPos.Y - clickPosition.Y;
            }
        }

        private void FencePanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            FencePanel.ReleaseMouseCapture();
        }
    }
}
