using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_ContactInformation : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string? _currentMobileNumber;
        private string? _currentEmail;
        private bool _isProcessing = false;
        private bool _isModalOpen = false;

        public Driver_ContactInformation()
        {
            InitializeComponent();
            ContactNumberEntry.TextChanged += OnContactNumberTextChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadUserData();
        }

        private async Task LoadUserData()
        {
            try
            {
                if (!AppSession.IsLoggedIn())
                {
                    await ShowModal("Notice", "Please login first.", "warning", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                string driverId = AppSession.GetDriverId();
                _currentMobileNumber = AppSession.GetMobileNumber();

                if (string.IsNullOrEmpty(driverId))
                {
                    await ShowModal("Notice", "User data not found.", "warning", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);

                if (driver != null)
                {
                    ContactNumberEntry.Text = ExtractPhoneNumberDigits(_currentMobileNumber ?? "");
                    EmailEntry.Text = driver.Email ?? "";
                    _currentEmail = driver.Email;
                }
                else
                {
                    await ShowModal("Notice", "Unable to load your information. Please try again.", "warning", false);
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Notice", $"Something went wrong: {ex.Message}", "warning", false);
            }
        }

        private string ExtractPhoneNumberDigits(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return "";

            if (phoneNumber.StartsWith("+63") && phoneNumber.Length == 13)
            {
                return phoneNumber.Substring(3);
            }
            else if (phoneNumber.StartsWith("63") && phoneNumber.Length == 12)
            {
                return phoneNumber.Substring(2);
            }
            else if (phoneNumber.Length == 10)
            {
                return phoneNumber;
            }

            string digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
            return digitsOnly.Length >= 10 ? digitsOnly.Substring(digitsOnly.Length - 10) : digitsOnly;
        }

        private string FormatPhoneNumberForSave(string phoneNumberDigits)
        {
            string digitsOnly = new string(phoneNumberDigits.Where(char.IsDigit).ToArray());

            if (digitsOnly.Length != 10)
            {
                throw new Exception("Phone number must be exactly 10 digits.");
            }

            return $"+63{digitsOnly}";
        }

        private void OnContactNumberTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                string newText = e.NewTextValue ?? "";
                string digitsOnly = new string(newText.Where(char.IsDigit).ToArray());

                if (digitsOnly.Length > 10)
                    digitsOnly = digitsOnly.Substring(0, 10);

                // Auto-add 9 if first digit is missing or not 9
                if (digitsOnly.Length > 0 && digitsOnly[0] != '9')
                    digitsOnly = "9" + (digitsOnly.Length > 1 ? digitsOnly.Substring(1) : "");

                if (entry.Text != digitsOnly)
                {
                    entry.Text = digitsOnly;
                    if (!string.IsNullOrEmpty(digitsOnly))
                        entry.CursorPosition = digitsOnly.Length;
                }
            }
        }

        private async void OnBackButtonClicked(object? sender, EventArgs e)
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
                            Text = "Exit Editing?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Your changes will not be saved if you exit.",
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
                                }.WithDriverContactInfoGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverContactInfoGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new DriverContactInfoExitConfirmationModalPage(modalContent, blurOverlay, this);
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

        private async void OnSubmitClicked(object? sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            string contactNumberDigits = ContactNumberEntry.Text?.Trim() ?? "";
            string email = EmailEntry.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(contactNumberDigits) || string.IsNullOrEmpty(email))
            {
                await ShowModal("Required Fields", "Please fill out all required fields.", "warning", false);
                return;
            }

            if (contactNumberDigits.Length != 10)
            {
                await ShowModal("Invalid Number", "Please enter a valid 10-digit mobile number.", "warning", false);
                ContactNumberEntry.Focus();
                return;
            }

            if (!contactNumberDigits.StartsWith("9"))
            {
                await ShowModal("Invalid Number", "Mobile number must start with 9.", "warning", false);
                ContactNumberEntry.Focus();
                return;
            }

            if (!IsValidEmail(email))
            {
                await ShowModal("Invalid Email", "Please enter a valid email address.", "warning", false);
                EmailEntry.Focus();
                return;
            }

            string formattedNumber = FormatPhoneNumberForSave(contactNumberDigits);
            bool isNumberChanged = formattedNumber != _currentMobileNumber;
            bool isEmailChanged = email != _currentEmail;

            if (isNumberChanged)
            {
                _isProcessing = true;
                ShowProcessing("Checking number...");

                try
                {
                    bool numberExists = await _firebaseConnection.CheckDriverMobileNumberExistsAsync(formattedNumber);
                    HideProcessing();
                    _isProcessing = false;

                    if (numberExists)
                    {
                        await ShowModal("Number Unavailable",
                                        "This mobile number is already in use. Please use a different number.",
                                        "warning", false);
                        return;
                    }
                }
                catch
                {
                    HideProcessing();
                    _isProcessing = false;
                    await ShowModal("Notice", "Unable to verify number. Please try again.", "warning", false);
                    return;
                }
            }

            if (isNumberChanged && isEmailChanged)
            {
                await NavigateToPhoneOtpVerification(formattedNumber, email, navigateToEmailOtp: true);
            }
            else if (isNumberChanged)
            {
                await NavigateToPhoneOtpVerification(formattedNumber, email, navigateToEmailOtp: false);
            }
            else if (isEmailChanged)
            {
                await NavigateToEmailOtpVerification(email, formattedNumber);
            }
            else
            {
                await ShowModal("No Changes", "No changes were made to your contact information.", "info", false);
            }
        }

        private async Task NavigateToPhoneOtpVerification(string newPhoneNumber, string email, bool navigateToEmailOtp)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_ContactOTP(
                    currentNumber: _currentMobileNumber ?? "",
                    newNumber: newPhoneNumber,
                    email: email,
                    navigateToEmailOtp: navigateToEmailOtp,
                    currentEmail: _currentEmail ?? ""
                ));
            }
            catch (Exception ex)
            {
                await ShowModal("Notice", $"Unable to verify number: {ex.Message}", "warning", false);
            }
        }

        private async Task NavigateToEmailOtpVerification(string newEmail, string phoneNumber)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_EmailOTP(
                    currentEmail: _currentEmail ?? "",
                    newEmail: newEmail,
                    phoneNumber: phoneNumber
                ));
            }
            catch (Exception ex)
            {
                await ShowModal("Notice", $"Unable to verify email: {ex.Message}", "warning", false);
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private void ShowProcessing(string message = "Checking...")
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

            var modal = new DriverContactInfoCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ContactNumberEntry.TextChanged -= OnContactNumberTextChanged;
        }
    }

    public class DriverContactInfoCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_ContactInformation _parentPage;

        public DriverContactInfoCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_ContactInformation parentPage)
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

    public class DriverContactInfoExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_ContactInformation _parentPage;

        public DriverContactInfoExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Driver_ContactInformation parentPage)
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

    public static class DriverContactInfoGridExtensions
    {
        public static Button WithDriverContactInfoGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}