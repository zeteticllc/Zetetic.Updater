using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using NLog;

namespace Zetetic.Updater
{
    /// <summary>
    /// Interaction logic for UpdateWindow.xaml
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public UpdateClient Model;
        private ProgressWindow _updateProgress;

        public UpdateWindow(UpdateClient client)
        {
            InitializeComponent();
            DataContext = Model = client;
        }

        private void ThisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WebBrowser.Navigate(Model.Manifest.ReleaseNotesUrl);

            Model.UpdateBegin += (o, args) =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _updateProgress = new ProgressWindow();
                    _updateProgress.Cancel += (s, eargs) =>
                    {
                        if (Model.CancelCommand.CanExecute(_updateProgress))
                        {
                            Model.CancelCommand.Execute(_updateProgress);
                        }
                    };
                    _updateProgress.ShowDialog();
                }));
            };

            Model.UpdateComplete += (o, args) =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _updateProgress.Close();
                    Close();
                }));
            };

            Model.ProgressUpdate += (percent) =>
            {
                Dispatcher.BeginInvoke((Action<int>)((x) =>
                {
                    _updateProgress.ProgressText = string.Format("{0}% complete", x);
                    _updateProgress.ProgressValue = x;
                }), percent);
            };

            Model.UpdateError += (o, args) =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    MessageBox.Show(((Exception)o).Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _updateProgress.Close();
                    Close();
                }));
            };
        }
    }
}
