using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using ServiceCo.Services;
using System;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Maui.Storage;

namespace ServiceCo
{
    public partial class Commuter_OTP : ContentPage
    {
        private readonly OtpServiceSelector _otpSelector;
        private readonly string _phoneNumber;
        private readonly bool _isFromPinForgot;

        private int _remainingSeconds = 180;
        private System.Timers.Timer _timer;
        private bool _isProcessing = false;
        private bool _keyboardEnabled = true;
        private int _failedAttempts = 0;
        private DateTime? _lockUntil = null;

        private Color _activeBorderColor = Color.FromArgb("#2E7D32");
        private Color _inactiveBorderColor = Color.FromArgb("#CCCCCC");
        private Color _errorBorderColor = Color.FromArgb("#DC3545");
        private Color _successBorderColor = Color.FromArgb("#28A745");

        private string[] _otpValues = new string[6];
        private int _currentOtpIndex = 0;

        public Commuter_OTP(string phoneNumber, OtpServiceSelector otpSelector, bool isFromPinForgot = false)
        {
            InitializeComponent();
            _otpSelector = otpSelector;
            _phoneNumber = phoneNumber;
            _isFromPinForgot = isFromPinForgot;

            NavigationPage.SetHasNavigationBar(this, false);

            PhoneNumberLabel.Text = FormatPhoneNumber(_phoneNumber);

            if (_isFromPinForgot)
            {
                TitleLabel.Text = "Login with OTP";
                SubtitleLabel.Text = "You're logging in with OTP instead of PIN";
                SubtitleLabel.IsVisible = true;
            }
            else
            {
                SubtitleLabel.IsVisible = false;
            }

            StartCountdown();
            InitializeOtpState();
            CheckLockStatus();
        }

        private string FormatPhoneNumber(string phone)
        {
            var cleaned = phone.Replace("+", "").Replace(" ", "");
            if (cleaned.Length == 12 && cleaned.StartsWith("63"))
            {
                return $"+{cleaned.Substring(0, 2)} {cleaned.Substring(2, 3)} {cleaned.Substring(5, 3)} {cleaned.Substring(8)}";
            }
            else if (cleaned.Length == 11 && cleaned.StartsWith("09"))
            {
                return $"+63 {cleaned.Substring(1, 3)} {cleaned.Substring(4, 3)} {cleaned.Substring(7)}";
            }
            return phone;
        }

        private void InitializeOtpState()
        {
            _otpValues = new string[6];
            _currentOtpIndex = 0;
            HideAllMessages();
            UpdateOtpDisplay();
            ResetOtpStatus();
        }

        private void UpdateOtpDisplay()
        {
            Otp1Entry.Text = _otpValues[0];
            Otp2Entry.Text = _otpValues[1];
            Otp3Entry.Text = _otpValues[2];
            Otp4Entry.Text = _otpValues[3];
            Otp5Entry.Text = _otpValues[4];
            Otp6Entry.Text = _otpValues[5];
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

        private void OnNumberClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing) return;

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
                    _ = VerifyOtpAsync();
                }
            }
        }

        private void OnBackspaceClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing) return;

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
            if (!_keyboardEnabled || _isProcessing) return;
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

        private void ResetOtpStatus()
        {
            OtpStatusLabel.Text = "Enter the 6-digit OTP";
            OtpStatusLabel.TextColor = _activeBorderColor;
        }

        private void UpdateOtpStatus(string message, bool isError = false)
        {
            OtpStatusLabel.Text = message;
            OtpStatusLabel.TextColor = isError ? _errorBorderColor : _activeBorderColor;
            HideAllMessages();
        }

        private async Task VerifyOtpAsync()
        {
            if (_isProcessing) return;

            string otp = string.Join("", _otpValues);

            if (otp.Length != 6)
            {
                UpdateOtpStatus("Please enter complete 6-digit OTP", true);
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
            ShowProcessing("Verifying OTP...");

            try
            {
                var (success, message, source) = await _otpSelector.VerifyOtpAsync(_phoneNumber, otp);

                if (!success)
                {
                    _failedAttempts++;
                    CheckAndApplyLock();

                    if (message.Contains("expired"))
                    {
                        UpdateOtpStatus("OTP has expired. Please request a new one.", true);
                    }
                    else
                    {
                        await ShowErrorAnimation();
                        UpdateOtpStatus("✗ Incorrect OTP. Please try again.", true);
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
                await HandleUserAfterOtpVerification();
            }
            catch (Exception ex)
            {
                UpdateOtpStatus($"An error occurred: {ex.Message}", true);
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

        private async Task HandleUserAfterOtpVerification()
        {
            try
            {
                var firebaseConnection = new FirebaseConnection();
                var existingUser = await firebaseConnection.GetCommuterByPhoneNumberAsync(_phoneNumber);

                if (existingUser != null)
                {
                    AppSession.LoginCommuter(existingUser.FirebaseKey, _phoneNumber);
                    await ShowSuccessAnimation();

                    bool hasName = !string.IsNullOrEmpty(existingUser.FirstName) &&
                                  !string.IsNullOrEmpty(existingUser.LastName);

                    if (hasName)
                    {
                        ShowSuccessMessage("Login successful! Redirecting...");
                        await Task.Delay(1500);
                        Application.Current.MainPage = new CommuterShell();
                    }
                    else
                    {
                        ShowSuccessMessage("OTP verified! Complete your profile.");
                        await Task.Delay(1500);
                        await Navigation.PushAsync(new Commuter_Name(_phoneNumber));
                    }
                }
                else
                {
                    ShowSuccessMessage("OTP verified! Set your PIN.");
                    await Task.Delay(1500);
                    await Navigation.PushAsync(new Commuter_Pin(_phoneNumber, false));
                }
            }
            catch (Exception ex)
            {
                UpdateOtpStatus($"Login failed: {ex.Message}", true);
                await Task.Delay(2000);
                await Navigation.PopAsync();
            }
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

        private void ShowSuccessMessage(string message)
        {
            HideAllMessages();
            SuccessMessageLabel.Text = message;
            SuccessMessage.IsVisible = true;
            MessageStack.IsVisible = true;
        }

        private void ShowErrorMessage(string message)
        {
            HideAllMessages();
            ErrorMessageLabel.Text = message;
            ErrorMessage.IsVisible = true;
            MessageStack.IsVisible = true;
        }

        private void ShowWarningMessage(string message)
        {
            HideAllMessages();
            WarningMessageLabel.Text = message;
            WarningMessage.IsVisible = true;
            MessageStack.IsVisible = true;
        }

        private void HideAllMessages()
        {
            SuccessMessage.IsVisible = false;
            ErrorMessage.IsVisible = false;
            WarningMessage.IsVisible = false;
            MessageStack.IsVisible = false;
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
            }
        }

        private void CheckLockStatus()
        {
            var lockUntilStr = Preferences.Get($"OtpLock_{_phoneNumber}", "");
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
                    Preferences.Remove($"OtpLock_{_phoneNumber}");
                    Preferences.Remove($"OtpFailedAttempts_{_phoneNumber}");
                }
            }

            var attempts = Preferences.Get($"OtpFailedAttempts_{_phoneNumber}", 0);
            _failedAttempts = attempts;
        }

        private void SaveLockStatus()
        {
            if (_lockUntil.HasValue)
                Preferences.Set($"OtpLock_{_phoneNumber}", _lockUntil.Value.ToString());
            Preferences.Set($"OtpFailedAttempts_{_phoneNumber}", _failedAttempts);
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

                ShowWarningMessage(message);
                DisableKeyboard();

                Device.StartTimer(TimeSpan.FromSeconds(1), () =>
                {
                    if (_lockUntil.HasValue && DateTime.Now < _lockUntil.Value)
                    {
                        var remaining = _lockUntil.Value - DateTime.Now;
                        if (remaining.TotalSeconds > 60)
                        {
                            WarningMessageLabel.Text = $"Too many attempts. Please wait {(int)remaining.TotalMinutes} minute(s).";
                        }
                        else
                        {
                            WarningMessageLabel.Text = $"Too many attempts. Please wait {(int)remaining.TotalSeconds} second(s).";
                        }
                        return true;
                    }
                    else
                    {
                        _lockUntil = null;
                        _failedAttempts = 0;
                        Preferences.Remove($"OtpLock_{_phoneNumber}");
                        Preferences.Remove($"OtpFailedAttempts_{_phoneNumber}");
                        EnableKeyboard();
                        HideAllMessages();
                        ClearAllOtp();
                        ResetOtpStatus();
                        return false;
                    }
                });
            }
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

        private async void ResendOTP(object sender, EventArgs e)
        {
            if (_isProcessing) return;

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
                var (success, message, source) = await _otpSelector.SendOtpAsync(_phoneNumber);

                if (!success)
                {
                    UpdateOtpStatus(message, true);
                    ResendLabel.IsVisible = true;
                    TimerLabel.IsVisible = false;
                    return;
                }

                UpdateOtpStatus("✓ New OTP sent successfully!", false);
                ClearAllOtp();
                StartCountdown();
            }
            catch (Exception ex)
            {
                UpdateOtpStatus($"✗ Failed to resend OTP: {ex.Message}", true);
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
            _remainingSeconds = 180;
            TimerLabel.IsVisible = true;
            ResendLabel.IsVisible = false;

            _timer?.Stop();
            _timer?.Dispose();

            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += OnTimerTick;
            _timer.Start();

            UpdateTimerDisplay();
        }

        private void OnTimerTick(object sender, ElapsedEventArgs e)
        {
            _remainingSeconds--;
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
                    _timer?.Stop();
                    TimerLabel.IsVisible = false;
                    ResendLabel.IsVisible = true;
                }
            });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _timer?.Stop();
            _timer?.Dispose();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            NavigationPage.SetHasNavigationBar(this, false);

            if (Parent is NavigationPage navPage)
            {
                navPage.BarBackgroundColor = Colors.Transparent;
                navPage.BarTextColor = Colors.Transparent;
            }
            _isProcessing = false;
            _keyboardEnabled = true;
            InitializeOtpState();
            CheckLockStatus();
        }
    }
}