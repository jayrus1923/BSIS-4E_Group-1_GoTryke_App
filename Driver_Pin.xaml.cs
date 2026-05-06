using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using ServiceCo.Services;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace ServiceCo
{
    public partial class Driver_Pin : ContentPage
    {
        private readonly string _phoneNumber;
        private readonly FirebaseConnection _firebaseConnection;
        private bool _isProcessing = false;
        private bool _isExistingUser = false;
        private bool _isInConfirmMode = false;
        private bool _autoSubmitEnabled = true;
        private int _failedAttempts = 0;
        private DateTime? _lockUntil = null;
        private bool _keyboardEnabled = true;

        private Color _activeBorderColor = Color.FromArgb("#2E7D32");
        private Color _inactiveBorderColor = Color.FromArgb("#CCCCCC");
        private Color _errorBorderColor = Color.FromArgb("#DC3545");
        private Color _successBorderColor = Color.FromArgb("#28A745");

        private string[] _pinValues = new string[6];
        private string[] _confirmPinValues = new string[6];
        private int _currentPinIndex = 0;
        private int _currentConfirmPinIndex = 0;

        public Driver_Pin(string phoneNumber, bool isExistingUser = false)
        {
            InitializeComponent();
            _phoneNumber = phoneNumber;
            _isExistingUser = isExistingUser;
            _firebaseConnection = new FirebaseConnection();

            NavigationPage.SetHasNavigationBar(this, false);

            SetupUI();
            InitializePinState();
            CheckLockStatus();
        }

        private void SetupUI()
        {
            if (_isExistingUser)
            {
                TitleLabel.Text = "Enter Your PIN";
                SubtitleLine1.Text = "Enter your 6-digit PIN to login";
                SubtitleLine2.Text = "";
                ConfirmPinStack.IsVisible = false;
                RequirementsFrame.IsVisible = false;
                ConfirmRequirementsFrame.IsVisible = false;
                PhoneNumberStack.IsVisible = true;
                ForgotPinStack.IsVisible = true;
                PhoneNumberLabel.Text = FormatPhoneNumber(_phoneNumber);
            }
            else
            {
                TitleLabel.Text = "Set Your Security PIN";
                SubtitleLine1.Text = "Create a 6-digit PIN for quick";
                SubtitleLine2.Text = "and secure access to your account";
                ConfirmPinStack.IsVisible = false;
                ConfirmRequirementsFrame.IsVisible = false;
                PhoneNumberStack.IsVisible = false;
                ForgotPinStack.IsVisible = false;
            }
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

        private void InitializePinState()
        {
            _pinValues = new string[6];
            _confirmPinValues = new string[6];
            _currentPinIndex = 0;
            _currentConfirmPinIndex = 0;
            _isInConfirmMode = false;
            HideAllMessages();
            UpdatePinDisplay();
            UpdatePinStatus("Enter your 6-digit PIN", false);
        }

        private void UpdatePinDisplay()
        {
            Pin1Dot.IsVisible = !string.IsNullOrEmpty(_pinValues[0]);
            Pin2Dot.IsVisible = !string.IsNullOrEmpty(_pinValues[1]);
            Pin3Dot.IsVisible = !string.IsNullOrEmpty(_pinValues[2]);
            Pin4Dot.IsVisible = !string.IsNullOrEmpty(_pinValues[3]);
            Pin5Dot.IsVisible = !string.IsNullOrEmpty(_pinValues[4]);
            Pin6Dot.IsVisible = !string.IsNullOrEmpty(_pinValues[5]);

            ConfirmPin1Dot.IsVisible = !string.IsNullOrEmpty(_confirmPinValues[0]);
            ConfirmPin2Dot.IsVisible = !string.IsNullOrEmpty(_confirmPinValues[1]);
            ConfirmPin3Dot.IsVisible = !string.IsNullOrEmpty(_confirmPinValues[2]);
            ConfirmPin4Dot.IsVisible = !string.IsNullOrEmpty(_confirmPinValues[3]);
            ConfirmPin5Dot.IsVisible = !string.IsNullOrEmpty(_confirmPinValues[4]);
            ConfirmPin6Dot.IsVisible = !string.IsNullOrEmpty(_confirmPinValues[5]);

            UpdateActiveBorder();
        }

        private void UpdateActiveBorder()
        {
            Pin1Border.Stroke = _inactiveBorderColor;
            Pin2Border.Stroke = _inactiveBorderColor;
            Pin3Border.Stroke = _inactiveBorderColor;
            Pin4Border.Stroke = _inactiveBorderColor;
            Pin5Border.Stroke = _inactiveBorderColor;
            Pin6Border.Stroke = _inactiveBorderColor;
            ConfirmPin1Border.Stroke = _inactiveBorderColor;
            ConfirmPin2Border.Stroke = _inactiveBorderColor;
            ConfirmPin3Border.Stroke = _inactiveBorderColor;
            ConfirmPin4Border.Stroke = _inactiveBorderColor;
            ConfirmPin5Border.Stroke = _inactiveBorderColor;
            ConfirmPin6Border.Stroke = _inactiveBorderColor;

            if (_isInConfirmMode)
            {
                if (_currentConfirmPinIndex < 6)
                {
                    switch (_currentConfirmPinIndex)
                    {
                        case 0: ConfirmPin1Border.Stroke = _activeBorderColor; break;
                        case 1: ConfirmPin2Border.Stroke = _activeBorderColor; break;
                        case 2: ConfirmPin3Border.Stroke = _activeBorderColor; break;
                        case 3: ConfirmPin4Border.Stroke = _activeBorderColor; break;
                        case 4: ConfirmPin5Border.Stroke = _activeBorderColor; break;
                        case 5: ConfirmPin6Border.Stroke = _activeBorderColor; break;
                    }
                }
            }
            else
            {
                if (_currentPinIndex < 6)
                {
                    switch (_currentPinIndex)
                    {
                        case 0: Pin1Border.Stroke = _activeBorderColor; break;
                        case 1: Pin2Border.Stroke = _activeBorderColor; break;
                        case 2: Pin3Border.Stroke = _activeBorderColor; break;
                        case 3: Pin4Border.Stroke = _activeBorderColor; break;
                        case 4: Pin5Border.Stroke = _activeBorderColor; break;
                        case 5: Pin6Border.Stroke = _activeBorderColor; break;
                    }
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
            if (_isInConfirmMode)
            {
                if (_currentConfirmPinIndex < 6)
                {
                    _confirmPinValues[_currentConfirmPinIndex] = number;
                    _currentConfirmPinIndex++;
                    UpdatePinDisplay();

                    if (_currentConfirmPinIndex == 6)
                    {
                        ValidateConfirmPin();
                    }
                }
            }
            else
            {
                if (_currentPinIndex < 6)
                {
                    _pinValues[_currentPinIndex] = number;
                    _currentPinIndex++;
                    UpdatePinDisplay();

                    UpdatePinStatus("Enter your 6-digit PIN", false);

                    if (_currentPinIndex == 6)
                    {
                        if (_isExistingUser)
                        {
                            if (_autoSubmitEnabled)
                            {
                                _ = SubmitPinAsync();
                            }
                        }
                        else
                        {
                            string pin = string.Join("", _pinValues);
                            string validationMessage = ValidatePinStrength(pin);

                            if (string.IsNullOrEmpty(validationMessage))
                            {
                                SwitchToConfirmMode();
                            }
                            else
                            {
                                UpdatePinStatus(validationMessage, true);
                                Device.StartTimer(TimeSpan.FromSeconds(1.5), () =>
                                {
                                    ClearAllPins();
                                    UpdatePinStatus("Enter your 6-digit PIN", false);
                                    return false;
                                });
                            }
                        }
                    }
                }
            }
        }

        private void SwitchToConfirmMode()
        {
            _isInConfirmMode = true;
            _currentConfirmPinIndex = 0;
            Array.Clear(_confirmPinValues, 0, _confirmPinValues.Length);
            EnterPinStack.IsVisible = false;
            RequirementsFrame.IsVisible = false;
            ConfirmPinStack.IsVisible = true;
            ConfirmRequirementsFrame.IsVisible = true;
            UpdatePinDisplay();
            PinMatchLabel.Text = "";
            PinMatchLabel.TextColor = _activeBorderColor;
        }

        private void SwitchBackToEnterPin(bool showMismatchError = false)
        {
            _isInConfirmMode = false;
            _currentConfirmPinIndex = 0;
            Array.Clear(_confirmPinValues, 0, _confirmPinValues.Length);

            if (showMismatchError)
            {
                Array.Clear(_pinValues, 0, _pinValues.Length);
                _currentPinIndex = 0;
            }

            EnterPinStack.IsVisible = true;
            RequirementsFrame.IsVisible = true;
            ConfirmPinStack.IsVisible = false;
            ConfirmRequirementsFrame.IsVisible = false;
            UpdatePinDisplay();
            PinMatchLabel.Text = "";

            if (showMismatchError)
            {
                UpdatePinStatus("✗ PINs do not match. Please try again.", true);
            }
            else
            {
                UpdatePinStatus("Enter your 6-digit PIN", false);
            }
        }

        private void ValidateConfirmPin()
        {
            string pin = string.Join("", _pinValues);
            string confirmPin = string.Join("", _confirmPinValues);

            if (pin == confirmPin)
            {
                PinMatchLabel.Text = "✓ PINs match";
                PinMatchLabel.TextColor = _successBorderColor;
                ConfirmPin1Border.Stroke = _successBorderColor;
                ConfirmPin2Border.Stroke = _successBorderColor;
                ConfirmPin3Border.Stroke = _successBorderColor;
                ConfirmPin4Border.Stroke = _successBorderColor;
                ConfirmPin5Border.Stroke = _successBorderColor;
                ConfirmPin6Border.Stroke = _successBorderColor;
                _autoSubmitEnabled = true;
                Device.StartTimer(TimeSpan.FromSeconds(0.5), () =>
                {
                    _ = SubmitNewPinAsync();
                    return false;
                });
            }
            else
            {
                PinMatchLabel.Text = "✗ PINs do not match";
                PinMatchLabel.TextColor = _errorBorderColor;
                ConfirmPin1Border.Stroke = _errorBorderColor;
                ConfirmPin2Border.Stroke = _errorBorderColor;
                ConfirmPin3Border.Stroke = _errorBorderColor;
                ConfirmPin4Border.Stroke = _errorBorderColor;
                ConfirmPin5Border.Stroke = _errorBorderColor;
                ConfirmPin6Border.Stroke = _errorBorderColor;

                Device.StartTimer(TimeSpan.FromSeconds(1.5), () =>
                {
                    SwitchBackToEnterPin(true);
                    return false;
                });
            }
        }

        private void OnBackspaceClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing) return;

            if (_isInConfirmMode)
            {
                if (_currentConfirmPinIndex > 0)
                {
                    _currentConfirmPinIndex--;
                    _confirmPinValues[_currentConfirmPinIndex] = string.Empty;
                    PinMatchLabel.Text = "";
                }
                else if (_currentConfirmPinIndex == 0 && !string.IsNullOrEmpty(_confirmPinValues[0]))
                {
                    _confirmPinValues[0] = string.Empty;
                }
                else if (_currentConfirmPinIndex == 0 && !_isExistingUser)
                {
                    SwitchBackToEnterPin(false);
                }
            }
            else
            {
                if (_currentPinIndex > 0)
                {
                    _currentPinIndex--;
                    _pinValues[_currentPinIndex] = string.Empty;

                    if (_currentPinIndex == 0)
                    {
                        UpdatePinStatus("Enter your 6-digit PIN", false);
                    }
                }
            }

            UpdatePinDisplay();
            HideAllMessages();
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            if (!_keyboardEnabled || _isProcessing) return;
            ClearAllPins();
        }

        private void ClearAllPins()
        {
            if (_isInConfirmMode)
            {
                Array.Clear(_confirmPinValues, 0, _confirmPinValues.Length);
                _currentConfirmPinIndex = 0;
                PinMatchLabel.Text = "";
            }
            else
            {
                Array.Clear(_pinValues, 0, _pinValues.Length);
                _currentPinIndex = 0;
            }

            UpdatePinDisplay();
            HideAllMessages();
            UpdatePinStatus(_isInConfirmMode ? "Confirm your 6-digit PIN" : "Enter your 6-digit PIN", false);
        }

        private void UpdatePinStatus(string message, bool isError = false)
        {
            PinStatusLabel.Text = message;
            PinStatusLabel.TextColor = isError ? _errorBorderColor : _activeBorderColor;
            HideAllMessages();
        }

        private void ShowProcessing(string message = "Validating...")
        {
            ProcessingLabel.Text = message;
            ProcessingOverlay.IsVisible = true;
            ProcessingIndicator.IsRunning = true;
            MessageStack.IsVisible = false;
            _keyboardEnabled = false;
        }

        private void HideProcessing()
        {
            ProcessingOverlay.IsVisible = false;
            ProcessingIndicator.IsRunning = false;
            _keyboardEnabled = true;
        }

        private void ShowSuccessMessage(string message)
        {
            HideAllMessages();
            SuccessMessageLabel.Text = message;
            SuccessMessage.IsVisible = true;
            MessageStack.IsVisible = true;
        }

        private void HideAllMessages()
        {
            SuccessMessage.IsVisible = false;
            ErrorMessage.IsVisible = false;
            WarningMessage.IsVisible = false;
            MessageStack.IsVisible = false;
        }

        private string ValidatePinStrength(string pin)
        {
            if (Regex.IsMatch(pin, @"^(.)\1+$"))
                return "PIN cannot have all same digits";
            if (IsSequential(pin))
                return "PIN cannot be sequential numbers";
            if (IsSimplePattern(pin))
                return "PIN is too predictable";
            return string.Empty;
        }

        private bool IsSequential(string pin)
        {
            int ascending = 0, descending = 0;
            for (int i = 0; i < pin.Length - 1; i++)
            {
                int current = pin[i] - '0';
                int next = pin[i + 1] - '0';
                if (next == current + 1) ascending++;
                if (next == current - 1) descending++;
            }
            return ascending == 5 || descending == 5;
        }

        private bool IsSimplePattern(string pin)
        {
            bool isAlternating = true;
            for (int i = 2; i < pin.Length; i++)
                if (pin[i] != pin[i % 2]) { isAlternating = false; break; }

            bool isMirrored = true;
            for (int i = 0; i < pin.Length / 2; i++)
                if (pin[i] != pin[pin.Length - 1 - i]) isMirrored = false;

            return isAlternating || isMirrored;
        }

        private async Task SubmitPinAsync()
        {
            if (_isProcessing) return;

            string pin = string.Join("", _pinValues);

            if (pin.Length != 6)
            {
                UpdatePinStatus("Please enter complete 6-digit PIN.", true);
                ClearAllPins();
                return;
            }

            if (IsLocked())
            {
                ShowLockMessage();
                ClearAllPins();
                return;
            }

            _isProcessing = true;
            _autoSubmitEnabled = false;
            ShowProcessing("Validating PIN...");

            try
            {
                await HandleExistingDriverPinVerification(pin);
            }
            catch
            {
                UpdatePinStatus("An error occurred", true);
                ClearAllPins();
            }
            finally
            {
                _isProcessing = false;
                _autoSubmitEnabled = true;
                HideProcessing();
            }
        }

        private async Task SubmitNewPinAsync()
        {
            if (_isProcessing) return;

            string pin = string.Join("", _pinValues);
            string confirmPin = string.Join("", _confirmPinValues);

            if (pin != confirmPin)
            {
                UpdatePinStatus("PINs do not match.", true);
                ClearAllPins();
                SwitchBackToEnterPin(true);
                return;
            }

            string validationMessage = ValidatePinStrength(pin);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                UpdatePinStatus(validationMessage, true);
                ClearAllPins();
                SwitchBackToEnterPin(true);
                return;
            }

            _isProcessing = true;
            ShowProcessing("Setting up your PIN...");

            try
            {
                await Navigation.PushAsync(new Driver_Name(_phoneNumber, pin));
            }
            catch
            {
                UpdatePinStatus("An error occurred", true);
                ClearAllPins();
            }
            finally
            {
                _isProcessing = false;
                HideProcessing();
            }
        }

        private async Task HandleExistingDriverPinVerification(string pin)
        {
            var existingDriver = await _firebaseConnection.GetDriverByPhoneNumberAsync(_phoneNumber);

            if (existingDriver == null)
            {
                UpdatePinStatus("Driver not found. Please register first.", true);
                await Task.Delay(2000);
                await Navigation.PopAsync();
                return;
            }

            if (string.IsNullOrEmpty(existingDriver.Pin) || !existingDriver.IsPinSet)
            {
                UpdatePinStatus("No PIN set. Use OTP instead.", true);
                await Task.Delay(2000);
                await Navigation.PopAsync();
                return;
            }

            if (existingDriver.Pin == pin)
            {
                _failedAttempts = 0;
                _lockUntil = null;
                AppSession.LoginDriver(existingDriver.FirebaseKey, _phoneNumber);
                await ShowSuccessAnimation();

                ShowSuccessMessage("Login successful! Redirecting...");
                await Task.Delay(1500);
                Application.Current.MainPage = new DriverShell();
            }
            else
            {
                _failedAttempts++;
                CheckAndApplyLock();
                await ShowErrorAnimation();
                UpdatePinStatus("✗ Incorrect PIN. Please try again.", true);
                await Task.Delay(1000);
                ClearAllPins();
            }
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
            var lockUntilStr = Preferences.Get($"DriverPinLock_{_phoneNumber}", "");
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
                    Preferences.Remove($"DriverPinLock_{_phoneNumber}");
                    Preferences.Remove($"DriverFailedAttempts_{_phoneNumber}");
                }
            }

            var attempts = Preferences.Get($"DriverFailedAttempts_{_phoneNumber}", 0);
            _failedAttempts = attempts;
        }

        private void SaveLockStatus()
        {
            if (_lockUntil.HasValue)
                Preferences.Set($"DriverPinLock_{_phoneNumber}", _lockUntil.Value.ToString());
            Preferences.Set($"DriverFailedAttempts_{_phoneNumber}", _failedAttempts);
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
                        Preferences.Remove($"DriverPinLock_{_phoneNumber}");
                        Preferences.Remove($"DriverFailedAttempts_{_phoneNumber}");
                        EnableKeyboard();
                        HideAllMessages();
                        ClearAllPins();
                        return false;
                    }
                });
            }
        }

        private void ShowWarningMessage(string message)
        {
            HideAllMessages();
            WarningMessageLabel.Text = message;
            WarningMessage.IsVisible = true;
            MessageStack.IsVisible = true;
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

        private async Task ShowSuccessAnimation()
        {
            var pins = new[] { Pin1Border, Pin2Border, Pin3Border, Pin4Border, Pin5Border, Pin6Border };
            foreach (var pin in pins)
                pin.Stroke = _successBorderColor;
            await Task.Delay(200);
            foreach (var pin in pins)
                pin.Stroke = _inactiveBorderColor;
            await Task.Delay(100);
            foreach (var pin in pins)
                pin.Stroke = _successBorderColor;
            await Task.Delay(300);
        }

        private async Task ShowErrorAnimation()
        {
            var pins = new[] { Pin1Border, Pin2Border, Pin3Border, Pin4Border, Pin5Border, Pin6Border };
            for (int i = 0; i < 3; i++)
            {
                foreach (var pin in pins)
                    pin.Stroke = _errorBorderColor;
                await Task.Delay(300);
                foreach (var pin in pins)
                    pin.Stroke = _inactiveBorderColor;
                await Task.Delay(100);
            }
        }

        private async void OnForgotPinClicked(object sender, EventArgs e)
        {
            if (_isProcessing) return;

            if (IsLocked())
            {
                ShowLockMessage();
                return;
            }

            ShowProcessing("Sending OTP...");

            try
            {
                var otpSelector = new OtpServiceSelector();
                var (success, message, source) = await otpSelector.SendOtpAsync(_phoneNumber);

                if (!success)
                {
                    UpdatePinStatus(message, true);
                    return;
                }

                ShowSuccessMessage("OTP sent successfully!");
                await Task.Delay(1500);
                await Navigation.PushAsync(new Driver_OTP(_phoneNumber, otpSelector, true));
            }
            catch
            {
                UpdatePinStatus("Failed to send OTP", true);
            }
            finally
            {
                HideProcessing();
            }
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
            _autoSubmitEnabled = true;
            InitializePinState();
            CheckLockStatus();

            if (AppSession.IsDriverLoggedIn() && _isExistingUser)
            {
                Dispatcher.Dispatch(() =>
                {
                    Application.Current!.MainPage = new DriverShell();
                });
            }
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isInConfirmMode && !_isExistingUser)
            {
                SwitchBackToEnterPin(false);
                return true;
            }

            Dispatcher.Dispatch(async () =>
            {
                await Navigation.PopAsync();
            });
            return true;
        }
    }
}