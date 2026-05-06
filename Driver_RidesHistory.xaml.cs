using Microsoft.Maui.Controls;
using Microsoft.Maui;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Driver_RidesHistory : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private List<RideRequest> _allHistoryRides = new List<RideRequest>();
        private string _currentFilter = "All";
        private bool _isLoading = false;
        private bool _isModalOpen = false;

        public Driver_RidesHistory()
        {
            InitializeComponent();
            InitializeRefreshView();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
            await LoadRideHistoryOptimized();
        }

        private void InitializeRefreshView()
        {
            HistoryRefreshView.Command = new Command(async () =>
            {
                await LoadRideHistoryOptimized();
                HistoryRefreshView.IsRefreshing = false;
            });
        }

        private async Task LoadRideHistoryOptimized()
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
                    HistoryRefreshView.IsVisible = false;
                    HistoryContainer.Children.Clear();
                });

                string driverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(driverId))
                {
                    await ShowModal("Error", "Please login to view ride history", "warning", true);
                    return;
                }

                Console.WriteLine($"🔍 Loading ride history for driver: {driverId}");

                var allRideRequests = await _firebaseConnection.GetAllRideRequestsAsync();

                if (allRideRequests == null || !allRideRequests.Any())
                {
                    ShowEmptyState();
                    return;
                }

                Console.WriteLine($"📊 Total rides in database: {allRideRequests.Count}");

                _allHistoryRides = new List<RideRequest>();

                foreach (var rideWithKey in allRideRequests)
                {
                    if (rideWithKey.DriverId == driverId &&
                        (rideWithKey.Status == "Completed" || rideWithKey.Status == "Cancelled"))
                    {
                        try
                        {
                            var completeRideRequest = await _firebaseConnection.GetRideRequestByIdAsync(rideWithKey.FirebaseKey);

                            if (completeRideRequest != null)
                            {
                                completeRideRequest.Id = rideWithKey.FirebaseKey;
                                _allHistoryRides.Add(completeRideRequest);
                            }
                            else
                            {
                                var basicRide = new RideRequest
                                {
                                    Id = rideWithKey.FirebaseKey,
                                    CommuterId = rideWithKey.CommuterId,
                                    CommuterName = rideWithKey.CommuterName,
                                    PickupLocation = rideWithKey.PickupLocation,
                                    DropoffLocation = rideWithKey.DropoffLocation,
                                    Fare = rideWithKey.Fare,
                                    Status = rideWithKey.Status,
                                    DriverId = rideWithKey.DriverId,
                                    RequestTime = rideWithKey.RequestTime
                                };
                                _allHistoryRides.Add(basicRide);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Error getting ride details {rideWithKey.FirebaseKey}: {ex.Message}");
                        }
                    }
                }

                _allHistoryRides = _allHistoryRides
                    .OrderByDescending(r => r.RequestTime)
                    .Take(50)
                    .ToList();

                Console.WriteLine($"✅ Found {_allHistoryRides.Count} history rides");

                if (!_allHistoryRides.Any())
                {
                    ShowEmptyState();
                    return;
                }

                ApplyFilterOptimized(_currentFilter);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading ride history: {ex.Message}");
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

        private void ApplyFilterOptimized(string filterType)
        {
            _currentFilter = filterType;
            UpdateFilterButtons();

            List<RideRequest> filteredRides = filterType switch
            {
                "Completed" => _allHistoryRides.Where(r => r.Status == "Completed").ToList(),
                "Cancelled" => _allHistoryRides.Where(r => r.Status == "Cancelled").ToList(),
                _ => _allHistoryRides
            };

            DisplayHistoryRides(filteredRides);
        }

        private void UpdateFilterButtons()
        {
            AllFilterButton.BackgroundColor = Color.FromArgb("#F5F5F5");
            ((Label)AllFilterButton.Content).TextColor = Color.FromArgb("#666666");
            ((Label)AllFilterButton.Content).FontAttributes = FontAttributes.None;

            CompletedFilterButton.BackgroundColor = Color.FromArgb("#F5F5F5");
            ((Label)CompletedFilterButton.Content).TextColor = Color.FromArgb("#666666");
            ((Label)CompletedFilterButton.Content).FontAttributes = FontAttributes.None;

            CancelledFilterButton.BackgroundColor = Color.FromArgb("#F5F5F5");
            ((Label)CancelledFilterButton.Content).TextColor = Color.FromArgb("#666666");
            ((Label)CancelledFilterButton.Content).FontAttributes = FontAttributes.None;

            switch (_currentFilter)
            {
                case "All":
                    AllFilterButton.BackgroundColor = Color.FromArgb("#2E7D32");
                    ((Label)AllFilterButton.Content).TextColor = Colors.White;
                    ((Label)AllFilterButton.Content).FontAttributes = FontAttributes.Bold;
                    break;

                case "Completed":
                    CompletedFilterButton.BackgroundColor = Color.FromArgb("#2E7D32");
                    ((Label)CompletedFilterButton.Content).TextColor = Colors.White;
                    ((Label)CompletedFilterButton.Content).FontAttributes = FontAttributes.Bold;
                    break;

                case "Cancelled":
                    CancelledFilterButton.BackgroundColor = Color.FromArgb("#2E7D32");
                    ((Label)CancelledFilterButton.Content).TextColor = Colors.White;
                    ((Label)CancelledFilterButton.Content).FontAttributes = FontAttributes.Bold;
                    break;
            }
        }

        private void DisplayHistoryRides(List<RideRequest> historyRides)
        {
            try
            {
                HistoryContainer.Children.Clear();

                if (!historyRides.Any())
                {
                    ShowEmptyState();
                    return;
                }

                var groupedRides = historyRides
                    .GroupBy(r => new { r.RequestTime.Year, r.RequestTime.Month })
                    .OrderByDescending(g => g.Key.Year)
                    .ThenByDescending(g => g.Key.Month);

                foreach (var monthGroup in groupedRides)
                {
                    var firstRide = monthGroup.First();
                    var monthName = firstRide.RequestTime.ToString("MMMM yyyy");

                    var monthHeader = new Frame
                    {
                        BackgroundColor = Color.FromArgb("#F0F9F0"),
                        BorderColor = Colors.Transparent,
                        Padding = new Thickness(15, 10),
                        Margin = new Thickness(0, 10, 0, 5),
                        CornerRadius = 10,
                        HasShadow = false
                    };

                    var monthLabel = new Label
                    {
                        Text = monthName.ToUpper(),
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 13,
                        TextColor = Color.FromArgb("#2E7D32")
                    };

                    monthHeader.Content = monthLabel;
                    HistoryContainer.Children.Add(monthHeader);

                    foreach (var ride in monthGroup)
                    {
                        AddHistoryCard(ride);
                    }
                }

                EmptyStateLayout.IsVisible = false;
                HistoryRefreshView.IsVisible = true;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error displaying rides: {ex.Message}");
                ShowEmptyState();
            }
        }

        private void AddHistoryCard(RideRequest ride)
        {
            try
            {
                var historyCard = new Frame
                {
                    BackgroundColor = Colors.White,
                    BorderColor = Color.FromArgb("#E0E0E0"),
                    CornerRadius = 15,
                    Padding = new Thickness(15),
                    HasShadow = true,
                    Margin = new Thickness(0, 0, 0, 15)
                };

                var mainGrid = new Grid
                {
                    RowDefinitions = new RowDefinitionCollection
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto }
                    },
                    RowSpacing = 15
                };

                var headerGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };

                var dateTimeLabel = new Label
                {
                    Text = $"{ride.RequestTime:MMMM dd, yyyy} • {ride.RequestTime:hh:mm tt}",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#666666"),
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(dateTimeLabel, 0);
                headerGrid.Children.Add(dateTimeLabel);

                var statusFrame = new Frame
                {
                    BackgroundColor = ride.Status == "Completed" ?
                        Color.FromArgb("#F0F9F0") : Color.FromArgb("#FFEBEE"),
                    Padding = new Thickness(12, 6),
                    CornerRadius = 8,
                    HasShadow = false,
                    VerticalOptions = LayoutOptions.Center
                };

                var statusLabel = new Label
                {
                    Text = ride.Status.ToUpper(),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = ride.Status == "Completed" ?
                        Color.FromArgb("#2E7D32") : Color.FromArgb("#D32F2F"),
                    VerticalOptions = LayoutOptions.Center
                };

                statusFrame.Content = statusLabel;
                Grid.SetColumn(statusFrame, 1);
                headerGrid.Children.Add(statusFrame);

                Grid.SetRow(headerGrid, 0);
                mainGrid.Children.Add(headerGrid);

                var fromGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Star }
                    },
                    ColumnSpacing = 12,
                    VerticalOptions = LayoutOptions.Center
                };

                var fromIconFrame = new Frame
                {
                    BackgroundColor = Color.FromArgb("#E3F2FD"),
                    Padding = new Thickness(8),
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 40,
                    VerticalOptions = LayoutOptions.Center,
                    HasShadow = false
                };

                var fromIcon = new Image
                {
                    Source = "pickup.png",
                    HeightRequest = 24,
                    WidthRequest = 24,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center
                };

                fromIconFrame.Content = fromIcon;
                Grid.SetColumn(fromIconFrame, 0);
                fromGrid.Children.Add(fromIconFrame);

                var fromStack = new VerticalStackLayout { Spacing = 4 };

                var fromTitle = new Label
                {
                    Text = "FROM",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#666666")
                };

                var fromAddress = new Label
                {
                    Text = FormatAddress(ride.PickupLocation, 50),
                    FontSize = 14,
                    TextColor = Color.FromArgb("#333333"),
                    LineBreakMode = LineBreakMode.WordWrap,
                    MaxLines = 2
                };

                fromStack.Children.Add(fromTitle);
                fromStack.Children.Add(fromAddress);
                Grid.SetColumn(fromStack, 1);
                fromGrid.Children.Add(fromStack);

                Grid.SetRow(fromGrid, 1);
                mainGrid.Children.Add(fromGrid);

                var toGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Star }
                    },
                    ColumnSpacing = 12,
                    VerticalOptions = LayoutOptions.Center
                };

                var toIconFrame = new Frame
                {
                    BackgroundColor = Color.FromArgb("#FFEBEE"),
                    Padding = new Thickness(8),
                    CornerRadius = 8,
                    HeightRequest = 40,
                    WidthRequest = 40,
                    VerticalOptions = LayoutOptions.Center,
                    HasShadow = false
                };

                var toIcon = new Image
                {
                    Source = "dropoff.png",
                    HeightRequest = 24,
                    WidthRequest = 24,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center
                };

                toIconFrame.Content = toIcon;
                Grid.SetColumn(toIconFrame, 0);
                toGrid.Children.Add(toIconFrame);

                var toStack = new VerticalStackLayout { Spacing = 4 };

                var toTitle = new Label
                {
                    Text = "TO",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#666666")
                };

                var toAddress = new Label
                {
                    Text = FormatAddress(ride.DropoffLocation, 50),
                    FontSize = 14,
                    TextColor = Color.FromArgb("#333333"),
                    LineBreakMode = LineBreakMode.WordWrap,
                    MaxLines = 2
                };

                toStack.Children.Add(toTitle);
                toStack.Children.Add(toAddress);
                Grid.SetColumn(toStack, 1);
                toGrid.Children.Add(toStack);

                Grid.SetRow(toGrid, 2);
                mainGrid.Children.Add(toGrid);

                var footerGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    ColumnSpacing = 10,
                    VerticalOptions = LayoutOptions.Center
                };

                var passengerStack = new VerticalStackLayout { Spacing = 2 };

                var passengerTitle = new Label
                {
                    Text = "PASSENGER",
                    FontSize = 10,
                    TextColor = Color.FromArgb("#666666")
                };

                var passengerNameLabel = new Label
                {
                    Text = ride.CommuterName ?? "Passenger",
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#333333")
                };

                passengerStack.Children.Add(passengerTitle);
                passengerStack.Children.Add(passengerNameLabel);
                Grid.SetColumn(passengerStack, 0);
                footerGrid.Children.Add(passengerStack);

                var fareStack = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.End
                };

                var fareTitle = new Label
                {
                    Text = "TOTAL FARE",
                    FontSize = 10,
                    TextColor = Color.FromArgb("#666666"),
                    HorizontalOptions = LayoutOptions.End
                };

                var fareValue = new Label
                {
                    Text = $"\u20B1{ride.Fare:F2}",
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#2E7D32"),
                    HorizontalOptions = LayoutOptions.End
                };

                fareStack.Children.Add(fareTitle);
                fareStack.Children.Add(fareValue);
                Grid.SetColumn(fareStack, 2);
                footerGrid.Children.Add(fareStack);

                var actionButton = new Button
                {
                    Text = "Details",
                    BackgroundColor = Color.FromArgb("#2E7D32"),
                    TextColor = Colors.White,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 8,
                    HeightRequest = 36,
                    WidthRequest = 90,
                    Padding = new Thickness(0),
                    VerticalOptions = LayoutOptions.Center
                };
                actionButton.Clicked += async (sender, e) => await OnHistoryRideTapped(ride);

                Grid.SetColumn(actionButton, 3);
                footerGrid.Children.Add(actionButton);

                Grid.SetRow(footerGrid, 3);
                mainGrid.Children.Add(footerGrid);

                var cardTap = new TapGestureRecognizer();
                cardTap.Tapped += async (sender, e) => await OnHistoryRideTapped(ride);
                historyCard.GestureRecognizers.Add(cardTap);

                historyCard.Content = mainGrid;
                HistoryContainer.Children.Add(historyCard);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error creating card: {ex.Message}");
            }
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

        private async Task OnHistoryRideTapped(RideRequest ride)
        {
            try
            {
                string rideId = ride.Id;

                if (string.IsNullOrEmpty(rideId))
                {
                    await ShowModal("Error", "Invalid ride ID", "error", false);
                    return;
                }

                Console.WriteLine($"📱 Fetching complete ride details for ID: {rideId}");

                var completeRide = await _firebaseConnection.GetRideRequestByIdAsync(rideId);

                if (completeRide == null)
                {
                    Console.WriteLine($"❌ Ride not found: {rideId}");
                    await ShowModal("Error", "Ride details not found", "error", false);
                    return;
                }

                completeRide.Id = rideId;

                var rideWithKey = new RideRequestWithKey
                {
                    FirebaseKey = rideId,
                    CommuterId = completeRide.CommuterId,
                    CommuterName = completeRide.CommuterName,
                    CommuterMobile = completeRide.CommuterMobile,
                    PickupLocation = completeRide.PickupLocation,
                    DropoffLocation = completeRide.DropoffLocation,
                    PickupLat = completeRide.PickupLat,
                    PickupLng = completeRide.PickupLng,
                    DropoffLat = completeRide.DropoffLat,
                    DropoffLng = completeRide.DropoffLng,
                    VehicleType = completeRide.VehicleType,
                    SeatingCapacity = completeRide.SeatingCapacity,
                    PassengerType = completeRide.PassengerType,
                    Fare = completeRide.Fare,
                    Distance = completeRide.Distance,
                    Status = completeRide.Status,
                    DriverId = completeRide.DriverId,
                    RequestTime = completeRide.RequestTime,
                    AcceptedTime = completeRide.AcceptedTime,
                    CompletedTime = completeRide.CompletedTime,
                    DriverName = completeRide.DriverName,
                    DriverMobileNumber = completeRide.DriverMobileNumber,
                    DriverProfileImageUrl = completeRide.DriverProfileImageUrl,
                    DriverVehicleType = completeRide.DriverVehicleType,
                    VehiclePlateNumber = completeRide.VehiclePlateNumber,
                    DriverRating = completeRide.DriverRating,
                    PaymentMethod = completeRide.PaymentMethod,
                    PaymentStatus = completeRide.PaymentStatus
                };

                Console.WriteLine($"✅ Navigating to ride details for ride: {rideId}");
                await Navigation.PushModalAsync(new Driver_RideDetails(rideWithKey));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Navigation error: {ex.Message}");
                await ShowModal("Error", $"Failed to load ride details: {ex.Message}", "error", false);
            }
        }

        private void ShowEmptyState()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EmptyStateLayout.IsVisible = true;
                HistoryRefreshView.IsVisible = false;
                HistoryContainer.Children.Clear();
            });
        }

        private void OnAllFilterTapped(object sender, EventArgs e)
        {
            ApplyFilterOptimized("All");
        }

        private void OnCompletedFilterTapped(object sender, EventArgs e)
        {
            ApplyFilterOptimized("Completed");
        }

        private void OnCancelledFilterTapped(object sender, EventArgs e)
        {
            ApplyFilterOptimized("Cancelled");
        }

        private async void OnRefreshButtonClicked(object sender, EventArgs e)
        {
            await LoadRideHistoryOptimized();
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await this.FadeTo(0, 250, Easing.CubicIn);
            await Navigation.PopAsync();
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

            var modalPage = new DriverRidesHistoryModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

    public class DriverRidesHistoryModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_RidesHistory _parentPage;

        public DriverRidesHistoryModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_RidesHistory parentPage)
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