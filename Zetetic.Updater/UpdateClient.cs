using System;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Threading;
using System.Net;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using NLog;

namespace Zetetic.Updater
{
    public sealed class UpdateClient : INotifyPropertyChanged, IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(object target, string propertyName)
        {
            PropertyChanged?.Invoke(target ?? this, new PropertyChangedEventArgs(propertyName));
        }

        private Thread _checkThread;
        private BackgroundWorker _downloadWorker;

        public string PinnedPublicKey { get; set; }

        public UpdateClient(Application app, string updateUrl, string pinnedPublicKey) : this(app, updateUrl)
        {
            PinnedPublicKey = pinnedPublicKey;
        }

        public UpdateClient(Application app, string updateUrl)
        {
            Logger.Info("initializing update check at {0}", updateUrl);
            App = app;
            UpdateUri = updateUrl;
        }

        public void Start()
        {
            if (_checkThread == null)
            {
                DoCheck = true;
                _checkThread = new Thread(CheckForUpdates);
                _checkThread.Start();
            }
        }

        #region Properties

        private Application _app;
        public Application App
        {
            get { return _app; }
            set
            {
                _app = value;
                OnPropertyChanged(this, "App");
            }
        }

        private string _updateUri;
        public string UpdateUri
        {
            get { return _updateUri; }
            set
            {
                _updateUri = value;
                OnPropertyChanged(this, "UpdateUri");
            }
        }

        private ReleaseManifest _manifest;
        public ReleaseManifest Manifest
        {
            get { return _manifest; }
            set
            {
                _manifest = value;
                OnPropertyChanged(this, "Manifest");
                OnPropertyChanged(this, "UpdateLabel");
            }
        }

        private bool _doUpdate;
        public bool DoUpdate
        {
            get { return _doUpdate; }
            set
            {
                _doUpdate = value;
                OnPropertyChanged(this, "DoUpdate");
            }
        }

        private bool _doCheck;
        public bool DoCheck
        {
            get { return _doCheck; }
            set
            {
                _doCheck = value;
                OnPropertyChanged(this, "DoCheck");
            }
        }

        private string _installerPath;
        public string InstallerPath
        {
            get { return _installerPath; }
            set
            {
                _installerPath = value;
                OnPropertyChanged(this, "InstallerPath");
            }
        }

        public string UpdateLabel => Manifest == null ? null :
            $"A new version of {Manifest.Name}, {Manifest.Version}, is now available. Would you like to download it?";

        #endregion

        #region Commands

        public event EventHandler UpdateBegin;
        public event EventHandler UpdateComplete;
        public event EventHandler UpdateError;

        private ICommand _updateCommand;
        public ICommand UpdateCommand
        {
            get
            {
                return _updateCommand ?? (_updateCommand = new RelayCommand(
                           (o) =>
                           {
                               DoCheck = false; // no further checks after the user provides input
                               UpdateBegin?.Invoke(o, EventArgs.Empty);
                               ExecuteUpdate();
                           },
                           (o) => Manifest != null));
            }
        }

        private ICommand _cancelCommand;
        public ICommand CancelCommand
        {
            get
            {
                return _cancelCommand ?? (_cancelCommand = new RelayCommand(
                           (o) =>
                           {
                               DoCheck = false;
                               _downloadWorker?.CancelAsync();
                               ((Window) o).Close();
                           },
                           (o) => o != null));
            }
        }
        #endregion

        public void StopAsync()
        {
            _checkThread?.Abort();
            _checkThread = null;
        }

        public event Action<UpdateClient> UpdateAvailable;

        public event Action<int> ProgressUpdate;

        public void CheckForUpdates()
        {
            var serializer = new XmlSerializer(typeof(ReleaseManifest));
            while (DoCheck)
            {
                try
                {
                    var request = WebRequest.Create(UpdateUri);
                    var response = request.GetResponse();
                    if (response.ContentLength > 0)
                    {
                        using (TextReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            Manifest = (ReleaseManifest)serializer.Deserialize(reader);

                            var manifestVersion = new Version(Manifest.Version);
                            var runningVersion = Assembly.GetEntryAssembly().GetName().Version;

                            if (manifestVersion.CompareTo(runningVersion) > 0)
                            {
                                Logger.Info("Manifest version {0} is greater than current application version {1}", manifestVersion, runningVersion);

                                UpdateAvailable?.Invoke(this);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorException($"unable to fetch path {UpdateUri}", ex);
                }
                if(DoCheck) Thread.Sleep(60 * 60 * 1000);
            }
        }

        public void ExecuteUpdate()
        {
            _downloadWorker = new BackgroundWorker()
            {
                WorkerSupportsCancellation = true,
                WorkerReportsProgress = true
            };

            _downloadWorker.DoWork += (s, e) =>
            {
                    var fileRequest = WebRequest.Create(Manifest.PackageUrl);
                    var fileResponse = fileRequest.GetResponse();

                    InstallerPath = Path.GetTempPath() + @"\" + GetFilenameFromUrl(Manifest.PackageUrl);
                    using (var stream = fileResponse.GetResponseStream())
                    {
                        ReadStreamToFile(stream, InstallerPath, (int)fileResponse.ContentLength);
                    }

                    if (_downloadWorker.CancellationPending)
                    {
                        e.Cancel = true;
                    }
                    else
                    {
                        CheckSignature(InstallerPath); // will throw on error
                    }
            };

            _downloadWorker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Cancelled) 
                {
                    Logger.Warn("update cancelled");
                    UpdateComplete?.Invoke(this, EventArgs.Empty);
                }
                else if (e.Error != null)
                {
                    Logger.WarnException("an error occured processing update", e.Error);
                    UpdateError?.Invoke(e.Error, EventArgs.Empty);
                }
                else
                {
                    Process.Start(InstallerPath);
                    Thread.Sleep(2000);
                    UpdateComplete?.Invoke(this, EventArgs.Empty);
                    App.Shutdown();
                }
            };

            _downloadWorker.ProgressChanged += (s, e) =>
            {
                ProgressUpdate?.Invoke(e.ProgressPercentage);
            };

            _downloadWorker.RunWorkerAsync();
        }

        private string GetFilenameFromUrl(string url)
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            return Uri.UnescapeDataString(segments[segments.Length - 1]);
        }

        private void ReadStreamToFile(Stream stream, string path, int fileSize)
        {
            var length = fileSize;
            var bufferSize = 1024 * 16;
            var buffer = new byte[bufferSize];
            
            if (fileSize <= 0) return;
            Logger.Info("preparing to read {0} bytes of data from download into file {1}", length, path);
            using (var fileStream = File.Open(path, FileMode.Create, FileAccess.Write))
            {
                while (length > 0 && (_downloadWorker == null || !_downloadWorker.CancellationPending))
                {
                    var toRead = (bufferSize > length) ? length : bufferSize;
                    var bytesRead = stream.Read(buffer, 0, toRead);
                    if (bytesRead == 0)
                    {
                        throw new Exception("server disconnected");
                    }
                    length -= bytesRead;
                    fileStream.Write(buffer, 0, bytesRead);
                    var pctComplete = (int)(((fileSize - length) / ((double)fileSize)) * 100.0);
                    if (_downloadWorker != null && _downloadWorker.WorkerReportsProgress) _downloadWorker.ReportProgress(pctComplete);
                }
            }
        }

        private X509Certificate GetCertificate(string path)
        {
            X509Certificate cert = null;
            try
            {
                Logger.Debug("Extracting X509 certificate from file {0}", path);
                cert = X509Certificate.CreateFromSignedFile(path);
            }
            catch (CryptographicException e)
            {
                Logger.Warn("MSI at {0} is not properly signed {1}", path, e.Message);
            }
            return cert;
        }

        private void CheckSignature(string path)
        {
            var currentPath = Assembly.GetEntryAssembly().Location;

            var msiCert = GetCertificate(path);
            
            if (msiCert == null) throw new Exception("no valid signatures present on installer");

            if (!string.IsNullOrEmpty(PinnedPublicKey))
            {
                // if there is a pinned key, require a match
                if (!PinnedPublicKey.Equals(msiCert.GetPublicKeyString(), StringComparison.CurrentCultureIgnoreCase))
                {
                    throw new Exception($"Installer public key {msiCert.GetPublicKeyString()} failed to match pinned public key {PinnedPublicKey}");
                }
            }
            else
            {
                var appCert = GetCertificate(currentPath);
                if (appCert == null) throw new Exception("no signatures present on current application");

                var publicKeyString = msiCert.GetPublicKeyString();
                if (publicKeyString != null && !(
                        msiCert.Subject.Equals(appCert.Subject, StringComparison.CurrentCultureIgnoreCase)
                        || publicKeyString.Equals(appCert.GetPublicKeyString(), StringComparison.CurrentCultureIgnoreCase)
                    ))
                {   
                    throw new Exception(
                        $"Installer certificate subject or public key({msiCert.Subject}/{msiCert.GetPublicKeyString()})does not match current application certificate subject or public key {appCert.Subject}/{appCert.GetPublicKeyString()}."
                    );
                }
            }

            // step 3. verify the signature on the installer is good
            Logger.Debug("Verifying file");
            var valid = WinTrust.VerifyEmbeddedSignature(path);
            if(!valid)
            {
                throw new Exception("Invalid authenticode signature");
            }
        }

#region IDisposable Members

        public void Dispose()
        {
            StopAsync();
        }

#endregion
    }
}
