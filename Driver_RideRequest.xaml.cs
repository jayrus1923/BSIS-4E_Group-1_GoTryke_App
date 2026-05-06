using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
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
    public partial class Driver_RideRequest : ContentPage, INotifyPropertyChanged
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private List<RideRequestWithKey> _pendingRequests = new List<RideRequestWithKey>();
        private List<RideRequestWithKey> _filteredRequests = new List<RideRequestWithKey>();
        private Driver _currentDriver;
        private bool _isDriverOnline = false;
        private bool _isRefreshing;
        private string _cachedTotalRides = "";
        private string _cachedTodayRides = "";
        private string _cachedTotalEarnings = "";
        private DateTime _lastStatsUpdate = DateTime.MinValue;
        private RideRequestWithKey _currentModalRequest;
        private Dictionary<string, ImageSource> _commuterProfileImages = new Dictionary<string, ImageSource>();
        private string _driverSeatingCapacity = "";
        private bool _canAcceptLargeSeats = false;
        private IDisposable _rideRequestListener;
        private bool _isListening = false;
        private bool _isModalOpen = false;

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand => new Command(async () =>
        {
            IsRefreshing = true;
            await LoadRideRequests();
            IsRefreshing = false;
        });

        public Driver_RideRequest()
        {
            InitializeComponent();
            BindingContext = this;
            Driver_Home.StatisticsUpdated += OnStatisticsUpdatedFromHome;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
            Console.WriteLine("=== DRIVER RIDE REQUEST PAGE APPEARING ===");
            await LoadDriverData();
            DisplaySharedStatistics();
            await LoadRideRequests();
            StartRideRequestListener();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopRideRequestListener();
            Driver_Home.StatisticsUpdated -= OnStatisticsUpdatedFromHome;
            Console.WriteLine("=== DRIVER RIDE REQUEST PAGE DISAPPEARING ===");
        }

        private async Task LoadDriverData()
        {
            try
            {
                string driverId = AppSession.GetDriverId();
                if (!string.IsNullOrEmpty(driverId))
                {
                    _currentDriver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                    _isDriverOnline = _currentDriver?.IsOnline ?? false;
                    UpdateOnlineStatusUI();
                    await LoadDriverSeatingCapacity(driverId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading driver data: {ex.Message}");
            }
        }

        private async Task LoadDriverSeatingCapacity(string driverId)
        {
            try
            {
                var tricycleInfo = await _firebaseConnection.GetTricycleInfoAsync(driverId);
                if (tricycleInfo != null && !string.IsNullOrEmpty(tricycleInfo.SeatingCapacity))
                {
                    _driverSeatingCapacity = tricycleInfo.SeatingCapacity;
                    _canAcceptLargeSeats = _driverSeatingCapacity.Contains("3-4") ||
                                           _driverSeatingCapacity.Contains("3") ||
                                           _driverSeatingCapacity.Contains("4") ||
                                           _driverSeatingCapacity.ToLower().Contains("large");
                    UpdateCapacityFilterUI();
                }
                else
                {
                    _driverSeatingCapacity = "1-2 seater";
                    _canAcceptLargeSeats = false;
                    UpdateCapacityFilterUI();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading seating capacity: {ex.Message}");
                _driverSeatingCapacity = "1-2 seater";
                _canAcceptLargeSeats = false;
                UpdateCapacityFilterUI();
            }
        }

        private void UpdateCapacityFilterUI()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (!string.IsNullOrEmpty(_driverSeatingCapacity))
                {
                    string capacityText = _driverSeatingCapacity;
                    string filterText = _canAcceptLargeSeats
                        ? "\nShowing 1-2 seater & 3-4 seater requests"
                        : "\nShowing 1-2 seater requests only";

                    CapacityFilterLabel.Text = $"Your vehicle: {capacityText} {filterText}";
                }
                else
                {
                    CapacityFilterLabel.Text = "Loading seating capacity...";
                }
            });
        }

        private void UpdateOnlineStatusUI()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (_isDriverOnline)
                {
                    OnlineIndicator.Color = Color.FromArgb("#4CAF50");
                    OnlineStatusLabel.Text = "Online";
                    OfflineWarning.IsVisible = false;
                    RealtimeIndicator.IsVisible = true;
                }
                else
                {
                    OnlineIndicator.Color = Color.FromArgb("#757575");
                    OnlineStatusLabel.Text = "Offline";
                    OfflineWarning.IsVisible = true;
                    RealtimeIndicator.IsVisible = false;
                    NoRequestsMessage.Text = "Switch to online mode in the Home screen";
                }
            });
        }

        private void OnStatisticsUpdatedFromHome()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                DisplaySharedStatistics();
            });
        }

        private void DisplaySharedStatistics()
        {
            try
            {
                if ((DateTime.Now - _lastStatsUpdate).TotalSeconds < 30 && !string.IsNullOrEmpty(_cachedTotalRides))
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        TotalCompletedRidesLabel.Text = _cachedTotalRides;
                        TodayRidesLabel.Text = _cachedTodayRides;

                        if (double.TryParse(_cachedTotalEarnings, out double earnings))
                        {
                            TotalEarningsLabel.Text = $"\u20B1{earnings:N2}";
                        }
                        else
                        {
                            TotalEarningsLabel.Text = "\u20B10.00";
                        }

                        StatsCard.IsVisible = true;
                    });
                    return;
                }

                string totalRides = Driver_Home.SharedTotalRides;
                string todayRides = Driver_Home.SharedTodayRides;
                string totalEarnings = Driver_Home.SharedTotalEarnings;

                _cachedTotalRides = totalRides;
                _cachedTodayRides = todayRides;
                _cachedTotalEarnings = totalEarnings;
                _lastStatsUpdate = DateTime.Now;

                Device.BeginInvokeOnMainThread(() =>
                {
                    TotalCompletedRidesLabel.Text = totalRides;
                    TodayRidesLabel.Text = todayRides;

                    if (double.TryParse(totalEarnings, out double earnings))
                    {
                        TotalEarningsLabel.Text = $"\u20B1{earnings:N2}";
                    }
                    else
                    {
                        TotalEarningsLabel.Text = "\u20B10.00";
                    }

                    StatsCard.IsVisible = true;
                });

                Console.WriteLine($"Using shared stats from Home page:");
                Console.WriteLine($"Total Rides: {totalRides}");
                Console.WriteLine($"Today Rides: {todayRides}");
                Console.WriteLine($"Total Earnings: {totalEarnings}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error displaying shared statistics: {ex.Message}");
                Device.BeginInvokeOnMainThread(() =>
                {
                    TotalCompletedRidesLabel.Text = "0";
                    TodayRidesLabel.Text = "0";
                    TotalEarningsLabel.Text = "₱0.00";
                    StatsCard.IsVisible = false;
                });
            }
        }

        private void StartRideRequestListener()
        {
            try
            {
                if (!_isDriverOnline || _isListening)
                    return;

                Console.WriteLine("=== STARTING REAL-TIME RIDE REQUEST LISTENER ===");

                _rideRequestListener?.Dispose();
                _rideRequestListener = _firebaseConnection.ListenToAllRideRequests(OnRideRequestUpdated);
                _isListening = true;

                Device.BeginInvokeOnMainThread(() =>
                {
                    RealtimeIndicator.IsVisible = true;
                });

                Console.WriteLine("✅ Real-time listener started");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error starting real-time listener: {ex.Message}");
            }
        }

        private void StopRideRequestListener()
        {
            try
            {
                if (_isListening && _rideRequestListener != null)
                {
                    Console.WriteLine("=== STOPPING REAL-TIME LISTENER ===");
                    _rideRequestListener.Dispose();
                    _rideRequestListener = null;
                    _isListening = false;

                    Device.BeginInvokeOnMainThread(() =>
                    {
                        RealtimeIndicator.IsVisible = false;
                    });

                    Console.WriteLine("✅ Real-time listener stopped");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error stopping real-time listener: {ex.Message}");
            }
        }

        private async void OnRideRequestUpdated(RideRequestWithKey rideRequest)
        {
            try
            {
                Console.WriteLine($"=== REAL-TIME UPDATE ===");
                Console.WriteLine($"Ride ID: {rideRequest.FirebaseKey}");
                Console.WriteLine($"Status: {rideRequest.Status}");
                Console.WriteLine($"Seating: {rideRequest.SeatingCapacity}");

                if (!MatchesSeatingCapacity(rideRequest.SeatingCapacity))
                {
                    Console.WriteLine($"Ride does not match seating capacity filter");
                    return;
                }

                if (rideRequest.Status == "Pending" && string.IsNullOrEmpty(rideRequest.DriverId))
                {
                    Console.WriteLine($"New pending ride request detected");

                    var existingIndex = _pendingRequests.FindIndex(r => r.FirebaseKey == rideRequest.FirebaseKey);

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (existingIndex >= 0)
                        {
                            _pendingRequests[existingIndex] = rideRequest;
                        }
                        else
                        {
                            _pendingRequests.Add(rideRequest);
                            await LoadCommuterProfilePictureAsync(rideRequest.CommuterId, rideRequest);
                        }

                        UpdateFilteredRequests();
                        ShowRideRequests();
                    });
                }
                else if (rideRequest.Status != "Pending" || !string.IsNullOrEmpty(rideRequest.DriverId))
                {
                    var existingIndex = _pendingRequests.FindIndex(r => r.FirebaseKey == rideRequest.FirebaseKey);

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (existingIndex >= 0)
                        {
                            _pendingRequests.RemoveAt(existingIndex);
                            UpdateFilteredRequests();
                            ShowRideRequests();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in real-time update: {ex.Message}");
            }
        }

        private bool MatchesSeatingCapacity(string requestSeatingCapacity)
        {
            if (string.IsNullOrEmpty(requestSeatingCapacity) || string.IsNullOrEmpty(_driverSeatingCapacity))
                return true;

            var requestSeating = requestSeatingCapacity.ToLower();
            var driverSeating = _driverSeatingCapacity.ToLower();

            if (driverSeating.Contains("1-2") || driverSeating.Contains("small"))
            {
                return requestSeating.Contains("1-2") || requestSeating.Contains("small");
            }
            else if (driverSeating.Contains("3-4") || driverSeating.Contains("large"))
            {
                return requestSeating.Contains("1-2") || requestSeating.Contains("small") ||
                       requestSeating.Contains("3-4") || requestSeating.Contains("large");
            }

            return true;
        }

        private void UpdateFilteredRequests()
        {
            if (string.IsNullOrEmpty(_driverSeatingCapacity))
            {
                _filteredRequests = new List<RideRequestWithKey>(_pendingRequests);
                return;
            }

            _filteredRequests = _pendingRequests
                .Where(r => MatchesSeatingCapacity(r.SeatingCapacity))
                .ToList();
        }

        private async Task LoadRideRequests()
        {
            try
            {
                IsBusy = true;
                _commuterProfileImages.Clear();

                Device.BeginInvokeOnMainThread(() =>
                {
                    NoRequestsLayout.IsVisible = false;
                    RideRequestsLayout.IsVisible = false;
                    OfflineWarning.IsVisible = !_isDriverOnline;
                    RideDetailsModal.IsVisible = false;
                });

                if (!_isDriverOnline)
                {
                    ShowNoRequests();
                    IsBusy = false;
                    return;
                }

                _pendingRequests = await _firebaseConnection.GetPendingRideRequestsAsync();

                Console.WriteLine($"Found {_pendingRequests?.Count ?? 0} pending ride requests");

                foreach (var request in _pendingRequests)
                {
                    await LoadCommuterProfilePictureAsync(request.CommuterId, request);
                }

                UpdateFilteredRequests();

                Console.WriteLine($"After filtering: {_filteredRequests.Count} matching requests");

                if (_filteredRequests == null || !_filteredRequests.Any())
                {
                    ShowNoRequests();
                }
                else
                {
                    ShowRideRequests();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ride requests: {ex.Message}");
                await ShowModal("Error", $"Failed to load ride requests: {ex.Message}", "error", false);
                ShowNoRequests();
            }
            finally
            {
                IsBusy = false;
                IsRefreshing = false;
            }
        }

        private async Task LoadCommuterProfilePictureAsync(string commuterId, RideRequestWithKey request)
        {
            try
            {
                if (string.IsNullOrEmpty(commuterId))
                {
                    _commuterProfileImages[commuterId ?? ""] = ImageSource.FromFile("profile_icon.png");
                    return;
                }

                if (_commuterProfileImages.ContainsKey(commuterId))
                    return;

                var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);
                if (commuter != null && !string.IsNullOrEmpty(commuter.ProfileImageUrl))
                {
                    if (Uri.IsWellFormedUriString(commuter.ProfileImageUrl, UriKind.Absolute))
                    {
                        _commuterProfileImages[commuterId] = ImageSource.FromUri(new Uri(commuter.ProfileImageUrl));
                    }
                    else
                    {
                        _commuterProfileImages[commuterId] = ImageSource.FromFile("profile_icon.png");
                    }
                }
                else
                {
                    _commuterProfileImages[commuterId] = ImageSource.FromFile("profile_icon.png");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading commuter profile: {ex.Message}");
                _commuterProfileImages[commuterId ?? ""] = ImageSource.FromFile("profile_icon.png");
            }
        }

        private void ShowNoRequests()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                NoRequestsLayout.IsVisible = true;
                RideRequestsLayout.IsVisible = false;
                RequestsCounterLabel.Text = "0 Available Requests";

                if (!_isDriverOnline)
                {
                    NoRequestsMessage.Text = "Switch to online mode in the Home screen";
                }
                else
                {
                    if (!string.IsNullOrEmpty(_driverSeatingCapacity))
                    {
                        string capacityText = _canAcceptLargeSeats ? "1-2 seater & 3-4 seater" : "1-2 seater";
                        NoRequestsMessage.Text = $"When commuters request {capacityText} rides, they will appear here for you to accept.";
                    }
                    else
                    {
                        NoRequestsMessage.Text = "When commuters request rides, they will appear here for you to accept.";
                    }
                }
            });
        }

        private void ShowRideRequests()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    NoRequestsLayout.IsVisible = false;
                    RideRequestsLayout.IsVisible = true;
                    RequestsCounterLabel.Text = $"{_filteredRequests.Count} Available Request{(_filteredRequests.Count > 1 ? "s" : "")}";

                    RideRequestsLayout.Children.Clear();

                    foreach (var request in _filteredRequests)
                    {
                        var rideRequestCard = CreateRideRequestCard(request);
                        RideRequestsLayout.Children.Add(rideRequestCard);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing ride requests: {ex.Message}");
                    ShowNoRequests();
                }
            });
        }

        private Border CreateRideRequestCard(RideRequestWithKey request)
        {
            var card = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E0E0E0"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 15),
                Shadow = new Shadow
                {
                    Brush = Color.FromArgb("#20000000"),
                    Offset = new Point(0, 2),
                    Radius = 4,
                    Opacity = 0.1f
                }
            };

            var mainGrid = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = 1 },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                }
            };

            var headerGrid = new Grid
            {
                BackgroundColor = Color.FromArgb("#F8F9FA"),
                Padding = new Thickness(15, 10),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var profileContainer = new Grid
            {
                HeightRequest = 36,
                WidthRequest = 36,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalOptions = LayoutOptions.Center
            };

            var circularBorder = new Border
            {
                Stroke = Color.FromArgb("#DDD"),
                StrokeThickness = 1,
                BackgroundColor = Colors.White,
                StrokeShape = new Ellipse(),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            var passengerImage = new Image
            {
                Source = _commuterProfileImages.ContainsKey(request.CommuterId ?? "")
                    ? _commuterProfileImages[request.CommuterId ?? ""]
                    : ImageSource.FromFile("profile_icon.png"),
                Aspect = Aspect.AspectFill,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            passengerImage.Clip = new EllipseGeometry
            {
                Center = new Point(18, 18),
                RadiusX = 18,
                RadiusY = 18
            };

            circularBorder.Content = passengerImage;
            profileContainer.Children.Add(circularBorder);

            var passengerStack = new VerticalStackLayout { Spacing = 2 };
            passengerStack.Children.Add(new Label
            {
                Text = request.CommuterName ?? "Passenger",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            string passengerTypeDisplay = GetPassengerTypeDisplay(request.PassengerType);
            passengerStack.Children.Add(new Label
            {
                Text = $"{passengerTypeDisplay} • {request.CommuterMobile}",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            });

            var timeLabel = new Label
            {
                Text = GetTimeAgo(request.RequestTime),
                FontSize = 11,
                TextColor = Color.FromArgb("#888888"),
                VerticalOptions = LayoutOptions.Center
            };

            headerGrid.Children.Add(profileContainer);
            headerGrid.Children.Add(passengerStack);
            headerGrid.Children.Add(timeLabel);
            Grid.SetColumn(passengerStack, 1);
            Grid.SetColumn(timeLabel, 2);

            var pickupGrid = new Grid
            {
                Padding = new Thickness(15, 10, 15, 5),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            var pickupIcon = new Frame
            {
                BackgroundColor = Color.FromArgb("#E8F5E9"),
                CornerRadius = 12,
                Padding = new Thickness(6),
                HeightRequest = 24,
                WidthRequest = 24,
                HasShadow = false,
                VerticalOptions = LayoutOptions.Start
            };

            var pickupImage = new Image
            {
                Source = "pickup.png",
                HeightRequest = 16,
                WidthRequest = 16,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            pickupIcon.Content = pickupImage;

            var pickupStack = new VerticalStackLayout { Spacing = 2 };
            pickupStack.Children.Add(new Label
            {
                Text = "Pickup Location",
                FontSize = 11,
                TextColor = Color.FromArgb("#666666"),
                FontAttributes = FontAttributes.Bold
            });
            pickupStack.Children.Add(new Label
            {
                Text = request.PickupLocation ?? "Pickup location",
                FontSize = 14,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.WordWrap,
                LineHeight = 1.2
            });

            pickupGrid.Children.Add(pickupIcon);
            pickupGrid.Children.Add(pickupStack);
            Grid.SetColumn(pickupStack, 1);

            var dropoffGrid = new Grid
            {
                Padding = new Thickness(15, 5, 15, 10),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            var dropoffIcon = new Frame
            {
                BackgroundColor = Color.FromArgb("#E3F2FD"),
                CornerRadius = 12,
                Padding = new Thickness(6),
                HeightRequest = 24,
                WidthRequest = 24,
                HasShadow = false,
                VerticalOptions = LayoutOptions.Start
            };

            var dropoffImage = new Image
            {
                Source = "dropoff.png",
                HeightRequest = 16,
                WidthRequest = 16,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            dropoffIcon.Content = dropoffImage;

            var dropoffStack = new VerticalStackLayout { Spacing = 2 };
            dropoffStack.Children.Add(new Label
            {
                Text = "Drop-off Location",
                FontSize = 11,
                TextColor = Color.FromArgb("#666666"),
                FontAttributes = FontAttributes.Bold
            });
            dropoffStack.Children.Add(new Label
            {
                Text = request.DropoffLocation ?? "Dropoff location",
                FontSize = 14,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.WordWrap,
                LineHeight = 1.2
            });

            dropoffGrid.Children.Add(dropoffIcon);
            dropoffGrid.Children.Add(dropoffStack);
            Grid.SetColumn(dropoffStack, 1);

            var separator = new BoxView
            {
                HeightRequest = 1,
                BackgroundColor = Color.FromArgb("#F0F0F0"),
                Margin = new Thickness(15, 0)
            };

            var detailsGrid = new Grid
            {
                Padding = new Thickness(15, 10),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            var fareStack = new VerticalStackLayout { Spacing = 2 };
            fareStack.Children.Add(new Label
            {
                Text = "Fare",
                FontSize = 11,
                TextColor = Color.FromArgb("#666666")
            });
            fareStack.Children.Add(new Label
            {
                Text = $"\u20B1{request.Fare:N2}",
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#2E7D32")
            });

            var distanceStack = new VerticalStackLayout { Spacing = 2 };
            distanceStack.Children.Add(new Label
            {
                Text = "Distance",
                FontSize = 11,
                TextColor = Color.FromArgb("#666666")
            });
            distanceStack.Children.Add(new Label
            {
                Text = $"{request.Distance:N1} km",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            var seatingStack = new VerticalStackLayout { Spacing = 2 };
            seatingStack.Children.Add(new Label
            {
                Text = "Seating",
                FontSize = 11,
                TextColor = Color.FromArgb("#666666")
            });
            seatingStack.Children.Add(new Label
            {
                Text = GetSeatingCapacityDisplay(request.SeatingCapacity),
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            detailsGrid.Children.Add(fareStack);
            detailsGrid.Children.Add(distanceStack);
            detailsGrid.Children.Add(seatingStack);
            Grid.SetColumn(distanceStack, 1);
            Grid.SetColumn(seatingStack, 2);

            var buttonsGrid = new Grid
            {
                Padding = new Thickness(15, 0, 15, 15),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 10
            };

            var viewButton = new Button
            {
                Text = "View Details",
                BackgroundColor = Color.FromArgb("#0288D1"),
                TextColor = Colors.White,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 10,
                HeightRequest = 45
            };
            viewButton.Clicked += async (s, e) => await OnViewRideRequest(request);

            var acceptButton = new Button
            {
                Text = "Accept Ride",
                BackgroundColor = Color.FromArgb("#2E7D32"),
                TextColor = Colors.White,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 10,
                HeightRequest = 45
            };
            acceptButton.Clicked += (s, e) => ShowRideRequestModal(request);

            buttonsGrid.Children.Add(viewButton);
            buttonsGrid.Children.Add(acceptButton);
            Grid.SetColumn(acceptButton, 1);

            mainGrid.Children.Add(headerGrid);
            mainGrid.Children.Add(pickupGrid);
            mainGrid.Children.Add(dropoffGrid);
            mainGrid.Children.Add(separator);
            mainGrid.Children.Add(detailsGrid);
            mainGrid.Children.Add(buttonsGrid);

            Grid.SetRow(pickupGrid, 1);
            Grid.SetRow(dropoffGrid, 2);
            Grid.SetRow(separator, 3);
            Grid.SetRow(detailsGrid, 4);
            Grid.SetRow(buttonsGrid, 5);

            card.Content = mainGrid;
            return card;
        }

        private async Task OnViewRideRequest(RideRequestWithKey request)
        {
            try
            {
                StopRideRequestListener();
                Console.WriteLine($"=== VIEW DETAILS CLICKED - Ride ID: {request.FirebaseKey} ===");
                await Navigation.PushModalAsync(new Driver_RidePending(
                    rideRequestId: request.FirebaseKey,
                    isViewOnly: true
                ));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error navigating to view details: {ex.Message}");
                await ShowModal("Error", "Failed to open ride details.", "error", false);
                StartRideRequestListener();
            }
        }

        private void ShowRideRequestModal(RideRequestWithKey request)
        {
            _currentModalRequest = request;
            PopulateRideDetailsModal(request);
            RideDetailsModal.IsVisible = true;
        }

        private void PopulateRideDetailsModal(RideRequestWithKey request)
        {
            RideDetailsContent.Children.Clear();

            var passengerSection = CreateModalSection("Passenger Information");
            RideDetailsContent.Children.Add(passengerSection);

            var passengerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                Padding = new Thickness(0, 10, 0, 15),
                ColumnSpacing = 15
            };

            var modalProfileContainer = new Grid
            {
                HeightRequest = 50,
                WidthRequest = 50,
                VerticalOptions = LayoutOptions.Center
            };

            var modalCircularBorder = new Border
            {
                Stroke = Color.FromArgb("#DDD"),
                StrokeThickness = 1,
                BackgroundColor = Colors.White,
                StrokeShape = new Ellipse(),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            var modalPassengerImage = new Image
            {
                Source = _commuterProfileImages.ContainsKey(request.CommuterId ?? "")
                    ? _commuterProfileImages[request.CommuterId ?? ""]
                    : ImageSource.FromFile("profile_icon.png"),
                Aspect = Aspect.AspectFill,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            modalPassengerImage.Clip = new EllipseGeometry
            {
                Center = new Point(25, 25),
                RadiusX = 25,
                RadiusY = 25
            };

            modalCircularBorder.Content = modalPassengerImage;
            modalProfileContainer.Children.Add(modalCircularBorder);

            var passengerInfoStack = new VerticalStackLayout { Spacing = 5 };
            passengerInfoStack.Children.Add(new Label
            {
                Text = request.CommuterName ?? "Passenger",
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            string passengerTypeDisplay = GetPassengerTypeDisplay(request.PassengerType);
            passengerInfoStack.Children.Add(new Label
            {
                Text = $"{passengerTypeDisplay} • {request.CommuterMobile}",
                FontSize = 14,
                TextColor = Color.FromArgb("#666666")
            });

            passengerInfoStack.Children.Add(new Label
            {
                Text = $"Requested: {GetTimeAgo(request.RequestTime)}",
                FontSize = 12,
                TextColor = Color.FromArgb("#888888")
            });

            passengerGrid.Children.Add(modalProfileContainer);
            passengerGrid.Children.Add(passengerInfoStack);
            Grid.SetColumn(passengerInfoStack, 1);
            RideDetailsContent.Children.Add(passengerGrid);

            var locationsSection = CreateModalSection("Trip Details");
            RideDetailsContent.Children.Add(locationsSection);

            var pickupStack = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Start };

            var pickupFrame = new Frame
            {
                BackgroundColor = Color.FromArgb("#E8F5E9"),
                CornerRadius = 10,
                Padding = new Thickness(10),
                HeightRequest = 40,
                WidthRequest = 40,
                HasShadow = false,
                VerticalOptions = LayoutOptions.Start
            };

            var pickupImage = new Image
            {
                Source = "pickup.png",
                HeightRequest = 20,
                WidthRequest = 20,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            pickupFrame.Content = pickupImage;

            var pickupInfoStack = new VerticalStackLayout { Spacing = 4 };
            pickupInfoStack.Children.Add(new Label
            {
                Text = "Pickup Location",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666"),
                FontAttributes = FontAttributes.Bold
            });
            pickupInfoStack.Children.Add(new Label
            {
                Text = request.PickupLocation ?? "Pickup location",
                FontSize = 14,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.WordWrap,
                LineHeight = 1.3
            });

            pickupStack.Children.Add(pickupFrame);
            pickupStack.Children.Add(pickupInfoStack);
            RideDetailsContent.Children.Add(pickupStack);

            var dropoffStack = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 15, 0, 0) };

            var dropoffFrame = new Frame
            {
                BackgroundColor = Color.FromArgb("#E3F2FD"),
                CornerRadius = 10,
                Padding = new Thickness(10),
                HeightRequest = 40,
                WidthRequest = 40,
                HasShadow = false,
                VerticalOptions = LayoutOptions.Start
            };

            var dropoffImage = new Image
            {
                Source = "dropoff.png",
                HeightRequest = 20,
                WidthRequest = 20,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            dropoffFrame.Content = dropoffImage;

            var dropoffInfoStack = new VerticalStackLayout { Spacing = 4 };
            dropoffInfoStack.Children.Add(new Label
            {
                Text = "Drop-off Location",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666"),
                FontAttributes = FontAttributes.Bold
            });
            dropoffInfoStack.Children.Add(new Label
            {
                Text = request.DropoffLocation ?? "Dropoff location",
                FontSize = 14,
                TextColor = Colors.Black,
                LineBreakMode = LineBreakMode.WordWrap,
                LineHeight = 1.3
            });

            dropoffStack.Children.Add(dropoffFrame);
            dropoffStack.Children.Add(dropoffInfoStack);
            RideDetailsContent.Children.Add(dropoffStack);

            var fareSection = CreateModalSection("Fare & Distance");
            RideDetailsContent.Children.Add(fareSection);

            var fareGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                Padding = new Thickness(0, 10, 0, 15)
            };

            var fareInfoStack = new VerticalStackLayout { Spacing = 2 };
            fareInfoStack.Children.Add(new Label
            {
                Text = "Fare",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            });
            fareInfoStack.Children.Add(new Label
            {
                Text = $"\u20B1{request.Fare:N2}",
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#2E7D32")
            });

            var distanceInfoStack = new VerticalStackLayout { Spacing = 2 };
            distanceInfoStack.Children.Add(new Label
            {
                Text = "Distance",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            });
            distanceInfoStack.Children.Add(new Label
            {
                Text = $"{request.Distance:N1} km",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            fareGrid.Children.Add(fareInfoStack);
            fareGrid.Children.Add(distanceInfoStack);
            Grid.SetColumn(distanceInfoStack, 1);
            RideDetailsContent.Children.Add(fareGrid);

            var additionalSection = CreateModalSection("Additional Information");
            RideDetailsContent.Children.Add(additionalSection);

            var additionalGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                Padding = new Thickness(0, 10, 0, 0)
            };

            var seatingInfoStack = new VerticalStackLayout { Spacing = 2 };
            seatingInfoStack.Children.Add(new Label
            {
                Text = "Seating Capacity",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            });
            seatingInfoStack.Children.Add(new Label
            {
                Text = GetSeatingCapacityDisplay(request.SeatingCapacity),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            var passengerTypeInfoStack = new VerticalStackLayout { Spacing = 2 };
            passengerTypeInfoStack.Children.Add(new Label
            {
                Text = "Passenger Type",
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            });
            passengerTypeInfoStack.Children.Add(new Label
            {
                Text = GetPassengerTypeDisplay(request.PassengerType),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black
            });

            additionalGrid.Children.Add(seatingInfoStack);
            additionalGrid.Children.Add(passengerTypeInfoStack);
            Grid.SetColumn(passengerTypeInfoStack, 1);
            RideDetailsContent.Children.Add(additionalGrid);
        }

        private Frame CreateModalSection(string title)
        {
            return new Frame
            {
                BackgroundColor = Color.FromArgb("#F8F9FA"),
                BorderColor = Color.FromArgb("#E0E0E0"),
                CornerRadius = 8,
                Padding = new Thickness(10),
                HasShadow = false,
                Content = new Label
                {
                    Text = title,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#2E7D32")
                }
            };
        }

        private async void OnCloseModalClicked(object sender, EventArgs e)
        {
            await AnimateModalClose();
            RideDetailsModal.IsVisible = false;
            _currentModalRequest = null;
        }

        private async void OnAcceptRideFromModalClicked(object sender, EventArgs e)
        {
            if (_currentModalRequest == null)
            {
                Console.WriteLine("❌ No current modal request");
                return;
            }

            try
            {
                StopRideRequestListener();

                Console.WriteLine($"=== ATTEMPTING TO ACCEPT RIDE FROM MODAL ===");
                Console.WriteLine($"RideRequestId: {_currentModalRequest.FirebaseKey}");
                Console.WriteLine($"Commuter: {_currentModalRequest.CommuterName}");

                if (!_isDriverOnline)
                {
                    await ShowModal("Offline Mode", "You need to be online to accept rides. Please switch to online mode in the Home screen.", "warning", false);
                    await AnimateModalClose();
                    RideDetailsModal.IsVisible = false;
                    StartRideRequestListener();
                    return;
                }

                string driverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(driverId))
                {
                    await ShowModal("Error", "Driver session not found. Please login again.", "error", false);
                    await AnimateModalClose();
                    RideDetailsModal.IsVisible = false;
                    StartRideRequestListener();
                    return;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                string driverName = $"{driver?.FirstName} {driver?.LastName}".Trim();

                Console.WriteLine($"Driver: {driverName} ({driverId})");

                var activeRide = await _firebaseConnection.GetActiveRideForDriverAsync(driverId);
                if (activeRide != null && activeRide.FirebaseKey != _currentModalRequest.FirebaseKey)
                {
                    Console.WriteLine($"❌ Driver already has active ride: {activeRide.FirebaseKey}");
                    await ShowModal("Active Ride", "You already have an active ride. Please complete your current ride before accepting a new one.", "warning", false);
                    await AnimateModalClose();
                    RideDetailsModal.IsVisible = false;
                    StartRideRequestListener();
                    return;
                }

                Console.WriteLine($"Checking if ride is still available...");

                bool isAlreadyTaken = await CheckIfRideAlreadyTaken(_currentModalRequest.FirebaseKey);
                if (isAlreadyTaken)
                {
                    Console.WriteLine($"❌ Ride is no longer available");
                    await ShowModal("Ride Unavailable", "This ride is no longer available. It may have been taken by another driver or cancelled by the passenger.", "warning", false);
                    await AnimateModalClose();
                    RideDetailsModal.IsVisible = false;
                    await LoadRideRequests();
                    StartRideRequestListener();
                    return;
                }

                Console.WriteLine($"Ride is available. Attempting to accept...");

                AcceptButton.IsEnabled = false;
                DeclineButton.IsEnabled = false;

                bool success = await _firebaseConnection.AcceptRideRequestAsync(
                    _currentModalRequest.FirebaseKey, driverId, driverName);

                if (success)
                {
                    Console.WriteLine($"✅ Ride request accepted successfully");

                    await _firebaseConnection.UpdateDriverStatusAsync(driverId, true);

                    await AnimateModalClose();
                    RideDetailsModal.IsVisible = false;

                    Console.WriteLine($"Navigating directly to Driver_StartedTrip...");
                    await Navigation.PushModalAsync(new Driver_StartedTrip(_currentModalRequest.FirebaseKey));
                }
                else
                {
                    Console.WriteLine($"❌ Failed to accept ride request");
                    await ShowModal("Acceptance Failed", "Unable to accept the ride. It may have been taken by another driver just now.", "error", false);
                    await AnimateModalClose();
                    RideDetailsModal.IsVisible = false;
                    await LoadRideRequests();
                    AcceptButton.IsEnabled = true;
                    DeclineButton.IsEnabled = true;
                    StartRideRequestListener();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error accepting ride: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                await ShowModal("Error", $"An error occurred: {ex.Message}. Please try again.", "error", false);
                await AnimateModalClose();
                RideDetailsModal.IsVisible = false;
                AcceptButton.IsEnabled = true;
                DeclineButton.IsEnabled = true;
                StartRideRequestListener();
            }
        }

        private async Task<bool> CheckIfRideAlreadyTaken(string rideRequestId)
        {
            try
            {
                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(rideRequestId);
                if (rideRequest != null)
                {
                    Console.WriteLine($"Checking ride status: {rideRequestId}");
                    Console.WriteLine($"Current Status: {rideRequest.Status}");
                    Console.WriteLine($"DriverId: {rideRequest.DriverId}");
                    Console.WriteLine($"CancelledBy: {rideRequest.CancelledBy}");

                    if (rideRequest.Status == "Cancelled" || !string.IsNullOrEmpty(rideRequest.CancelledBy))
                    {
                        Console.WriteLine($"Ride {rideRequestId} has been cancelled");
                        return true;
                    }

                    if (!string.IsNullOrEmpty(rideRequest.DriverId) && rideRequest.DriverId.Trim() != "")
                    {
                        Console.WriteLine($"Ride already has driver: {rideRequest.DriverId}");
                        return true;
                    }

                    if (rideRequest.Status != "Pending" && rideRequest.Status != "Searching")
                    {
                        Console.WriteLine($"Ride status is {rideRequest.Status}, not available");
                        return true;
                    }

                    Console.WriteLine($"Ride {rideRequestId} is available for acceptance");
                    return false;
                }

                Console.WriteLine($"Ride request {rideRequestId} not found");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking ride status: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task AnimateModalClose()
        {
            try
            {
                var modalContent = RideDetailsModal.Children.OfType<Frame>().FirstOrDefault();
                var blurOverlay = RideDetailsModal.Children.OfType<Grid>().FirstOrDefault();

                if (modalContent != null && blurOverlay != null)
                {
                    await Task.WhenAll(
                        modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                        modalContent.FadeTo(0, 200, Easing.CubicIn),
                        modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                        blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error animating modal close: {ex.Message}");
                await Task.CompletedTask;
            }
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.UtcNow - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            else if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes}m ago";
            else if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours}h ago";
            else
                return $"{(int)timeSpan.TotalDays}d ago";
        }

        private string GetPassengerTypeDisplay(string passengerType)
        {
            return passengerType?.ToLower() switch
            {
                "student" => "Student",
                "senior" => "Senior/PWD",
                "senior/pwd" => "Senior/PWD",
                "pwd" => "Senior/PWD",
                _ => "Regular"
            };
        }

        private string GetSeatingCapacityDisplay(string seatingCapacity)
        {
            if (string.IsNullOrEmpty(seatingCapacity))
                return "1-2 seater";

            var cleaned = seatingCapacity.Trim().ToLower();

            if (cleaned.Contains("1") && cleaned.Contains("2"))
                return "1-2 seater";
            else if (cleaned.Contains("3") && cleaned.Contains("4"))
                return "3-4 seater";
            else if (cleaned.Contains("1-2"))
                return "1-2 seater";
            else if (cleaned.Contains("3-4"))
                return "3-4 seater";
            else if (cleaned.Contains("small") || cleaned.Contains("1-2"))
                return "1-2 seater";
            else if (cleaned.Contains("large") || cleaned.Contains("3-4"))
                return "3-4 seater";
            else
                return seatingCapacity;
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            try
            {
                await this.FadeTo(0, 250, Easing.CubicIn);
                StopRideRequestListener();
                Console.WriteLine("=== GOING BACK TO DRIVER HOME ===");
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error going back: {ex.Message}");
                await Navigation.PopAsync();
            }
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

            var modal = new DriverRideRequestModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

        public new event PropertyChangedEventHandler PropertyChanged;

        protected new void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        ~Driver_RideRequest()
        {
            StopRideRequestListener();
        }
    }

    public class DriverRideRequestModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_RideRequest _parentPage;

        public DriverRideRequestModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_RideRequest parentPage)
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