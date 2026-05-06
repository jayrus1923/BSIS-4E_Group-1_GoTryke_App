using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_RideSummary : ContentPage
    {
        private string _rideRequestId;
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private RideRequest _currentRideRequest;
        private Driver _driverInfo;
        private bool _isProcessing = false;
        private bool _isModalOpen = false;

        public Driver_RideSummary(string rideRequestId)
        {
            InitializeComponent();
            _rideRequestId = rideRequestId;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadRideData();
            await LoadDriverStats();
        }

        private async Task LoadRideData()
        {
            try
            {
                _currentRideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);

                if (_currentRideRequest != null)
                {
                    RideIdLabel.Text = _currentRideRequest.Id ?? "Unknown";
                    PassengerLabel.Text = _currentRideRequest.CommuterName ?? "Passenger";
                    PickupLabel.Text = ShortenAddress(_currentRideRequest.PickupLocation);
                    DropoffLabel.Text = ShortenAddress(_currentRideRequest.DropoffLocation);
                    FareLabel.Text = $"₱{_currentRideRequest.Fare:F0}";
                    PaymentMethodLabel.Text = _currentRideRequest.PaymentMethod ?? "Cash";
                    PaymentStatusLabel.Text = "Paid & Confirmed";
                    TimeLabel.Text = DateTime.Now.ToString("hh:mm tt");
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Failed to load ride data: " + ex.Message, "error", false);
            }
        }

        private async Task LoadDriverStats()
        {
            try
            {
                string driverId = AppSession.GetDriverId();
                if (!string.IsNullOrEmpty(driverId))
                {
                    _driverInfo = await _firebaseConnection.GetDriverByIdAsync(driverId);

                    if (_driverInfo != null)
                    {
                        RidesTodayLabel.Text = (_driverInfo.TodayCompletedRides ?? 0).ToString();
                        EarningsTodayLabel.Text = $"₱{(_driverInfo.TodayEarnings ?? 0):F0}";
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Error", "Failed to load stats: " + ex.Message, "error", false);
            }
        }

        private string ShortenAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";
            var cleanAddress = address.Replace(", Malolos, Bulacan", "")
                                     .Replace(", Bulacan", "")
                                     .Replace("Malolos, ", "")
                                     .Trim();
            if (cleanAddress.Length > 25)
                return cleanAddress.Substring(0, 22) + "...";
            return cleanAddress;
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            _isModalOpen = true;
            string iconSource = "complaint.png";
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
                            Text = "Exit Ride Summary?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to exit?",
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
                                }.WithDriverRideSummaryGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverRideSummaryGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new DriverRideSummaryExitModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(exitModal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async void OnViewHistoryClicked(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;
            await ShowModal("Coming Soon", "Ride history feature is coming soon!", "info", false);
        }

        private void ShowProcessing(string message = "Loading...")
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

            var modal = new DriverRideSummaryCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

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

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_isProcessing && !_isModalOpen)
                {
                    _isModalOpen = true;
                    string iconSource = "complaint.png";
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
                                    Text = "Exit Ride Summary?",
                                    FontSize = 18,
                                    FontAttributes = FontAttributes.Bold,
                                    TextColor = iconColor,
                                    HorizontalOptions = LayoutOptions.Center
                                },
                                new Label
                                {
                                    Text = "Are you sure you want to exit?",
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
                                        }.WithDriverRideSummaryGridColumn(0),
                                        new Button
                                        {
                                            Text = "EXIT",
                                            BackgroundColor = iconColor,
                                            TextColor = Colors.White,
                                            CornerRadius = 10,
                                            HeightRequest = 45,
                                            FontSize = 14,
                                            FontAttributes = FontAttributes.Bold
                                        }.WithDriverRideSummaryGridColumn(1)
                                    }
                                }
                            }
                        }
                    };

                    var exitModal = new DriverRideSummaryExitModalPage(modalContent, blurOverlay, this);
                    await Navigation.PushModalAsync(exitModal);

                    await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
                    await Task.WhenAll(
                        modalContent.FadeTo(1, 350, Easing.SpringOut),
                        modalContent.ScaleTo(1, 450, Easing.SpringOut)
                    );
                }
            });
            return true;
        }
    }

    public class DriverRideSummaryCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_RideSummary _parentPage;

        public DriverRideSummaryCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_RideSummary parentPage)
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

    public class DriverRideSummaryExitModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_RideSummary _parentPage;

        public DriverRideSummaryExitModalPage(Frame modalContent, Grid blurOverlay, Driver_RideSummary parentPage)
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
                    await _parentPage.Navigation.PopToRootAsync();
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

    public static class DriverRideSummaryGridExtensions
    {
        public static Button WithDriverRideSummaryGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}