using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ServiceCo
{
    public partial class Commuter_Rides : ContentPage, INotifyPropertyChanged
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private List<RideRequestWithKey> _allRides = new List<RideRequestWithKey>();
        private string _commuterId;
        private bool _isLoading = false;
        private IDisposable _ridesListener;
        private bool _isModalOpen = false;

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }

        public Commuter_Rides()
        {
            InitializeComponent();
            BindingContext = this;
            RefreshCommand = new Command(async () => await RefreshRidesList());
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
            _commuterId = AppSession.GetCommuterId();
            await LoadRides();
            StartRidesListener();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _ridesListener?.Dispose();
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await this.FadeTo(0, 250, Easing.CubicIn);
                await Navigation.PopModalAsync();
            });
            return true;
        }

        private void StartRidesListener()
        {
            try
            {
                if (string.IsNullOrEmpty(_commuterId)) return;

                _ridesListener?.Dispose();

                _ridesListener = _firebaseConnection.ListenForRideStatusChangeRealTimeForCommuter(
                    _commuterId,
                    async (rideRequest) =>
                    {
                        if (rideRequest != null)
                        {
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                await RefreshRidesList();
                            });
                        }
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting rides listener: {ex.Message}");
            }
        }

        private async Task RefreshRidesList()
        {
            try
            {
                IsRefreshing = true;

                var allRideRequests = await _firebaseConnection.GetAllRideRequestsAsync();

                if (allRideRequests == null) return;

                _allRides = allRideRequests
                    .Where(r => r.CommuterId == _commuterId)
                    .OrderByDescending(r => r.RequestTime)
                    .ToList();

                DisplayRides();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing rides: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private async Task LoadRides()
        {
            try
            {
                if (_isLoading) return;
                _isLoading = true;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingIndicator.IsVisible = true;
                    LoadingIndicator.IsRunning = true;
                    EmptyStateLayout.IsVisible = false;
                    RidesRefreshView.IsVisible = false;
                });

                _commuterId = AppSession.GetCommuterId();
                if (string.IsNullOrEmpty(_commuterId))
                {
                    await ShowModal("Error", "Please login to view your rides", "error", true);
                    return;
                }

                var allRideRequests = await _firebaseConnection.GetAllRideRequestsAsync();

                if (allRideRequests == null || !allRideRequests.Any())
                {
                    ShowEmptyState();
                    return;
                }

                _allRides = allRideRequests
                    .Where(r => r.CommuterId == _commuterId)
                    .OrderByDescending(r => r.RequestTime)
                    .ToList();

                if (!_allRides.Any())
                {
                    ShowEmptyState();
                    return;
                }

                DisplayRides();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading rides: {ex.Message}");
                await ShowModal("Error", "Failed to load ride history", "error", false);
                ShowEmptyState();
            }
            finally
            {
                _isLoading = false;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                });
            }
        }

        private void DisplayRides()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    RidesListContainer.Children.Clear();

                    var activeRides = _allRides.Where(r =>
                        r.Status == "Pending" ||
                        r.Status == "Accepted" ||
                        r.Status == "Picking Up" ||
                        r.Status == "Arrived at Pickup" ||
                        r.Status == "Trip Started").ToList();

                    var completedRides = _allRides.Where(r => r.Status == "Completed").Take(5).ToList();
                    var cancelledRides = _allRides.Where(r => r.Status == "Cancelled").Take(3).ToList();
                    var paymentPendingRides = _allRides.Where(r => r.Status == "Payment Pending").ToList();

                    if (activeRides.Any())
                    {
                        AddSectionHeader("🛵 ACTIVE RIDES");
                        foreach (var ride in activeRides)
                        {
                            AddRideCard(ride, true);
                        }
                    }

                    if (paymentPendingRides.Any())
                    {
                        AddSectionHeader("💳 PAYMENT PENDING");
                        foreach (var ride in paymentPendingRides)
                        {
                            AddRideCard(ride, false, true);
                        }
                    }

                    if (completedRides.Any())
                    {
                        AddSectionHeader("✓ RECENT COMPLETED");
                        foreach (var ride in completedRides)
                        {
                            AddRideCard(ride);
                        }
                    }

                    if (cancelledRides.Any())
                    {
                        AddSectionHeader("✗ RECENT CANCELLED");
                        foreach (var ride in cancelledRides)
                        {
                            AddRideCard(ride);
                        }
                    }

                    EmptyStateLayout.IsVisible = false;
                    RidesRefreshView.IsVisible = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error displaying rides: {ex.Message}");
                    ShowEmptyState();
                }
            });
        }

        private void AddSectionHeader(string title)
        {
            var headerFrame = new Frame
            {
                BackgroundColor = Color.FromArgb("#F0F9F0"),
                BorderColor = Colors.Transparent,
                Padding = new Thickness(15, 10),
                Margin = new Thickness(0, 5, 0, 5),
                CornerRadius = 10,
                HasShadow = false
            };

            var headerLabel = new Label
            {
                Text = title,
                FontAttributes = FontAttributes.Bold,
                FontSize = 14,
                TextColor = Color.FromArgb("#2E7D32")
            };

            headerFrame.Content = headerLabel;
            RidesListContainer.Children.Add(headerFrame);
        }

        private void AddRideCard(RideRequestWithKey ride, bool isActive = false, bool isPaymentPending = false)
        {
            var cardTap = new TapGestureRecognizer();

            if (isPaymentPending)
            {
                cardTap.Tapped += async (sender, e) => await OnPaymentPendingCardTapped(ride);
            }
            else if (isActive)
            {
                cardTap.Tapped += async (sender, e) => await OnActiveRideCardTapped(ride);
            }
            else
            {
                cardTap.Tapped += async (sender, e) => await OnRideCardTapped(ride);
            }

            var rideCard = new Frame
            {
                BackgroundColor = Colors.White,
                BorderColor = Color.FromArgb("#E8E8E8"),
                CornerRadius = 15,
                Padding = new Thickness(15),
                HasShadow = true,
                Margin = new Thickness(0, 0, 0, 12),
                GestureRecognizers = { cardTap }
            };

            var mainGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 15
            };

            var vehicleIconFrame = new Frame
            {
                BackgroundColor = Colors.White,
                Padding = new Thickness(10),
                CornerRadius = 12,
                HeightRequest = 50,
                WidthRequest = 50,
                BorderColor = Color.FromArgb("#E0E0E0"),
                VerticalOptions = LayoutOptions.Center
            };

            var vehicleImage = new Image
            {
                Source = "tricycle.png",
                HeightRequest = 30,
                WidthRequest = 30,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };

            vehicleIconFrame.Content = vehicleImage;
            Grid.SetColumn(vehicleIconFrame, 0);
            mainGrid.Children.Add(vehicleIconFrame);

            var infoStack = new VerticalStackLayout
            {
                Spacing = 6
            };

            var dropoffLabel = new Label
            {
                Text = FormatAddress(ride.DropoffLocation, 30),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#333333"),
                LineBreakMode = LineBreakMode.TailTruncation
            };
            infoStack.Children.Add(dropoffLabel);

            var dateTimeText = $"{GetDayOfWeek(ride.RequestTime)} • {ride.RequestTime:MMM dd, yyyy} • {ride.RequestTime:hh:mm tt}";
            var dateTimeLabel = new Label
            {
                Text = dateTimeText,
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            };
            infoStack.Children.Add(dateTimeLabel);

            var fareStatusGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 10
            };

            var fareLabel = new Label
            {
                Text = $"₱{ride.Fare:F0}",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#2E7D32")
            };
            Grid.SetColumn(fareLabel, 0);
            fareStatusGrid.Children.Add(fareLabel);

            var statusFrame = new Frame
            {
                BackgroundColor = GetStatusBackgroundColor(ride.Status),
                Padding = new Thickness(8, 4),
                CornerRadius = 6,
                HasShadow = false,
                HorizontalOptions = LayoutOptions.Start
            };

            var statusLabel = new Label
            {
                Text = GetStatusShortText(ride.Status),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = GetStatusColor(ride.Status)
            };

            statusFrame.Content = statusLabel;
            Grid.SetColumn(statusFrame, 1);
            fareStatusGrid.Children.Add(statusFrame);

            infoStack.Children.Add(fareStatusGrid);
            Grid.SetColumn(infoStack, 1);
            mainGrid.Children.Add(infoStack);

            Button actionButton = null;

            if (isPaymentPending)
            {
                actionButton = new Button
                {
                    Text = "PAY NOW",
                    BackgroundColor = Color.FromArgb("#4CAF50"),
                    TextColor = Colors.White,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 80,
                    Padding = new Thickness(2),
                    VerticalOptions = LayoutOptions.Center
                };
                actionButton.Clicked += async (sender, e) => await OnPaymentPendingCardTapped(ride);
            }
            else if (isActive)
            {
                actionButton = new Button
                {
                    Text = "TRACK",
                    BackgroundColor = Color.FromArgb("#2196F3"),
                    TextColor = Colors.White,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 80,
                    Padding = new Thickness(2),
                    VerticalOptions = LayoutOptions.Center
                };
                actionButton.Clicked += async (sender, e) => await OnActiveRideCardTapped(ride);
            }
            else if (ride.Status == "Completed")
            {
                actionButton = new Button
                {
                    Text = "REBOOK",
                    BackgroundColor = Color.FromArgb("#2E7D32"),
                    TextColor = Colors.White,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 80,
                    Padding = new Thickness(2),
                    VerticalOptions = LayoutOptions.Center
                };
                actionButton.Clicked += async (sender, e) => await OnRebookClicked(ride);
            }
            else if (ride.Status == "Cancelled")
            {
                actionButton = new Button
                {
                    Text = "VIEW",
                    BackgroundColor = Color.FromArgb("#757575"),
                    TextColor = Colors.White,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 80,
                    Padding = new Thickness(2),
                    VerticalOptions = LayoutOptions.Center
                };
                actionButton.Clicked += async (sender, e) => await OnRideCardTapped(ride);
            }
            else
            {
                actionButton = new Button
                {
                    Text = "VIEW",
                    BackgroundColor = Color.FromArgb("#E8F5E9"),
                    TextColor = Color.FromArgb("#2E7D32"),
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 80,
                    Padding = new Thickness(2),
                    VerticalOptions = LayoutOptions.Center
                };
                actionButton.Clicked += async (sender, e) => await OnRideCardTapped(ride);
            }

            if (actionButton != null)
            {
                Grid.SetColumn(actionButton, 2);
                mainGrid.Children.Add(actionButton);
            }

            rideCard.Content = mainGrid;
            RidesListContainer.Children.Add(rideCard);
        }

        private async Task OnPaymentPendingCardTapped(RideRequestWithKey ride)
        {
            try
            {
                Console.WriteLine($"💰 Payment pending ride tapped: {ride.FirebaseKey}");
                await Navigation.PushModalAsync(new Commuter_Payment(ride.FirebaseKey));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error navigating to payment: {ex.Message}");
                await ShowModal("Error", "Failed to open payment page", "error", false);
            }
        }

        private async Task OnActiveRideCardTapped(RideRequestWithKey ride)
        {
            try
            {
                Console.WriteLine($"🚗 Active ride tapped: {ride.FirebaseKey} - Status: {ride.Status}");

                if (ride.Status == "Payment Pending")
                {
                    await Navigation.PushModalAsync(new Commuter_Payment(ride.FirebaseKey));
                }
                else
                {
                    await Navigation.PushModalAsync(new Commuter_SearchDriver(
                        ride.PickupLocation,
                        ride.DropoffLocation,
                        ride.SeatingCapacity,
                        ride.Fare,
                        ride.FirebaseKey
                    ));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error navigating to active ride: {ex.Message}");
                await ShowModal("Error", "Failed to open ride details", "error", false);
            }
        }

        private async Task OnRideCardTapped(RideRequestWithKey ride)
        {
            try
            {
                Console.WriteLine($"📋 Ride details tapped: {ride.FirebaseKey}");
                await Navigation.PushModalAsync(new Commuter_RideDetails(ride));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error navigating to ride details: {ex.Message}");
                await ShowModal("Error", "Failed to open ride details", "error", false);
            }
        }

        private async Task OnPayNowClicked(RideRequestWithKey ride)
        {
            await Navigation.PushModalAsync(new Commuter_Payment(ride.FirebaseKey));
        }

        private string GetDayOfWeek(DateTime date)
        {
            return date.DayOfWeek.ToString().Substring(0, 3).ToUpper();
        }

        private string GetStatusShortText(string status)
        {
            return status switch
            {
                "Completed" => "COMPLETED",
                "Cancelled" => "CANCELLED",
                "Pending" => "SEARCHING",
                "Accepted" => "DRIVER FOUND",
                "Picking Up" => "PICKING UP",
                "Arrived at Pickup" => "ARRIVED",
                "Trip Started" => "IN PROGRESS",
                "Payment Pending" => "PAY NOW",
                _ => status.ToUpper()
            };
        }

        private Color GetStatusBackgroundColor(string status)
        {
            return status switch
            {
                "Completed" => Color.FromArgb("#F0F9F0"),
                "Cancelled" => Color.FromArgb("#FFEBEE"),
                "Pending" => Color.FromArgb("#FFF8E1"),
                "Accepted" => Color.FromArgb("#E3F2FD"),
                "Picking Up" => Color.FromArgb("#E8F5E9"),
                "Arrived at Pickup" => Color.FromArgb("#E1F5FE"),
                "Trip Started" => Color.FromArgb("#FFF3E0"),
                "Payment Pending" => Color.FromArgb("#FFF3E0"),
                _ => Color.FromArgb("#F5F5F5")
            };
        }

        private Color GetStatusColor(string status)
        {
            return status switch
            {
                "Completed" => Color.FromArgb("#2E7D32"),
                "Cancelled" => Color.FromArgb("#F44336"),
                "Pending" => Color.FromArgb("#FFB300"),
                "Accepted" => Color.FromArgb("#2196F3"),
                "Picking Up" => Color.FromArgb("#2196F3"),
                "Arrived at Pickup" => Color.FromArgb("#0288D1"),
                "Trip Started" => Color.FromArgb("#F57C00"),
                "Payment Pending" => Color.FromArgb("#F57C00"),
                _ => Color.FromArgb("#666666")
            };
        }

        private string FormatAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown Location";

            var cleanAddress = address.Replace(", Malolos, Bulacan", "")
                                     .Replace(", Bulacan", "")
                                     .Replace("Malolos, ", "")
                                     .Trim();

            if (cleanAddress.Length > maxLength)
                return cleanAddress.Substring(0, maxLength - 3) + "...";

            return cleanAddress;
        }

        private async Task OnRebookClicked(RideRequestWithKey ride)
        {
            try
            {
                string commuterId = AppSession.GetCommuterId();
                var activeRide = await _firebaseConnection.GetActiveRideForCommuterAsync(commuterId);

                if (activeRide != null && (activeRide.Status == "Pending" ||
                    activeRide.Status == "Accepted"))
                {
                    await ShowModal("Active Ride",
                        "You already have an active ride. Please complete or cancel it before booking a new one.",
                        "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_SearchLocation(
                    ride.PickupLocation,
                    ride.DropoffLocation));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rebooking ride: {ex.Message}");
                await ShowModal("Error", "Failed to rebook ride", "error", false);
            }
        }

        private void ShowEmptyState()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EmptyStateLayout.IsVisible = true;
                RidesRefreshView.IsVisible = false;
                RidesListContainer.Children.Clear();
            });
        }

        private async void OnHistoryButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new Commuter_RidesHistory());
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "phone.png" : "sad.png";
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

            var modalPage = new CommuterRidesModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

        public new event PropertyChangedEventHandler PropertyChanged;
        protected new void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CommuterRidesModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_Rides _parentPage;

        public CommuterRidesModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_Rides parentPage)
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
}