using Firebase.Database;
using Firebase.Database.Query;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_TricycleProfile : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly FirebaseStorageService _storageService = new FirebaseStorageService();
        private TricycleInfo _currentTricycle;
        private Driver _currentDriver;
        private FirebaseClient _firebaseClient;

        public Driver_TricycleProfile()
        {
            InitializeComponent();
            Console.WriteLine("?? Driver Tricycle Profile initialized");

            _firebaseClient = new FirebaseClient("https://serviceco-37c60-default-rtdb.firebaseio.com/");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            Console.WriteLine("?? Loading tricycle profile data...");
            await LoadTricycleData();
        }

        private async Task LoadTricycleData()
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Session Expired", "Please login again", "OK");
                    await Navigation.PopModalAsync();
                    return;
                }

                string driverId = AppSession.GetDriverId();
                Console.WriteLine($"?? Loading tricycle data for driver: {driverId}");

                SetLoadingState(true);

                _currentDriver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                if (_currentDriver == null)
                {
                    await DisplayAlert("Error", "Driver information not found", "OK");
                    SetLoadingState(false);
                    return;
                }

                UpdateDriverUI(_currentDriver);

                _currentTricycle = await _firebaseConnection.GetTricycleInfoAsync(driverId);

                if (_currentTricycle == null)
                {
                    Console.WriteLine("?? No tricycle info found, creating default");
                    _currentTricycle = await CreateDefaultTricycleInfo(driverId);
                }

                UpdateTricycleUI(_currentTricycle);

                await LoadRequirementsStatus(driverId);

                Console.WriteLine("? Tricycle profile loaded successfully");
                SetLoadingState(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error loading tricycle profile: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load tricycle profile: {ex.Message}", "OK");
                SetLoadingState(false);
            }
        }

        private async Task<TricycleInfo> CreateDefaultTricycleInfo(string driverId)
        {
            var defaultTricycle = new TricycleInfo
            {
                Id = Guid.NewGuid().ToString(),
                DriverId = driverId,
                VehicleType = _currentDriver?.VehicleType ?? "Tricycle",
                Status = "Inactive",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _firebaseConnection.SaveTricycleInfoAsync(defaultTricycle);
            return defaultTricycle;
        }

        private void UpdateDriverUI(Driver driver)
        {
            try
            {
                DriverNameLabel.Text = $"{driver.FirstName} {driver.LastName}".Trim();
                DriverLicenseLabel.Text = !string.IsNullOrEmpty(driver.LicenseNumber)
                    ? driver.LicenseNumber
                    : "Not specified";

                DriverMobileLabel.Text = !string.IsNullOrEmpty(driver.MobileNumber)
                    ? FormatPhoneNumber(driver.MobileNumber)
                    : "Not specified";

                DriverEmailLabel.Text = !string.IsNullOrEmpty(driver.Email)
                    ? driver.Email
                    : "Not specified";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error updating driver UI: {ex.Message}");
            }
        }

        private void UpdateTricycleUI(TricycleInfo tricycle)
        {
            try
            {
                TricycleNameLabel.Text = $"{tricycle.VehicleType} Profile";

                string status = tricycle.Status ?? "Active";
                TricycleStatusLabel.Text = $"Status: {status}";

                StatusIndicator.Color = GetStatusColor(status);

                PlateNumberLabel.Text = !string.IsNullOrEmpty(tricycle.PlateNumber)
                    ? tricycle.PlateNumber.ToUpper()
                    : "Not specified";

                BodyNumberLabel.Text = !string.IsNullOrEmpty(tricycle.BodyNumber)
                    ? tricycle.BodyNumber
                    : "Not specified";

                MakeModelLabel.Text = !string.IsNullOrEmpty(tricycle.MakeModel)
                    ? tricycle.MakeModel
                    : "Not specified";

                YearModelLabel.Text = !string.IsNullOrEmpty(tricycle.YearModel)
                    ? tricycle.YearModel
                    : "Not specified";

                ColorLabel.Text = !string.IsNullOrEmpty(tricycle.Color)
                    ? tricycle.Color
                    : "Not specified";

                EngineNumberLabel.Text = !string.IsNullOrEmpty(tricycle.EngineNumber)
                    ? tricycle.EngineNumber
                    : "Not specified";

                ChassisNumberLabel.Text = !string.IsNullOrEmpty(tricycle.ChassisNumber)
                    ? tricycle.ChassisNumber
                    : "Not specified";

                SeatingCapacityLabel.Text = !string.IsNullOrEmpty(tricycle.SeatingCapacity)
                    ? tricycle.SeatingCapacity
                    : "Not specified";

                VehicleTypeLabel.Text = !string.IsNullOrEmpty(tricycle.VehicleType)
                    ? tricycle.VehicleType
                    : "Not specified";

                if (!string.IsNullOrEmpty(tricycle.VehiclePhotoUrl))
                {
                    TricycleImage.Source = ImageSource.FromUri(new Uri(tricycle.VehiclePhotoUrl));
                }
                else if (_currentDriver?.Documents?.ContainsKey("VEHICLE_PHOTO") == true)
                {
                    string vehiclePhotoUrl = _currentDriver.Documents["VEHICLE_PHOTO"];
                    if (!string.IsNullOrEmpty(vehiclePhotoUrl))
                    {
                        TricycleImage.Source = ImageSource.FromUri(new Uri(vehiclePhotoUrl));
                    }
                }

                Console.WriteLine($"?? UI updated for tricycle: {tricycle.PlateNumber}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error updating UI: {ex.Message}");
            }
        }

        private async Task LoadRequirementsStatus(string driverId)
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);

                RequirementsList.Children.Clear();

                if (driver == null)
                {
                    RequirementsStatusLabel.Text = "Driver information not found";
                    return;
                }

                var requiredDocuments = new List<(string Key, string DisplayName)>
                {
                    ("2X2_PICTURE", "2x2 Picture"),
                    ("CEDULA", "Cedula"),
                    ("BARANGAY_CLEARANCE", "Barangay Clearance"),
                    ("DRIVERS_LICENSE", "Driver's License"),
                    ("OR_CR", "OR/CR"),
                    ("VEHICLE_INSPECTION", "Vehicle Inspection Report"),
                    ("TODA_ID", "TODA ID Membership")
                };

                int uploadedCount = 0;
                int totalCount = requiredDocuments.Count;
                int approvedCount = 0;

                foreach (var doc in requiredDocuments)
                {
                    bool isUploaded = driver.Documents != null &&
                                      driver.Documents.ContainsKey(doc.Key) &&
                                      !string.IsNullOrEmpty(driver.Documents[doc.Key]);

                    bool isApproved = CheckIfDocumentApproved(doc.Key, driver);

                    string status = isApproved ? "? Approved" :
                                   isUploaded ? "?? Uploaded" : "? Not Uploaded";

                    string statusColor = isApproved ? "#2E7D32" :
                                        isUploaded ? "#FF9800" : "#F44336";

                    uploadedCount += isUploaded ? 1 : 0;
                    approvedCount += isApproved ? 1 : 0;

                    AddRequirementItem(doc.DisplayName, status, statusColor);
                }

                double progress = totalCount > 0 ? (double)approvedCount / totalCount : 0;
                RequirementsProgressBar.Progress = progress;
                ProgressPercentageLabel.Text = $"{(int)(progress * 100)}%";
                ProgressGrid.IsVisible = true;

                string driverStatus = driver.Status?.ToUpper() ?? "PENDING";

                switch (driverStatus)
                {
                    case "APPROVED":
                    case "ACTIVE":
                        RequirementsStatusLabel.Text = "? All requirements approved. Account is active!";
                        RequirementsStatusLabel.TextColor = Color.FromArgb("#2E7D32");
                        break;

                    case "PENDING":
                    case "UNDER REVIEW":
                        RequirementsStatusLabel.Text = "?? Application under review. Please wait for approval.";
                        RequirementsStatusLabel.TextColor = Color.FromArgb("#FF9800");
                        break;

                    case "REJECTED":
                        RequirementsStatusLabel.Text = "? Application rejected. Please contact support.";
                        RequirementsStatusLabel.TextColor = Color.FromArgb("#F44336");
                        break;

                    case "DOCUMENTS PENDING":
                        RequirementsStatusLabel.Text = uploadedCount > 0
                            ? $"{uploadedCount}/{totalCount} documents uploaded. Waiting for approval."
                            : "Please upload all required documents.";
                        RequirementsStatusLabel.TextColor = Color.FromArgb("#FF9800");
                        break;

                    default:
                        RequirementsStatusLabel.Text = uploadedCount > 0
                            ? $"{uploadedCount}/{totalCount} documents uploaded."
                            : "No documents uploaded yet.";
                        RequirementsStatusLabel.TextColor = Color.FromArgb("#666666");
                        break;
                }

                Console.WriteLine($"?? Requirements status: {uploadedCount}/{totalCount} uploaded, {approvedCount} approved");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"?? Error loading requirements: {ex.Message}");
                RequirementsStatusLabel.Text = "Error loading requirements status";
                RequirementsStatusLabel.TextColor = Color.FromArgb("#F44336");
            }
        }

        private bool CheckIfDocumentApproved(string documentKey, Driver driver)
        {
            string driverStatus = driver.Status?.ToUpper() ?? "PENDING";

            if (driverStatus == "APPROVED" || driverStatus == "ACTIVE")
            {
                return driver.Documents != null &&
                       driver.Documents.ContainsKey(documentKey) &&
                       !string.IsNullOrEmpty(driver.Documents[documentKey]);
            }

            return false;
        }

        private void AddRequirementItem(string displayName, string status, string colorHex)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 10
            };

            var nameLabel = new Label
            {
                Text = displayName,
                FontSize = 14,
                TextColor = Colors.Black,
                VerticalOptions = LayoutOptions.Center
            };

            var statusLabel = new Label
            {
                Text = status,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                TextColor = Color.FromArgb(colorHex)
            };

            grid.Add(nameLabel, 0, 0);
            grid.Add(statusLabel, 1, 0);

            RequirementsList.Children.Add(grid);
        }

        private Color GetStatusColor(string status)
        {
            return status.ToUpper() switch
            {
                "ACTIVE" => Color.FromArgb("#4CAF50"),
                "APPROVED" => Color.FromArgb("#4CAF50"),
                "PENDING" => Color.FromArgb("#FF9800"),
                "UNDER REVIEW" => Color.FromArgb("#FF9800"),
                "INACTIVE" => Color.FromArgb("#F44336"),
                "REJECTED" => Color.FromArgb("#F44336"),
                "SUSPENDED" => Color.FromArgb("#F44336"),
                "DOCUMENTS PENDING" => Color.FromArgb("#FF9800"),
                _ => Color.FromArgb("#666666")
            };
        }

        private string FormatPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return "Not available";

            phoneNumber = phoneNumber.Trim();

            if (phoneNumber.StartsWith("+63") && phoneNumber.Length == 13)
            {
                return $"+63 {phoneNumber.Substring(3, 3)} {phoneNumber.Substring(6, 3)} {phoneNumber.Substring(9, 4)}";
            }
            else if (phoneNumber.StartsWith("09") && phoneNumber.Length == 11)
            {
                return $"+63 {phoneNumber.Substring(1, 3)} {phoneNumber.Substring(4, 3)} {phoneNumber.Substring(7, 4)}";
            }

            return phoneNumber;
        }

        private void SetLoadingState(bool isLoading)
        {
            EditTricycleButton.IsEnabled = !isLoading;
            ViewDocumentsButton.IsEnabled = !isLoading;
            ViewRequirementsButton.IsEnabled = !isLoading;
            RefreshButton.IsEnabled = !isLoading;

            if (isLoading)
            {
                RequirementsStatusLabel.Text = "Loading requirements...";
            }
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? Back button clicked");
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error going back: {ex.Message}");
                await DisplayAlert("Error", "Cannot go back: " + ex.Message, "OK");
            }
        }

        private async void OnRefreshButtonClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? Refresh button clicked");
                await LoadTricycleData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error refreshing: {ex.Message}");
            }
        }

        private async void OnViewRequirementsClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? View Requirements clicked");
                await Navigation.PushModalAsync(new Driver_Requirements());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error opening requirements: {ex.Message}");
                await DisplayAlert("Error", "Cannot open requirements: " + ex.Message, "OK");
            }
        }

        private async void OnEditTricycleClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? Edit Trycycle Information clicked");

                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Error", "Please login to continue", "OK");
                    return;
                }

                if (_currentTricycle == null)
                {
                    await DisplayAlert("Error", "Tricycle information not found", "OK");
                    return;
                }

                await Navigation.PushModalAsync(new EditTricyclePage(_currentTricycle));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error opening edit page: {ex.Message}");
                await DisplayAlert("Error", $"Cannot edit tricycle info: {ex.Message}", "OK");
            }
        }

        private async void OnViewDocumentsClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? View Documents clicked");

                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Error", "Please login to continue", "OK");
                    return;
                }

                var driverId = AppSession.GetDriverId();
                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);

                if (driver?.Documents == null || driver.Documents.Count == 0)
                {
                    await DisplayAlert("No Documents", "You haven't uploaded any documents yet.", "OK");
                    return;
                }

                await ShowDocumentsWithStatus(driver.Documents, driver.Status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error viewing documents: {ex.Message}");
                await DisplayAlert("Error", "Cannot view documents: " + ex.Message, "OK");
            }
        }

        private async Task ShowDocumentsWithStatus(Dictionary<string, string> documents, string driverStatus)
        {
            try
            {
                string message = "?? Uploaded Documents\n\n";
                int count = 1;

                foreach (var doc in documents)
                {
                    string docName = GetDocumentDisplayName(doc.Key);
                    string status = string.IsNullOrEmpty(doc.Value) ? "? Not Uploaded" :
                                   (driverStatus == "Approved" || driverStatus == "Active") ? "? Approved" : "?? Uploaded";

                    message += $"{count}. {docName}: {status}\n";
                    count++;
                }

                message += $"\n?? Status: {driverStatus}";

                await DisplayAlert("Your Documents", message, "OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error showing document list: {ex.Message}");
                await DisplayAlert("Error", "Cannot display documents", "OK");
            }
        }

        private string GetDocumentDisplayName(string documentKey)
        {
            return documentKey.ToUpper() switch
            {
                "2X2_PICTURE" => "2x2 Picture",
                "CEDULA" => "Cedula",
                "BARANGAY_CLEARANCE" => "Barangay Clearance",
                "DRIVERS_LICENSE" => "Driver's License",
                "OR_CR" => "OR/CR Document",
                "VEHICLE_INSPECTION" => "Vehicle Inspection Report",
                "TODA_ID" => "TODA ID Membership",
                "VEHICLE_PHOTO" => "Vehicle Photo",
                "PROFILE_PHOTO" => "Profile Photo",
                _ => documentKey.Replace("_", " ")
            };
        }

        private async void OnChangePhotoClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("?? Change Vehicle Photo clicked");

                var result = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Select Vehicle Photo"
                });

                if (result != null)
                {
                    await UploadVehiclePhoto(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error changing photo: {ex.Message}");
                await DisplayAlert("Error", "Failed to change vehicle photo: " + ex.Message, "OK");
            }
        }

        private async Task UploadVehiclePhoto(FileResult photo)
        {
            try
            {
                if (!AppSession.IsDriverLoggedIn())
                {
                    await DisplayAlert("Error", "Please login to continue", "OK");
                    return;
                }

                string driverId = AppSession.GetDriverId();

                await DisplayAlert("Uploading", "Uploading vehicle photo...", "OK");

                var downloadUrl = await _storageService.UploadProfilePictureAsync(driverId, photo, "driver");

                _currentTricycle.VehiclePhotoUrl = downloadUrl;
                _currentTricycle.UpdatedAt = DateTime.UtcNow;

                await _firebaseConnection.SaveTricycleInfoAsync(_currentTricycle);

                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                if (driver != null)
                {
                    if (driver.Documents == null)
                        driver.Documents = new Dictionary<string, string>();

                    driver.Documents["VEHICLE_PHOTO"] = downloadUrl;

                    await _firebaseClient
                        .Child("Drivers")
                        .Child(driverId)
                        .PatchAsync(new { Documents = driver.Documents });
                }

                TricycleImage.Source = ImageSource.FromUri(new Uri(downloadUrl));

                await DisplayAlert("Success", "Vehicle photo updated successfully!", "OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error uploading vehicle photo: {ex.Message}");
                await DisplayAlert("Error", "Failed to upload vehicle photo: " + ex.Message, "OK");
            }
        }
    }
}