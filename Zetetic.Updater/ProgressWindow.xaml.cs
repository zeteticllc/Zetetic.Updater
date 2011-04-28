using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Zetetic.Updater
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window
    {
        public string ProgressText
        {
            set
            {
                _labelProgress.Content = value;
            }
        }

        public int ProgressValue
        {
            set
            {
                _progressSync.Value = value;
            }
        }

        public ProgressWindow()
        {
            InitializeComponent();
        }

        public event EventHandler Cancel;

        private void _buttonCancel_Click(object sender, RoutedEventArgs e)
        {
            if (Cancel != null) Cancel(sender, e);
        }
    }
}
