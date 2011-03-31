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
    public partial class UpdateWindow : Window, IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public UpdateWindowViewModel Model;
        private ProgressWindow _updateProgress;

        public UpdateWindow(Application app, string updateUrl)
        {
            InitializeComponent();

            logger.Info("initializing update check at {0}", updateUrl);

            DataContext = Model = new UpdateWindowViewModel(app, updateUrl);

            Model.UpdateAvailable = (o, args) => 
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    WebBrowser.Navigate(Model.Manifest.ReleaseNotesUrl);
                    Focus();
                    ShowDialog();
                }));
            };

            Model.UpdateBegin = (o, args) =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _updateProgress = new ProgressWindow();
                    _updateProgress.Show();
                }));
            };

            Model.UpdateComplete = (o, args) =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    _updateProgress.Close();
                    Close();
                }));
            };

            Model.ProgressUpdate += (percent) =>
            {
                Dispatcher.BeginInvoke((UpdateWindowViewModel.ProgressUpdateHandler)((x) =>
                {
                    _updateProgress.ProgressText = string.Format("{0}% complete", x);
                    _updateProgress.ProgressValue = x;
                }), percent);
            };

            Model.UpdateError = (o, args) =>
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    MessageBox.Show(((Exception) o).Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _updateProgress.Close();
                    Close();
                }));
            };
        }

        #region IDisposable Members

        public virtual void Dispose()
        {
            if (Model != null)
                Model.StopAsync();
            Model = null;
        }

        #endregion
    }
}
