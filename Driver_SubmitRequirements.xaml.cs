using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_SubmitRequirements : ContentPage
    {
        private int uploadedCount = 0;
        private readonly int totalDocuments = 7;
        private readonly FirebaseStorageService _storageService;
        private readonly FirebaseConnection _firebaseConnection;
        private readonly Firebase.DriverData _driverData;
        private readonly string _pin;

        private Dictionary<string, FileResult> _uploadedDocuments = new Dictionary<string, FileResult>();
        private Dictionary<string, string> _documentUrls = new Dictionary<string, string>();
        private string _driverId = "";
        private bool _isRegistrationSuccessful = false;
        private bool _isModalOpen = false;

        public Driver_SubmitRequirements(Firebase.DriverData driverData, string pin = null)
        {
            InitializeComponent();

            progressLabel.Text = "0/7";
            progressDesc.Text = "0 out of 7 documents uploaded";

            _driverData = driverData;
            _pin = pin;

            if (string.IsNullOrEmpty(_driverData.VehicleType))
            {
                _driverData.VehicleType = "Tricycle";
            }

            _storageService = new FirebaseStorageService();
            _firebaseConnection = new FirebaseConnection();

            UpdateProgress();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
            NavigationPage.SetHasNavigationBar(this, false);

            if (Parent is NavigationPage navPage)
            {
                navPage.BarBackgroundColor = Colors.Transparent;
                navPage.BarTextColor = Colors.Transparent;
            }
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await ShowExitConfirmation();
            });
            return true;
        }

        private async Task ShowExitConfirmation()
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = "complaint.png";
            Color iconColor = Color.FromArgb("#DC3545");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 20,
                Padding = 25,
                Margin = 40,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.7,
                TranslationY = 0,
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image
                        {
                            Source = iconSource,
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Exit Registration?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to go back? Your progress will be lost.",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitionCollection
                            {
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                            },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button
                                {
                                    Text = "CANCEL",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverSubmitColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverSubmitColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new DriverSubmitRequirementsExitModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(exitModal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public void CloseModal(bool closePage)
        {
            _isModalOpen = false;
            if (closePage)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Navigation.PopModalAsync();
                });
            }
        }

        private async void OnUpload2x2Clicked(object sender, EventArgs e) =>
            await UploadDocument("2x2 Picture", Photo2x2Label, Photo2x2Button);
        private async void OnUploadCedulaClicked(object sender, EventArgs e) =>
            await UploadDocument("Cedula", CedulaLabel, CedulaButton);
        private async void OnUploadClearanceClicked(object sender, EventArgs e) =>
            await UploadDocument("Barangay Clearance", ClearanceLabel, ClearanceButton);
        private async void OnUploadLicenseClicked(object sender, EventArgs e) =>
            await UploadDocument("Driver's License", LicenseLabel, LicenseButton);
        private async void OnUploadORCRClicked(object sender, EventArgs e) =>
            await UploadDocument("ORCR", ORCRLabel, ORCRButton);
        private async void OnUploadPlateNumberClicked(object sender, EventArgs e) =>
            await UploadDocument("Plate Number", PlateNumberLabel, PlateNumberButton);
        private async void OnUploadGcashClicked(object sender, EventArgs e) =>
            await UploadDocument("GCash QR Code", GcashLabel, GcashButton);

        private async Task UploadDocument(string documentType, Label statusLabel, Button uploadButton)
        {
            try
            {
                if (_uploadedDocuments.ContainsKey(documentType))
                {
                    bool overwrite = await ShowConfirmModal("Document Exists",
                        $"{documentType} already uploaded. Replace it?");

                    if (!overwrite) return;

                    uploadedCount--;
                    _uploadedDocuments.Remove(documentType);
                    _documentUrls.Remove(documentType);
                    UpdateProgress();
                }

                var fileResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = $"Select {documentType}",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.Android, new[] { "image/jpeg", "image/png", "application/pdf" } },
                        { DevicePlatform.iOS, new[] { "public.image", "com.adobe.pdf" } },
                        { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".pdf" } },
                        { DevicePlatform.macOS, new[] { ".jpg", ".jpeg", ".png", ".pdf" } }
                    })
                });

                if (fileResult != null)
                {
                    using var stream = await fileResult.OpenReadAsync();
                    if (stream.Length > 10 * 1024 * 1024)
                    {
                        await ShowModal("Error", "File size must be <10MB", "error", false);
                        return;
                    }

                    _uploadedDocuments[documentType] = fileResult;
                    uploadedCount++;

                    statusLabel.Text = $"✓ {documentType} uploaded";
                    statusLabel.TextColor = Color.FromArgb("#27AE60");
                    uploadButton.Text = "Change";
                    uploadButton.BackgroundColor = Color.FromArgb("#F0F0F0");
                    uploadButton.TextColor = Color.FromArgb("#2E7D32");

                    UpdateProgress();
                    await ShowModal("Success", $"{documentType} uploaded successfully!", "success", false);
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Error", $"Unable to upload {documentType}: {ex.Message}", "error", false);
            }
        }

        private void UpdateProgress()
        {
            double progress = (double)uploadedCount / totalDocuments;
            UploadProgressBar.Progress = progress;

            progressLabel.Text = $"{uploadedCount}/{totalDocuments}";
            progressDesc.Text = $"{uploadedCount} out of {totalDocuments} documents uploaded";

            submitButton.IsEnabled = uploadedCount == totalDocuments;
            submitButton.BackgroundColor = uploadedCount == totalDocuments ? Color.FromArgb("#27AE60") : Color.FromArgb("#B8C3B0");
            submitButton.TextColor = uploadedCount == totalDocuments ? Colors.White : Color.FromArgb("#666666");
        }

        private async void OnSubmitClicked(object sender, EventArgs e)
        {
            if (uploadedCount != totalDocuments)
            {
                await ShowModal("Incomplete", "Please upload all 7 required documents before submitting.", "warning", false);
                return;
            }

            var submitBtn = sender as Button;
            _isRegistrationSuccessful = false;

            try
            {
                submitBtn.Text = "Processing...";
                submitBtn.IsEnabled = false;
                DisableAllUploadButtons();
                LoadingOverlay.IsVisible = true;
                UploadStatusLabel.Text = "Creating driver account...";

                bool exists = await _firebaseConnection.CheckDriverMobileNumberExistsAsync(_driverData.PhoneNumber);
                if (exists)
                    throw new Exception("This phone number is already registered.");

                _driverId = await _firebaseConnection.SimpleSaveDriverAsync(_driverData, _pin);
                if (string.IsNullOrEmpty(_driverId))
                    throw new Exception("Failed to create driver account.");

                UploadStatusLabel.Text = $"Uploading {_uploadedDocuments.Count} documents...";
                _documentUrls = await _storageService.UploadDriverDocumentsAsync(_driverId, _uploadedDocuments);

                if (_documentUrls == null || _documentUrls.Count == 0)
                    throw new Exception("Failed to upload documents.");

                UploadStatusLabel.Text = "Saving document URLs...";
                bool success = false;

                try
                {
                    success = await _firebaseConnection.UpdateDriverWithDocumentsAsync(
                        _driverId,
                        _documentUrls,
                        "Pending",
                        "Inactive"
                    );
                }
                catch { }

                if (!success)
                {
                    await Task.Delay(1000);
                    try
                    {
                        success = await _firebaseConnection.UpdateDriverWithDocumentsAsync(
                            _driverId,
                            _documentUrls,
                            "Pending",
                            "Inactive"
                        );
                    }
                    catch { }
                }

                _isRegistrationSuccessful = true;

                AppSession.CurrentDriverId = _driverId;
                AppSession.CurrentDriverPhone = _driverData.PhoneNumber;
                AppSession.LoginDriver(_driverId, _driverData.PhoneNumber);
                AppSession.SaveDriverName(_driverData.FirstName, _driverData.LastName);

                LoadingOverlay.IsVisible = false;

                await ShowModal(
                    "REGISTRATION SUCCESSFUL",
                    "Your application has been submitted successfully!\n\nDocument Status: Pending Review\nTricycle Status: Inactive\n\nIt will be reviewed within 24-48 hours.",
                    "success",
                    true
                );

                Application.Current.MainPage = new DriverShell();
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;

                if (!_isRegistrationSuccessful)
                    await ShowModal("Registration Failed", ex.Message, "error", false);

                submitBtn.Text = "Submit Application";
                submitBtn.IsEnabled = true;
                EnableAllUploadButtons();
            }
        }

        private void DisableAllUploadButtons()
        {
            Photo2x2Button.IsEnabled = false;
            CedulaButton.IsEnabled = false;
            ClearanceButton.IsEnabled = false;
            LicenseButton.IsEnabled = false;
            ORCRButton.IsEnabled = false;
            PlateNumberButton.IsEnabled = false;
            GcashButton.IsEnabled = false;
        }

        private void EnableAllUploadButtons()
        {
            Photo2x2Button.IsEnabled = true;
            CedulaButton.IsEnabled = true;
            ClearanceButton.IsEnabled = true;
            LicenseButton.IsEnabled = true;
            ORCRButton.IsEnabled = true;
            PlateNumberButton.IsEnabled = true;
            GcashButton.IsEnabled = true;
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "phone.png" : "sad.png";
            Color iconColor = type == "success" ? Color.FromArgb("#2E7D32") :
                             type == "warning" ? Color.FromArgb("#FF9800") :
                             type == "info" ? Color.FromArgb("#2196F3") : Color.FromArgb("#D32F2F");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 20,
                Padding = 25,
                Margin = 40,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.7,
                TranslationY = 0,
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image
                        {
                            Source = iconSource,
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = title,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Button
                        {
                            Text = "OK",
                            BackgroundColor = iconColor,
                            TextColor = Colors.White,
                            CornerRadius = 10,
                            HeightRequest = 45,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        }
                    }
                }
            };

            var modalPage = new DriverSubmitRequirementsModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task<bool> ShowConfirmModal(string title, string message)
        {
            if (_isModalOpen) return false;

            var tcs = new TaskCompletionSource<bool>();

            _isModalOpen = true;

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var modalContent = new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 20,
                Padding = 25,
                Margin = 40,
                HasShadow = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = 0,
                Scale = 0.7,
                TranslationY = 0,
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image
                        {
                            Source = "signal.png",
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = title,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#FF9800"),
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitionCollection
                            {
                                new ColumnDefinition { Width = GridLength.Star },
                                new ColumnDefinition { Width = GridLength.Star }
                            },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button
                                {
                                    Text = "Cancel",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                },
                                new Button
                                {
                                    Text = "Replace",
                                    BackgroundColor = Color.FromArgb("#2E7D32"),
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }
                            }
                        }
                    }
                }
            };

            var modalPage = new DriverSubmitRequirementsConfirmModalPage(modalContent, blurOverlay, tcs, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            return await tcs.Task;
        }

        public void CloseConfirmModal(TaskCompletionSource<bool> tcs, bool result)
        {
            _isModalOpen = false;
            tcs?.TrySetResult(result);
        }
    }

    public class DriverSubmitRequirementsModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_SubmitRequirements _parentPage;

        public DriverSubmitRequirementsModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_SubmitRequirements parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var button = FindButton(modalContent.Content);
            if (button != null)
            {
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await AnimateModalExit();
                };
            }

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private Button FindButton(object element)
        {
            if (element is Button btn)
                return btn;

            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButton(child);
                    if (result != null)
                        return result;
                }
            }

            if (element is ContentView cv && cv.Content != null)
            {
                return FindButton(cv.Content);
            }

            if (element is Frame frame && frame.Content != null)
            {
                return FindButton(frame.Content);
            }

            return null;
        }

        private async Task AnimateModalExit()
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal(_closePageOnOk);
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit();
            });
            return true;
        }
    }

    public class DriverSubmitRequirementsConfirmModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private TaskCompletionSource<bool> _tcs;
        private Driver_SubmitRequirements _parentPage;

        public DriverSubmitRequirementsConfirmModalPage(Frame modalContent, Grid blurOverlay, TaskCompletionSource<bool> tcs, Driver_SubmitRequirements parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _tcs = tcs;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                cancelButton.IsEnabled = false;
                confirmButton.IsEnabled = false;
                await AnimateModalExit(false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                cancelButton.IsEnabled = false;
                confirmButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool result)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseConfirmModal(_tcs, result);
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit(false);
            });
            return true;
        }
    }

    public class DriverSubmitRequirementsExitModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_SubmitRequirements _parentPage;

        public DriverSubmitRequirementsExitModalPage(Frame modalContent, Grid blurOverlay, Driver_SubmitRequirements parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var exitButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            exitButton.Clicked += async (s, e) =>
            {
                exitButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool exit)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal(false);

            if (exit)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        if (Navigation.ModalStack.Count > 1)
                        {
                            await Navigation.PopModalAsync();
                        }
                        else
                        {
                            Application.Current.MainPage = new Driver_MobileNumber();
                        }
                    }
                    catch
                    {
                        Application.Current.MainPage = new Driver_MobileNumber();
                    }
                });
            }
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit(false);
            });
            return true;
        }
    }

    public static class DriverSubmitRequirementsExtensions
    {
        public static Button WithDriverSubmitColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}