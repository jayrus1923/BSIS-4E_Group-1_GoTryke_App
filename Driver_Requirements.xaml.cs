using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_Requirements : ContentPage
    {
        private FirebaseConnection _firebaseConnection;
        private string _driverId;
        private Dictionary<string, string> _documentUrls;
        private string _currentDocumentKey;
        private string _currentDocumentUrl;
        private double _currentScale = 1;
        private double _startScale = 1;
        private double _xOffset = 0;
        private double _yOffset = 0;
        private bool _isZoomed = false;

        public Driver_Requirements()
        {
            InitializeComponent();
            _firebaseConnection = new FirebaseConnection();
            _driverId = Preferences.Get("DriverId", "");
            _documentUrls = new Dictionary<string, string>();

            InitializeTapGestures();
            this.Appearing += OnPageAppearing;
        }

        private void InitializeTapGestures()
        {
            Picture2x2Grid.GestureRecognizers.Clear();
            Picture2x2Grid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("2x2_Picture"))
            });

            CedulaGrid.GestureRecognizers.Clear();
            CedulaGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("Cedula"))
            });

            BarangayClearanceGrid.GestureRecognizers.Clear();
            BarangayClearanceGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("Barangay_Clearance"))
            });

            DriversLicenseGrid.GestureRecognizers.Clear();
            DriversLicenseGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("Driver's_License"))
            });

            ORCRGrid.GestureRecognizers.Clear();
            ORCRGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("ORCR"))
            });

            PlateNumberGrid.GestureRecognizers.Clear();
            PlateNumberGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("Plate_Number"))
            });

            GcashQRGrid.GestureRecognizers.Clear();
            GcashQRGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnDocumentTapped("GCash_QR_Code"))
            });
        }

        private async void OnPageAppearing(object sender, EventArgs e)
        {
            await LoadDriverRequirements();
            SetupRealtimeListener();
        }

        private async Task LoadDriverRequirements()
        {
            if (string.IsNullOrEmpty(_driverId))
            {
                ShowError("Driver information not found. Please login again.");
                return;
            }

            try
            {
                ShowLoading(true);
                var driver = await _firebaseConnection.GetDriverByIdAsync(_driverId);

                if (driver != null)
                {
                    if (driver.Documents != null)
                    {
                        _documentUrls = new Dictionary<string, string>(driver.Documents);
                    }
                    UpdateUIWithDriverData(driver);
                }
                else
                {
                    ShowError("Unable to load your requirements. Please try again.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Error loading data: {ex.Message}");
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void UpdateUIWithDriverData(Driver driver)
        {
            UpdateStatusMessages(driver.DocumentStatus, driver.RejectionReason);
            UpdateDocumentStatus("2x2_Picture", driver.Documents, driver.DocumentStatus);
            UpdateDocumentStatus("Cedula", driver.Documents, driver.DocumentStatus);
            UpdateDocumentStatus("Barangay_Clearance", driver.Documents, driver.DocumentStatus);
            UpdateDocumentStatus("Driver's_License", driver.Documents, driver.DocumentStatus);
            UpdateDocumentStatus("ORCR", driver.Documents, driver.DocumentStatus);
            UpdateDocumentStatus("Plate_Number", driver.Documents, driver.DocumentStatus);
            UpdateDocumentStatus("GCash_QR_Code", driver.Documents, driver.DocumentStatus);
        }

        private void UpdateStatusMessages(string documentStatus, string rejectionReason)
        {
            switch (documentStatus?.ToLower())
            {
                case "approved":
                    StatusTitleLabel.Text = "Congratulations!";
                    StatusTitleLabel.TextColor = Color.FromArgb("#2E7D32");
                    StatusMessageLabel.Text = "Your account is approved. All requirements verified — you can now go active to receive ride requests.";
                    StatusMessageLabel.TextColor = Color.FromArgb("#666666");
                    RejectionFrame.IsVisible = false;
                    break;

                case "rejected":
                    StatusTitleLabel.Text = "Documents Rejected";
                    StatusTitleLabel.TextColor = Color.FromArgb("#D32F2F");
                    StatusMessageLabel.Text = "Some of your documents were rejected. Please check the rejection reason below and upload new documents.";
                    StatusMessageLabel.TextColor = Color.FromArgb("#666666");

                    if (!string.IsNullOrEmpty(rejectionReason))
                    {
                        RejectionReasonLabel.Text = rejectionReason;
                        RejectionReasonLabel.TextColor = Color.FromArgb("#D32F2F");
                        RejectionFrame.IsVisible = true;
                    }
                    else
                    {
                        RejectionFrame.IsVisible = false;
                    }
                    break;

                default:
                    StatusTitleLabel.Text = "Under Review";
                    StatusTitleLabel.TextColor = Color.FromArgb("#FFA000");
                    StatusMessageLabel.Text = "Your documents are currently being reviewed. This usually takes 1-2 business days. You'll be notified once verified.";
                    StatusMessageLabel.TextColor = Color.FromArgb("#666666");
                    RejectionFrame.IsVisible = false;
                    break;
            }
        }

        private void UpdateDocumentStatus(string documentKey, Dictionary<string, string> documents, string overallStatus)
        {
            Label statusLabel = GetStatusLabel(documentKey);
            if (statusLabel == null) return;

            bool hasDocument = documents != null && documents.ContainsKey(documentKey) && !string.IsNullOrEmpty(documents[documentKey]);

            if (!hasDocument)
            {
                statusLabel.Text = "Not Uploaded";
                statusLabel.TextColor = Color.FromArgb("#9E9E9E");
            }
            else
            {
                switch (overallStatus?.ToLower())
                {
                    case "approved":
                        statusLabel.Text = "Approved";
                        statusLabel.TextColor = Color.FromArgb("#2E7D32");
                        break;

                    case "rejected":
                        statusLabel.Text = "Rejected";
                        statusLabel.TextColor = Color.FromArgb("#D32F2F");
                        break;

                    default:
                        statusLabel.Text = "Pending";
                        statusLabel.TextColor = Color.FromArgb("#FFA000");
                        break;
                }
            }
        }

        private Label GetStatusLabel(string documentKey)
        {
            return documentKey switch
            {
                "2x2_Picture" => Picture2x2Status,
                "Cedula" => CedulaStatus,
                "Barangay_Clearance" => BarangayClearanceStatus,
                "Driver's_License" => DriversLicenseStatus,
                "ORCR" => ORCRStatus,
                "Plate_Number" => PlateNumberStatus,
                "GCash_QR_Code" => GcashQRStatus,
                _ => null
            };
        }

        private async Task OnDocumentTapped(string documentKey)
        {
            try
            {
                if (_documentUrls == null || !_documentUrls.ContainsKey(documentKey))
                {
                    await ReloadDocuments();
                    if (_documentUrls == null || !_documentUrls.ContainsKey(documentKey))
                    {
                        await DisplayAlert("Not Found", $"No {documentKey} document found.", "OK");
                        return;
                    }
                }

                string imageUrl = _documentUrls[documentKey];

                if (string.IsNullOrEmpty(imageUrl))
                {
                    await DisplayAlert("No Image", $"No image available for {documentKey}.", "OK");
                    return;
                }

                _currentDocumentKey = documentKey;
                _currentDocumentUrl = imageUrl;

                var statusLabel = GetStatusLabel(documentKey);
                if (statusLabel != null && statusLabel.Text == "Rejected")
                {
                    ReuploadButton.IsVisible = true;
                    NoActionLabel.IsVisible = false;
                }
                else
                {
                    ReuploadButton.IsVisible = false;
                    NoActionLabel.IsVisible = true;
                }

                PopupTitleLabel.Text = documentKey.Replace("_", " ");
                await ShowImagePopup(imageUrl);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to open document: {ex.Message}", "OK");
            }
        }

        private async Task ReloadDocuments()
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_driverId);
                if (driver?.Documents != null)
                {
                    _documentUrls = new Dictionary<string, string>(driver.Documents);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reloading documents: {ex.Message}");
            }
        }

        private async Task ShowImagePopup(string imageUrl)
        {
            try
            {
                ResetImageZoom();
                PopupLoadingIndicator.IsRunning = true;
                PopupLoadingIndicator.IsVisible = true;
                PopupImage.Source = null;

                if (!imageUrl.Contains("?alt=media"))
                {
                    imageUrl += "?alt=media";
                }

                PopupImage.Source = ImageSource.FromUri(new Uri(imageUrl));
                await Task.Delay(500);

                PopupLoadingIndicator.IsRunning = false;
                PopupLoadingIndicator.IsVisible = false;
                ImagePopupOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Failed to load image", "OK");
                PopupLoadingIndicator.IsRunning = false;
                PopupLoadingIndicator.IsVisible = false;
            }
        }

        private void OnClosePopupClicked(object sender, EventArgs e)
        {
            ClosePopup();
        }

        private void OnPopupOverlayTapped(object sender, EventArgs e)
        {
            if (!_isZoomed)
            {
                ClosePopup();
            }
        }

        private void ClosePopup()
        {
            ImagePopupOverlay.IsVisible = false;
            PopupImage.Source = null;
            ResetImageZoom();
        }

        private void OnImagePinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
        {
            if (e.Status == GestureStatus.Started)
            {
                _startScale = _currentScale;
                _isZoomed = true;
            }

            if (e.Status == GestureStatus.Running)
            {
                _currentScale = Math.Max(1, Math.Min(5, _startScale * e.Scale));
                PopupImage.Scale = _currentScale;
                PopupImageScrollView.Orientation = _currentScale > 1 ? ScrollOrientation.Both : ScrollOrientation.Neither;
            }

            if (e.Status == GestureStatus.Completed)
            {
                if (_currentScale <= 1)
                {
                    ResetImageZoom();
                }
            }
        }

        private void OnImagePanUpdated(object sender, PanUpdatedEventArgs e)
        {
            if (_currentScale <= 1) return;

            switch (e.StatusType)
            {
                case GestureStatus.Running:
                    _xOffset = e.TotalX;
                    _yOffset = e.TotalY;

                    var maxX = (PopupImage.Width * (_currentScale - 1)) / 2;
                    var maxY = (PopupImage.Height * (_currentScale - 1)) / 2;

                    _xOffset = Math.Max(-maxX, Math.Min(maxX, _xOffset));
                    _yOffset = Math.Max(-maxY, Math.Min(maxY, _yOffset));

                    PopupImage.TranslationX = _xOffset;
                    PopupImage.TranslationY = _yOffset;
                    break;
            }
        }

        private void OnImageDoubleTapped(object sender, EventArgs e)
        {
            if (_currentScale > 1)
            {
                ResetImageZoom();
            }
            else
            {
                _currentScale = 2;
                PopupImage.Scale = _currentScale;
                PopupImageScrollView.Orientation = ScrollOrientation.Both;
                _isZoomed = true;
            }
        }

        private void ResetImageZoom()
        {
            _currentScale = 1;
            _startScale = 1;
            _xOffset = 0;
            _yOffset = 0;
            _isZoomed = false;

            PopupImage.Scale = 1;
            PopupImage.TranslationX = 0;
            PopupImage.TranslationY = 0;
            PopupImageScrollView.Orientation = ScrollOrientation.Neither;
        }

        private async void OnReuploadClicked(object sender, EventArgs e)
        {
            ImagePopupOverlay.IsVisible = false;
            bool confirm = await DisplayAlert("Reupload Document",
                $"Are you sure you want to reupload your {_currentDocumentKey?.Replace("_", " ")}?",
                "Yes", "No");

            if (confirm)
            {
                await DisplayAlert("Info", "Please go to the document upload section to reupload your document.", "OK");
            }
        }

        private void SetupRealtimeListener()
        {
            try
            {
                var listener = _firebaseConnection.ListenForDriverDocumentStatus(_driverId,
                    async (documentStatus, registrationCompleted, rejectionReason) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            UpdateStatusMessages(documentStatus, rejectionReason);
                            await ReloadDocuments();

                            if (!string.IsNullOrEmpty(documentStatus))
                            {
                                UpdateDocumentStatus("2x2_Picture", _documentUrls, documentStatus);
                                UpdateDocumentStatus("Cedula", _documentUrls, documentStatus);
                                UpdateDocumentStatus("Barangay_Clearance", _documentUrls, documentStatus);
                                UpdateDocumentStatus("Driver's_License", _documentUrls, documentStatus);
                                UpdateDocumentStatus("ORCR", _documentUrls, documentStatus);
                                UpdateDocumentStatus("Plate_Number", _documentUrls, documentStatus);
                                UpdateDocumentStatus("GCash_QR_Code", _documentUrls, documentStatus);
                            }
                        });
                    });

                if (listener != null)
                {
                    this.Disappearing += (s, e) => listener.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting up listener: {ex.Message}");
            }
        }

        private void ShowLoading(bool show)
        {
            LoadingLayout.IsVisible = show;
            RequirementsList.IsVisible = !show;
            ErrorLabel.IsVisible = false;
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorLabel.TextColor = Color.FromArgb("#D32F2F");
            ErrorLabel.IsVisible = true;
            LoadingLayout.IsVisible = false;
            RequirementsList.IsVisible = false;
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}