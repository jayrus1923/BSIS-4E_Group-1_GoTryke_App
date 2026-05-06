using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.IO;

namespace ServiceCo
{
    public partial class Commuter_Payment : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _rideRequestId;
        private string _driverId;
        private string _commuterId;
        private string _commuterName;
        private double _fare;
        private string _selectedPaymentMethod = "";
        private int _rating = 0;
        private Frame[] _stars;
        private IDisposable _paymentListener;
        private bool _paymentSent = false;
        private bool _isPaymentConfirmed = false;
        private bool _isListenerActive = true;
        private string _gcashQrUrl = "";
        private DateTime _paymentTime;
        private bool _isModalOpen = false;

        public Commuter_Payment(string rideRequestId)
        {
            InitializeComponent();
            _rideRequestId = rideRequestId;
            _commuterId = AppSession.GetCommuterId();
            _commuterName = AppSession.GetCommuterName();

            SetupUI();
            SetupRatingButtons();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _isListenerActive = true;
            await LoadRideData();
            StartPaymentListener();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isListenerActive = false;
            _paymentListener?.Dispose();
        }

        private void SetupUI()
        {
            _stars = new Frame[] { Star1, Star2, Star3, Star4, Star5 };

            SelectedPaymentLabel.Text = "Select Payment";
            SelectedPaymentDescription.Text = "Tap to change payment method";

            PayButton.IsEnabled = false;
            PaymentSentFrame.IsVisible = false;
            PaymentConfirmedFrame.IsVisible = false;
            QrCodeFrame.IsVisible = false;
            RealtimeStatusFrame.IsVisible = false;
            FullScreenQrGrid.IsVisible = false;

            DriverImage.Source = "profile_icon.png";
        }

        private void SetupRatingButtons()
        {
            PoorButton.Clicked += OnPoorButtonClicked;
            AverageButton.Clicked += OnAverageButtonClicked;
            ExcellentButton.Clicked += OnExcellentButtonClicked;
        }

        private void OnPoorButtonClicked(object sender, EventArgs e) => SetRatingFromFeedback("Poor", 1);
        private void OnAverageButtonClicked(object sender, EventArgs e) => SetRatingFromFeedback("Average", 3);
        private void OnExcellentButtonClicked(object sender, EventArgs e) => SetRatingFromFeedback("Excellent", 5);

        private void SetRatingFromFeedback(string feedback, int stars)
        {
            _rating = stars;
            UpdateStars(stars);
            RatingText.Text = $"{stars} star{(stars > 1 ? "s" : "")} - {feedback}";

            ResetFeedbackButtons();
            switch (feedback)
            {
                case "Poor":
                    PoorButton.BackgroundColor = Color.FromArgb("#FFCDD2");
                    PoorButton.TextColor = Color.FromArgb("#D32F2F");
                    break;
                case "Average":
                    AverageButton.BackgroundColor = Color.FromArgb("#FFF3E0");
                    AverageButton.TextColor = Color.FromArgb("#F57C00");
                    break;
                case "Excellent":
                    ExcellentButton.BackgroundColor = Color.FromArgb("#E8F5E9");
                    ExcellentButton.TextColor = Color.FromArgb("#388E3C");
                    break;
            }

            if (!string.IsNullOrEmpty(_selectedPaymentMethod)) EnablePayButton();
        }

        private void ResetFeedbackButtons()
        {
            PoorButton.BackgroundColor = Color.FromArgb("#F5F5F5");
            PoorButton.TextColor = Color.FromArgb("#666666");
            AverageButton.BackgroundColor = Color.FromArgb("#F5F5F5");
            AverageButton.TextColor = Color.FromArgb("#666666");
            ExcellentButton.BackgroundColor = Color.FromArgb("#F5F5F5");
            ExcellentButton.TextColor = Color.FromArgb("#666666");
        }

        private async Task LoadRideData()
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
                if (rideRequest == null)
                {
                    LoadingOverlay.IsVisible = false;
                    await ShowModal("Error", "Ride not found", "error", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                _fare = rideRequest.Fare;
                _driverId = rideRequest.DriverId;

                RideIdLabel.Text = rideRequest.Id ?? "Ride";
                PickupLabel.Text = ShortenAddress(rideRequest.PickupLocation);
                DropoffLabel.Text = ShortenAddress(rideRequest.DropoffLocation);
                DistanceLabel.Text = $"{rideRequest.Distance:F1} km";
                DurationLabel.Text = $"{rideRequest.EstimatedTime} mins";
                TotalFareLabel.Text = $"₱{_fare:F0}";
                QrInstructionsLabel.Text = $"4. Enter ₱{_fare:F0} and confirm";

                if (!string.IsNullOrEmpty(_driverId))
                {
                    var driver = await _firebaseConnection.GetDriverByIdAsync(_driverId);
                    if (driver != null)
                    {
                        DriverNameLabel.Text = $"{driver.FirstName} {driver.LastName}";
                        DriverRatingLabel.Text = driver.Rating.ToString("F1");
                        DriverRidesLabel.Text = $"{driver.TotalCompletedRides ?? 0} rides";

                        if (!string.IsNullOrEmpty(driver.ProfileImageUrl))
                        {
                            try
                            {
                                DriverImage.Source = ImageSource.FromUri(new Uri(driver.ProfileImageUrl));
                            }
                            catch
                            {
                                DriverImage.Source = "profile_icon.png";
                            }
                        }

                        if (driver.Documents != null && driver.Documents.ContainsKey("GCash_QR_Code"))
                        {
                            _gcashQrUrl = driver.Documents["GCash_QR_Code"];

                            if (!string.IsNullOrEmpty(_gcashQrUrl))
                            {
                                try
                                {
                                    QrCodeImage.Source = ImageSource.FromUri(new Uri(_gcashQrUrl));
                                    FullScreenQrImage.Source = ImageSource.FromUri(new Uri(_gcashQrUrl));
                                    DownloadQrButton.IsVisible = true;
                                    QrStatusFrame.IsVisible = true;
                                    QrStatusFrame.BackgroundColor = Color.FromArgb("#E8F5E9");
                                    QrStatusLabel.Text = "✅ Driver's GCash QR Code Ready";
                                    QrStatusLabel.TextColor = Color.FromArgb("#2E7D32");
                                }
                                catch
                                {
                                    ShowNoQRCode();
                                }
                            }
                            else
                            {
                                ShowNoQRCode();
                            }
                        }
                        else
                        {
                            ShowNoQRCode();
                        }
                    }
                    else
                    {
                        ShowNoQRCode();
                    }
                }

                if (!string.IsNullOrEmpty(rideRequest.PaymentMethod))
                {
                    _selectedPaymentMethod = rideRequest.PaymentMethod;
                    SelectPaymentMethod(_selectedPaymentMethod);
                }
                else
                {
                    string savedMethod = await _firebaseConnection.GetDefaultPaymentMethodAsync(_commuterId);
                    if (!string.IsNullOrEmpty(savedMethod))
                    {
                        _selectedPaymentMethod = savedMethod;
                        SelectPaymentMethod(_selectedPaymentMethod);
                        await _firebaseConnection.UpdatePaymentMethodAsync(_rideRequestId, _selectedPaymentMethod);
                    }
                }

                string currentStatus = rideRequest.PaymentStatus ?? "";
                _paymentSent = currentStatus == "Waiting To Confirm";
                _isPaymentConfirmed = currentStatus == "Confirmed";

                if (!string.IsNullOrEmpty(rideRequest.PaymentReceivedTime))
                {
                    try
                    {
                        _paymentTime = DateTime.Parse(rideRequest.PaymentReceivedTime);
                    }
                    catch { }
                }

                UpdatePaymentStatusUI(currentStatus);

                LoadingOverlay.IsVisible = false;
            }
            catch
            {
                LoadingOverlay.IsVisible = false;
                await ShowModal("Connection Error", "Failed to load ride details. Please check your internet.", "warning", false);
            }
        }

        private void ShowNoQRCode()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                QrCodeImage.Source = "no_qr.png";
                FullScreenQrImage.Source = "no_qr.png";
                DownloadQrButton.IsVisible = false;
                QrStatusFrame.IsVisible = true;
                QrStatusFrame.BackgroundColor = Color.FromArgb("#FFF3E0");
                QrStatusLabel.Text = "ℹ️ GCash QR Code Not Available";
                QrStatusLabel.TextColor = Color.FromArgb("#FF9800");
            });
        }

        private void UpdatePaymentStatusUI(string paymentStatus)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (paymentStatus)
                {
                    case "Waiting To Confirm":
                        PaymentStatusIcon.Text = "⏳";
                        PaymentStatusLabel.Text = "PAYMENT SENT";
                        PaymentStatusSubtitle.Text = "Waiting for driver confirmation";
                        PaymentStatusBanner.BackgroundColor = Color.FromArgb("#FFF3E0");
                        StatusIconFrame.BackgroundColor = Color.FromArgb("#FF9800");

                        PaymentSentFrame.IsVisible = true;
                        PaymentSentMessage.Text = "Payment sent. Waiting for driver to confirm receipt.";

                        PayButton.IsVisible = true;
                        SkipRatingButton.IsVisible = true;
                        RatingFrame.IsVisible = true;

                        QrCodeFrame.IsVisible = false;

                        RealtimeStatusFrame.IsVisible = true;
                        RealtimeStatusText.Text = "Payment sent. Waiting for driver confirmation...";
                        RealtimeStatusIndicator.IsRunning = true;

                        if (_paymentTime != DateTime.MinValue)
                        {
                            PaymentSentTimeLabel.Text = $"Sent at: {_paymentTime:hh:mm tt}";
                            PaymentSentTimeLabel.IsVisible = true;
                        }
                        break;

                    case "Confirmed":
                        PaymentStatusIcon.Text = "✅";
                        PaymentStatusLabel.Text = "PAYMENT CONFIRMED";
                        PaymentStatusSubtitle.Text = "Driver confirmed payment";
                        PaymentStatusBanner.BackgroundColor = Color.FromArgb("#E8F5E9");
                        StatusIconFrame.BackgroundColor = Color.FromArgb("#4CAF50");

                        PaymentSentFrame.IsVisible = false;
                        PaymentConfirmedFrame.IsVisible = true;
                        ConfirmedAmountLabel.Text = $"₱{_fare:F0}";
                        RealtimeStatusFrame.IsVisible = false;

                        PayButton.IsVisible = false;
                        SkipRatingButton.IsVisible = false;
                        RatingFrame.IsVisible = false;
                        QrCodeFrame.IsVisible = false;

                        if (_paymentTime != DateTime.MinValue)
                        {
                            PaymentConfirmedTimeLabel.Text = $"Paid at: {_paymentTime:hh:mm tt}";
                            PaymentConfirmedTimeLabel.IsVisible = true;
                        }

                        StartAutoRedirect();
                        break;

                    default:
                        PaymentStatusIcon.Text = "💳";
                        PaymentStatusLabel.Text = "COMPLETE YOUR PAYMENT";
                        PaymentStatusSubtitle.Text = "Select payment method and rate driver";
                        PaymentStatusBanner.BackgroundColor = Color.FromArgb("#E8F5E9");
                        StatusIconFrame.BackgroundColor = Color.FromArgb("#4CAF50");

                        PayButton.IsVisible = true;
                        SkipRatingButton.IsVisible = true;
                        RatingFrame.IsVisible = true;
                        PaymentSentFrame.IsVisible = false;
                        PaymentConfirmedFrame.IsVisible = false;
                        RealtimeStatusFrame.IsVisible = false;

                        QrCodeFrame.IsVisible = (_selectedPaymentMethod == "GCash" && !string.IsNullOrEmpty(_gcashQrUrl));

                        PaymentSentTimeLabel.IsVisible = false;
                        PaymentConfirmedTimeLabel.IsVisible = false;
                        break;
                }
            });
        }

        private void SelectPaymentMethod(string method)
        {
            _selectedPaymentMethod = method;

            if (method == "Cash")
            {
                SelectedPaymentIcon.Source = "cash.png";
                SelectedPaymentIconFrame.BackgroundColor = Color.FromArgb("#E8F5E9");
                SelectedPaymentLabel.Text = "Cash Payment";
                SelectedPaymentDescription.Text = "Pay cash to driver";
                QrCodeFrame.IsVisible = false;
            }
            else if (method == "GCash")
            {
                SelectedPaymentIcon.Source = "gcash.png";
                SelectedPaymentIconFrame.BackgroundColor = Color.FromArgb("#E8F4FD");
                SelectedPaymentLabel.Text = "GCash Payment";
                SelectedPaymentDescription.Text = "Scan QR code to pay";

                QrCodeFrame.IsVisible = !string.IsNullOrEmpty(_gcashQrUrl);

                if (string.IsNullOrEmpty(_gcashQrUrl))
                {
                    ShowModal("GCash Not Available", "This driver hasn't set up GCash QR code yet. Please select Cash payment.", "warning", false);
                }
            }

            if (_rating > 0 || _paymentSent) EnablePayButton();
        }

        private void StartPaymentListener()
        {
            try
            {
                _paymentListener = _firebaseConnection.ListenForPaymentStatusChangeMegaFast(
                    _rideRequestId,
                    async (rideRequest) =>
                    {
                        if (!_isListenerActive || rideRequest == null) return;

                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            if (rideRequest.PaymentStatus == "Waiting To Confirm" && !_paymentSent)
                            {
                                _paymentTime = DateTime.Now;
                            }

                            UpdatePaymentStatusUI(rideRequest.PaymentStatus);

                            if (rideRequest.PaymentStatus == "Confirmed" && !_isPaymentConfirmed)
                            {
                                _isPaymentConfirmed = true;
                                RealtimeStatusText.Text = "✅ Payment confirmed by driver!";
                                RealtimeStatusIndicator.IsRunning = false;
                                ShowModal("Payment Confirmed", $"Driver confirmed payment of ₱{_fare:F0}", "success", false);
                            }
                        });
                    });
            }
            catch { }
        }

        private void StartAutoRedirect()
        {
            Device.StartTimer(TimeSpan.FromSeconds(3), () =>
            {
                if (!_isListenerActive) return false;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Application.Current.MainPage = new CommuterShell();
                });
                return false;
            });
        }

        private string ShortenAddress(string address, int maxLength = 30)
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

        private void EnablePayButton()
        {
            if (_paymentSent) return;

            PayButton.IsEnabled = true;
            PayButton.BackgroundColor = Color.FromArgb("#2E7D32");
            PayButton.TextColor = Colors.White;
        }

        private async void OnPayButtonClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPaymentMethod))
            {
                await ShowModal("Select Payment", "Please select payment method", "warning", false);
                return;
            }

            if (_selectedPaymentMethod == "GCash" && string.IsNullOrEmpty(_gcashQrUrl))
            {
                await ShowModal("GCash Not Available", "This driver hasn't set up GCash QR code. Please select Cash payment instead.", "warning", false);
                return;
            }

            if (_selectedPaymentMethod == "GCash")
            {
                await ShowGCashConfirmation();
                return;
            }

            await ShowPaymentConfirmation();
        }

        private async Task ShowGCashConfirmation()
        {
            _isModalOpen = true;

            string iconSource = "gcash.png";
            Color iconColor = Color.FromArgb("#007AFF");

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
                            Text = "GCash Payment",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = $"1. Open GCash app\n2. Scan QR code\n3. Enter ₱{_fare:F0}\n4. Complete payment\n\nTap 'ALREADY PAID' after payment",
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
                                }.WithPaymentGridColumn(0),
                                new Button
                                {
                                    Text = "ALREADY PAID",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new CommuterPaymentGCashModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void ConfirmGCashPayment()
        {
            _isModalOpen = false;
            await ShowPaymentConfirmation();
        }

        private async Task ShowPaymentConfirmation()
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
                            Text = $"Pay ₱{_fare:F0} using {_selectedPaymentMethod}?",
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
                                }.WithPaymentGridColumn(0),
                                new Button
                                {
                                    Text = "CONFIRM",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new CommuterPaymentConfirmModalPage(modalContent, blurOverlay, this);
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
            await ProcessPayment();
        }

        private async Task ProcessPayment()
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                if (_rating > 0 && !string.IsNullOrEmpty(_driverId))
                {
                    await _firebaseConnection.SaveCommuterRatingAsync(_rideRequestId, _driverId, _rating, _commuterName);
                }

                _paymentTime = DateTime.Now;

                bool success = await _firebaseConnection.SendPaymentWithFeedbackAsync(
                    _rideRequestId,
                    _selectedPaymentMethod,
                    _fare,
                    _commuterName);

                if (success)
                {
                    await _firebaseConnection.SaveDefaultPaymentMethodAsync(_commuterId, _selectedPaymentMethod);

                    LoadingOverlay.IsVisible = false;
                    _paymentSent = true;
                    UpdatePaymentStatusUI("Waiting To Confirm");

                    PayButton.IsEnabled = false;
                    PayButton.IsVisible = false;
                    SkipRatingButton.IsVisible = false;

                    await ShowModal("Payment Sent", $"Payment of ₱{_fare:F0} sent via {_selectedPaymentMethod} at {_paymentTime:hh:mm tt}", "success", false);
                }
                else
                {
                    LoadingOverlay.IsVisible = false;
                    await ShowModal("Error", "Payment failed. Please try again.", "error", false);
                }
            }
            catch
            {
                LoadingOverlay.IsVisible = false;
                await ShowModal("Connection Error", "Payment failed. Please check your internet.", "warning", false);
            }
        }

        private void OnStarTapped(object sender, EventArgs e)
        {
            if (_paymentSent) return;

            if (sender is Frame starFrame)
            {
                var tap = starFrame.GestureRecognizers[0] as TapGestureRecognizer;
                if (tap?.CommandParameter != null && int.TryParse(tap.CommandParameter.ToString(), out int starCount))
                {
                    _rating = starCount;
                    UpdateStars(starCount);
                    RatingText.Text = $"{starCount} star{(starCount > 1 ? "s" : "")}";
                    ResetFeedbackButtons();
                    if (!string.IsNullOrEmpty(_selectedPaymentMethod)) EnablePayButton();
                }
            }
        }

        private void UpdateStars(int count)
        {
            for (int i = 0; i < _stars.Length; i++)
            {
                if (_stars[i].Content is Image starImage)
                {
                    starImage.Source = i < count ? "star_filled.png" : "star_outline.png";
                }
            }
        }

        private async void OnSkipRatingClicked(object sender, EventArgs e)
        {
            await ShowSkipRatingConfirmation();
        }

        private async Task ShowSkipRatingConfirmation()
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
                            Text = "Skip Rating?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Skip rating driver?",
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
                                }.WithPaymentGridColumn(0),
                                new Button
                                {
                                    Text = "SKIP",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithPaymentGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new CommuterPaymentSkipModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async void ConfirmSkipRating()
        {
            _isModalOpen = false;
            _rating = 0;
            ResetFeedbackButtons();
            UpdateStars(0);
            RatingText.Text = "Rating skipped";
            if (!string.IsNullOrEmpty(_selectedPaymentMethod)) EnablePayButton();
        }

        private async void OnChangePaymentClicked(object sender, EventArgs e)
        {
            if (_paymentSent)
            {
                await ShowModal("Payment Sent", "Cannot change payment method after payment sent.", "warning", false);
                return;
            }

            var paymentOptionPage = new Commuter_PaymentOption(_selectedPaymentMethod, _rideRequestId);
            paymentOptionPage.PaymentMethodSelected += async (s, method) =>
            {
                SelectPaymentMethod(method);
                await _firebaseConnection.UpdatePaymentMethodAsync(_rideRequestId, method);
                await _firebaseConnection.SaveDefaultPaymentMethodAsync(_commuterId, method);
            };

            await Navigation.PushModalAsync(paymentOptionPage);
        }

        private void OnQrCodeTapped(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_gcashQrUrl))
            {
                FullScreenQrGrid.IsVisible = true;
            }
        }

        private void OnCloseFullScreenClicked(object sender, EventArgs e)
        {
            FullScreenQrGrid.IsVisible = false;
        }

        private async void OnDownloadQrClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                using (var client = new System.Net.Http.HttpClient())
                {
                    var imageBytes = await client.GetByteArrayAsync(_gcashQrUrl);
                    string filename = $"GCash_QR_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string filePath = Path.Combine(FileSystem.CacheDirectory, filename);
                    await File.WriteAllBytesAsync(filePath, imageBytes);

                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "Save QR Code",
                        File = new ShareFile(filePath)
                    });
                }

                LoadingOverlay.IsVisible = false;
            }
            catch
            {
                LoadingOverlay.IsVisible = false;
                await ShowModal("Error", "Failed to download QR code", "error", false);
            }
        }

        private void OnReturnHomeClicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new CommuterShell();
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

            var modalPage = new CommuterPaymentModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

    public class CommuterPaymentModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_Payment _parentPage;

        public CommuterPaymentModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_Payment parentPage)
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

    public class CommuterPaymentGCashModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_Payment _parentPage;

        public CommuterPaymentGCashModalPage(Frame modalContent, Grid blurOverlay, Commuter_Payment parentPage)
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
                _parentPage.ConfirmGCashPayment();
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

    public class CommuterPaymentConfirmModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_Payment _parentPage;

        public CommuterPaymentConfirmModalPage(Frame modalContent, Grid blurOverlay, Commuter_Payment parentPage)
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

    public class CommuterPaymentSkipModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_Payment _parentPage;

        public CommuterPaymentSkipModalPage(Frame modalContent, Grid blurOverlay, Commuter_Payment parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var skipButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            skipButton.Clicked += async (s, e) =>
            {
                skipButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool skip)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();

            if (skip)
            {
                _parentPage.ConfirmSkipRating();
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

    public static class CommuterPaymentGridExtensions
    {
        public static Button WithPaymentGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}