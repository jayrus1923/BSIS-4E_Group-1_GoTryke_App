using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_IDVerification : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly FirebaseStorageService _storageService = new FirebaseStorageService();
        private FileResult _idFrontPhoto;
        private FileResult _idBackPhoto;
        private FileResult _selfiePhoto;
        private string _commuterId;
        private bool _isModalOpen = false;
        private bool _isProcessing = false;
        private FareConfiguration _fareConfig;
        private string _currentVerifiedType;
        private List<string> _availableIdTypes;

        public Commuter_IDVerification(string preselectedType = null)
        {
            InitializeComponent();
            _commuterId = AppSession.GetCommuterId();

            FullNameEntry.TextChanged += OnFormChanged;
            IDNumberEntry.TextChanged += OnFormChanged;
            AgreementCheck.CheckedChanged += OnFormChanged;
            UserTypePicker.SelectedIndexChanged += OnUserTypeChanged;

            LoadFareConfigurationAsync();
            LoadAvailableIdTypesAsync(preselectedType);
        }

        private async void LoadAvailableIdTypesAsync(string preselectedType)
        {
            try
            {
                var allIdTypes = new List<string> { "Student", "Senior Citizen", "PWD" };
                _availableIdTypes = new List<string>(allIdTypes);

                // Check if commuter is already verified
                bool isStudentVerified = await _firebaseConnection.IsUserVerifiedAsync(_commuterId, "Student");
                bool isSeniorVerified = await _firebaseConnection.IsUserVerifiedAsync(_commuterId, "Senior Citizen");
                bool isPwdVerified = await _firebaseConnection.IsUserVerifiedAsync(_commuterId, "PWD");

                if (isStudentVerified) _availableIdTypes.Remove("Student");
                if (isSeniorVerified) _availableIdTypes.Remove("Senior Citizen");
                if (isPwdVerified) _availableIdTypes.Remove("PWD");

                // Store current verified type for display
                if (isStudentVerified) _currentVerifiedType = "Student";
                else if (isSeniorVerified) _currentVerifiedType = "Senior Citizen";
                else if (isPwdVerified) _currentVerifiedType = "PWD";

                UserTypePicker.ItemsSource = _availableIdTypes;

                if (!string.IsNullOrEmpty(preselectedType) && _availableIdTypes.Contains(preselectedType))
                {
                    UserTypePicker.SelectedIndex = _availableIdTypes.IndexOf(preselectedType);
                }
                else if (_availableIdTypes.Count > 0)
                {
                    UserTypePicker.SelectedIndex = 0;
                }
                else
                {
                    SubmitButton.IsEnabled = false;
                    await ShowModal("Already Verified", $"You are already verified as {_currentVerifiedType}. You cannot submit another verification.", "info", false);
                }

                if (UserTypePicker.SelectedIndex >= 0 && _availableIdTypes.Count > 0)
                {
                    string selected = _availableIdTypes[UserTypePicker.SelectedIndex];
                    DiscountInfoLabel.Text = GetDiscountText(selected);
                }

                CheckExistingRequest();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading available ID types: {ex.Message}");
                _availableIdTypes = new List<string> { "Student", "Senior Citizen", "PWD" };
                UserTypePicker.ItemsSource = _availableIdTypes;
                if (_availableIdTypes.Count > 0) UserTypePicker.SelectedIndex = 0;
            }
        }

        private async void LoadFareConfigurationAsync()
        {
            try
            {
                _fareConfig = await _firebaseConnection.GetFareConfigurationAsync();

                if (UserTypePicker.SelectedIndex >= 0 && _availableIdTypes != null && _availableIdTypes.Count > 0)
                {
                    string selected = _availableIdTypes[UserTypePicker.SelectedIndex];
                    DiscountInfoLabel.Text = GetDiscountText(selected);
                }
            }
            catch
            {
                _fareConfig = new FareConfiguration();
            }
        }

        private int GetDiscountPercentage(string userType)
        {
            if (_fareConfig == null) return 20;

            return userType switch
            {
                "Student" => _fareConfig.studentDiscount > 0 ? (int)_fareConfig.studentDiscount : 20,
                "Senior Citizen" => _fareConfig.seniorDiscount > 0 ? (int)_fareConfig.seniorDiscount : 20,
                "PWD" => _fareConfig.pwdDiscount > 0 ? (int)_fareConfig.pwdDiscount : 20,
                _ => 20
            };
        }

        private string GetDiscountText(string userType)
        {
            int discount = GetDiscountPercentage(userType);
            return $"Get {discount}% discount on {userType.ToLower()} IDs!";
        }

        private async void CheckExistingRequest()
        {
            try
            {
                var existing = await _firebaseConnection.GetPendingVerificationAsync(_commuterId);
                if (existing != null)
                {
                    await ShowModal(existing.Status == "approved" ? "Verified!" :
                                   existing.Status == "rejected" ? "Verification Rejected" :
                                   "Pending Review",
                                   existing.Status == "approved" ? "Your ID has been verified! You can now enjoy discounts." :
                                   existing.Status == "rejected" ? $"Rejected: {existing.Notes}" :
                                   "Your verification is still being reviewed by our team.",
                                   existing.Status == "approved" ? "success" :
                                   existing.Status == "rejected" ? "error" : "info", false);
                }
            }
            catch { }
        }

        private void OnUserTypeChanged(object sender, EventArgs e)
        {
            if (UserTypePicker.SelectedIndex >= 0 && _availableIdTypes != null && _availableIdTypes.Count > 0)
            {
                string selected = _availableIdTypes[UserTypePicker.SelectedIndex];
                IDBackSection.IsVisible = (selected == "Senior Citizen" || selected == "PWD");
                DiscountInfoLabel.Text = GetDiscountText(selected);
            }
            ValidateForm();
        }

        private async void OnIDFrontTapped(object sender, EventArgs e)
        {
            try
            {
                if (_isProcessing || _isModalOpen) return;

                string action = await DisplayActionSheet("Take ID Front Photo", "Cancel", null, "Take Photo", "Choose from Gallery");

                if (action == "Take Photo")
                {
                    if (MediaPicker.Default.IsCaptureSupported)
                    {
                        _idFrontPhoto = await MediaPicker.Default.CapturePhotoAsync();
                    }
                }
                else if (action == "Choose from Gallery")
                {
                    _idFrontPhoto = await MediaPicker.Default.PickPhotoAsync();
                }

                if (_idFrontPhoto != null)
                {
                    var stream = await _idFrontPhoto.OpenReadAsync();
                    IDFrontImage.Source = ImageSource.FromStream(() => stream);
                    IDFrontImage.IsVisible = true;
                    IDFrontPlaceholder.IsVisible = false;
                    ValidateForm();
                }
            }
            catch
            {
                await ShowModal("Error", "Failed to take photo", "error", false);
            }
        }

        private async void OnIDBackTapped(object sender, EventArgs e)
        {
            try
            {
                if (_isProcessing || _isModalOpen) return;

                string action = await DisplayActionSheet("Take ID Back Photo", "Cancel", null, "Take Photo", "Choose from Gallery");

                if (action == "Take Photo")
                {
                    if (MediaPicker.Default.IsCaptureSupported)
                    {
                        _idBackPhoto = await MediaPicker.Default.CapturePhotoAsync();
                    }
                }
                else if (action == "Choose from Gallery")
                {
                    _idBackPhoto = await MediaPicker.Default.PickPhotoAsync();
                }

                if (_idBackPhoto != null)
                {
                    var stream = await _idBackPhoto.OpenReadAsync();
                    IDBackImage.Source = ImageSource.FromStream(() => stream);
                    IDBackImage.IsVisible = true;
                    IDBackPlaceholder.IsVisible = false;
                    ValidateForm();
                }
            }
            catch
            {
                await ShowModal("Error", "Failed to take photo", "error", false);
            }
        }

        private async void OnSelfieTapped(object sender, EventArgs e)
        {
            try
            {
                if (_isProcessing || _isModalOpen) return;

                string action = await DisplayActionSheet("Take Selfie", "Cancel", null, "Take Photo", "Choose from Gallery");

                if (action == "Take Photo")
                {
                    if (MediaPicker.Default.IsCaptureSupported)
                    {
                        _selfiePhoto = await MediaPicker.Default.CapturePhotoAsync();
                    }
                }
                else if (action == "Choose from Gallery")
                {
                    _selfiePhoto = await MediaPicker.Default.PickPhotoAsync();
                }

                if (_selfiePhoto != null)
                {
                    var stream = await _selfiePhoto.OpenReadAsync();
                    SelfieImage.Source = ImageSource.FromStream(() => stream);
                    SelfieImage.IsVisible = true;
                    SelfiePlaceholder.IsVisible = false;
                    ValidateForm();
                }
            }
            catch
            {
                await ShowModal("Error", "Failed to take photo", "error", false);
            }
        }

        private void OnFormChanged(object sender, EventArgs e) => ValidateForm();

        private bool ValidateForm()
        {
            if (_availableIdTypes == null || _availableIdTypes.Count == 0 || UserTypePicker.SelectedIndex < 0)
                return false;

            string selected = _availableIdTypes[UserTypePicker.SelectedIndex];
            bool needsBackID = (selected == "Senior Citizen" || selected == "PWD");

            bool isValid = UserTypePicker.SelectedIndex >= 0 &&
                          !string.IsNullOrWhiteSpace(FullNameEntry.Text) &&
                          !string.IsNullOrWhiteSpace(IDNumberEntry.Text) &&
                          _idFrontPhoto != null &&
                          _selfiePhoto != null &&
                          AgreementCheck.IsChecked;

            if (needsBackID)
            {
                isValid = isValid && _idBackPhoto != null;
            }

            SubmitButton.IsEnabled = isValid;
            SubmitButton.BackgroundColor = isValid ?
                Color.FromArgb("#2E7D32") :
                Color.FromArgb("#A5D6A7");

            return isValid;
        }

        private bool HasChanges()
        {
            return (UserTypePicker.SelectedIndex >= 0 && _availableIdTypes != null && _availableIdTypes.Count > 0) ||
                   !string.IsNullOrWhiteSpace(FullNameEntry.Text) ||
                   !string.IsNullOrWhiteSpace(IDNumberEntry.Text) ||
                   _idFrontPhoto != null ||
                   _idBackPhoto != null ||
                   _selfiePhoto != null;
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;
            await ShowExitConfirmation();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_isProcessing && !_isModalOpen)
                {
                    await ShowExitConfirmation();
                }
            });
            return true;
        }

        private async Task ShowExitConfirmation()
        {
            if (!HasChanges())
            {
                await Navigation.PopModalAsync();
                return;
            }

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
                            Text = "Exit Verification?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Your progress will not be saved if you exit.",
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
                                }.WithIDVerificationGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithIDVerificationGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new CommuterIDVerificationExitConfirmationModalPage(modalContent, blurOverlay, this);
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

        private async void OnSubmitClicked(object sender, EventArgs e)
        {
            try
            {
                if (!ValidateForm())
                {
                    string selected = _availableIdTypes[UserTypePicker.SelectedIndex];
                    string message = (selected == "Senior Citizen" || selected == "PWD")
                        ? "Please fill all fields and take photos (front and back of ID)."
                        : "Please fill all fields and take photos.";

                    await ShowModal("Incomplete", message, "warning", false);
                    return;
                }

                _isProcessing = true;
                ShowProcessing("Uploading images...");

                string idFrontUrl = await UploadImage(_idFrontPhoto, "id_front", "verification/ids_front");
                string selfieUrl = await UploadImage(_selfiePhoto, "selfie", "verification/selfies");

                string idBackUrl = "";
                string selectedType = _availableIdTypes[UserTypePicker.SelectedIndex];
                if (selectedType == "Senior Citizen" || selectedType == "PWD")
                {
                    idBackUrl = await UploadImage(_idBackPhoto, "id_back", "verification/ids_back");
                }

                ShowProcessing("Submitting verification...");

                var request = new VerificationRequest
                {
                    CommuterId = _commuterId,
                    UserType = selectedType,
                    FullName = FullNameEntry.Text,
                    IdNumber = IDNumberEntry.Text,
                    IdFrontImageUrl = idFrontUrl,
                    IdBackImageUrl = idBackUrl,
                    SelfieImageUrl = selfieUrl,
                    Status = "pending",
                    SubmittedAt = DateTime.UtcNow
                };

                bool success = await _firebaseConnection.SubmitVerificationRequestAsync(request);

                HideProcessing();
                _isProcessing = false;

                if (success)
                {
                    await ShowModal("Verification Submitted!", "Your ID verification is now pending review. We'll notify you once approved.", "success", true);
                }
                else
                {
                    await ShowModal("Error", "Failed to submit verification", "error", false);
                }
            }
            catch (Exception ex)
            {
                HideProcessing();
                _isProcessing = false;
                await ShowModal("Error", $"Submission failed: {ex.Message}", "error", false);
            }
        }

        private async Task<string> UploadImage(FileResult photo, string documentType, string folder)
        {
            try
            {
                if (photo == null)
                    throw new Exception("No photo selected");

                Console.WriteLine($"📤 Uploading {documentType} to folder: {folder}");

                string imageUrl = await _storageService.UploadImageAsync(
                    _commuterId,
                    documentType,
                    photo,
                    folder
                );

                Console.WriteLine($"✅ Upload successful: {imageUrl}");
                return imageUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Upload failed: {ex.Message}");
                throw new Exception($"Failed to upload image: {ex.Message}");
            }
        }

        private void ShowProcessing(string message = "Uploading...")
        {
            ProcessingLabel.Text = message;
            ProcessingOverlay.IsVisible = true;
            ProcessingIndicator.IsRunning = true;
        }

        private void HideProcessing()
        {
            ProcessingOverlay.IsVisible = false;
            ProcessingIndicator.IsRunning = false;
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "document.png" : "sad.png";
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

            var modal = new CommuterIDVerificationCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }
    }

    public class CommuterIDVerificationCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_IDVerification _parentPage;

        public CommuterIDVerificationCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_IDVerification parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var button = (modalContent.Content as VerticalStackLayout).Children[3] as Button;
            button.Clicked += async (s, e) =>
            {
                button.IsEnabled = false;
                await AnimateModalExit();
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
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
                var button = (_modalContent.Content as VerticalStackLayout).Children[3] as Button;
                if (button != null)
                    button.IsEnabled = false;

                await AnimateModalExit();
            });

            return true;
        }
    }

    public class CommuterIDVerificationExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_IDVerification _parentPage;

        public CommuterIDVerificationExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Commuter_IDVerification parentPage)
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
                    await _parentPage.Navigation.PopModalAsync();
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

    public static class CommuterIDVerificationGridExtensions
    {
        public static Button WithIDVerificationGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}