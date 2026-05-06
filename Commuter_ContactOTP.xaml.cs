using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using ServiceCo.Services;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace ServiceCo
{
    public partial class Commuter_ContactOTP : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly OtpServiceSelector _otpSelector;
        private readonly string _currentNumber;
        private readonly string _newNumber;
        private readonly string _email;
        private readonly bool _navigateToEmailOtp;
        private readonly string _currentEmail;

        private static string _lastNumber = "";
        private static int _remainingSeconds = 180;
        private static System.Timers.Timer _timer;
        private static DateTime _expiryTime;
        private static bool _isTimerRunning = false;

        private string[] _otpValues = new string[6];
        private int _currentOtpIndex = 0;
        private bool _isProcessing = false;
        private bool _keyboardEnabled = true;
        private bool _isModalOpen = false;
        private int _failedAttempts = 0;
        private DateTime? _lockUntil = null;

        private Color _activeBorderColor = Color.FromArgb("#2E7D32");
        private Color _inactiveBorderColor = Color.FromArgb("#CCCCCC");
        private Color _errorBorderColor = Color.FromArgb("#DC3545");
        private Color _successBorderColor = Color.FromArgb("#28A745");

        public Commuter_ContactOTP(string currentNumber, string newNumber, string email, bool navigateToEmailOtp, string currentEmail)
        {
            InitializeComponent();
            _firebaseConnection = new FirebaseConnection();
            _otpSelector = new OtpServiceSelector();
            _currentNumber = currentNumber;
            _newNumber = newNumber;
            _email = email;
            _navigateToEmailOtp = navigateToEmailOtp;
            _currentEmail = currentEmail;

            PhoneNumberLabel.Text = FormatPhoneNumberForDisplay(newNumber);

            InitializeOtpState();

            if (_lastNumber != newNumber)
            {
                _lastNumber = newNumber;
                StartCountdown();
                _ = SendOtpAsync();
            }
            else
            {
                StartCountdown();
            }
        }

        private string FormatPhoneNumberForDisplay(string phoneNumber)
        {
            if (phoneNumber.StartsWith("+63") && phoneNumber.Length == 13)
            {
                return $"+63 {phoneNumber.Substring(3, 3)} {phoneNumber.Substring(6, 3)} {phoneNumber.Substring(9, 4)}";
            }
            return phoneNumber;
        }

        private async Task SendOtpAsync()
        {
            try
            {
                var (success, message, source) = await _otpSelector.SendOtpAsync(_newNumber);
                if (!success)
                {
                    OtpStatusLabel.Text = "Failed to send OTP. Please try again.";
                    OtpStatusLabel.TextColor = _errorBorderColor;
                }
            }
            catch
            {
                OtpStatusLabel.Text = "Failed to send OTP. Please try again.";
                OtpStatusLabel.TextColor = _errorBorderColor;
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
            CheckLockStatus();
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

        private bool IsLocked()
        {
            if (_lockUntil.HasValue)
                return DateTime.Now < _lockUntil.Value;
            return false;
        }

        private void CheckAndApplyLock()
        {
            if (_failedAttempts >= 10)
            {
                if (_failedAttempts >= 30)
                    _lockUntil = DateTime.Now.AddMinutes(2);
                else if (_failedAttempts >= 20)
                    _lockUntil = DateTime.Now.AddMinutes(1);
                else if (_failedAttempts >= 10)
                    _lockUntil = DateTime.Now.AddSeconds(30);

                SaveLockStatus();
                ShowLockMessage();
            }
        }

        private void CheckLockStatus()
        {
            var lockUntilStr = Preferences.Get($"ContactOtpLock_{_newNumber}", "");
            if (!string.IsNullOrEmpty(lockUntilStr) && DateTime.TryParse(lockUntilStr, out var lockUntil))
            {
                if (DateTime.Now < lockUntil)
                {
                    _lockUntil = lockUntil;
                    ShowLockMessage();
                }
                else
                {
                    _lockUntil = null;
                    _failedAttempts = 0;
                    Preferences.Remove($"ContactOtpLock_{_newNumber}");
                    Preferences.Remove($"ContactOtpFailedAttempts_{_newNumber}");
                }
            }

            var attempts = Preferences.Get($"ContactOtpFailedAttempts_{_newNumber}", 0);
            _failedAttempts = attempts;
        }

        private void SaveLockStatus()
        {
            if (_lockUntil.HasValue)
                Preferences.Set($"ContactOtpLock_{_newNumber}", _lockUntil.Value.ToString());
            Preferences.Set($"ContactOtpFailedAttempts_{_newNumber}", _failedAttempts);
        }

        private void ShowLockMessage()
        {
            if (_lockUntil.HasValue)
            {
                var timeLeft = _lockUntil.Value - DateTime.Now;
                int secondsLeft = (int)timeLeft.TotalSeconds;
                int minutesLeft = (int)timeLeft.TotalMinutes;

                string message = secondsLeft > 60
                    ? $"Too many attempts. Please wait {minutesLeft} minute(s)."
                    : $"Too many attempts. Please wait {secondsLeft} second(s).";

                OtpStatusLabel.Text = message;
                OtpStatusLabel.TextColor = _errorBorderColor;
                DisableKeyboard();

                Device.StartTimer(TimeSpan.FromSeconds(1), () =>
                {
                    if (_lockUntil.HasValue && DateTime.Now < _lockUntil.Value)
                    {
                        var remaining = _lockUntil.Value - DateTime.Now;
                        if (remaining.TotalSeconds > 60)
                        {
                            OtpStatusLabel.Text = $"Too many attempts. Please wait {(int)remaining.TotalMinutes} minute(s).";
                        }
                        else
                        {
                            OtpStatusLabel.Text = $"Too many attempts. Please wait {(int)remaining.TotalSeconds} second(s).";
                        }
                        return true;
                    }
                    else
                    {
                        _lockUntil = null;
                        _failedAttempts = 0;
                        Preferences.Remove($"ContactOtpLock_{_newNumber}");
                        Preferences.Remove($"ContactOtpFailedAttempts_{_newNumber}");
                        EnableKeyboard();
                        HideAllMessages();
                        ClearAllOtp();
                        ResetOtpStatus();
                        return false;
                    }
                });
            }
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

            if (IsLocked())
            {
                ShowLockMessage();
                ClearAllOtp();
                return;
            }

            _isProcessing = true;
            _keyboardEnabled = false;
            ShowProcessing("Verifying...");

            try
            {
                var (success, message, source) = await _otpSelector.VerifyOtpAsync(_newNumber, enteredOTP);

                if (!success)
                {
                    _failedAttempts++;
                    CheckAndApplyLock();

                    if (message.Contains("expired"))
                    {
                        OtpStatusLabel.Text = "OTP has expired. Please request a new one.";
                        OtpStatusLabel.TextColor = _errorBorderColor;
                    }
                    else
                    {
                        await ShowErrorAnimation();
                        OtpStatusLabel.Text = "✗ Incorrect OTP. Please try again.";
                        OtpStatusLabel.TextColor = _errorBorderColor;
                        Device.StartTimer(TimeSpan.FromSeconds(1.5), () =>
                        {
                            ClearAllOtp();
                            return false;
                        });
                    }
                    return;
                }

                _failedAttempts = 0;
                _lockUntil = null;
                Preferences.Remove($"ContactOtpLock_{_newNumber}");
                Preferences.Remove($"ContactOtpFailedAttempts_{_newNumber}");

                string commuterId = AppSession.GetCommuterId();

                if (string.IsNullOrEmpty(commuterId))
                {
                    await ShowModal("Session Expired", "Your session has expired. Please login again.", "warning", false);
                    await Navigation.PushAsync(new Commuter_MobileNumber());
                    return;
                }

                var updatedCommuter = new Commuter
                {
                    MobileNumber = _newNumber,
                    Email = _currentEmail
                };

                await _firebaseConnection.UpdateContactInfoAsync(commuterId, updatedCommuter);

                AppSession.LoginCommuter(commuterId, _newNumber);

                if (_navigateToEmailOtp && _email != _currentEmail)
                {
                    await ShowModal("Phone Verified", "Your phone number has been verified! Now verifying your email...", "success", true);
                    await Navigation.PushAsync(new Commuter_EmailOTP(
                        currentEmail: _currentEmail,
                        newEmail: _email,
                        phoneNumber: _newNumber
                    ));
                }
                else
                {
                    await ShowModal("Success", "Your contact information has been updated successfully!", "success", true);
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Update Failed", $"Unable to update your information: {ex.Message}", "error", false);
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

            var modal = new ContactOTPCustomModalPage(modalContent, blurOverlay, closePageOnOk, type, this);
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
                                }.WithContactOTPGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithContactOTPGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new ContactOTPExitConfirmationModalPage(modalContent, blurOverlay, this);
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

            if (IsLocked())
            {
                ShowLockMessage();
                return;
            }

            ResendLabel.IsVisible = false;
            TimerLabel.IsVisible = true;

            _isProcessing = true;
            _keyboardEnabled = false;
            ShowProcessing("Sending OTP...");

            try
            {
                var (success, message, source) = await _otpSelector.SendOtpAsync(_newNumber);

                if (!success)
                {
                    OtpStatusLabel.Text = message;
                    OtpStatusLabel.TextColor = _errorBorderColor;
                    ResendLabel.IsVisible = true;
                    TimerLabel.IsVisible = false;
                    return;
                }

                ClearAllOtp();
                _lastNumber = _newNumber;
                StartCountdown();

                OtpStatusLabel.Text = "✓ New OTP sent successfully!";
                OtpStatusLabel.TextColor = _successBorderColor;
            }
            catch (Exception ex)
            {
                OtpStatusLabel.Text = $"✗ Failed to resend OTP: {ex.Message}";
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

        private void DisableKeyboard()
        {
            _keyboardEnabled = false;
            Num1.IsEnabled = false;
            Num2.IsEnabled = false;
            Num3.IsEnabled = false;
            Num4.IsEnabled = false;
            Num5.IsEnabled = false;
            Num6.IsEnabled = false;
            Num7.IsEnabled = false;
            Num8.IsEnabled = false;
            Num9.IsEnabled = false;
            Num0.IsEnabled = false;
            ClearButton.IsEnabled = false;
            BackspaceButton.IsEnabled = false;
        }

        private void EnableKeyboard()
        {
            _keyboardEnabled = true;
            Num1.IsEnabled = true;
            Num2.IsEnabled = true;
            Num3.IsEnabled = true;
            Num4.IsEnabled = true;
            Num5.IsEnabled = true;
            Num6.IsEnabled = true;
            Num7.IsEnabled = true;
            Num8.IsEnabled = true;
            Num9.IsEnabled = true;
            Num0.IsEnabled = true;
            ClearButton.IsEnabled = true;
            BackspaceButton.IsEnabled = true;
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
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }
            }
            catch { }
        }
    }

    public class ContactOTPCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private string _modalType;
        private Commuter_ContactOTP _parentPage;

        public ContactOTPCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, string modalType, Commuter_ContactOTP parentPage)
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

    public class ContactOTPExitConfirmationModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_ContactOTP _parentPage;

        public ContactOTPExitConfirmationModalPage(Frame modalContent, Grid blurOverlay, Commuter_ContactOTP parentPage)
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

    public static class ContactOTPGridExtensions
    {
        public static Button WithContactOTPGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}