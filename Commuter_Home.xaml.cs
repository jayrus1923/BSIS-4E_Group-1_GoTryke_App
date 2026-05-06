using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Net.Http;
using System.Linq;

namespace ServiceCo
{
    public partial class Commuter_Home : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private RideRequestWithKey _currentActiveRide;
        private IDisposable _rideStatusListener;
        private IDisposable _bannerListener;
        private IDisposable _autoSlideTimer;
        private string _currentCommuterId;
        private bool _isAutoSliding = true;
        private List<BannerWithKey> _bannerItems = new();
        private List<Frame> _indicatorDots = new();
        private bool _hasInternetConnection = true;
        private HttpClient _httpClient;

        public Commuter_Home()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(3);
            LoadBannersAsync();
            SetupBannerListener();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                if (!await CheckInternetConnection())
                {
                    await ShowConnectionError();
                    ShowEmptyBanner();
                    return;
                }

                bool accountExists = await VerifyCommuterAccount();
                if (!accountExists)
                {
                    await RedirectToGetStarted();
                    return;
                }

                await LoadUserData();
                await CheckActiveRide();
                StartRideStatusListener();
                _hasInternetConnection = true;
                HideConnectionError();
            }
            catch
            {
                ShowEmptyBanner();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopRideStatusListener();
            StopBannerListener();
            StopAutoSlide();
        }

        private async Task LoadBannersAsync()
        {
            try
            {
                var activeBanners = await _firebaseConnection.GetActiveBannersAsync();
                if (activeBanners != null && activeBanners.Count > 0)
                {
                    _bannerItems = activeBanners
                        .OrderBy(b => b.DisplayOrder)
                        .Select(b => new BannerWithKey
                        {
                            FirebaseKey = b.FirebaseKey,
                            ImageUrl = b.ImageUrl,
                            Title = b.Title,
                            RedirectLink = b.RedirectLink,
                            DisplayOrder = b.DisplayOrder,
                            Clicks = b.Clicks
                        })
                        .ToList();

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        ShowBannerSlider();
                        SetupCarousel();
                        CreateIndicatorDots();
                        StartAutoSlide();
                    });
                }
                else
                {
                    ShowEmptyBanner();
                }
            }
            catch
            {
                ShowEmptyBanner();
            }
        }

        private void SetupBannerListener()
        {
            try
            {
                StopBannerListener();
                _bannerListener = _firebaseConnection.ListenForBannerChanges((updatedBanners) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var activeBanners = updatedBanners
                            .Where(b => b.Status == "active")
                            .OrderBy(b => b.DisplayOrder)
                            .ToList();

                        _bannerItems = activeBanners
                            .Select(b => new BannerWithKey
                            {
                                FirebaseKey = b.FirebaseKey,
                                ImageUrl = b.ImageUrl,
                                Title = b.Title,
                                RedirectLink = b.RedirectLink,
                                DisplayOrder = b.DisplayOrder,
                                Clicks = b.Clicks
                            })
                            .ToList();

                        if (_bannerItems.Count > 0)
                        {
                            ShowBannerSlider();
                            SetupCarousel();
                            CreateIndicatorDots();
                            BannerCarousel.Position = 0;
                            UpdateIndicator(0);
                        }
                        else
                        {
                            ShowEmptyBanner();
                        }
                    });
                });
            }
            catch
            {
            }
        }

        private void StopBannerListener()
        {
            try
            {
                _bannerListener?.Dispose();
                _bannerListener = null;
            }
            catch
            {
            }
        }

        private void SetupCarousel()
        {
            if (_bannerItems.Count == 0) return;
            BannerCarousel.PositionChanged -= OnCarouselPositionChanged;
            BannerCarousel.ItemsSource = null;
            BannerCarousel.ItemsSource = _bannerItems;
            BannerCarousel.PositionChanged += OnCarouselPositionChanged;
            BannerCarousel.Position = 0;
            UpdateIndicator(0);
        }

        private void OnCarouselPositionChanged(object sender, PositionChangedEventArgs e)
        {
            int position = e.CurrentPosition;
            if (position >= 0 && position < _bannerItems.Count)
            {
                UpdateIndicator(position);
            }
        }

        private void CreateIndicatorDots()
        {
            IndicatorContainer.Children.Clear();
            _indicatorDots.Clear();
            if (_bannerItems.Count == 0)
            {
                IndicatorContainer.IsVisible = false;
                return;
            }
            for (int i = 0; i < _bannerItems.Count; i++)
            {
                var dot = new Frame
                {
                    BackgroundColor = i == 0 ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0"),
                    CornerRadius = 5,
                    HeightRequest = 10,
                    WidthRequest = 10,
                    Margin = new Thickness(4, 0),
                    Padding = 0,
                    HasShadow = false
                };
                _indicatorDots.Add(dot);
                IndicatorContainer.Children.Add(dot);
            }
            IndicatorContainer.IsVisible = _bannerItems.Count > 1;
        }

        private void UpdateIndicator(int position)
        {
            if (_indicatorDots == null || _indicatorDots.Count == 0) return;
            for (int i = 0; i < _indicatorDots.Count; i++)
            {
                _indicatorDots[i].BackgroundColor = i == position
                    ? Color.FromArgb("#2E7D32")
                    : Color.FromArgb("#E0E0E0");
            }
        }

        private void StartAutoSlide()
        {
            StopAutoSlide();
            if (_bannerItems.Count <= 1) return;
            _isAutoSliding = true;
            _autoSlideTimer = Observable.Interval(TimeSpan.FromSeconds(4))
                .Subscribe(_ =>
                {
                    if (_isAutoSliding && _bannerItems.Count > 0)
                    {
                        MainThread.BeginInvokeOnMainThread(AutoSlideToNext);
                    }
                });
        }

        private void StopAutoSlide()
        {
            _autoSlideTimer?.Dispose();
            _autoSlideTimer = null;
        }

        private void AutoSlideToNext()
        {
            if (_bannerItems.Count == 0) return;
            int nextPos = (BannerCarousel.Position + 1) % _bannerItems.Count;
            BannerCarousel.Position = nextPos;
        }

        private async void OnBannerTapped(object sender, EventArgs e)
        {
            try
            {
                var tappedEventArgs = e as TappedEventArgs;
                var bannerItem = tappedEventArgs?.Parameter as BannerWithKey;
                if (bannerItem == null) return;

                if (_hasInternetConnection && !string.IsNullOrEmpty(bannerItem.FirebaseKey))
                {
                    try
                    {
                        await _firebaseConnection.IncrementBannerClickAsync(bannerItem.FirebaseKey);
                        bannerItem.Clicks++;
                    }
                    catch
                    {
                    }
                }
                await ShowFullscreenImage(bannerItem.ImageUrl, bannerItem.Title, bannerItem.RedirectLink);
            }
            catch
            {
            }
        }

        private async Task ShowFullscreenImage(string imageUrl, string title, string redirectLink = "")
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl)) return;

                var grid = new Grid();
                var image = new Image
                {
                    Source = imageUrl,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(20)
                };
                grid.Children.Add(image);

                var closeButton = new Button
                {
                    Text = "✕",
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.White,
                    FontSize = 25,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Start,
                    Margin = 20,
                    WidthRequest = 50,
                    HeightRequest = 50,
                    CornerRadius = 25,
                    Background = new SolidColorBrush(Color.FromArgb("#80000000"))
                };
                closeButton.Clicked += async (s, e) => await Navigation.PopModalAsync();
                grid.Children.Add(closeButton);

                var titleFrame = new Frame
                {
                    BackgroundColor = Color.FromArgb("#80000000"),
                    Padding = new Thickness(20, 10),
                    CornerRadius = 8,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 0, 30),
                    Content = new Label
                    {
                        Text = title,
                        TextColor = Colors.White,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold
                    }
                };
                grid.Children.Add(titleFrame);

                if (!string.IsNullOrEmpty(redirectLink))
                {
                    var linkFrame = new Frame
                    {
                        BackgroundColor = Color.FromArgb("#2E7D32"),
                        Padding = new Thickness(15, 10),
                        CornerRadius = 25,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.End,
                        Margin = new Thickness(0, 0, 0, 100),
                        HasShadow = true
                    };
                    var linkStack = new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            new Label { Text = "🔗", FontSize = 18, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center },
                            new Label { Text = "Open Link", FontSize = 16, TextColor = Colors.White, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
                        }
                    };
                    linkFrame.Content = linkStack;
                    var tapGesture = new TapGestureRecognizer();
                    tapGesture.Tapped += async (s, e) =>
                    {
                        await Navigation.PopModalAsync();
                        try
                        {
                            await Launcher.OpenAsync(new Uri(redirectLink));
                        }
                        catch
                        {
                            await ShowErrorModal("Cannot open link");
                        }
                    };
                    linkFrame.GestureRecognizers.Add(tapGesture);
                    grid.Children.Add(linkFrame);
                }

                var fullscreenPage = new ContentPage
                {
                    BackgroundColor = Colors.Black,
                    Content = grid
                };
                await Navigation.PushModalAsync(fullscreenPage);
            }
            catch
            {
                await ShowErrorModal("Failed to load image");
            }
        }

        private void ShowBannerSlider()
        {
            BannerCarousel.IsVisible = true;
            EmptyBannerContent.IsVisible = false;
            IndicatorContainer.IsVisible = true;
        }

        private void ShowEmptyBanner()
        {
            BannerCarousel.IsVisible = false;
            EmptyBannerContent.IsVisible = true;
            IndicatorContainer.IsVisible = false;
        }

        private async Task<bool> CheckInternetConnection()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://www.google.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task ShowConnectionError()
        {
            _hasInternetConnection = false;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ConnectionErrorLabel.Text = "No internet connection. Some features may be limited.";
                ConnectionErrorGrid.IsVisible = true;
            });
        }

        private void HideConnectionError()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ConnectionErrorGrid.IsVisible = false;
            });
        }

        private async Task<bool> VerifyCommuterAccount()
        {
            _currentCommuterId = AppSession.GetCommuterId();
            if (string.IsNullOrEmpty(_currentCommuterId) || !AppSession.IsCommuterLoggedIn())
                return false;
            var commuter = await _firebaseConnection.GetCommuterByIdAsync(_currentCommuterId);
            return commuter != null;
        }

        private async Task RedirectToGetStarted()
        {
            AppSession.LogoutCommuter();
            await ShowErrorModal("Your account could not be found. Please register again.");
            await Navigation.PushModalAsync(new GetStarted());
        }

        private async Task LoadUserData()
        {
            try
            {
                _currentCommuterId = AppSession.GetCommuterId();
                if (!string.IsNullOrEmpty(_currentCommuterId))
                {
                    var commuter = await _firebaseConnection.GetCommuterByIdAsync(_currentCommuterId);
                    if (commuter != null && !string.IsNullOrEmpty(commuter.FirstName))
                    {
                        string greeting = GetTimeBasedGreeting();
                        GreetingLabel.Text = $"{greeting}, {commuter.FirstName}!";
                    }
                    else
                    {
                        GreetingLabel.Text = $"{GetTimeBasedGreeting()}!";
                    }
                }
                else
                {
                    GreetingLabel.Text = $"{GetTimeBasedGreeting()}!";
                }
            }
            catch
            {
                GreetingLabel.Text = $"{GetTimeBasedGreeting()}!";
            }
        }

        private string GetTimeBasedGreeting()
        {
            int hour = DateTime.Now.Hour;
            if (hour >= 5 && hour < 12) return "Good morning";
            if (hour >= 12 && hour < 18) return "Good afternoon";
            return "Good evening";
        }

        private async Task CheckActiveRide()
        {
            try
            {
                _currentCommuterId = AppSession.GetCommuterId();
                if (string.IsNullOrEmpty(_currentCommuterId))
                {
                    HideActiveRideBanner();
                    return;
                }
                _currentActiveRide = await _firebaseConnection.GetActiveRideForCommuterAsync(_currentCommuterId);
                if (_currentActiveRide != null && ShouldShowActiveRideBanner(_currentActiveRide.Status))
                {
                    ShowActiveRideBanner(_currentActiveRide);
                }
                else
                {
                    HideActiveRideBanner();
                }
            }
            catch
            {
                HideActiveRideBanner();
            }
        }

        private void StartRideStatusListener()
        {
            if (string.IsNullOrEmpty(_currentCommuterId)) return;
            StopRideStatusListener();
            _rideStatusListener = _firebaseConnection.ListenForRideStatusChangeRealTimeForCommuter(
                _currentCommuterId,
                (rideRequest) =>
                {
                    if (rideRequest != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            var rideWithKey = new RideRequestWithKey
                            {
                                FirebaseKey = rideRequest.Id,
                                PickupLocation = rideRequest.PickupLocation,
                                DropoffLocation = rideRequest.DropoffLocation,
                                Status = rideRequest.Status,
                                SeatingCapacity = rideRequest.SeatingCapacity,
                                Fare = rideRequest.Fare,
                                RequestTime = rideRequest.RequestTime,
                                DriverId = rideRequest.DriverId
                            };
                            _currentActiveRide = rideWithKey;
                            if (ShouldShowActiveRideBanner(rideRequest.Status))
                            {
                                ShowActiveRideBanner(rideWithKey);
                            }
                            else
                            {
                                HideActiveRideBanner();
                            }
                        });
                    }
                });
        }

        private void StopRideStatusListener()
        {
            _rideStatusListener?.Dispose();
            _rideStatusListener = null;
        }

        private bool ShouldShowActiveRideBanner(string status)
        {
            return status != "Completed" && status != "Cancelled" && !string.IsNullOrEmpty(status);
        }

        private void ShowActiveRideBanner(RideRequestWithKey activeRide)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ActiveRideBanner.IsVisible = true;
                switch (activeRide.Status)
                {
                    case "Pending":
                        RideStatusBadge.BackgroundColor = Color.FromArgb("#FFB300");
                        RideStatusBadgeText.Text = "SEARCHING";
                        RideStatusLabel.Text = "Looking for your driver";
                        RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                        break;
                    case "Accepted":
                        RideStatusBadge.BackgroundColor = Color.FromArgb("#2E7D32");
                        RideStatusBadgeText.Text = "DRIVER FOUND";
                        RideStatusLabel.Text = "Driver is on the way";
                        RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                        break;
                    case "Picking Up":
                        RideStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                        RideStatusBadgeText.Text = "PICKING UP";
                        RideStatusLabel.Text = "Driver is picking you up";
                        RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                        break;
                    case "Arrived at Pickup":
                    case "Arrived at Pickup Location":
                        RideStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                        RideStatusBadgeText.Text = "ARRIVED";
                        RideStatusLabel.Text = "Driver has arrived";
                        RideTimeLabel.Text = "Now";
                        break;
                    case "Trip Started":
                        RideStatusBadge.BackgroundColor = Color.FromArgb("#F57C00");
                        RideStatusBadgeText.Text = "ON TRIP";
                        RideStatusLabel.Text = "Trip in progress";
                        RideTimeLabel.Text = "Now";
                        break;
                    case "Payment Pending":
                        RideStatusBadge.BackgroundColor = Color.FromArgb("#4CAF50");
                        RideStatusBadgeText.Text = "PAY NOW";
                        RideStatusLabel.Text = "Complete your payment";
                        RideTimeLabel.Text = "Now";
                        break;
                    default:
                        ActiveRideBanner.IsVisible = false;
                        return;
                }
                PickupLabel.Text = activeRide.PickupLocation;
                DropoffLabel.Text = activeRide.DropoffLocation;
            });
        }

        private void HideActiveRideBanner()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ActiveRideBanner.IsVisible = false;
                _currentActiveRide = null;
            });
        }

        private string GetTimeAgo(DateTime requestTime)
        {
            var diff = DateTime.UtcNow - requestTime;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hr ago";
            return $"{(int)diff.TotalDays} day ago";
        }

        public void UpdateRideStatus(string status, string pickupLocation = "", string dropoffLocation = "")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (ShouldShowActiveRideBanner(status))
                    {
                        ActiveRideBanner.IsVisible = true;

                        switch (status)
                        {
                            case "Pending":
                                RideStatusBadge.BackgroundColor = Color.FromArgb("#FFB300");
                                RideStatusBadgeText.Text = "SEARCHING";
                                RideStatusLabel.Text = "Looking for your driver";
                                RideTimeLabel.Text = "Just now";
                                break;
                            case "Accepted":
                                RideStatusBadge.BackgroundColor = Color.FromArgb("#2E7D32");
                                RideStatusBadgeText.Text = "DRIVER FOUND";
                                RideStatusLabel.Text = "Driver is on the way";
                                RideTimeLabel.Text = "Just now";
                                break;
                            case "Picking Up":
                                RideStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                                RideStatusBadgeText.Text = "PICKING UP";
                                RideStatusLabel.Text = "Driver is picking you up";
                                RideTimeLabel.Text = "Now";
                                break;
                            case "Arrived at Pickup":
                            case "Arrived at Pickup Location":
                                RideStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                                RideStatusBadgeText.Text = "ARRIVED";
                                RideStatusLabel.Text = "Driver has arrived";
                                RideTimeLabel.Text = "Now";
                                break;
                            case "Trip Started":
                                RideStatusBadge.BackgroundColor = Color.FromArgb("#F57C00");
                                RideStatusBadgeText.Text = "ON TRIP";
                                RideStatusLabel.Text = "Trip in progress";
                                RideTimeLabel.Text = "Now";
                                break;
                            case "Payment Pending":
                                RideStatusBadge.BackgroundColor = Color.FromArgb("#4CAF50");
                                RideStatusBadgeText.Text = "PAY NOW";
                                RideStatusLabel.Text = "Complete your payment";
                                RideTimeLabel.Text = "Now";
                                break;
                            default:
                                ActiveRideBanner.IsVisible = false;
                                return;
                        }

                        if (!string.IsNullOrEmpty(pickupLocation))
                            PickupLabel.Text = pickupLocation;

                        if (!string.IsNullOrEmpty(dropoffLocation))
                            DropoffLabel.Text = dropoffLocation;
                    }
                    else
                    {
                        ActiveRideBanner.IsVisible = false;
                        _currentActiveRide = null;
                    }
                }
                catch
                {
                }
            });
        }

        private async void OnActiveRideBannerTapped(object sender, EventArgs e)
        {
            if (_currentActiveRide == null) return;
            if (_currentActiveRide.Status == "Payment Pending")
            {
                await Navigation.PushModalAsync(new Commuter_Payment(_currentActiveRide.FirebaseKey));
            }
            else
            {
                await Navigation.PushModalAsync(new Commuter_SearchDriver(
                    _currentActiveRide.PickupLocation,
                    _currentActiveRide.DropoffLocation,
                    _currentActiveRide.SeatingCapacity,
                    _currentActiveRide.Fare,
                    _currentActiveRide.FirebaseKey
                ));
            }
        }

        private async void OnSearchFrameTapped(object sender, EventArgs e)
        {
            await HandleNavigation();
        }

        private async void OnBookRideClicked(object sender, EventArgs e)
        {
            await HandleNavigation();
        }

        private async void OnSavedRouteClicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new Commuter_SaveRoute());
        }

        private async Task HandleNavigation()
        {
            string commuterId = AppSession.GetCommuterId();
            if (!string.IsNullOrEmpty(commuterId))
            {
                var activeRide = await _firebaseConnection.GetActiveRideForCommuterAsync(commuterId);
                if (activeRide != null && ShouldShowActiveRideBanner(activeRide.Status))
                {
                    if (activeRide.Status == "Payment Pending")
                    {
                        await Navigation.PushModalAsync(new Commuter_Payment(activeRide.FirebaseKey));
                    }
                    else
                    {
                        await Navigation.PushModalAsync(new Commuter_SearchDriver(
                            activeRide.PickupLocation,
                            activeRide.DropoffLocation,
                            activeRide.SeatingCapacity,
                            activeRide.Fare,
                            activeRide.FirebaseKey
                        ));
                    }
                    return;
                }
            }
            await Navigation.PushModalAsync(new Commuter_SearchLocation());
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            var refreshView = sender as RefreshView;
            if (refreshView == null) return;
            try
            {
                bool hasInternet = await CheckInternetConnection();

                if (!hasInternet)
                {
                    await ShowConnectionError();
                    refreshView.IsRefreshing = false;
                    return;
                }

                if (hasInternet && !_hasInternetConnection)
                {
                    _hasInternetConnection = true;
                    HideConnectionError();
                }

                await LoadUserData();
                await CheckActiveRide();
                await LoadBannersAsync();
                StartRideStatusListener();
                SetupBannerListener();
            }
            finally
            {
                refreshView.IsRefreshing = false;
            }
        }

        private async void OnRetryConnectionClicked(object sender, EventArgs e)
        {
            try
            {
                ConnectionErrorLabel.Text = "Reconnecting...";
                if (await CheckInternetConnection())
                {
                    _hasInternetConnection = true;
                    HideConnectionError();
                    await LoadUserData();
                    await CheckActiveRide();
                    await LoadBannersAsync();
                    StartRideStatusListener();
                    SetupBannerListener();
                    StartAutoSlide();
                }
                else
                {
                    ConnectionErrorLabel.Text = "Still no internet connection. Please check your network.";
                    await ShowErrorModal("Unable to connect. Please check your network.");
                }
            }
            catch
            {
                ConnectionErrorLabel.Text = "No internet connection. Some features may be limited.";
            }
        }

        private async Task ShowErrorModal(string message)
        {
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
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
            {
                new Label
                {
                    Text = "⚠️",
                    FontSize = 50,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = message,
                    FontSize = 16,
                    TextColor = Color.FromArgb("#D32F2F"),
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Button
                {
                    Text = "OK",
                    BackgroundColor = Color.FromArgb("#2E7D32"),
                    TextColor = Colors.White,
                    CornerRadius = 10,
                    HeightRequest = 45,
                    FontAttributes = FontAttributes.Bold
                }
            }
                }
            };

            var contentGrid = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            // Create custom page with back button override
            var modal = new ModalPage(modalContent, blurOverlay);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            var button = (modalContent.Content as VerticalStackLayout).Children[2] as Button;
            button.Clicked += async (s, e) =>
            {
                button.IsEnabled = false;
                await AnimateModalExit(modal, modalContent, blurOverlay);
            };
        }

        private async Task AnimateModalExit(ContentPage modal, Frame modalContent, Grid blurOverlay)
        {
            await Task.WhenAll(
                modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                modalContent.FadeTo(0, 200, Easing.CubicIn),
                modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );
            await Navigation.PopModalAsync();
        }

        // Custom modal page class inside same file
        public class ModalPage : ContentPage
        {
            private Frame _modalContent;
            private Grid _blurOverlay;

            public ModalPage(Frame modalContent, Grid blurOverlay)
            {
                _modalContent = modalContent;
                _blurOverlay = blurOverlay;
                BackgroundColor = Colors.Transparent;
                Content = new Grid
                {
                    Children = { blurOverlay, modalContent }
                };
            }

            protected override bool OnBackButtonPressed()
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var button = (_modalContent.Content as VerticalStackLayout).Children[2] as Button;
                    button.IsEnabled = false;

                    await Task.WhenAll(
                        _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                        _modalContent.FadeTo(0, 200, Easing.CubicIn),
                        _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                        _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                    );

                    await Navigation.PopModalAsync();
                });

                return true;
            }
        }
    }
}