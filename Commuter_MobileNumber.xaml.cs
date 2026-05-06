using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using ServiceCo.Services;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System.Timers;

namespace ServiceCo
{
    public partial class Commuter_MobileNumber : ContentPage
    {
        private readonly OtpServiceSelector _otpSelector;
        private readonly FirebaseConnection _firebaseConnection;
        private bool _isProcessing;
        private bool _isModalOpen;

        private static int _globalAttempts = 0;
        private static DateTime _globalLockUntil;
        private const int MaxAttempts = 3;
        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
        private System.Timers.Timer _countdownTimer;

        public Commuter_MobileNumber()
        {
            InitializeComponent();
            _otpSelector = new OtpServiceSelector();
            _firebaseConnection = new FirebaseConnection();
            NavigationPage.SetHasNavigationBar(this, false);

            LoadLockStatus();
            UpdateUIState();
            CheckGlobalLockOnLoad();
        }

        private void LoadLockStatus()
        {
            _globalAttempts = Preferences.Get("GlobalAttempts", 0);
            string lockTimeStr = Preferences.Get("GlobalLockUntil", "");
            if (!string.IsNullOrEmpty(lockTimeStr) && DateTime.TryParse(lockTimeStr, out DateTime lockUntil))
            {
                if (DateTime.Now < lockUntil)
                {
                    _globalLockUntil = lockUntil;
                }
                else
                {
                    _globalAttempts = 0;
                    Preferences.Set("GlobalAttempts", 0);
                    Preferences.Remove("GlobalLockUntil");
                }
            }
        }

        private void CheckGlobalLockOnLoad()
        {
            if (_globalAttempts >= MaxAttempts && DateTime.Now < _globalLockUntil)
            {
                ShowGlobalRateLimit(_globalLockUntil);
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            this.Opacity = 0;
            this.FadeTo(1, 200, Easing.CubicOut);
            PhoneNumberEntry.Focus();
            CheckGlobalLockOnLoad();
        }

        private void OnPhoneNumberChanged(object sender, TextChangedEventArgs e)
        {
            string digitsOnly = new string(e.NewTextValue?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());

            if (digitsOnly.Length > 10)
                digitsOnly = digitsOnly.Substring(0, 10);

            if (digitsOnly.Length > 0 && digitsOnly[0] != '9')
                digitsOnly = "9" + (digitsOnly.Length > 1 ? digitsOnly.Substring(1) : "");

            if (PhoneNumberEntry.Text != digitsOnly)
            {
                PhoneNumberEntry.Text = digitsOnly;
                if (!string.IsNullOrEmpty(digitsOnly))
                    PhoneNumberEntry.CursorPosition = digitsOnly.Length;
            }

            UpdateUIState();
        }

        private void UpdateUIState()
        {
            string phoneNumber = PhoneNumberEntry.Text?.Trim() ?? "";
            bool isValid = phoneNumber.Length == 10 &&
                          phoneNumber.StartsWith("9") &&
                          Regex.IsMatch(phoneNumber, @"^9\d{9}$");

            bool isGloballyLocked = _globalAttempts >= MaxAttempts && DateTime.Now < _globalLockUntil;

            NextButton.IsEnabled = isValid && !_isProcessing && !isGloballyLocked;
            NextButton.BackgroundColor = NextButton.IsEnabled
                ? Color.FromArgb("#2E7D32")
                : Color.FromArgb("#DADADA");

            NextButton.Text = _isProcessing ? "Sending..." : (isValid ? "Continue →" : "Next →");
        }

        private void ShowGlobalRateLimit(DateTime lockUntil)
        {
            var timeLeft = lockUntil - DateTime.Now;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                RateLimitFrame.IsVisible = true;
                UpdateRateLimitTimer(timeLeft);
                UpdateUIState();
            });

            _countdownTimer?.Stop();
            _countdownTimer?.Dispose();

            _countdownTimer = new System.Timers.Timer(1000);
            _countdownTimer.Elapsed += (s, e) =>
            {
                var remaining = _globalLockUntil - DateTime.Now;
                if (remaining.TotalSeconds <= 0)
                {
                    _countdownTimer.Stop();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _globalAttempts = 0;
                        Preferences.Set("GlobalAttempts", 0);
                        Preferences.Remove("GlobalLockUntil");
                        RateLimitFrame.IsVisible = false;
                        UpdateUIState();
                    });
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() => UpdateRateLimitTimer(remaining));
                }
            };
            _countdownTimer.Start();
        }

        private void UpdateRateLimitTimer(TimeSpan timeLeft)
        {
            RateLimitTimer.Text = $"{(int)timeLeft.TotalMinutes:D2}:{timeLeft.Seconds:D2}";
            RateLimitMessage.Text = $"Too many attempts. Please wait {(int)timeLeft.TotalMinutes} minute(s) and {timeLeft.Seconds} second(s).";
        }

        private bool CanMakeAttempt()
        {
            if (_globalAttempts >= MaxAttempts)
            {
                if (DateTime.Now < _globalLockUntil)
                    return false;
                else
                {
                    _globalAttempts = 0;
                    Preferences.Set("GlobalAttempts", 0);
                    Preferences.Remove("GlobalLockUntil");
                    return true;
                }
            }
            return true;
        }

        private void RecordGlobalAttempt()
        {
            _globalAttempts++;
            Preferences.Set("GlobalAttempts", _globalAttempts);

            if (_globalAttempts >= MaxAttempts)
            {
                _globalLockUntil = DateTime.Now.Add(Cooldown);
                Preferences.Set("GlobalLockUntil", _globalLockUntil.ToString("o"));
                ShowGlobalRateLimit(_globalLockUntil);
            }
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            if (!CanMakeAttempt())
            {
                ShowGlobalRateLimit(_globalLockUntil);
                return;
            }

            string phoneNumber = PhoneNumberEntry.Text?.Trim();

            if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length != 10 || !phoneNumber.StartsWith("9"))
            {
                await ShowResultModal("Invalid Number", "Please enter a valid 10-digit mobile number starting with 9", "cancelled.png", false);
                return;
            }

            string fullPhoneNumber = $"+63{phoneNumber}";

            _isProcessing = true;
            UpdateUIState();

            try
            {
                var existingUser = await _firebaseConnection.GetCommuterByPhoneNumberAsync(fullPhoneNumber);

                if (existingUser != null)
                {
                    _isProcessing = false;
                    UpdateUIState();

                    if (!string.IsNullOrEmpty(existingUser.Pin))
                    {
                        await Navigation.PushAsync(new Commuter_Pin(fullPhoneNumber, true));
                    }
                    else
                    {
                        await Navigation.PushAsync(new Commuter_Name(fullPhoneNumber));
                    }
                }
                else
                {
                    RecordGlobalAttempt();

                    var (success, message, source) = await _otpSelector.SendOtpAsync(fullPhoneNumber);

                    _isProcessing = false;
                    UpdateUIState();

                    if (success)
                    {
                        string modeText = "";
                        string displayNumber = $"+63{phoneNumber}";
                        await ShowResultModal("OTP Sent", $"Verification code sent to {displayNumber}{modeText}", "complete.png", true);
                        await Navigation.PushAsync(new Commuter_OTP(fullPhoneNumber, _otpSelector));
                    }
                    else
                    {
                        await ShowResultModal("Not Sent", "Failed to send OTP. Please try again.", "cancelled.png", false);
                    }
                }
            }
            catch
            {
                _isProcessing = false;
                UpdateUIState();
                await ShowResultModal("Error", "An error occurred. Please try again.", "cancelled.png", false);
            }
        }

        private async Task ShowResultModal(string title, string message, string imageSource, bool isSuccess)
        {
            if (_isModalOpen) return;

            try
            {
                _isModalOpen = true;

                var alertView = new StackLayout
                {
                    Spacing = 20,
                    Padding = 20,
                    BackgroundColor = Colors.White,
                    Children =
                    {
                        new Image
                        {
                            Source = imageSource,
                            HeightRequest = 60,
                            WidthRequest = 60,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = title,
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = isSuccess ? Color.FromArgb("#2E7D32") : Color.FromArgb("#d32f2f"),
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
                            BackgroundColor = isSuccess ? Color.FromArgb("#2E7D32") : Color.FromArgb("#d32f2f"),
                            TextColor = Colors.White,
                            CornerRadius = 8,
                            HeightRequest = 40,
                            FontSize = 14
                        }
                    }
                };

                var okButton = (Button)alertView.Children[3];
                bool isClosing = false;

                var modalPage = new ContentPage
                {
                    Content = new Border
                    {
                        StrokeThickness = 0,
                        Padding = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 20 },
                        Content = alertView,
                        WidthRequest = 300,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        BackgroundColor = Colors.White
                    },
                    BackgroundColor = Color.FromArgb("#88000000")
                };

                modalPage.Opacity = 0;

                okButton.Clicked += async (s, args) =>
                {
                    if (isClosing) return;
                    isClosing = true;

                    okButton.IsEnabled = false;
                    okButton.Opacity = 0.5;

                    await modalPage.FadeTo(0, 200, Easing.CubicOut);
                    await Navigation.PopModalAsync();
                    _isModalOpen = false;
                };

                await Navigation.PushModalAsync(modalPage);
                await modalPage.FadeTo(1, 200, Easing.CubicOut);
            }
            catch
            {
                _isModalOpen = false;
            }
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isModalOpen)
            {
                return true;
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await this.FadeTo(0, 200, Easing.CubicOut);
                await Navigation.PopAsync();
            });

            return true;
        }
    }
}