using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ServiceCo
{
    public partial class Driver_UploadRejected : ContentPage, INotifyPropertyChanged
    {
        private readonly FirebaseStorageService _storageService = new FirebaseStorageService();
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly string _driverId;
        private readonly List<string> _rejectedDocumentKeys;
        private ObservableCollection<RejectedDocument> _documents;
        private Dictionary<string, FileResult> _selectedFiles = new Dictionary<string, FileResult>();
        private int _totalDocuments;
        private int _selectedCount;

        public ObservableCollection<RejectedDocument> Documents
        {
            get => _documents;
            set
            {
                _documents = value;
                OnPropertyChanged();
            }
        }

        public Driver_UploadRejected(string driverId, List<string> rejectedDocumentKeys)
        {
            InitializeComponent();
            BindingContext = this;

            _driverId = driverId;
            _rejectedDocumentKeys = rejectedDocumentKeys ?? new List<string>();
            _documents = new ObservableCollection<RejectedDocument>();

            LoadRejectionReason();
            LoadDocuments();
        }

        private async void LoadRejectionReason()
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_driverId);
                if (driver != null && !string.IsNullOrEmpty(driver.RejectionReason))
                {
                    RejectionReasonLabel.Text = driver.RejectionReason;
                }
                else
                {
                    RejectionReasonLabel.Text = "Your documents need to be reuploaded.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading rejection reason: {ex.Message}");
                RejectionReasonLabel.Text = "Please reupload your documents.";
            }
        }

        private async void LoadDocuments()
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_driverId);
                var existingDocuments = driver?.Documents ?? new Dictionary<string, string>();

                _documents.Clear();
                _totalDocuments = _rejectedDocumentKeys.Count;
                _selectedCount = 0;
                _selectedFiles.Clear();

                foreach (var docKey in _rejectedDocumentKeys)
                {
                    bool hasExisting = existingDocuments.ContainsKey(docKey);

                    var document = new RejectedDocument
                    {
                        DocumentKey = docKey,
                        DisplayName = GetDisplayName(docKey),
                        Icon = GetIcon(docKey),
                        IconBackgroundColor = GetIconBackgroundColor(docKey),
                        IsUploaded = false,
                        HasExistingFile = hasExisting,
                        StatusText = "Select file",
                        StatusColor = Color.FromArgb("#EF4444"),
                        UploadStatusText = "PENDING",
                        UploadStatusColor = Color.FromArgb("#EF4444"),
                        Parent = this
                    };

                    document.SelectFileCommand = new Command<RejectedDocument>(async (doc) =>
                    {
                        await SelectFileAsync(doc);
                    });

                    _documents.Add(document);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DocumentsCollectionView.ItemsSource = null;
                    DocumentsCollectionView.ItemsSource = _documents;
                });

                UpdateProgress();
                CheckSubmitAllButton();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load documents: {ex.Message}", "OK");
            }
        }

        private async Task SelectFileAsync(RejectedDocument document)
        {
            try
            {
                var fileResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = $"Select {document.DisplayName}",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.Android, new[] { "image/jpeg", "image/png", "image/jpg", "application/pdf" } },
                        { DevicePlatform.iOS, new[] { "public.image", "com.adobe.pdf" } },
                        { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".pdf" } }
                    })
                });

                if (fileResult == null) return;

                using var stream = await fileResult.OpenReadAsync();
                if (stream.Length > 10 * 1024 * 1024) 
                {
                    await DisplayAlert("Error", "File size must be less than 10MB", "OK");
                    return;
                }

                _selectedFiles[document.DocumentKey] = fileResult;

                var ext = Path.GetExtension(fileResult.FileName).ToLower();
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

                document.SelectedFileName = fileResult.FileName;
                document.HasSelectedFile = true;
                document.IsUploaded = false;

                if (imageExtensions.Contains(ext))
                {
                    var memoryStream = new MemoryStream();
                    stream.Position = 0;
                    await stream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    document.SelectedFilePreview = ImageSource.FromStream(() =>
                    {
                        var ms = new MemoryStream(memoryStream.ToArray());
                        return ms;
                    });
                    document.IsImage = true;
                }
                else
                {
                    document.FileIcon = GetFileIcon(fileResult.FileName);
                    document.IsImage = false;
                }

                document.StatusText = "Ready to upload";
                document.StatusColor = Color.FromArgb("#FF9800");

                _selectedCount = _selectedFiles.Count;
                UpdateProgress();
                CheckSubmitAllButton();

                await DisplayAlert("File Selected",
                    $"{document.DisplayName} is ready.\n\nClick SUBMIT ALL when you've selected all files.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to select file: {ex.Message}", "OK");
            }
        }

        private async void OnSubmitAllClicked(object sender, EventArgs e)
        {
            if (_selectedFiles.Count != _totalDocuments)
            {
                await DisplayAlert("Incomplete",
                    $"Please select all {_totalDocuments} documents first.\n\nSelected: {_selectedFiles.Count} of {_totalDocuments}",
                    "OK");
                return;
            }

            try
            {
                SubmitAllButton.IsEnabled = false;
                SubmitAllButton.Text = "UPLOADING...";
                LoadingOverlay.IsVisible = true;
                UploadStatusLabel.Text = "Uploading to storage...";

                Console.WriteLine($"\n🚀 STARTING REUPLOAD FOR DRIVER: {_driverId}");
                Console.WriteLine($"Documents to upload: {_selectedFiles.Count}");

                var uploadedUrls = await _storageService.UploadReuploadedDocumentsAsync(
                    _driverId,
                    _selectedFiles
                );

                if (uploadedUrls == null || uploadedUrls.Count == 0)
                    throw new Exception("Failed to upload files to storage");

                Console.WriteLine($"✅ All files uploaded to storage. URLs: {uploadedUrls.Count}");

                UploadStatusLabel.Text = "Saving to database (ReuploadDocuments)...";

                foreach (var url in uploadedUrls)
                {
                    string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    string uniqueKey = $"{url.Key}_{timestamp}";

                    await _firebaseConnection.SaveReuploadedDocumentAsync(
                        _driverId,
                        uniqueKey,  
                        url.Value
                    );
                    Console.WriteLine($"✅ Saved to ReuploadDocuments: {url.Key}");
                }

                UploadStatusLabel.Text = "Updating driver record...";

                await _firebaseConnection.UpdateMultipleRejectedDocumentsAsync(
                    _driverId,
                    uploadedUrls
                );

                Console.WriteLine($"✅ Updated driver record with new document URLs");
                Console.WriteLine($"✅ DocumentStatus set to Pending Review");

                LoadingOverlay.IsVisible = false;

                await DisplayAlert(
                    "✅ SUCCESS",
                    $"All {uploadedUrls.Count} documents have been uploaded successfully!\n\nThey will be reviewed by admin.",
                    "OK"
                );

                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", $"Upload failed: {ex.Message}", "OK");

                SubmitAllButton.IsEnabled = true;
                SubmitAllButton.Text = "SUBMIT ALL FOR REVIEW";

                Console.WriteLine($"❌ Upload error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void CheckSubmitAllButton()
        {
            bool allSelected = _selectedFiles.Count == _totalDocuments && _totalDocuments > 0;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                SubmitAllButton.IsEnabled = allSelected;
                SubmitAllButton.BackgroundColor = allSelected ?
                    Color.FromArgb("#27AE60") : Color.FromArgb("#B8C3B0");
                SubmitAllButton.TextColor = allSelected ?
                    Colors.White : Color.FromArgb("#666666");
            });
        }

        private void UpdateProgress()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressLabel.Text = $"{_selectedCount} of {_totalDocuments} documents selected";
                int percentage = _totalDocuments > 0 ? (_selectedCount * 100 / _totalDocuments) : 0;
                ProgressPercentageLabel.Text = $"{percentage}%";
                ProgressBar.Progress = _totalDocuments > 0 ? (double)_selectedCount / _totalDocuments : 0;
            });
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            if (_selectedCount > 0 && _selectedCount < _totalDocuments)
            {
                bool confirm = await DisplayAlert(
                    "Incomplete Upload",
                    $"You've selected {_selectedCount} of {_totalDocuments} documents.\n\nAre you sure you want to go back? Your selections will be lost.",
                    "Yes", "No");

                if (!confirm) return;
            }
            else if (_selectedCount == _totalDocuments && _totalDocuments > 0)
            {
                bool confirm = await DisplayAlert(
                    "Ready to Submit",
                    "All documents are selected but not yet uploaded.\n\nGo back without uploading?",
                    "Yes", "No");

                if (!confirm) return;
            }

            await Navigation.PopModalAsync();
        }

        private void OnImageTapped(object sender, EventArgs e)
        {
            if (sender is Image image && image.BindingContext is RejectedDocument document)
            {
                if (document.IsImage && document.SelectedFilePreview != null)
                {
                    FullScreenPreviewImage.Source = document.SelectedFilePreview;
                    FullScreenPreviewGrid.IsVisible = true;
                }
            }
        }

        private void OnCloseFullScreenClicked(object sender, EventArgs e)
        {
            FullScreenPreviewGrid.IsVisible = false;
            FullScreenPreviewImage.Source = null;
        }

        private string GetFileIcon(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".pdf" => "pdf_icon.png",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "image_icon.png",
                ".doc" or ".docx" => "doc_icon.png",
                ".xls" or ".xlsx" => "excel_icon.png",
                _ => "file_icon.png"
            };
        }

        private string GetDisplayName(string docKey)
        {
            return docKey switch
            {
                "2x2_Picture" => "2x2 ID Picture",
                "Cedula" => "Cedula",
                "Barangay_Clearance" => "Barangay Clearance",
                "Driver's_License" => "Driver's License",
                "ORCR" => "OR/CR",
                "Plate_Number" => "Plate Number",
                "GCash_QR_Code" => "GCash QR Code",
                _ => docKey.Replace("_", " ")
            };
        }

        private string GetIcon(string docKey)
        {
            return docKey switch
            {
                "2x2_Picture" => "🖼️",
                "Cedula" => "📄",
                "Barangay_Clearance" => "📋",
                "Driver's_License" => "🪪",
                "ORCR" => "🚗",
                "Plate_Number" => "🔢",
                "GCash_QR_Code" => "📱",
                _ => "📄"
            };
        }

        private Color GetIconBackgroundColor(string docKey)
        {
            return docKey switch
            {
                "2x2_Picture" => Color.FromArgb("#E3F2FD"),
                "Cedula" => Color.FromArgb("#FFF3E0"),
                "Barangay_Clearance" => Color.FromArgb("#E8F5E9"),
                "Driver's_License" => Color.FromArgb("#F3E5F5"),
                "ORCR" => Color.FromArgb("#FFEBEE"),
                "Plate_Number" => Color.FromArgb("#E0F2F1"),
                "GCash_QR_Code" => Color.FromArgb("#FCE4EC"),
                _ => Color.FromArgb("#F5F5F5")
            };
        }

        public new event PropertyChangedEventHandler PropertyChanged;
        protected new void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RejectedDocument : INotifyPropertyChanged
    {
        private bool _isUploaded;
        private string _statusText;
        private Color _statusColor;
        private string _uploadStatusText;
        private Color _uploadStatusColor;
        private string _selectedFileName;
        private bool _hasSelectedFile;
        private ImageSource _selectedFilePreview;
        private bool _isImage;
        private string _fileIcon;

        public string DocumentKey { get; set; }
        public string DisplayName { get; set; }
        public string Icon { get; set; }
        public Color IconBackgroundColor { get; set; }
        public bool HasExistingFile { get; set; }
        public Driver_UploadRejected Parent { get; set; }
        public ICommand SelectFileCommand { get; set; }

        public bool IsUploaded
        {
            get => _isUploaded;
            set
            {
                _isUploaded = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public Color StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                OnPropertyChanged();
            }
        }

        public string UploadStatusText
        {
            get => _uploadStatusText;
            set
            {
                _uploadStatusText = value;
                OnPropertyChanged();
            }
        }

        public Color UploadStatusColor
        {
            get => _uploadStatusColor;
            set
            {
                _uploadStatusColor = value;
                OnPropertyChanged();
            }
        }

        public string SelectedFileName
        {
            get => _selectedFileName;
            set
            {
                _selectedFileName = value;
                OnPropertyChanged();
            }
        }

        public bool HasSelectedFile
        {
            get => _hasSelectedFile;
            set
            {
                _hasSelectedFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewBorderColor));
            }
        }

        public ImageSource SelectedFilePreview
        {
            get => _selectedFilePreview;
            set
            {
                _selectedFilePreview = value;
                OnPropertyChanged();
            }
        }

        public bool IsImage
        {
            get => _isImage;
            set
            {
                _isImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotImage));
            }
        }

        public bool IsNotImage => !_isImage;

        public string FileIcon
        {
            get => _fileIcon ?? "file_icon.png";
            set
            {
                _fileIcon = value;
                OnPropertyChanged();
            }
        }

        public Color PreviewBorderColor => HasSelectedFile ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0");

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}