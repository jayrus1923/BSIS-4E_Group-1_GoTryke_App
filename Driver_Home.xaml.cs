using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Net.Http;

namespace ServiceCo
{
    public partial class Driver_Home : ContentPage, INotifyPropertyChanged
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private RideRequestWithKey _currentActiveRide;
        private Driver _currentDriver;
        private TricycleInfo _tricycleInfo;
        private bool _isDriverOnline = false;
        private bool _isRefreshing;
        private IDisposable _rideStatusListener;
        private IDisposable _documentStatusListener;
        private IDisposable _accountStatusListener;
        private IDisposable _suspensionExpiryListener;
        private bool _isTricycleListenerActive = false;
        private IDisposable _bannerListener;
        private bool _isAutoSliding = true;
        private List<BannerWithKey> _bannerItems = new();
        private List<Frame> _indicatorDots = new();
        private IDisposable _ratingListener;
        private string _currentDriverId;
        private bool _isInitialLoad = true;
        private double _currentRating = 5.0;
        private bool _isRatingLoaded = false;
        private bool _isCheckingAccount = false;
        private bool _hasInternetConnection = true;
        private HttpClient _httpClient;
        private bool _hasShownApprovedModal = false;
        private bool _hasShownTricycleApprovedModal = false;
        private bool _hasShownTricycleApprovedSuccessModal = false;
        private List<string> _pendingDocumentsToReupload = new List<string>();
        private bool _hasRedirected = false;
        private bool _showFullDashboard = false;
        private bool _isDriverApproved = false;
        private bool _isTricycleApproved = false;
        private string _tricycleStatus = "Pending";
        private string _tricycleRejectionReason = "";
        private string _accountStatus = "Active";
        private string _suspensionReason = "";
        private string _suspensionUntil = "";
        private string _deactivationReason = "";
        private bool _isModalOpen = false;
        private bool _hasShownReactivatedModal = false;

        public static string SharedTotalRides = "0";
        public static string SharedTodayRides = "0";
        public static string SharedTotalEarnings = "0";
        public static event Action StatisticsUpdated;

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }

        public Driver_Home()
        {
            InitializeComponent();
            BindingContext = this;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(3);

            string driverId = AppSession.GetDriverId();
            _hasShownApprovedModal = Preferences.Get($"ApprovedModalShown_{driverId}", false);
            _hasShownTricycleApprovedModal = Preferences.Get($"TricycleApprovedModalShown_{driverId}", false);
            _hasShownTricycleApprovedSuccessModal = Preferences.Get($"TricycleApprovedSuccessModalShown_{driverId}", false);
            _hasShownReactivatedModal = Preferences.Get($"ReactivatedModalShown_{driverId}", false);

            LoadBannersAsync();
            SetupBannerListener();

            MessagingCenter.Subscribe<Driver_Notification>(this, "UpdateNotificationBadge", async (sender) =>
            {
                await UpdateNotificationBadge();
            });
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            _isInitialLoad = true;
            _hasShownReactivatedModal = false;

            try
            {
                if (!await CheckInternetConnection())
                {
                    await ShowConnectionError();
                    ShowEmptyBanner();
                    _isInitialLoad = false;
                    return;
                }

                bool accountExists = await VerifyDriverAccount();
                if (!accountExists)
                {
                    await RedirectToGetStarted();
                    return;
                }

                await LoadUserData();
                await LoadTricycleInfo();
                await LoadDriverStatistics();
                await CheckActiveRide();
                await CheckDriverStatusAndShowView();

                StartDocumentStatusListener();
                StartTricycleStatusListener();
                StartAccountStatusListener();
                StartRideStatusListener();
                StartSuspensionExpiryListener();
                await UpdateNotificationBadge();

                _hasInternetConnection = true;
                HideConnectionError();
            }
            catch (Exception ex)
            {
                if (IsNetworkError(ex))
                {
                    await ShowConnectionError();
                }
                ShowEmptyBanner();
            }
            finally
            {
                _isInitialLoad = false;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopRideStatusListener();
            StopDocumentStatusListener();
            StopTricycleStatusListener();
            StopAccountStatusListener();
            _suspensionExpiryListener?.Dispose();
            StopBannerListener();
            StopAutoSlide();
            StopRatingListener();
            MessagingCenter.Unsubscribe<Driver_Notification>(this, "UpdateNotificationBadge");
        }

        private async void OnContactSupportClicked(object sender, EventArgs e)
        {
            await ShowModal("Contact Support", "Please email support@serviceco.com or call +639123456789", "info", false);
        }

        private void StartSuspensionExpiryListener()
        {
            try
            {
                if (!_hasInternetConnection || string.IsNullOrEmpty(_currentDriverId)) return;

                _suspensionExpiryListener?.Dispose();

                _suspensionExpiryListener = _firebaseConnection.ListenForDriverAccountStatus(
                    _currentDriverId,
                    async (accountStatus, suspensionReason, deactivationReason, suspendedUntil) =>
                    {
                        bool wasSuspended = _accountStatus == "Suspended";
                        bool isNowActive = accountStatus == "Active";
                        bool statusChangedToActive = wasSuspended && isNowActive;

                        if (statusChangedToActive && !_hasShownReactivatedModal)
                        {
                            _hasShownReactivatedModal = true;
                            Preferences.Set($"ReactivatedModalShown_{_currentDriverId}", true);

                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                await ShowReactivatedModal();
                                await CheckDriverStatusAndShowView();
                            });
                        }
                    });
            }
            catch { }
        }

        private void StartAccountStatusListener()
        {
            try
            {
                if (!_hasInternetConnection || string.IsNullOrEmpty(_currentDriverId)) return;

                Device.StartTimer(TimeSpan.FromSeconds(3), () =>
                {
                    if (string.IsNullOrEmpty(_currentDriverId)) return false;

                    Task.Run(async () =>
                    {
                        try
                        {
                            var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                            if (driver != null)
                            {
                                string newAccountStatus = driver.AccountStatus ?? "Active";
                                string newSuspensionReason = driver.SuspensionReason ?? "";
                                string newSuspensionUntil = driver.SuspendedUntil ?? "";
                                string newDeactivationReason = driver.DeactivationReason ?? "";

                                bool wasSuspended = _accountStatus == "Suspended";
                                bool isNowActive = newAccountStatus == "Active";
                                bool statusChangedToActive = wasSuspended && isNowActive;

                                if (_accountStatus != newAccountStatus ||
                                    _suspensionReason != newSuspensionReason ||
                                    _suspensionUntil != newSuspensionUntil ||
                                    _deactivationReason != newDeactivationReason)
                                {
                                    _accountStatus = newAccountStatus;
                                    _suspensionReason = newSuspensionReason;
                                    _suspensionUntil = newSuspensionUntil;
                                    _deactivationReason = newDeactivationReason;

                                    MainThread.BeginInvokeOnMainThread(async () =>
                                    {
                                        await CheckDriverStatusAndShowView();

                                        if (statusChangedToActive && !_hasShownReactivatedModal)
                                        {
                                            _hasShownReactivatedModal = true;
                                            Preferences.Set($"ReactivatedModalShown_{_currentDriverId}", true);
                                            await ShowReactivatedModal();
                                        }

                                        await UpdateNotificationBadge();
                                    });
                                }
                            }
                        }
                        catch { }
                    });

                    return true;
                });
            }
            catch { }
        }

        private async Task ShowReactivatedModal()
        {
            await ShowModal("Account Reactivated", "Your suspension period has expired. Your account is now active again! You can now go online and accept rides.", "success", false);
        }

        private void StopAccountStatusListener() { }

        private async Task UpdateUIBasedOnAccountStatus()
        {
            try
            {
                if (_accountStatus == "Suspended")
                {
                    SuspendedView.IsVisible = true;
                    ApprovedView.IsVisible = false;
                    DeactivatedView.IsVisible = false;
                    WaitingView.IsVisible = false;
                    RejectedView.IsVisible = false;
                    ActiveSwitch.IsEnabled = false;
                    ActiveSwitch.IsToggled = false;

                    string untilText = "";
                    if (!string.IsNullOrEmpty(_suspensionUntil))
                    {
                        try
                        {
                            DateTime untilDate = DateTime.Parse(_suspensionUntil);
                            untilText = untilDate.ToString("MMMM dd, yyyy");
                        }
                        catch { }
                    }

                    SuspendedMessageLabel.Text = !string.IsNullOrEmpty(_suspensionReason)
                        ? $"Reason: {_suspensionReason}"
                        : "Your account has been suspended by admin.";

                    SuspendedUntilLabel.Text = !string.IsNullOrEmpty(untilText)
                        ? $"Suspended until: {untilText}"
                        : "";
                }
                else if (_accountStatus == "Deactivated")
                {
                    DeactivatedView.IsVisible = true;
                    ApprovedView.IsVisible = false;
                    SuspendedView.IsVisible = false;
                    WaitingView.IsVisible = false;
                    RejectedView.IsVisible = false;
                    ActiveSwitch.IsEnabled = false;
                    ActiveSwitch.IsToggled = false;

                    DeactivatedMessageLabel.Text = !string.IsNullOrEmpty(_deactivationReason)
                        ? $"Reason: {_deactivationReason}"
                        : "Your account has been deactivated by admin.";
                }
                else if (_accountStatus == "Active")
                {
                    SuspendedView.IsVisible = false;
                    DeactivatedView.IsVisible = false;

                    if (_isDriverApproved)
                    {
                        if (_isTricycleApproved)
                        {
                            ApprovedView.IsVisible = true;
                            ActiveSwitch.IsEnabled = true;
                            RideRequestButton.IsVisible = true;
                            UpdateOnlineStatusUI();
                        }
                        else
                        {
                            ApprovedView.IsVisible = true;
                            ActiveSwitch.IsEnabled = true;
                            ActiveSwitch.IsToggled = false;
                            RideRequestButton.IsVisible = false;
                            OnlineStatusText.Text = "Offline";
                            OnlineStatusText.FontSize = 14;
                        }
                    }
                }
            }
            catch { }
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
            catch { }
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

                            int currentPosition = ImageCarousel.Position;
                            if (currentPosition >= _bannerItems.Count)
                            {
                                ImageCarousel.Position = 0;
                            }
                            UpdateIndicator(ImageCarousel.Position);
                        }
                        else
                        {
                            ShowEmptyBanner();
                        }
                    });
                });
            }
            catch { }
        }

        private void StopBannerListener()
        {
            try
            {
                _bannerListener?.Dispose();
                _bannerListener = null;
            }
            catch { }
        }

        private void SetupCarousel()
        {
            if (_bannerItems.Count == 0) return;

            ImageCarousel.PositionChanged -= OnCarouselPositionChanged;
            ImageCarousel.ItemsSource = null;
            ImageCarousel.ItemsSource = _bannerItems;
            ImageCarousel.PositionChanged += OnCarouselPositionChanged;

            if (ImageCarousel.Position >= _bannerItems.Count)
            {
                ImageCarousel.Position = 0;
            }
            UpdateIndicator(ImageCarousel.Position);
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

            Device.StartTimer(TimeSpan.FromSeconds(4), () =>
            {
                if (_isAutoSliding && _bannerItems.Count > 0)
                {
                    MainThread.BeginInvokeOnMainThread(AutoSlideToNext);
                }
                return true;
            });
        }

        private void StopAutoSlide()
        {
            _isAutoSliding = false;
        }

        private void AutoSlideToNext()
        {
            if (_bannerItems.Count == 0) return;
            int nextPos = (ImageCarousel.Position + 1) % _bannerItems.Count;
            ImageCarousel.Position = nextPos;
        }

        private async void OnCarouselItemTapped(object sender, EventArgs e)
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
                    catch { }
                }

                await ShowFullscreenImage(bannerItem.ImageUrl, bannerItem.Title, bannerItem.RedirectLink);
            }
            catch { }
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
                        catch { }
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
            catch { }
        }

        private void ShowBannerSlider()
        {
            ImageCarousel.IsVisible = true;
            EmptyBannerContent.IsVisible = false;
            IndicatorContainer.IsVisible = true;
        }

        private void ShowEmptyBanner()
        {
            ImageCarousel.IsVisible = false;
            EmptyBannerContent.IsVisible = true;
            IndicatorContainer.IsVisible = false;
        }

        private async Task LoadTricycleInfo()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    _tricycleInfo = null;
                    return;
                }

                _tricycleInfo = await _firebaseConnection.GetTricycleInfoAsync(_currentDriverId);

                if (_tricycleInfo == null)
                {
                    _tricycleStatus = "Not Set";
                    _isTricycleApproved = false;
                    _tricycleRejectionReason = "";
                }
                else
                {
                    string oldStatus = _tricycleStatus;
                    _tricycleStatus = _tricycleInfo.TricycleStatus ?? "Pending";
                    _isTricycleApproved = _tricycleStatus == "Approved";
                    _tricycleRejectionReason = _tricycleInfo.RejectionReason ?? "";

                    if (_isTricycleApproved && oldStatus != "Approved" && !_hasShownTricycleApprovedSuccessModal)
                    {
                        _hasShownTricycleApprovedSuccessModal = false;
                    }
                }
            }
            catch
            {
                _tricycleStatus = "Error";
                _isTricycleApproved = false;
            }
        }

        private async Task CheckDriverStatusAndShowView()
        {
            try
            {
                _currentDriverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(_currentDriverId)) return;

                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver == null) return;

                string docStatus = driver.DocumentStatus ?? "Pending";
                bool regCompleted = driver.RegistrationCompleted;
                _accountStatus = driver.AccountStatus ?? "Active";
                _suspensionReason = driver.SuspensionReason ?? "";
                _suspensionUntil = driver.SuspendedUntil ?? "";
                _deactivationReason = driver.DeactivationReason ?? "";

                _isDriverApproved = regCompleted && docStatus == "Approved";

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (_accountStatus == "Suspended")
                    {
                        SuspendedView.IsVisible = true;
                        ApprovedView.IsVisible = false;
                        DeactivatedView.IsVisible = false;
                        WaitingView.IsVisible = false;
                        RejectedView.IsVisible = false;
                        TricycleRejectedSection.IsVisible = false;
                        TricyclePendingSection.IsVisible = false;
                        FullDashboardSection.IsVisible = false;
                        ActiveSwitch.IsEnabled = false;
                        ActiveSwitch.IsToggled = false;

                        string untilText = "";
                        if (!string.IsNullOrEmpty(_suspensionUntil))
                        {
                            try
                            {
                                DateTime untilDate = DateTime.Parse(_suspensionUntil);
                                untilText = untilDate.ToString("MMMM dd, yyyy");
                            }
                            catch { }
                        }

                        SuspendedMessageLabel.Text = !string.IsNullOrEmpty(_suspensionReason)
                            ? $"Reason: {_suspensionReason}"
                            : "Your account has been suspended by admin.";

                        SuspendedUntilLabel.Text = !string.IsNullOrEmpty(untilText)
                            ? $"Suspended until: {untilText}"
                            : "";
                    }
                    else if (_accountStatus == "Deactivated")
                    {
                        DeactivatedView.IsVisible = true;
                        ApprovedView.IsVisible = false;
                        SuspendedView.IsVisible = false;
                        WaitingView.IsVisible = false;
                        RejectedView.IsVisible = false;
                        TricycleRejectedSection.IsVisible = false;
                        TricyclePendingSection.IsVisible = false;
                        FullDashboardSection.IsVisible = false;
                        ActiveSwitch.IsEnabled = false;
                        ActiveSwitch.IsToggled = false;

                        DeactivatedMessageLabel.Text = !string.IsNullOrEmpty(_deactivationReason)
                            ? $"Reason: {_deactivationReason}"
                            : "Your account has been deactivated by admin.";
                    }
                    else if (regCompleted && docStatus == "Approved")
                    {
                        WaitingView.IsVisible = false;
                        RejectedView.IsVisible = false;
                        SuspendedView.IsVisible = false;
                        DeactivatedView.IsVisible = false;
                        ApprovedView.IsVisible = true;

                        if (_tricycleStatus == "Rejected")
                        {
                            TricycleRejectedSection.IsVisible = true;
                            TricyclePendingSection.IsVisible = false;
                            FullDashboardSection.IsVisible = false;
                            ActiveSwitch.IsEnabled = false;
                            ActiveSwitch.IsToggled = false;
                            RideRequestButton.IsVisible = false;

                            TricycleRejectionReasonLabel.Text = !string.IsNullOrEmpty(_tricycleRejectionReason)
                                ? $"Reason: {_tricycleRejectionReason}"
                                : "Your tricycle details were rejected by admin.";
                        }
                        else if (_isTricycleApproved)
                        {
                            bool wasTricyclePending = TricyclePendingSection.IsVisible;
                            bool justApproved = !_hasShownTricycleApprovedSuccessModal && (wasTricyclePending || _tricycleStatus == "Approved");

                            TricycleRejectedSection.IsVisible = false;
                            TricyclePendingSection.IsVisible = false;
                            FullDashboardSection.IsVisible = true;
                            ActiveSwitch.IsEnabled = true;
                            RideRequestButton.IsVisible = true;
                            UpdateOnlineStatusUI();

                            if (justApproved)
                            {
                                ShowTricycleApprovedSuccessModal();
                                _hasShownTricycleApprovedSuccessModal = true;
                                Preferences.Set($"TricycleApprovedSuccessModalShown_{_currentDriverId}", true);
                            }

                            if (!_hasShownTricycleApprovedModal)
                            {
                                ShowTricycleApprovedModal();
                                _hasShownTricycleApprovedModal = true;
                                Preferences.Set($"TricycleApprovedModalShown_{_currentDriverId}", true);
                            }
                        }
                        else
                        {
                            TricycleRejectedSection.IsVisible = false;
                            TricyclePendingSection.IsVisible = true;
                            FullDashboardSection.IsVisible = false;
                            ActiveSwitch.IsEnabled = false;
                            ActiveSwitch.IsToggled = false;
                            RideRequestButton.IsVisible = false;

                            if (_tricycleStatus == "Not Set" || string.IsNullOrEmpty(_tricycleInfo?.PlateNumber))
                            {
                                var messageLabel = TricyclePendingSection.Children
                                    .OfType<Frame>().First()
                                    .Content as StackLayout;
                                var textStack = (messageLabel?.Children[1] as StackLayout);
                                if (textStack != null)
                                {
                                    ((Label)textStack.Children[1]).Text = "Please complete your tricycle details first.";
                                    ((Label)textStack.Children[2]).Text = "Fill up all required information to proceed.";
                                }
                                GoToEditTricycleButton.Text = "Complete Now";
                            }
                        }

                        if (!_hasShownApprovedModal)
                        {
                            ShowApprovedModal();
                            _hasShownApprovedModal = true;
                            Preferences.Set($"ApprovedModalShown_{_currentDriverId}", true);
                        }
                    }
                    else if (docStatus == "Rejected" || docStatus == "Pending Review")
                    {
                        WaitingView.IsVisible = false;
                        RejectedView.IsVisible = true;
                        ApprovedView.IsVisible = false;
                        SuspendedView.IsVisible = false;
                        DeactivatedView.IsVisible = false;
                        TricycleRejectedSection.IsVisible = false;
                        TricyclePendingSection.IsVisible = false;
                        FullDashboardSection.IsVisible = false;
                        ActiveSwitch.IsEnabled = false;
                        ActiveSwitch.IsToggled = false;

                        if (docStatus == "Pending Review")
                        {
                            RejectionReasonLabel.Text = "Your reuploaded documents are under review by admin.";
                        }
                        else
                        {
                            RejectionReasonLabel.Text = driver.RejectionReason ??
                                "Your documents did not meet the requirements. Please re-upload the required documents.";
                        }

                        _pendingDocumentsToReupload = driver.RejectedDocuments ?? new List<string>();
                    }
                    else
                    {
                        WaitingView.IsVisible = true;
                        RejectedView.IsVisible = false;
                        ApprovedView.IsVisible = false;
                        SuspendedView.IsVisible = false;
                        DeactivatedView.IsVisible = false;
                        TricycleRejectedSection.IsVisible = false;
                        TricyclePendingSection.IsVisible = false;
                        FullDashboardSection.IsVisible = false;
                        ActiveSwitch.IsEnabled = false;
                        ActiveSwitch.IsToggled = false;
                    }
                });
            }
            catch { }
        }

        private async void ShowTricycleApprovedModal()
        {
            await ShowModal("Tricycle Approved!", "Your tricycle has been approved by admin! You can now go online.", "success", false);
        }

        private async void ShowApprovedModal()
        {
            await ShowModal("Account Approved!", "Your account has been approved by admin! Please complete your tricycle details.", "success", false);
        }

        private void ShowTricycleApprovedSuccessModal()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TricycleApprovedModal.IsVisible = true;
                TricycleApprovedContent.IsVisible = true;
            });
        }

        private void OnCloseTricycleApprovedModal(object sender, EventArgs e)
        {
            TricycleApprovedModal.IsVisible = false;
            TricycleApprovedContent.IsVisible = false;
        }

        private void UpdateOnlineStatusUI()
        {
            try
            {
                bool canGoOnline = CheckIfCanGoOnline();

                ActiveSwitch.IsEnabled = true;

                if (!canGoOnline)
                {
                    if (_isDriverOnline)
                    {
                        _ = _firebaseConnection.UpdateDriverStatusAsync(_currentDriverId, false);
                        _isDriverOnline = false;
                    }

                    ActiveSwitch.IsToggled = false;
                    OnlineStatusText.Text = "Offline";
                    OnlineStatusText.TextColor = Color.FromArgb("#757575");
                    OnlineStatusText.FontSize = 16;
                }
                else
                {
                    ActiveSwitch.IsToggled = _isDriverOnline;
                    OnlineStatusText.Text = _isDriverOnline ? "Online" : "Offline";
                    OnlineStatusText.TextColor = _isDriverOnline ? Color.FromArgb("#2E7D32") : Color.FromArgb("#757575");
                    OnlineStatusText.FontSize = 16;
                }
            }
            catch { }
        }

        private bool CheckIfCanGoOnline()
        {
            try
            {
                if (!_isDriverApproved) return false;
                if (!_isTricycleApproved) return false;
                if (_accountStatus != "Active") return false;

                bool hasLicense = !string.IsNullOrWhiteSpace(_currentDriver?.LicenseNumber) &&
                                 _currentDriver.LicenseNumber != "Unknown" &&
                                 _currentDriver.LicenseNumber != "Not Set";

                bool hasPlate = !string.IsNullOrWhiteSpace(_currentDriver?.PlateNumber) &&
                               _currentDriver.PlateNumber != "Unknown" &&
                               _currentDriver.PlateNumber != "Not Set";

                bool hasSeatingCapacity = _tricycleInfo != null &&
                                         !string.IsNullOrWhiteSpace(_tricycleInfo.SeatingCapacity) &&
                                         _tricycleInfo.SeatingCapacity != "Unknown";

                bool isTricycleActive = _tricycleInfo != null &&
                                       _tricycleInfo.Status == "Active";

                return hasLicense && hasPlate && hasSeatingCapacity && isTricycleActive;
            }
            catch
            {
                return false;
            }
        }

        private void StartTricycleStatusListener()
        {
            try
            {
                if (!_hasInternetConnection || string.IsNullOrEmpty(_currentDriverId)) return;

                StopTricycleStatusListener();

                _isTricycleListenerActive = true;

                Device.StartTimer(TimeSpan.FromSeconds(5), () =>
                {
                    if (!_isTricycleListenerActive) return false;

                    Task.Run(async () =>
                    {
                        try
                        {
                            var tricycle = await _firebaseConnection.GetTricycleInfoAsync(_currentDriverId);
                            if (tricycle != null)
                            {
                                string newStatus = tricycle.TricycleStatus ?? "Pending";
                                bool newApproved = newStatus == "Approved";
                                string newRejectionReason = tricycle.RejectionReason ?? "";

                                if (_tricycleStatus != newStatus ||
                                    _isTricycleApproved != newApproved ||
                                    _tricycleRejectionReason != newRejectionReason)
                                {
                                    _tricycleInfo = tricycle;
                                    _tricycleStatus = newStatus;
                                    _isTricycleApproved = newApproved;
                                    _tricycleRejectionReason = newRejectionReason;

                                    MainThread.BeginInvokeOnMainThread(async () =>
                                    {
                                        await CheckDriverStatusAndShowView();

                                        if (newApproved && !_hasShownTricycleApprovedSuccessModal)
                                        {
                                            ShowTricycleApprovedSuccessModal();
                                            _hasShownTricycleApprovedSuccessModal = true;
                                            Preferences.Set($"TricycleApprovedSuccessModalShown_{_currentDriverId}", true);
                                        }
                                    });
                                }
                            }
                        }
                        catch { }
                    });

                    return _isTricycleListenerActive;
                });
            }
            catch { }
        }

        private void StopTricycleStatusListener()
        {
            _isTricycleListenerActive = false;
        }

        private void StartDocumentStatusListener()
        {
            try
            {
                if (!_hasInternetConnection || string.IsNullOrEmpty(_currentDriverId)) return;

                StopDocumentStatusListener();

                _documentStatusListener = _firebaseConnection.ListenForDriverDocumentStatus(
                    _currentDriverId,
                    async (docStatus, regCompleted, rejectionReason) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            _isDriverApproved = regCompleted && docStatus == "Approved";
                            await LoadTricycleInfo();
                            await CheckDriverStatusAndShowView();
                            await UpdateNotificationBadge();
                        });
                    });
            }
            catch { }
        }

        private void StopDocumentStatusListener()
        {
            try
            {
                _documentStatusListener?.Dispose();
                _documentStatusListener = null;
            }
            catch { }
        }

        private void OnGoToDashboardClicked(object sender, EventArgs e)
        {
            ApprovedModal.IsVisible = false;
        }

        private async void OnReuploadClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_UploadRejected(_currentDriverId, _pendingDocumentsToReupload));
            }
            catch
            {
                await ShowModal("Error", "Failed to open re-upload page.", "error", false);
            }
        }

        private async Task UpdateNotificationBadge()
        {
            try
            {
                int unreadCount = await GetUnreadNotificationCount();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (unreadCount > 0)
                    {
                        NotificationBadge.IsVisible = true;
                        NotificationBadgeText.Text = unreadCount > 9 ? "9+" : unreadCount.ToString();
                    }
                    else
                    {
                        NotificationBadge.IsVisible = false;
                    }
                });
            }
            catch { }
        }

        private async Task<int> GetUnreadNotificationCount()
        {
            try
            {
                int count = 0;
                var clearedIds = GetClearedNotificationIds();

                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver != null)
                {
                    if (driver.DocumentStatus == "Rejected" && !string.IsNullOrEmpty(driver.RejectionReason))
                    {
                        var rejectId = $"reject_{_currentDriverId}";
                        if (!clearedIds.Contains(rejectId)) count++;
                    }

                    if (driver.RegistrationCompleted && driver.DocumentStatus == "Approved" && !_hasShownApprovedModal)
                    {
                        var approveId = $"approve_{_currentDriverId}";
                        if (!clearedIds.Contains(approveId)) count++;
                    }

                    if (driver.AccountStatus == "Suspended" && !string.IsNullOrEmpty(driver.SuspensionReason))
                    {
                        var suspendId = $"suspend_{_currentDriverId}";
                        if (!clearedIds.Contains(suspendId)) count++;
                    }

                    if (driver.AccountStatus == "Deactivated" && !string.IsNullOrEmpty(driver.DeactivationReason))
                    {
                        var deactivateId = $"deactivate_{_currentDriverId}";
                        if (!clearedIds.Contains(deactivateId)) count++;
                    }
                }

                var tricycle = await _firebaseConnection.GetTricycleInfoAsync(_currentDriverId);
                if (tricycle != null && tricycle.TricycleStatus == "Approved" && !_hasShownTricycleApprovedModal)
                {
                    var tricycleId = $"tricycle_approve_{_currentDriverId}";
                    if (!clearedIds.Contains(tricycleId)) count++;
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private HashSet<string> GetClearedNotificationIds()
        {
            try
            {
                var serialized = Preferences.Get($"ClearedNotifications_{_currentDriverId}", "[]");
                return System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(serialized) ?? new HashSet<string>();
            }
            catch
            {
                return new HashSet<string>();
            }
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

        private bool IsNetworkError(Exception ex)
        {
            string errorMessage = ex.Message.ToLower();
            return errorMessage.Contains("network") ||
                   errorMessage.Contains("connection") ||
                   errorMessage.Contains("timeout") ||
                   errorMessage.Contains("nameresolutionfailure") ||
                   errorMessage.Contains("socket") ||
                   errorMessage.Contains("host");
        }

        private async Task ShowConnectionError()
        {
            _hasInternetConnection = false;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ConnectionErrorLabel.Text = "No internet connection. Some features may be limited.";
                ConnectionErrorGrid.IsVisible = true;
                ActiveSwitch.IsEnabled = false;
            });
        }

        private void HideConnectionError()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ConnectionErrorGrid.IsVisible = false;
            });
        }

        private async Task<bool> VerifyDriverAccount()
        {
            try
            {
                if (_isCheckingAccount) return true;
                _isCheckingAccount = true;

                _currentDriverId = AppSession.GetDriverId();

                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    return false;
                }

                if (!AppSession.IsDriverLoggedIn())
                {
                    return false;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);

                if (driver == null)
                {
                    return false;
                }

                _isCheckingAccount = false;
                return true;
            }
            catch (Exception ex)
            {
                if (IsNetworkError(ex))
                {
                    _isCheckingAccount = false;
                    await ShowConnectionError();
                    return true;
                }
                _isCheckingAccount = false;
                return false;
            }
        }

        private async Task RedirectToGetStarted()
        {
            try
            {
                AppSession.LogoutDriver();
                await ShowModal("Account Not Found", "Your driver account could not be found. Please register again.", "warning", true);
                await Navigation.PushModalAsync(new GetStarted());
            }
            catch { }
        }

        private async Task LoadUserData()
        {
            try
            {
                _currentDriverId = AppSession.GetDriverId();

                if (!string.IsNullOrEmpty(_currentDriverId))
                {
                    _currentDriver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);

                    if (_currentDriver != null && !string.IsNullOrEmpty(_currentDriver.FirstName))
                    {
                        string greeting = GetTimeBasedGreeting();
                        GreetingLabel.Text = greeting;
                        DriverNameLabel.Text = $"{_currentDriver.FirstName} {_currentDriver.LastName}".Trim();

                        _isDriverOnline = _currentDriver.IsOnline;

                        await LoadDriverRating(_currentDriverId);
                        StartRatingListener(_currentDriverId);
                    }
                    else
                    {
                        await RedirectToGetStarted();
                    }
                }
                else
                {
                    await RedirectToGetStarted();
                }
            }
            catch { }
        }

        private async Task LoadDriverStatistics()
        {
            try
            {
                if (!_hasInternetConnection) return;

                string driverId = AppSession.GetDriverId();
                if (!string.IsNullOrEmpty(driverId))
                {
                    var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                    if (driver != null)
                    {
                        UpdateStatisticsDisplay(driver);
                    }
                }
            }
            catch { }
        }

        private async Task LoadDriverRating(string driverId)
        {
            try
            {
                if (!_hasInternetConnection) return;

                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                if (driver == null) return;

                double finalRating = 5.0;

                if (driver.Rating > 0)
                    finalRating = driver.Rating;
                else if (driver.AverageRating > 0)
                    finalRating = driver.AverageRating;

                _currentRating = finalRating;
                UpdateRatingDisplay(finalRating);
            }
            catch { }
        }

        private async Task CheckActiveRide()
        {
            try
            {
                if (!_hasInternetConnection) return;

                _currentDriverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(_currentDriverId)) return;

                var allRideRequests = await _firebaseConnection.GetAllRideRequestsAsync();

                if (allRideRequests != null && allRideRequests.Count > 0)
                {
                    var activeRide = allRideRequests
                        .Where(r => r.DriverId == _currentDriverId &&
                                   r.Status != "Completed" &&
                                   r.Status != "Cancelled")
                        .OrderByDescending(r => r.RequestTime)
                        .FirstOrDefault();

                    if (activeRide != null)
                    {
                        _currentActiveRide = activeRide;
                        ShowActiveRideBanner(activeRide);
                    }
                    else
                    {
                        HideActiveRideBanner();
                        _hasRedirected = false;
                    }
                }
                else
                {
                    HideActiveRideBanner();
                    _hasRedirected = false;
                }
            }
            catch
            {
                HideActiveRideBanner();
                _hasRedirected = false;
            }
        }

        private void StartRideStatusListener()
        {
            try
            {
                if (!_hasInternetConnection || string.IsNullOrEmpty(_currentDriverId)) return;

                StopRideStatusListener();

                _rideStatusListener = _firebaseConnection.ListenForRideStatusChangeRealTimeForDriver(
                    _currentDriverId,
                    (rideRequest) =>
                    {
                        if (rideRequest != null)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                try
                                {
                                    var rideWithKey = new RideRequestWithKey
                                    {
                                        FirebaseKey = rideRequest.Id,
                                        PickupLocation = rideRequest.PickupLocation,
                                        DropoffLocation = rideRequest.DropoffLocation,
                                        Status = rideRequest.Status,
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
                                        _hasRedirected = false;
                                    }
                                }
                                catch { }
                            });
                        }
                    });
            }
            catch { }
        }

        private void StopRideStatusListener()
        {
            try
            {
                _rideStatusListener?.Dispose();
                _rideStatusListener = null;
            }
            catch { }
        }

        private bool ShouldShowActiveRideBanner(string status)
        {
            return status != "Completed" &&
                   status != "Cancelled" &&
                   !string.IsNullOrEmpty(status);
        }

        private void ShowActiveRideBanner(RideRequestWithKey activeRide)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    ActiveRideBanner.IsVisible = true;

                    switch (activeRide.Status)
                    {
                        case "Pending":
                            RideStatusBadge.BackgroundColor = Color.FromArgb("#FFB300");
                            RideStatusBadgeText.Text = "SEARCHING";
                            RideStatusLabel.Text = "Looking for passenger";
                            RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                            break;
                        case "Accepted":
                            RideStatusBadge.BackgroundColor = Color.FromArgb("#2E7D32");
                            RideStatusBadgeText.Text = "ACCEPTED";
                            RideStatusLabel.Text = "Go to pickup location";
                            RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                            break;
                        case "Picking Up":
                            RideStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                            RideStatusBadgeText.Text = "PICKING UP";
                            RideStatusLabel.Text = "Pick up passenger";
                            RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                            break;
                        case "Arrived at Pickup":
                            RideStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                            RideStatusBadgeText.Text = "ARRIVED";
                            RideStatusLabel.Text = "Passenger boarding";
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
                            RideStatusBadgeText.Text = "PAYMENT";
                            RideStatusLabel.Text = "Awaiting payment";
                            RideTimeLabel.Text = "Now";
                            break;
                        default:
                            RideStatusBadge.BackgroundColor = Color.FromArgb("#757575");
                            RideStatusBadgeText.Text = activeRide.Status?.ToUpper() ?? "UNKNOWN";
                            RideStatusLabel.Text = $"Ride: {activeRide.Status}";
                            RideTimeLabel.Text = GetTimeAgo(activeRide.RequestTime);
                            break;
                    }

                    PickupLabel.Text = activeRide.PickupLocation ?? "Pickup location";
                    DropoffLabel.Text = activeRide.DropoffLocation ?? "Dropoff location";
                }
                catch { }
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

        private void UpdateRatingDisplay(double rating)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RatingLabel.Text = rating.ToString("0.0");
                UpdateStarImages(rating);
            });
        }

        private void UpdateStarImages(double rating)
        {
            var stars = new[] { Star1, Star2, Star3, Star4, Star5 };
            int fullStars = (int)Math.Floor(rating);

            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].Source = i < fullStars ? "star_filled.png" : "star_outline.png";
            }
        }

        private void StartRatingListener(string driverId)
        {
            try
            {
                if (!_hasInternetConnection) return;

                StopRatingListener();

                _ratingListener = _firebaseConnection.ListenForDriverRatingUpdates(
                    driverId,
                    async (averageRating) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            UpdateRatingDisplay(averageRating);
                        });
                    });
            }
            catch { }
        }

        private void StopRatingListener()
        {
            try
            {
                _ratingListener?.Dispose();
                _ratingListener = null;
            }
            catch { }
        }

        private string GetTimeBasedGreeting()
        {
            int hour = DateTime.Now.Hour;
            if (hour >= 5 && hour < 12) return "Good Morning!";
            if (hour >= 12 && hour < 18) return "Good Afternoon!";
            return "Good Evening!";
        }

        private void UpdateStatisticsDisplay(Driver driver)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TotalRidesLabel.Text = (driver.TotalCompletedRides ?? 0).ToString("N0");
                TodayRidesLabel.Text = (driver.TodayCompletedRides ?? 0).ToString("N0");
                TotalEarningsLabel.Text = $"₱{(driver.TotalEarnings ?? 0):N2}";
                TodayEarningsLabel.Text = $"₱{(driver.TodayEarnings ?? 0):N2}";

                SharedTotalRides = TotalRidesLabel.Text;
                SharedTodayRides = TodayRidesLabel.Text;
                SharedTotalEarnings = (driver.TotalEarnings ?? 0).ToString("N2");

                StatisticsUpdated?.Invoke();
            });
        }

        private async void OnNotificationButtonClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_Notification());
            }
            catch { }
        }

        private async void OnRideRequestClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_RideRequest());
            }
            catch { }
        }

        private async void OnActiveRideBannerTapped(object sender, EventArgs e)
        {
            if (_currentActiveRide == null) return;

            try
            {
                if (_currentActiveRide.Status == "Payment Pending")
                {
                    await Navigation.PushModalAsync(new Driver_WaitingPayment(_currentActiveRide.FirebaseKey));
                }
                else
                {
                    await Navigation.PushModalAsync(new Driver_StartedTrip(_currentActiveRide.FirebaseKey));
                }
            }
            catch { }
        }

        private async void OnActiveSwitchToggled(object sender, ToggledEventArgs e)
        {
            try
            {
                if (!await CheckInternetConnection())
                {
                    await ShowModal("Connection Error", "Cannot change online status. Please check your connection.", "warning", false);
                    ActiveSwitch.IsToggled = !e.Value;
                    return;
                }

                if (e.Value)
                {
                    var (canGoOnline, reason) = await CanDriverGoOnlineWithReason();
                    if (!canGoOnline)
                    {
                        ActiveSwitch.IsToggled = false;

                        if (reason == "TRICYCLE_PENDING")
                        {
                            ShowTricyclePendingModal();
                        }
                        else if (reason.StartsWith("SUSPENDED"))
                        {
                            var parts = reason.Split('|');
                            string suspendReason = parts.Length > 1 ? parts[1] : "";
                            string untilDate = parts.Length > 2 ? parts[2] : "";
                            ShowAccountRestrictedModal("Suspended", suspendReason, untilDate);
                        }
                        else if (reason.StartsWith("DEACTIVATED"))
                        {
                            var parts = reason.Split('|');
                            string deactivateReason = parts.Length > 1 ? parts[1] : "";
                            ShowAccountRestrictedModal("Deactivated", deactivateReason);
                        }
                        else if (reason.StartsWith("MISSING"))
                        {
                            var parts = reason.Split('|');
                            var missingItems = parts.Skip(1).ToList();
                            ShowMissingRequirementsModal(missingItems);
                        }
                        else
                        {
                            await ShowModal("Cannot Go Online", reason, "warning", false);
                        }
                        return;
                    }
                }
                else
                {
                    if (_currentActiveRide != null)
                    {
                        ShowOfflineRestrictionModal();
                        ActiveSwitch.IsToggled = true;
                        return;
                    }
                }

                if (e.Value)
                {
                    OnlineStatusText.Text = "Online";
                    OnlineStatusText.TextColor = Color.FromArgb("#2E7D32");
                }
                else
                {
                    OnlineStatusText.Text = "Offline";
                    OnlineStatusText.TextColor = Color.FromArgb("#757575");
                }

                var driverId = AppSession.GetDriverId();
                if (!string.IsNullOrEmpty(driverId))
                {
                    bool success = await _firebaseConnection.UpdateDriverStatusAsync(driverId, e.Value);
                    if (success)
                    {
                        _isDriverOnline = e.Value;
                        if (_currentDriver != null)
                        {
                            _currentDriver.IsOnline = e.Value;
                        }

                        if (e.Value)
                        {
                            await CheckActiveRide();
                        }
                    }
                    else
                    {
                        ActiveSwitch.IsToggled = !e.Value;
                    }
                }
            }
            catch
            {
                ActiveSwitch.IsToggled = !e.Value;
            }
        }

        private async Task<(bool canGoOnline, string reason)> CanDriverGoOnlineWithReason()
        {
            try
            {
                if (!_isDriverApproved)
                {
                    return (false, "Your driver account is not yet approved by admin.");
                }

                if (!_isTricycleApproved)
                {
                    return (false, "TRICYCLE_PENDING");
                }

                if (_accountStatus != "Active")
                {
                    if (_accountStatus == "Suspended")
                    {
                        string untilText = "";
                        if (!string.IsNullOrEmpty(_suspensionUntil))
                        {
                            try
                            {
                                DateTime untilDate = DateTime.Parse(_suspensionUntil);
                                untilText = $" until {untilDate:MMMM dd, yyyy}";
                            }
                            catch { }
                        }
                        return (false, $"SUSPENDED|{_suspensionReason}|{untilText}");
                    }
                    if (_accountStatus == "Deactivated")
                    {
                        return (false, $"DEACTIVATED|{_deactivationReason}");
                    }
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver == null) return (false, "Driver information not found.");

                var tricycle = await _firebaseConnection.GetTricycleInfoAsync(_currentDriverId);

                List<string> missingItems = new List<string>();

                bool hasLicense = !string.IsNullOrWhiteSpace(driver.LicenseNumber) &&
                                 driver.LicenseNumber != "Unknown" &&
                                 driver.LicenseNumber != "Not Set";

                if (!hasLicense)
                {
                    missingItems.Add("Driver's License Number");
                }

                bool hasPlate = !string.IsNullOrWhiteSpace(driver.PlateNumber) &&
                               driver.PlateNumber != "Unknown" &&
                               driver.PlateNumber != "Not Set";

                if (!hasPlate)
                {
                    missingItems.Add("Plate Number");
                }

                if (tricycle == null)
                {
                    missingItems.Add("Complete Tricycle Information");
                }
                else
                {
                    bool hasSeatingCapacity = !string.IsNullOrWhiteSpace(tricycle.SeatingCapacity) &&
                                             tricycle.SeatingCapacity != "Unknown";

                    if (!hasSeatingCapacity)
                    {
                        missingItems.Add("Seating Capacity");
                    }

                    if (tricycle.Status?.ToLower() != "active")
                    {
                        missingItems.Add($"Tricycle Status must be 'Active' (current: {tricycle.Status})");
                    }
                }

                if (missingItems.Count > 0)
                {
                    return (false, $"MISSING|{string.Join("|", missingItems)}");
                }

                return (true, "OK");
            }
            catch
            {
                return (false, "Error checking requirements. Please try again.");
            }
        }

        private void ShowOfflineRestrictionModal()
        {
            if (_currentActiveRide == null) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    OfflineRestrictionModal.IsVisible = true;
                    OfflineRestrictionContent.IsVisible = true;

                    ModalPickupLabel.Text = _currentActiveRide.PickupLocation ?? "Unknown pickup";
                    ModalDropoffLabel.Text = _currentActiveRide.DropoffLocation ?? "Unknown dropoff";
                    ModalFareLabel.Text = $"₱{_currentActiveRide.Fare:N2}";

                    switch (_currentActiveRide.Status)
                    {
                        case "Pending":
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#FFB300");
                            ModalStatusText.Text = "⏳";
                            ModalStatusLabel.Text = "PENDING - Searching for passenger";
                            break;
                        case "Accepted":
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#2E7D32");
                            ModalStatusText.Text = "✓";
                            ModalStatusLabel.Text = "ACCEPTED - Go to pickup location";
                            break;
                        case "Picking Up":
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                            ModalStatusText.Text = "🚗";
                            ModalStatusLabel.Text = "PICKING UP - Pick up passenger";
                            break;
                        case "Arrived at Pickup":
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#0288D1");
                            ModalStatusText.Text = "📍";
                            ModalStatusLabel.Text = "ARRIVED - Passenger boarding";
                            break;
                        case "Trip Started":
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#F57C00");
                            ModalStatusText.Text = "🛵";
                            ModalStatusLabel.Text = "ON TRIP - Trip in progress";
                            break;
                        case "Payment Pending":
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#4CAF50");
                            ModalStatusText.Text = "💰";
                            ModalStatusLabel.Text = "PAYMENT - Awaiting payment";
                            break;
                        default:
                            ModalStatusBadge.BackgroundColor = Color.FromArgb("#757575");
                            ModalStatusText.Text = "🔄";
                            ModalStatusLabel.Text = $"{_currentActiveRide.Status}";
                            break;
                    }

                    ModalActionButton.Text = _currentActiveRide.Status == "Payment Pending"
                        ? "GO TO PAYMENT" : "VIEW RIDE DETAILS";
                }
                catch { }
            });
        }

        private void OnCloseOfflineRestrictionModal(object sender, EventArgs e)
        {
            OfflineRestrictionModal.IsVisible = false;
            OfflineRestrictionContent.IsVisible = false;
        }

        private async void OnViewRideDetailsClicked(object sender, EventArgs e)
        {
            OfflineRestrictionModal.IsVisible = false;
            OfflineRestrictionContent.IsVisible = false;

            if (_currentActiveRide != null)
            {
                if (_currentActiveRide.Status == "Payment Pending")
                {
                    await Navigation.PushModalAsync(new Driver_WaitingPayment(_currentActiveRide.FirebaseKey));
                }
                else
                {
                    await Navigation.PushModalAsync(new Driver_StartedTrip(_currentActiveRide.FirebaseKey));
                }
            }
        }

        public async void OnRefreshing(object sender, EventArgs e)
        {
            var refreshView = sender as RefreshView;
            if (refreshView == null) return;

            try
            {
                if (!await CheckInternetConnection())
                {
                    await ShowConnectionError();
                    refreshView.IsRefreshing = false;
                    return;
                }

                await RefreshActiveRideStatus();
            }
            catch { }
            finally
            {
                refreshView.IsRefreshing = false;
            }
        }

        public async Task RefreshActiveRideStatus()
        {
            await LoadUserData();
            await LoadTricycleInfo();
            await LoadDriverStatistics();
            await CheckActiveRide();
            await CheckDriverStatusAndShowView();
            await UpdateNotificationBadge();
            await LoadBannersAsync();
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
                    await RefreshActiveRideStatus();
                    StartDocumentStatusListener();
                    StartTricycleStatusListener();
                    StartAccountStatusListener();
                    StartRideStatusListener();
                    StartSuspensionExpiryListener();
                    SetupBannerListener();
                    StartAutoSlide();
                    await ShowModal("Success", "Connection restored!", "success", false);
                }
                else
                {
                    ConnectionErrorLabel.Text = "Still no internet connection. Please check your network.";
                }
            }
            catch
            {
                ConnectionErrorLabel.Text = "No internet connection. Some features may be limited.";
            }
        }

        private void ShowTricyclePendingModal()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TricyclePendingModal.IsVisible = true;
                TricyclePendingContent.IsVisible = true;

                if (!_isTricycleApproved)
                {
                    TricyclePendingMessage.Text = "Your tricycle details are pending admin approval.";
                }
                else if (_tricycleStatus == "Not Set")
                {
                    TricyclePendingMessage.Text = "Please complete your tricycle details first.";
                    TricyclePendingEditButton.Text = "Complete Now";
                }
                else
                {
                    TricyclePendingMessage.Text = "Your tricycle profile needs attention.";
                }
            });
        }

        private void OnCloseTricyclePendingModal(object sender, EventArgs e)
        {
            TricyclePendingModal.IsVisible = false;
            TricyclePendingContent.IsVisible = false;
        }

        private void ShowMissingRequirementsModal(List<string> missingItems)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RequirementsList.Children.Clear();

                foreach (var item in missingItems)
                {
                    var itemStack = new HorizontalStackLayout { Spacing = 10 };

                    var bulletLabel = new Label
                    {
                        Text = "•",
                        FontSize = 16,
                        TextColor = Color.FromArgb("#EF4444"),
                        VerticalOptions = LayoutOptions.Center
                    };

                    var textLabel = new Label
                    {
                        Text = item,
                        FontSize = 14,
                        TextColor = Color.FromArgb("#333333"),
                        VerticalOptions = LayoutOptions.Center
                    };

                    itemStack.Children.Add(bulletLabel);
                    itemStack.Children.Add(textLabel);

                    RequirementsList.Children.Add(itemStack);
                }

                MissingRequirementsModal.IsVisible = true;
                MissingRequirementsContent.IsVisible = true;
            });
        }

        private void OnCloseMissingRequirementsModal(object sender, EventArgs e)
        {
            MissingRequirementsModal.IsVisible = false;
            MissingRequirementsContent.IsVisible = false;
        }

        private void ShowAccountRestrictedModal(string accountStatus, string reason, string untilDate = "")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (accountStatus == "Suspended")
                {
                    AccountRestrictedHeader.BackgroundColor = Color.FromArgb("#F59E0B");
                    AccountRestrictedIcon.Text = "⏸️";
                    AccountRestrictedTitle.Text = "Account Suspended";
                    AccountRestrictedMessage.Text = !string.IsNullOrEmpty(reason)
                        ? $"Reason: {reason}"
                        : "Your account has been suspended by admin.";

                    if (!string.IsNullOrEmpty(untilDate))
                    {
                        AccountRestrictedUntilLabel.Text = $"Suspended until: {untilDate}";
                        AccountRestrictedUntilLabel.IsVisible = true;
                    }

                    AccountRestrictedInfo.Text = "You cannot go online or accept rides while suspended.";
                    AccountRestrictedSupportButton.BackgroundColor = Color.FromArgb("#F59E0B");
                }
                else if (accountStatus == "Deactivated")
                {
                    AccountRestrictedHeader.BackgroundColor = Color.FromArgb("#EF4444");
                    AccountRestrictedIcon.Text = "🚫";
                    AccountRestrictedTitle.Text = "Account Deactivated";
                    AccountRestrictedMessage.Text = !string.IsNullOrEmpty(reason)
                        ? $"Reason: {reason}"
                        : "Your account has been deactivated by admin.";
                    AccountRestrictedUntilLabel.IsVisible = false;
                    AccountRestrictedInfo.Text = "You can no longer use this account.";
                    AccountRestrictedSupportButton.BackgroundColor = Color.FromArgb("#EF4444");
                }

                AccountRestrictedModal.IsVisible = true;
                AccountRestrictedContent.IsVisible = true;
            });
        }

        private void OnCloseAccountRestrictedModal(object sender, EventArgs e)
        {
            AccountRestrictedModal.IsVisible = false;
            AccountRestrictedContent.IsVisible = false;
        }

        private async void OnEditTricycleClicked(object sender, EventArgs e)
        {
            try
            {
                OnCloseTricyclePendingModal(sender, e);
                OnCloseMissingRequirementsModal(sender, e);
                OnCloseAccountRestrictedModal(sender, e);

                if (_tricycleInfo == null)
                {
                    _tricycleInfo = await _firebaseConnection.GetOrCreateTricycleInfoAsync(_currentDriverId);

                    if (_tricycleInfo != null && string.IsNullOrEmpty(_tricycleInfo.PlateNumber))
                    {
                        _tricycleInfo.PlateNumber = "";
                    }
                }

                await Navigation.PushModalAsync(new EditTricyclePage(_tricycleInfo));
            }
            catch
            {
                await ShowModal("Error", "Failed to open edit page", "error", false);
            }
        }

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            await ShowLogoutConfirmation();
        }

        private async Task ShowLogoutConfirmation()
        {
            _isModalOpen = true;

            string iconSource = "complaint.png";
            Color iconColor = Color.FromArgb("#DC3545");

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
                            Text = "Logout?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to log out?",
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
                                }.WithDriverHomeGridColumn(0),
                                new Button
                                {
                                    Text = "LOGOUT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverHomeGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modalPage = new DriverHomeLogoutModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public void CloseLogoutModal(bool performLogout)
        {
            _isModalOpen = false;
            if (performLogout)
            {
                try
                {
                    AppSession.LogoutAll();
                    ShowSuccessModalAndNavigate();
                }
                catch (Exception ex)
                {
                    ShowModal("Error", "Logout failed: " + ex.Message, "error", false);
                }
            }
        }

        private async void ShowSuccessModalAndNavigate()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

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
                            Text = "Logged Out",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "You have been logged out successfully.",
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

            var modalPage = new DriverHomeSuccessModalPage(modalContent, blurOverlay, tcs);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            var button = FindButtonInModal(modalContent, "OK");
            if (button != null)
            {
                button.Clicked += async (s, e) =>
                {
                    button.IsEnabled = false;
                    await Task.WhenAll(
                        modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                        modalContent.FadeTo(0, 200, Easing.CubicIn),
                        modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                        blurOverlay.FadeTo(0, 200, Easing.CubicIn)
                    );
                    await Navigation.PopModalAsync();
                    tcs.SetResult(true);
                };
            }

            await tcs.Task;
            Application.Current.MainPage = new GetStarted();
        }

        private Button FindButtonInModal(object element, string text)
        {
            if (element is Button btn && btn.Text == text)
                return btn;

            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButtonInModal(child, text);
                    if (result != null)
                        return result;
                }
            }

            if (element is ContentView cv && cv.Content != null)
            {
                return FindButtonInModal(cv.Content, text);
            }

            if (element is Frame frame && frame.Content != null)
            {
                return FindButtonInModal(frame.Content, text);
            }

            return null;
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

            var modalPage = new DriverHomeModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

    public class DriverHomeModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_Home _parentPage;

        public DriverHomeModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_Home parentPage)
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

    public class DriverHomeLogoutModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_Home _parentPage;

        public DriverHomeLogoutModalPage(Frame modalContent, Grid blurOverlay, Driver_Home parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var cancelButton = FindButtonByText(modalContent.Content, "CANCEL");
            var logoutButton = FindButtonByText(modalContent.Content, "LOGOUT");

            if (cancelButton != null)
            {
                cancelButton.Clicked += async (s, e) =>
                {
                    await AnimateModalExit(false);
                };
            }

            if (logoutButton != null)
            {
                logoutButton.Clicked += async (s, e) =>
                {
                    logoutButton.IsEnabled = false;
                    await AnimateModalExit(true);
                };
            }

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private Button FindButtonByText(object element, string text)
        {
            if (element is Button btn && btn.Text == text)
                return btn;

            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButtonByText(child, text);
                    if (result != null)
                        return result;
                }
            }

            if (element is ContentView cv && cv.Content != null)
            {
                return FindButtonByText(cv.Content, text);
            }

            if (element is Frame frame && frame.Content != null)
            {
                return FindButtonByText(frame.Content, text);
            }

            return null;
        }

        private async Task AnimateModalExit(bool logout)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseLogoutModal(logout);
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

    public class DriverHomeSuccessModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private TaskCompletionSource<bool> _tcs;
        private bool _isNavigating = false;

        public DriverHomeSuccessModalPage(Frame modalContent, Grid blurOverlay, TaskCompletionSource<bool> tcs)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _tcs = tcs;

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
            _tcs.SetResult(true);
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isNavigating) return true;

            _isNavigating = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await AnimateModalExit();
            });
            return true;
        }
    }

    public static class DriverHomeGridExtensions
    {
        public static Button WithDriverHomeGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}