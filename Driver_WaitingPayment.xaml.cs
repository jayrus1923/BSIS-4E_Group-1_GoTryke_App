using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

namespace ServiceCo
{
    public partial class Driver_WaitingPayment : ContentPage
    {
        private string _rideRequestId;
        private string _driverId;
        private string _commuterId;
        private string _commuterName;
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private RideRequest _currentRideRequest;
        private IDisposable _paymentListener;
        private IDisposable _paymentMonitor;
        private IDisposable _paymentMethodListener;
        private string _paymentMethod = "";
        private bool _paymentMarkedAsPaid = false;
        private Commuter _passengerInfo;
        private bool _isListenerActive = true;
        private bool _isPaymentConfirmed = false;
        private double _fareAmount = 0;
        private bool _isProcessingPayment = false;
        private bool _isFirstLoad = true;
        private bool _isRefreshing = false;
        private bool _isModalOpen = false;
        private DateTime _paymentReceivedTime;

        public Driver_WaitingPayment(string rideRequestId)
        {
            InitializeComponent();
            _rideRequestId = rideRequestId;
            _driverId = AppSession.GetDriverId();
            SetupUI();
            SetupPullToRefresh();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _isListenerActive = true;
            _isFirstLoad = true;
            await LoadRideData();
            StartUltraFastRealtimeListeners();
            StartPaymentMethodListener();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isListenerActive = false;
            _paymentListener?.Dispose();
            _paymentMonitor?.Dispose();
            _paymentMethodListener?.Dispose();
        }

        private void SetupUI()
        {
            PaymentReceivedButton.IsEnabled = false;
            RatingReceivedFrame.IsVisible = false;
            PaymentReadyFrame.IsVisible = false;
            RealtimeStatusIndicator.IsRunning = true;
            ReturnHomeButton.IsVisible = false;
            ReturnHomeButton.IsEnabled = false;
            PaymentTimeLabel.IsVisible = false;
            PassengerImage.Source = "profile_icon.png";
        }

        private void SetupPullToRefresh()
        {
            RefreshContainer.Refreshing += async (sender, e) =>
            {
                if (_isRefreshing) return;
                _isRefreshing = true;
                await RefreshData();
                RefreshContainer.IsRefreshing = false;
                _isRefreshing = false;
            };
        }

        private async Task RefreshData()
        {
            try
            {
                await LoadRideData();
                await ShowModal("Refreshed", "Payment information updated.", "success", false);
            }
            catch
            {
                await ShowModal("Connection Error", "Unable to refresh. Please check your internet connection.", "warning", false);
            }
        }

        private DateTime ConvertToPhilippineTime(DateTime utcDateTime)
        {
            try
            {
                return utcDateTime.AddHours(8);
            }
            catch
            {
                return utcDateTime;
            }
        }

        private string FormatPhilippineTime(DateTime utcDateTime)
        {
            try
            {
                DateTime phTime = ConvertToPhilippineTime(utcDateTime);
                return phTime.ToString("hh:mm tt");
            }
            catch
            {
                return utcDateTime.ToString("hh:mm tt");
            }
        }

        private async Task LoadRideData()
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                _currentRideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);

                if (_currentRideRequest == null)
                {
                    LoadingOverlay.IsVisible = false;
                    await ShowModal("Error", "Ride not found. Please go back and try again.", "error", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                _commuterId = _currentRideRequest.CommuterId;
                _commuterName = _currentRideRequest.CommuterName;
                _fareAmount = _currentRideRequest.Fare;

                string rideDisplayId = !string.IsNullOrEmpty(_currentRideRequest.Id)
                    ? _currentRideRequest.Id
                    : _rideRequestId;
                RideIdLabel.Text = rideDisplayId;

                if (!string.IsNullOrEmpty(_currentRideRequest.PaymentReceivedTime))
                {
                    try
                    {
                        _paymentReceivedTime = DateTime.Parse(_currentRideRequest.PaymentReceivedTime);
                    }
                    catch { }
                }

                await LoadPassengerDetails();

                PassengerNameLabel.Text = _commuterName ?? "Passenger";
                FareLabel.Text = $"₱{_currentRideRequest.Fare:F0}";
                DistanceLabel.Text = $"{_currentRideRequest.Distance:F1} km";
                DurationLabel.Text = $"{_currentRideRequest.EstimatedTime} mins";
                PickupLabel.Text = ShortenAddress(_currentRideRequest.PickupLocation);
                DropoffLabel.Text = ShortenAddress(_currentRideRequest.DropoffLocation);

                string currentStatus = _currentRideRequest.PaymentStatus ?? "";
                _paymentMethod = _currentRideRequest.PaymentMethod ?? "";
                await UpdatePaymentUI(currentStatus);

                if (_currentRideRequest.Rating > 0)
                {
                    ShowRating(_currentRideRequest.Rating);
                }

                LoadingOverlay.IsVisible = false;
                _isFirstLoad = false;
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await ShowModal("Connection Error", "Failed to load ride details. Please check your internet and try again.", "warning", false);
            }
        }

        private void StartPaymentMethodListener()
        {
            try
            {
                _paymentMethodListener = _firebaseConnection.ListenForPaymentMethodChange(
                    _rideRequestId,
                    async (paymentMethod) =>
                    {
                        if (!_isListenerActive || string.IsNullOrEmpty(paymentMethod)) return;

                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            if (_paymentMethod != paymentMethod)
                            {
                                _paymentMethod = paymentMethod;
                                UpdatePaymentMethodUI(paymentMethod, "Updated by passenger");
                                ShowModal("Payment Method Updated", $"Passenger changed to {paymentMethod} payment", "info", false);
                            }
                        });
                    });
            }
            catch { }
        }

        private async Task UpdatePaymentUI(string paymentStatus)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(_paymentMethod))
                    {
                        UpdatePaymentMethodUI(_paymentMethod, "Selected by passenger");
                    }
                    else
                    {
                        SelectedPaymentLabel.Text = "Select Payment";
                        SelectedPaymentDescription.Text = "Passenger is selecting payment method";
                    }

                    switch (paymentStatus)
                    {
                        case "Waiting To Confirm":
                            if (string.IsNullOrEmpty(_paymentMethod))
                            {
                                Device.BeginInvokeOnMainThread(async () =>
                                {
                                    await LoadCommuterDefaultPayment();
                                    await UpdatePaymentUI(paymentStatus);
                                });
                                return;
                            }

                            _paymentMarkedAsPaid = true;
                            PaymentReadyFrame.IsVisible = true;

                            StatusTitleLabel.Text = "Payment Ready";
                            StatusSubtitleLabel.Text = "Passenger has sent payment";
                            StatusIconLabel.Text = "💰";
                            StatusIconFrame.BackgroundColor = Color.FromArgb("#4CAF50");
                            PaymentStatusBanner.BackgroundColor = Color.FromArgb("#E8F5E9");

                            RealtimeStatusLabel.Text = $"✅ {_paymentMethod} payment sent!";
                            RealtimeStatusIndicator.Color = Color.FromArgb("#4CAF50");
                            RealtimeStatusIndicator.IsRunning = true;

                            ((Label)PaymentStatusBadge.Content).Text = "Ready";
                            ((Label)PaymentStatusBadge.Content).TextColor = Color.FromArgb("#2E7D32");
                            PaymentStatusBadge.BackgroundColor = Color.FromArgb("#E8F5E9");

                            if (_paymentReceivedTime != DateTime.MinValue)
                            {
                                PaymentTimeLabel.Text = $"Received at: {FormatPhilippineTime(_paymentReceivedTime)}";
                                PaymentTimeLabel.IsVisible = true;
                            }

                            PaymentReceivedButton.IsEnabled = true;
                            PaymentReceivedButton.BackgroundColor = Color.FromArgb("#4CAF50");
                            PaymentReceivedButton.TextColor = Colors.White;
                            PaymentReceivedButton.Text = $"CONFIRM ₱{_fareAmount:F0} RECEIVED";

                            ReturnHomeButton.IsVisible = false;
                            ReturnHomeButton.IsEnabled = false;

                            if (!_isFirstLoad)
                            {
                                try { Vibration.Vibrate(100); } catch { }
                            }
                            break;

                        case "Confirmed":
                            _paymentMarkedAsPaid = true;
                            _isPaymentConfirmed = true;
                            PaymentReadyFrame.IsVisible = false;

                            StatusTitleLabel.Text = "Payment Confirmed";
                            StatusSubtitleLabel.Text = "Payment received and confirmed";
                            StatusIconLabel.Text = "✅";
                            StatusIconFrame.BackgroundColor = Color.FromArgb("#4CAF50");
                            PaymentStatusBanner.BackgroundColor = Color.FromArgb("#E8F5E9");

                            RealtimeStatusLabel.Text = "✅ Payment confirmed! Ride completed.";
                            RealtimeStatusIndicator.IsRunning = false;
                            RealtimeStatusIndicator.Color = Color.FromArgb("#4CAF50");

                            ((Label)PaymentStatusBadge.Content).Text = "Confirmed";
                            ((Label)PaymentStatusBadge.Content).TextColor = Color.FromArgb("#2E7D32");
                            PaymentStatusBadge.BackgroundColor = Color.FromArgb("#E8F5E9");

                            if (_paymentReceivedTime != DateTime.MinValue)
                            {
                                PaymentTimeLabel.Text = $"Confirmed at: {FormatPhilippineTime(DateTime.UtcNow)} (Received: {FormatPhilippineTime(_paymentReceivedTime)})";
                                PaymentTimeLabel.IsVisible = true;
                            }

                            PaymentReceivedButton.IsEnabled = false;
                            PaymentReceivedButton.Text = "✅ PAYMENT CONFIRMED";
                            PaymentReceivedButton.BackgroundColor = Color.FromArgb("#E0E0E0");

                            ReturnHomeButton.IsVisible = true;
                            ReturnHomeButton.IsEnabled = true;
                            ReturnHomeButton.BackgroundColor = Color.FromArgb("#2E7D32");
                            ReturnHomeButton.TextColor = Colors.White;

                            StartAutoReturnToDriverShell();

                            try { Vibration.Vibrate(200); } catch { }
                            break;

                        default:
                            _paymentMarkedAsPaid = false;
                            PaymentReadyFrame.IsVisible = false;

                            StatusTitleLabel.Text = "Waiting for Payment";
                            StatusSubtitleLabel.Text = "Passenger is completing payment";
                            StatusIconLabel.Text = "⏳";
                            StatusIconFrame.BackgroundColor = Color.FromArgb("#FF9800");
                            PaymentStatusBanner.BackgroundColor = Color.FromArgb("#FFF3E0");

                            RealtimeStatusLabel.Text = "Waiting for passenger payment...";
                            RealtimeStatusIndicator.Color = Color.FromArgb("#2E7D32");
                            RealtimeStatusIndicator.IsRunning = true;

                            ((Label)PaymentStatusBadge.Content).Text = "Waiting";
                            ((Label)PaymentStatusBadge.Content).TextColor = Color.FromArgb("#FF9800");
                            PaymentStatusBadge.BackgroundColor = Color.FromArgb("#FFF3E0");

                            PaymentTimeLabel.IsVisible = false;

                            PaymentReceivedButton.IsEnabled = false;
                            PaymentReceivedButton.BackgroundColor = Color.FromArgb("#E0E0E0");
                            PaymentReceivedButton.TextColor = Color.FromArgb("#999999");
                            PaymentReceivedButton.Text = "CONFIRM PAYMENT RECEIVED";

                            ReturnHomeButton.IsVisible = false;
                            ReturnHomeButton.IsEnabled = false;
                            break;
                    }
                }
                catch { }
            });
        }

        private void StartUltraFastRealtimeListeners()
        {
            try
            {
                _paymentListener = _firebaseConnection.ListenForPaymentStatusChangeMegaFast(
                    _rideRequestId,
                    async (rideRequest) =>
                    {
                        if (!_isListenerActive || rideRequest == null) return;

                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            try
                            {
                                _currentRideRequest = rideRequest;

                                if (rideRequest.PaymentStatus == "Waiting To Confirm" && !_paymentMarkedAsPaid)
                                {
                                    if (!string.IsNullOrEmpty(rideRequest.PaymentReceivedTime))
                                    {
                                        _paymentReceivedTime = DateTime.Parse(rideRequest.PaymentReceivedTime);
                                    }
                                }

                                if (!string.IsNullOrEmpty(rideRequest.PaymentMethod) &&
                                    rideRequest.PaymentMethod != _paymentMethod)
                                {
                                    _paymentMethod = rideRequest.PaymentMethod;
                                    UpdatePaymentMethodUI(_paymentMethod, "Selected by passenger");
                                }

                                await UpdatePaymentUI(rideRequest.PaymentStatus);

                                if (rideRequest.Rating > 0)
                                {
                                    ShowRating(rideRequest.Rating);
                                }

                                if (rideRequest.PaymentStatus == "Confirmed" && !_isPaymentConfirmed)
                                {
                                    _isPaymentConfirmed = true;
                                    await ShowModal("Payment Confirmed", $"₱{rideRequest?.Fare:F0} confirmed!", "success", false);
                                }
                            }
                            catch { }
                        });
                    });

                _paymentMonitor = _firebaseConnection.StartPaymentStatusMonitor(
                    _rideRequestId,
                    async (status) =>
                    {
                        if (!_isListenerActive || string.IsNullOrEmpty(status)) return;
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await UpdatePaymentUI(status);
                        });
                    });
            }
            catch { }
        }

        private async Task LoadPassengerDetails()
        {
            try
            {
                if (!string.IsNullOrEmpty(_commuterId))
                {
                    _passengerInfo = await _firebaseConnection.GetCommuterByIdAsync(_commuterId);
                    if (_passengerInfo != null && !string.IsNullOrEmpty(_passengerInfo.ProfileImageUrl))
                    {
                        try
                        {
                            PassengerImage.Source = ImageSource.FromUri(new Uri(_passengerInfo.ProfileImageUrl));
                        }
                        catch
                        {
                            PassengerImage.Source = "profile_icon.png";
                        }
                    }
                }
            }
            catch { }
        }

        private async Task LoadCommuterDefaultPayment()
        {
            try
            {
                if (!string.IsNullOrEmpty(_commuterId))
                {
                    string defaultMethod = await _firebaseConnection.GetDefaultPaymentMethodAsync(_commuterId);
                    if (!string.IsNullOrEmpty(defaultMethod))
                    {
                        _paymentMethod = defaultMethod;
                    }
                }
            }
            catch { }
        }

        private string ShortenAddress(string address, int maxLength = 40)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";

            var cleanAddress = address.Replace(", Malolos, Bulacan", "")
                                     .Replace(", Bulacan", "")
                                     .Replace("Malolos, ", "")
                                     .Replace("Philippines", "")
                                     .Trim();

            if (cleanAddress.Length > maxLength)
                return cleanAddress.Substring(0, maxLength - 3) + "...";
            return cleanAddress;
        }

        private void StartAutoReturnToDriverShell()
        {
            Device.StartTimer(TimeSpan.FromSeconds(3), () =>
            {
                if (!_isListenerActive || !_isPaymentConfirmed) return false;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    Application.Current.MainPage = new DriverShell();
                });
                return false;
            });
        }

        private void UpdatePaymentMethodUI(string method, string description)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (method == "Cash")
                {
                    SelectedPaymentIconFrame.BackgroundColor = Color.FromArgb("#E8F5E9");
                    SelectedPaymentIcon.Source = "cash.png";
                    SelectedPaymentLabel.Text = "Cash Payment";
                    SelectedPaymentDescription.Text = description;
                    SelectedPaymentFrame.BorderColor = Color.FromArgb("#E0E0E0");

                    ((Label)PaymentStatusBadge.Content).Text = "Cash";
                    ((Label)PaymentStatusBadge.Content).TextColor = Color.FromArgb("#FF9800");
                    PaymentStatusBadge.BackgroundColor = Color.FromArgb("#FFF3E0");
                }
                else if (method == "GCash")
                {
                    SelectedPaymentIconFrame.BackgroundColor = Color.FromArgb("#E8F4FD");
                    SelectedPaymentIcon.Source = "gcash.png";
                    SelectedPaymentLabel.Text = "GCash Payment";
                    SelectedPaymentDescription.Text = description;
                    SelectedPaymentFrame.BorderColor = Color.FromArgb("#007AFF");

                    ((Label)PaymentStatusBadge.Content).Text = "GCash";
                    ((Label)PaymentStatusBadge.Content).TextColor = Color.FromArgb("#007AFF");
                    PaymentStatusBadge.BackgroundColor = Color.FromArgb("#E8F4FD");
                }
                else
                {
                    SelectedPaymentLabel.Text = "Select Payment";
                    SelectedPaymentDescription.Text = "Waiting for passenger selection...";
                }
            });
        }

        private void ShowRating(int rating)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RatingReceivedFrame.IsVisible = true;
                UpdateReceivedStars(rating);
                RatingMessageLabel.Text = $"{_commuterName} rated your service";
            });
        }

        private void UpdateReceivedStars(int rating)
        {
            var stars = new Label[] { ReceivedStar1, ReceivedStar2, ReceivedStar3, ReceivedStar4, ReceivedStar5 };
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].TextColor = i < rating ?
                    Color.FromArgb("#FFD700") :
                    Color.FromArgb("#E0E0E0");
            }
        }

        private async void OnPaymentReceivedClicked(object sender, EventArgs e)
        {
            if (_isProcessingPayment) return;

            if (_currentRideRequest?.PaymentStatus != "Waiting To Confirm")
            {
                await ShowModal("Wait", "Passenger hasn't marked payment as sent yet", "warning", false);
                return;
            }

            if (string.IsNullOrEmpty(_paymentMethod))
            {
                await ShowModal("Error", "Payment method not specified", "warning", false);
                return;
            }

            string phTimeDisplay = _paymentReceivedTime != DateTime.MinValue
                ? FormatPhilippineTime(_paymentReceivedTime)
                : "unknown time";

            await ShowPaymentConfirmation(phTimeDisplay);
        }

        private async Task ShowPaymentConfirmation(string phTimeDisplay)
        {
            _isModalOpen = true;

            string iconSource = "happy.png";
            Color iconColor = Color.FromArgb("#2E7D32");

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
                            Text = "Confirm Payment",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = $"Received ₱{_currentRideRequest?.Fare:F0} from {_commuterName}?\n\nPayment Method: {_paymentMethod}\nReceived at: {phTimeDisplay}",
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
                                    Text = "NO",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithWaitingPaymentGridColumn(0),
                                new Button
                                {
                                    Text = "YES, CONFIRM",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithWaitingPaymentGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new WaitingPaymentConfirmModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void ConfirmPayment()
        {
            _isModalOpen = false;
            await ConfirmPaymentReceived();
        }

        private async Task ConfirmPaymentReceived()
        {
            if (_isProcessingPayment) return;

            _isProcessingPayment = true;

            try
            {
                LoadingOverlay.IsVisible = true;

                bool success = await _firebaseConnection.ConfirmPaymentWithFeedbackAsync(
                    _rideRequestId,
                    _currentRideRequest.Fare,
                    _driverId);

                LoadingOverlay.IsVisible = false;

                if (success)
                {
                    await UpdatePaymentUI("Confirmed");
                    await ShowModal("Payment Confirmed", $"₱{_currentRideRequest?.Fare:F0} received from {_commuterName}!", "success", false);
                    _isPaymentConfirmed = true;
                    Application.Current.MainPage = new DriverShell();
                }
                else
                {
                    await ShowModal("Error", "Confirmation failed. Please try again.", "error", false);
                }
            }
            catch
            {
                LoadingOverlay.IsVisible = false;
                await ShowModal("Connection Error", "Failed to confirm payment. Please check your internet.", "warning", false);
            }
            finally
            {
                _isProcessingPayment = false;
            }
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            if (_isPaymentConfirmed)
            {
                Application.Current.MainPage = new DriverShell();
                return;
            }

            await ShowBackConfirmationModal();
        }

        private async Task ShowBackConfirmationModal()
        {
            _isModalOpen = true;

            string iconSource = "complaint.png";
            Color iconColor = Color.FromArgb("#FF9800");

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
                            Text = "Leave Payment?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to leave? You won't receive payment confirmation.",
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
                                    Text = "STAY",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithWaitingPaymentGridColumn(0),
                                new Button
                                {
                                    Text = "LEAVE",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithWaitingPaymentGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new WaitingPaymentBackModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void ConfirmBack()
        {
            _isModalOpen = false;
            await Navigation.PopModalAsync();
        }

        private async void OnReturnHomeClicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new DriverShell();
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

            var modalPage = new WaitingPaymentModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modalPage);

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
    }

    public class WaitingPaymentModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_WaitingPayment _parentPage;

        public WaitingPaymentModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_WaitingPayment parentPage)
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

    public class WaitingPaymentConfirmModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_WaitingPayment _parentPage;

        public WaitingPaymentConfirmModalPage(Frame modalContent, Grid blurOverlay, Driver_WaitingPayment parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();

            if (confirm)
            {
                _parentPage.ConfirmPayment();
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

    public class WaitingPaymentBackModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_WaitingPayment _parentPage;

        public WaitingPaymentBackModalPage(Frame modalContent, Grid blurOverlay, Driver_WaitingPayment parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();

            if (confirm)
            {
                _parentPage.ConfirmBack();
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

    public static class WaitingPaymentGridExtensions
    {
        public static Button WithWaitingPaymentGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}