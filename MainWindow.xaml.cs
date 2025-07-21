using System.Collections.Generic;
using System.Windows;

namespace Floatie
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void LaunchFloatieWidgets(object sender, RoutedEventArgs e)
        {
            var fileTypes = new Dictionary<string, (string IconPath, string Label)>
            {
                { "Folder",     ("/Assets/folder.png",    "Folder") },
                { "PDF",        ("/Assets/pdf.png",       "PDF") },
                { "Image",      ("/Assets/picture.png",   "Image") },
                { "Document",   ("/Assets/documents.png", "Document") },
                { "Executable", ("/Assets/exe.png",       "EXE") },
                { "Script",     ("/Assets/code.png",      "Script") },
                { "Archive",    ("/Assets/zip.png",       "Zip") }
            };

            int offset = 50;

            foreach (var fileType in fileTypes)
            {
                var floatie = new FloatieWindow(fileType.Key, fileType.Value.IconPath)
                {
                    Left = offset,
                    Top = offset
                };

                offset += 60;
                floatie.Show();
            }
        }
    }
}
