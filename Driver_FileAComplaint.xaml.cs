using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_FileAComplaint : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly FirebaseStorageService _firebaseStorage = new FirebaseStorageService();

        private string _rideId;
        private string _rideFirebaseKey;
        private string _passengerId;
        private string _driverId;
        private string _driverName;
        private string _driverPhone;

        private List<FileResult> _selectedFiles = new List<FileResult>();
        private const int MAX_FILES = 5;

        public Driver_FileAComplaint(
            string accusedName = "",
            string accusedId = "",
            string accusedPhone = "",
            string rideId = "",
            string rideFirebaseKey = "",
            string complainantType = "driver")
        {
            InitializeComponent();

            _rideId = rideId; 
            _rideFirebaseKey = rideFirebaseKey;
            _passengerId = accusedId;

            _driverId = AppSession.GetDriverId();
            _driverName = AppSession.GetDriverName();
            _driverPhone = AppSession.GetDriverMobileNumber();

            Console.WriteLine($"📝 Complaint by driver: {_driverName} ({_driverId})");
            Console.WriteLine($"📋 For Ride ID: {_rideId} (FirebaseKey: {_rideFirebaseKey})");
            Console.WriteLine($"👤 Against Passenger: {accusedName} (ID: {accusedId})");

            if (!string.IsNullOrEmpty(accusedName))
                PassengerNameEntry.Text = accusedName;

            if (!string.IsNullOrEmpty(accusedPhone))
                PassengerPhoneEntry.Text = accusedPhone;

            if (!string.IsNullOrEmpty(accusedId))
                PassengerIdLabel.Text = accusedId;

            if (!string.IsNullOrEmpty(rideId))
                RideIdLabel.Text = rideId;

            CategoryValidationLabel.IsVisible = false;
            ComplaintValidationLabel.IsVisible = false;

            this.Loaded += (s, e) =>
            {
                // Focus on nothing
            };
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private async void OnAddFileClicked(object sender, EventArgs e)
        {
            if (_selectedFiles.Count >= MAX_FILES)
            {
                FileLimitWarning.Text = "Maximum 5 files only";
                FileLimitWarning.IsVisible = true;
                await Task.Delay(3000);
                FileLimitWarning.IsVisible = false;
                return;
            }

            try
            {
                var options = new PickOptions
                {
                    PickerTitle = "Select Evidence File",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.Android, new[] { "image/jpeg", "image/jpg", "image/png", "application/pdf" } },
                        { DevicePlatform.iOS, new[] { "public.image", "public.jpeg", "public.png", "com.adobe.pdf" } },
                        { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".pdf" } },
                        { DevicePlatform.macOS, new[] { "jpg", "jpeg", "png", "pdf" } },
                    })
                };

                var fileResult = await FilePicker.Default.PickAsync(options);

                if (fileResult != null)
                {
                    using var stream = await fileResult.OpenReadAsync();
                    if (stream.Length > 10 * 1024 * 1024)
                    {
                        FileLimitWarning.Text = "File size exceeds 10MB limit";
                        FileLimitWarning.IsVisible = true;
                        await Task.Delay(3000);
                        FileLimitWarning.IsVisible = false;
                        return;
                    }

                    _selectedFiles.Add(fileResult);
                    UpdateFileList();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unable to select file: {ex.Message}", "OK");
            }
        }

        private void UpdateFileList()
        {
            FilesStackLayout.Children.Clear();

            for (int i = 0; i < _selectedFiles.Count; i++)
            {
                var file = _selectedFiles[i];
                var fileName = Path.GetFileName(file.FileName);
                var fileNumber = i + 1;

                var fileFrame = new Frame
                {
                    BackgroundColor = Color.FromArgb("#F8F9FA"),
                    BorderColor = Color.FromArgb("#E0E0E0"),
                    CornerRadius = 8,
                    Padding = new Thickness(12, 8),
                    HeightRequest = 60,
                    HasShadow = false
                };

                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = 35 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 90 },
                new ColumnDefinition { Width = 45 }
            },
                    ColumnSpacing = 10,
                    VerticalOptions = LayoutOptions.Center
                };

                var numberLabel = new Label
                {
                    Text = $"{fileNumber}.",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#333333"),
                    VerticalOptions = LayoutOptions.Center
                };
                grid.Add(numberLabel, 0);

                var nameLabel = new Label
                {
                    Text = fileName.Length > 15 ? fileName.Substring(0, 12) + "..." : fileName,
                    FontSize = 13,
                    TextColor = Color.FromArgb("#333333"),
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                grid.Add(nameLabel, 1);

                var previewButton = new Button
                {
                    Text = "Preview",
                    BackgroundColor = Color.FromArgb("#2E7D32"),
                    TextColor = Colors.White,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 10,
                    HeightRequest = 40,
                    WidthRequest = 85,
                    VerticalOptions = LayoutOptions.Center
                };

                var fileIndex = i;
                previewButton.Clicked += async (s, e) => await OnPreviewFileClicked(file, fileIndex);
                grid.Add(previewButton, 2);

                var deleteButton = new ImageButton
                {
                    Source = "cancelled.png",
                    HeightRequest = 32,
                    WidthRequest = 32,
                    BackgroundColor = Colors.Transparent,
                    VerticalOptions = LayoutOptions.Center
                };

                deleteButton.Clicked += (s, e) => RemoveFile(fileIndex);
                grid.Add(deleteButton, 3);

                fileFrame.Content = grid;
                FilesStackLayout.Children.Add(fileFrame);
            }

            FileLimitWarning.IsVisible = _selectedFiles.Count >= MAX_FILES;
        }

        private async Task OnPreviewFileClicked(FileResult file, int index)
        {
            try
            {
                var extension = Path.GetExtension(file.FileName).ToLower();
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

                if (imageExtensions.Contains(extension))
                {
                    LoadingOverlay.IsVisible = true;

                    using var stream = await file.OpenReadAsync();
                    var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    FullScreenPreviewImage.Source = ImageSource.FromStream(() => memoryStream);

                    LoadingOverlay.IsVisible = false;
                    FullScreenPreviewGrid.IsVisible = true;
                }
                else
                {
                    FileLimitWarning.Text = "Preview not available for this file type";
                    FileLimitWarning.IsVisible = true;
                    await Task.Delay(2000);
                    FileLimitWarning.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", $"Failed to preview: {ex.Message}", "OK");
            }
        }

        private void OnCloseFullScreenClicked(object sender, EventArgs e)
        {
            FullScreenPreviewGrid.IsVisible = false;
            FullScreenPreviewImage.Source = null;
        }

        private void RemoveFile(int index)
        {
            if (index >= 0 && index < _selectedFiles.Count)
            {
                _selectedFiles.RemoveAt(index);
                UpdateFileList();
            }
        }

        private async void SubmitButton_Clicked(object sender, EventArgs e)
        {
            string passengerName = PassengerNameEntry.Text;
            string complaint = ComplaintEditor.Text;
            string category = CategoryPicker.SelectedItem?.ToString();

            CategoryValidationLabel.IsVisible = false;
            ComplaintValidationLabel.IsVisible = false;

            if (string.IsNullOrWhiteSpace(passengerName))
            {
                await DisplayAlert("Missing", "Please enter passenger's name.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                CategoryValidationLabel.Text = "Please select a category";
                CategoryValidationLabel.IsVisible = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(complaint))
            {
                ComplaintValidationLabel.Text = "Please write your complaint";
                ComplaintValidationLabel.IsVisible = true;
                return;
            }

            SubmitButton.IsEnabled = false;
            SubmitButton.Text = "Submitting...";
            LoadingOverlay.IsVisible = true;

            try
            {
                var attachmentUrls = new List<string>();

                if (_selectedFiles.Any())
                {
                    int uploaded = 0;
                    foreach (var file in _selectedFiles)
                    {
                        try
                        {
                            var url = await _firebaseStorage.UploadImageAsync(
                                _driverId,
                                $"complaint_evidence_{DateTime.UtcNow.Ticks}_{uploaded}",
                                file);

                            if (!string.IsNullOrEmpty(url))
                            {
                                attachmentUrls.Add(url);
                                uploaded++;
                            }
                        }
                        catch (Exception fileEx)
                        {
                            Console.WriteLine($"Failed to upload file: {fileEx.Message}");
                        }
                    }
                }

                var complaintObj = new Complaint
                {
                    ComplainantId = _driverId,
                    ComplainantName = _driverName,
                    ComplainantType = "driver",
                    ComplainantPhone = _driverPhone,

                    AccusedId = _passengerId,
                    AccusedName = passengerName,
                    AccusedType = "commuter",
                    AccusedPhone = PassengerPhoneEntry.Text ?? "",
                    VehicleNumber = "",

                    RideId = _rideFirebaseKey, 
                    RideDisplayId = _rideId,   
                    Category = category,
                    Description = complaint,
                    AttachmentUrls = attachmentUrls,

                    Status = "Pending",
                    DateSubmitted = DateTime.UtcNow
                };

                string complaintId = await _firebaseConnection.SaveComplaintAsync(complaintObj);

                LoadingOverlay.IsVisible = false;

                string fileMessage = attachmentUrls.Count > 0 ? $"{attachmentUrls.Count} file(s) attached" : "No files attached";

                SuccessTitleLabel.Text = "Complaint Submitted!";
                SuccessMessageLabel.Text = "Your complaint against passenger has been received.";
                SuccessDetailsLabel.Text = $"ID: {complaintId.Substring(0, 8)}...\nRide: {_rideId}\nCategory: {category}\n{fileMessage}";

                SuccessModalGrid.IsVisible = true;
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", $"Failed: {ex.Message}", "OK");
                SubmitButton.IsEnabled = true;
                SubmitButton.Text = "Submit Complaint";
            }
        }

        private async void OnSuccessModalOkClicked(object sender, EventArgs e)
        {
            SuccessModalGrid.IsVisible = false;
            await Navigation.PopModalAsync();
        }
    }
}