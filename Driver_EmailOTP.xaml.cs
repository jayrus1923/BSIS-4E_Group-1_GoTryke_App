using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace ServiceCo
{
    public partial class Driver_EmailOTP : ContentPage
    {
        private readonly FirebaseEmail _firebaseEmail = new FirebaseEmail();
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly string _currentEmail;
        private readonly string _newEmail;
        private readonly string _phoneNumber;
        private string _generatedOTP;

        private static string _lastEmail = "";
        private static int _remainingSeconds = 180;
        private static System.Timers.Timer _timer;
        private static DateTime _expiryTime;
        private static bool _isTimerRunning = false;

        private string[] _otpValues = new string[6];
        private int _currentOtpIndex = 0;
        private bool _isProcessing = false;
        private bool _keyboardEnabled = true;
        private bool _isModalOpen = false;

        private Color _activeBorderColor = Color.FromArgb("#2E7D32");
        private Color _inactiveBorderColor = Color.FromArgb("#CCCCCC");
        private Color _errorBorderColor = Color.FromArgb("#DC3545");
        private Color _successBorderColor = Color.FromArgb("#28A745");

        public Driver_EmailOTP(string currentEmail, string newEmail, string phoneNumber)
        {
            InitializeComponent();
            _currentEmail = currentEmail;
            _newEmail = newEmail;
            _phoneNumber = phoneNumber;
            _generatedOTP = GenerateOTP();

            EmailLabel.Text = newEmail;
            SendOTPToEmail();
            InitializeOtpState();

            _lastEmail = newEmail;
            StartCountdown();
        }

        private string GenerateOTP()
        {
            Random random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private async void SendOTPToEmail()
        {
            try
            {
                ShowProcessing("Sending OTP...");
                bool sent = await _firebaseEmail.SendEmailOTPAsync(_newEmail, _generatedOTP);
                HideProcessing();

                if (!sent)
                {
                    await ShowModal("Error", "Failed to send OTP. Please try again.", "error", false);
                    await Navigation.PopModalAsync();
                }
            }
            catch
            {
                HideProcessing();
                await ShowModal("Error", "Failed to send OTP. Please try again.", "error", false);
                await Navigation.PopModalAsync();
            }
        }

        private void InitializeOtpState()
        {
            _otpValues = new string[6];
            _currentOtpIndex = 0;

            Otp1.Text = "";
            Otp2.Text = "";
            Otp3.Text = "";
            Otp4.Text = "";
            Otp5.Text = "";
            Otp6.Text = "";

            UpdateOtpDisplay();
            ResetOtpStatus();
        }

        private void UpdateOtpDisplay()
        {
            Otp1.Text = _otpValues[0] ?? "";
            Otp2.Text = _otpValues[1] ?? "";
            Otp3.Text = _otpValues[2] ?? "";
            Otp4.Text = _otpValues[3] ?? "";
            Otp5.Text = _otpValues[4] ?? "";
            Otp6.Text = _otpValues[5] ?? "";

            UpdateActiveBorder();
        }

        private void UpdateActiveBorder()
        {
            Otp1Border.Stroke = _inactiveBorderColor;
            Otp2Border.Stroke = _inactiveBorderColor;
            Otp3Border.Stroke = _inactiveBorderColor;
            Otp4Border.Stroke = _inactiveBorderColor;
            Otp5Border.Stroke = _inactiveBorderColor;
            Otp6Border.Stroke = _inactiveBorderColor;

            if (_currentOtpIndex < 6)
            {
                switch (_currentOtpIndex)
                {
                    case 0: Otp1Border.Stroke = _activeBorderColor; break;
                    case 1: Otp2Border.Stroke = _activeBorderColor; break;
                    case 2: Otp3Border.Stroke = _activeBorderColor; break;
                    case 3: Otp4Border.Stroke = _activeBorderColor; break;
                    case 4: Otp5Border.Stroke = _activeBorderColor; break;
                    case 5: Otp6Border.Stroke = _activeBorderColor; break;
                }
            }
        }

        private void ResetOtpStatus()
        {
            OtpStatusLabel.Text = "Enter the 6-digit OTP";
            OtpStatusLabel.TextColor = _activeBorderColor;
        }

        private void HideAllMessages()
        {
            SuccessMessage.IsVisible = false;
            ErrorMessage.IsVisible = false;
            MessageStack.IsVisible = false;
        }

        private void OnNumberClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing || _isModalOpen) return;

            if (sender is Button button)
            {
                string number = button.Text;
                AddNumber(number);
            }
        }

        private void AddNumber(string number)
        {
            if (_currentOtpIndex < 6)
            {
                _otpValues[_currentOtpIndex] = number;
                _currentOtpIndex++;
                UpdateOtpDisplay();

                if (_currentOtpIndex == 1)
                {
                    ResetOtpStatus();
                }

                if (_currentOtpIndex == 6)
                {
                    _ = VerifyOTP();
                }
            }
        }

        private void OnBackspaceClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing || _isModalOpen) return;

            if (_currentOtpIndex > 0)
            {
                _currentOtpIndex--;
                _otpValues[_currentOtpIndex] = string.Empty;
            }

            UpdateOtpDisplay();
            HideAllMessages();

            if (_currentOtpIndex == 0)
            {
                ResetOtpStatus();
            }
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing || _isModalOpen) return;
            ClearAllOtp();
        }

        private void ClearAllOtp()
        {
            _otpValues = new string[6];
            _currentOtpIndex = 0;
            UpdateOtpDisplay();
            HideAllMessages();
            ResetOtpStatus();
        }

        private async Task VerifyOTP()
        {
            if (_isProcessing || _isModalOpen) return;

            string enteredOTP = string.Join("", _otpValues);

            if (enteredOTP.Length != 6)
            {
                OtpStatusLabel.Text = "Please enter complete 6-digit OTP";
                OtpStatusLabel.TextColor = _errorBorderColor;
                ClearAllOtp();
                return;
            }

            _isProcessing = true;
            _keyboardEnabled = false;
            ShowProcessing("Verifying...");

            try
            {
                bool isValid = await _firebaseEmail.VerifyEmailOTPAsync(_newEmail, enteredOTP);

                if (!isValid)
                {
                    await ShowErrorAnimation();
                    OtpStatusLabel.Text = "✗ Incorrect OTP. Please try again.";
                    OtpStatusLabel.TextColor = _errorBorderColor;
                    ClearAllOtp();
                    return;
                }

                await ShowSuccessAnimation();

                string driverId = AppSession.GetDriverId();

                if (string.IsNullOrEmpty(driverId))
                {
                    await ShowModal("Session Expired", "Your session has expired. Please login again.", "warning", false);
                    await Navigation.PushAsync(new Driver_MobileNumber());
                    return;
                }

                await _firebaseEmail.UpdateDriverEmailAsync(driverId, _newEmail);

                var updatedDriver = new Driver
                {
                    MobileNumber = _phoneNumber,
                    Email = _newEmail
                };
                await _firebaseConnection.UpdateDriverContactInfoAsync(driverId, updatedDriver);

                await ShowModal("Success", "Your email address has been updated successfully!", "success", true);
            }
            catch
            {
                await ShowModal("Update Failed", "Unable to update your email. Please try again.", "error", false);
                ClearAllOtp();
            }
            finally
            {
                _isProcessing = false;
                _keyboardEnabled = true;
                HideProcessing();
            }
        }

        private async Task ShowErrorAnimation()
        {
            var borders = new[] { Otp1Border, Otp2Border, Otp3Border, Otp4Border, Otp5Border, Otp6Border };

            for (int i = 0; i < 3; i++)
            {
                foreach (var border in borders)
                    border.Stroke = _errorBorderColor;
                await Task.Delay(200);
                foreach (var border in borders)
                    border.Stroke = _inactiveBorderColor;
                await Task.Delay(100);
            }
        }

        private async Task ShowSuccessAnimation()
        {
            var borders = new[] { Otp1Border, Otp2Border, Otp3Border, Otp4Border, Otp5Border, Otp6Border };

            foreach (var border in borders)
                border.Stroke = _successBorderColor;
            await Task.Delay(200);
            foreach (var border in borders)
                border.Stroke = _inactiveBorderColor;
            await Task.Delay(100);
            foreach (var border in borders)
                border.Stroke = _successBorderColor;
            await Task.Delay(300);
        }

        private void ShowProcessing(string message = "Verifying...")
        {
            ProcessingLabel.Text = message;
            ProcessingOverlay.IsVisible = true;
            ProcessingIndicator.IsRunning = true;
            MessageStack.IsVisible = false;
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
                               type == "warning" ? "complaint.png" : "sad.png";
            Color iconColor = type == "success" ? Color.FromArgb("#2E7D32") :
                             type == "warning" ? Color.FromArgb("#FF9800") : Color.FromArgb("#D32F2F");

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

            var modal = new DriverCustomModalPage(modalContent, blurOverlay, closePageOnOk, type, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
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
                            Text = "Exit Verification?",
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
                                }.WithDriverGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new DriverExitConfirmationModalPage(modalContent, blurOverlay, this);
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

        private async void ResendOTP(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            ResendLabel.IsVisible = false;
            TimerLabel.IsVisible = true;

            _isProcessing = true;
            _keyboardEnabled = false;
            ShowProcessing("Resending OTP...");

            try
            {
                _generatedOTP = GenerateOTP();
                bool sent = await _firebaseEmail.SendEmailOTPAsync(_newEmail, _generatedOTP);

                if (sent)
                {
                    ClearAllOtp();
                    _lastEmail = _newEmail;
                    StartCountdown();

                    OtpStatusLabel.Text = "✓ New OTP sent successfully!";
                    OtpStatusLabel.TextColor = _successBorderColor;
                }
                else
                {
                    OtpStatusLabel.Text = "✗ Failed to resend OTP";
                    OtpStatusLabel.TextColor = _errorBorderColor;
                    ResendLabel.IsVisible = true;
                    TimerLabel.IsVisible = false;
                }
            }
            catch
            {
                OtpStatusLabel.Text = "✗ Failed to resend OTP";
                OtpStatusLabel.TextColor = _errorBorderColor;
                ResendLabel.IsVisible = true;
                TimerLabel.IsVisible = false;
            }
            finally
            {
                _isProcessing = false;
                _keyboardEnabled = true;
                HideProcessing();
            }
        }

        private void StartCountdown()
        {
            _expiryTime = DateTime.Now.AddSeconds(180);
            _remainingSeconds = 180;
            _isTimerRunning = true;

            TimerLabel.IsVisible = true;
            ResendLabel.IsVisible = false;

            try
            {
                if (_timer != null)
                {
                    _timer.Elapsed -= OnTimerTick;
                    _timer.Stop();
                    _timer.Dispose();
                }
            }
            catch { }

            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnTimerTick;
            _timer.Start();

            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_remainingSeconds > 0)
                {
                    int minutes = _remainingSeconds / 60;
                    int seconds = _remainingSeconds % 60;
                    TimerLabel.Text = $"Resend available in {minutes:D1}:{seconds:D2}";
                }
                else
                {
                    TimerLabel.IsVisible = false;
                    ResendLabel.IsVisible = true;
                }
            });
        }

        private void OnTimerTick(object sender, ElapsedEventArgs e)
        {
            TimeSpan remaining = _expiryTime - DateTime.Now;
            _remainingSeconds = (int)remaining.TotalSeconds;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_remainingSeconds > 0)
                {
                    int minutes = _remainingSeconds / 60;
                    int seconds = _remainingSeconds % 60;
                    TimerLabel.Text = $"Resend available in {minutes:D1}:{seconds:D2}";
                    TimerLabel.IsVisible = true;
                    ResendLabel.IsVisible = false;
                }
                else
                {
                    _isTimerRunning = false;
                    TimerLabel.IsVisible = false;
                    ResendLabel.IsVisible = true;

                    try
                    {
                        _timer?.Stop();
                    }
                    catch { }
                }
            });
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
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

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
        }
    }

    public class DriverCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private string _modalType;
        private Driver_EmailOTP _parentPage;

        public DriverCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, string modalType, Driver_EmailOTP parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _modalType = modalType;
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
            _parentPage.CloseModal(_closePageOnOk && _modalType == "success");
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

    public class DriverExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_EmailOTP _parentPage;

        public DriverExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Driver_EmailOTP parentPage)
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

    public static class DriverGridExtensions
    {
        public static Button WithDriverGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}