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
    public partial class Driver_Rides : ContentPage, INotifyPropertyChanged
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private List<RideRequestWithKey> _allRides = new List<RideRequestWithKey>();
        private string _currentDriverId;
        private bool _isLoading = false;
        private IDisposable _accountStatusListener;
        private IDisposable _documentStatusListener;
        private IDisposable _ridesListener;
        private IDisposable _tricycleStatusListener;
        private TricycleInfo _tricycleInfo;
        private bool _isModalOpen = false;
        private bool _hasShownReactivatedModal = false;

        private bool _isDriverApproved = false;
        private bool _isTricycleApproved = false;
        private string _tricycleStatus = "Pending";
        private string _tricycleRejectionReason = "";
        private string _accountStatus = "Active";
        private string _suspensionReason = "";
        private string _suspensionUntil = "";
        private string _deactivationReason = "";
        private List<string> _pendingDocumentsToReupload = new List<string>();

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

        public Driver_Rides()
        {
            InitializeComponent();
            BindingContext = this;
            RefreshCommand = new Command(async () => await RefreshRidesList());
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            string driverId = AppSession.GetDriverId();
            _hasShownReactivatedModal = Preferences.Get($"ReactivatedModalShown_{driverId}", false);
            await InitializePage();
            StartRealTimeListeners();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _accountStatusListener?.Dispose();
            _documentStatusListener?.Dispose();
            _ridesListener?.Dispose();
            _tricycleStatusListener?.Dispose();
        }

        private async Task InitializePage()
        {
            try
            {
                _currentDriverId = AppSession.GetDriverId();

                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    await ShowModal("Error", "Please login first", "warning", true);
                    await Navigation.PopAsync();
                    return;
                }

                await LoadUserData();
                await LoadTricycleInfo();
                await CheckDriverStatusAndShowView();

                if (_isDriverApproved && _accountStatus == "Active" && _isTricycleApproved)
                {
                    await LoadRides();
                    StartRidesListener();
                }
            }
            catch { }
        }

        private async Task LoadUserData()
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver != null) { }
            }
            catch { }
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
                    _tricycleStatus = _tricycleInfo.TricycleStatus ?? "Pending";
                    _isTricycleApproved = _tricycleStatus == "Approved";
                    _tricycleRejectionReason = _tricycleInfo.RejectionReason ?? "";
                }
            }
            catch
            {
                _tricycleStatus = "Error";
                _isTricycleApproved = false;
            }
        }

        private void StartRealTimeListeners()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentDriverId)) return;

                _documentStatusListener = _firebaseConnection.ListenForDriverDocumentStatus(
                    _currentDriverId,
                    async (docStatus, regCompleted, rejectionReason) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            _isDriverApproved = regCompleted && docStatus == "Approved";
                            await CheckDriverStatusAndShowView();

                            if (_isDriverApproved && _accountStatus == "Active" && _isTricycleApproved)
                            {
                                await LoadRides();
                                StartRidesListener();
                            }
                        });
                    });

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
                                bool isExpired = wasSuspended && isNowActive;

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

                                        if (isExpired && !_hasShownReactivatedModal)
                                        {
                                            _hasShownReactivatedModal = true;
                                            Preferences.Set($"ReactivatedModalShown_{_currentDriverId}", true);
                                            await ShowReactivatedModal();
                                        }
                                    });
                                }
                            }
                        }
                        catch { }
                    });

                    return true;
                });

                Device.StartTimer(TimeSpan.FromSeconds(5), () =>
                {
                    if (string.IsNullOrEmpty(_currentDriverId)) return false;

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
            await ShowModal("Account Reactivated", "Your suspension period has expired. Your account is now active again!", "success", false);
        }

        private void StartRidesListener()
        {
            try
            {
                _ridesListener?.Dispose();
                _ridesListener = _firebaseConnection.ListenForRideStatusChangeRealTimeForDriver(
                    _currentDriverId,
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
            catch { }
        }

        private async Task CheckDriverStatusAndShowView()
        {
            try
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver == null) return;

                string docStatus = driver.DocumentStatus ?? "Pending";
                bool regCompleted = driver.RegistrationCompleted;
                _accountStatus = driver.AccountStatus ?? "Active";
                _suspensionReason = driver.SuspensionReason ?? "";
                _suspensionUntil = driver.SuspendedUntil ?? "";
                _deactivationReason = driver.DeactivationReason ?? "";

                _isDriverApproved = regCompleted && docStatus == "Approved";

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    EmptyStateLayout.IsVisible = false;
                    RidesRefreshView.IsVisible = false;
                    StatusViewsScrollView.IsVisible = false;
                    WaitingView.IsVisible = false;
                    RejectedView.IsVisible = false;
                    SuspendedView.IsVisible = false;
                    DeactivatedView.IsVisible = false;
                    TricyclePendingSection.IsVisible = false;
                    TricycleRejectedSection.IsVisible = false;
                    HistoryButton.IsVisible = false;

                    if (_accountStatus == "Suspended")
                    {
                        StatusViewsScrollView.IsVisible = true;
                        SuspendedView.IsVisible = true;

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
                        StatusViewsScrollView.IsVisible = true;
                        DeactivatedView.IsVisible = true;
                        DeactivatedMessageLabel.Text = !string.IsNullOrEmpty(_deactivationReason)
                            ? $"Reason: {_deactivationReason}"
                            : "Your account has been deactivated by admin.";
                    }
                    else if (regCompleted && docStatus == "Approved")
                    {
                        if (_tricycleStatus == "Rejected")
                        {
                            StatusViewsScrollView.IsVisible = true;
                            TricycleRejectedSection.IsVisible = true;
                            TricycleRejectionReasonLabel.Text = !string.IsNullOrEmpty(_tricycleRejectionReason)
                                ? $"Reason: {_tricycleRejectionReason}"
                                : "Your tricycle details were rejected by admin.";
                        }
                        else if (_isTricycleApproved)
                        {
                            if (_allRides.Count > 0)
                            {
                                RidesRefreshView.IsVisible = true;
                            }
                            else
                            {
                                EmptyStateLayout.IsVisible = true;
                            }
                            HistoryButton.IsVisible = true;
                        }
                        else
                        {
                            StatusViewsScrollView.IsVisible = true;
                            TricyclePendingSection.IsVisible = true;

                            if (_tricycleStatus == "Not Set" || string.IsNullOrEmpty(_tricycleInfo?.PlateNumber))
                            {
                                GoToEditTricycleButton.Text = "Complete Now";
                            }
                        }
                    }
                    else if (docStatus == "Rejected" || docStatus == "Pending Review")
                    {
                        StatusViewsScrollView.IsVisible = true;
                        RejectedView.IsVisible = true;

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

                        if (_pendingDocumentsToReupload.Count == 0)
                        {
                            string singleDoc = ParseDocumentFromReason(driver.RejectionReason ?? "");
                            _pendingDocumentsToReupload = new List<string> { singleDoc };
                        }
                    }
                    else
                    {
                        StatusViewsScrollView.IsVisible = true;
                        WaitingView.IsVisible = true;
                    }
                });
            }
            catch { }
        }

        private string ParseDocumentFromReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Driver's_License";

            reason = reason.ToLower();

            if (reason.Contains("license") || reason.Contains("driver's"))
                return "Driver's_License";
            if (reason.Contains("2x2") || reason.Contains("picture"))
                return "2x2_Picture";
            if (reason.Contains("cedula"))
                return "Cedula";
            if (reason.Contains("barangay") || reason.Contains("clearance"))
                return "Barangay_Clearance";
            if (reason.Contains("orcr") || reason.Contains("or/cr"))
                return "ORCR";
            if (reason.Contains("plate"))
                return "Plate_Number";
            if (reason.Contains("gcash") || reason.Contains("qr"))
                return "GCash_QR_Code";

            return "Driver's_License";
        }

        private DateTime ToPhilippineTime(DateTime utcDateTime)
        {
            if (utcDateTime == DateTime.MinValue) return DateTime.MinValue;
            return utcDateTime.AddHours(8);
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

        private async void OnContactSupportClicked(object sender, EventArgs e)
        {
            await ShowModal("Contact Support", "Please email support@serviceco.com or call +639123456789", "info", false);
        }

        private async void OnEditTricycleClicked(object sender, EventArgs e)
        {
            try
            {
                if (_tricycleInfo == null)
                {
                    _tricycleInfo = await _firebaseConnection.GetOrCreateTricycleInfoAsync(_currentDriverId);
                }
                await Navigation.PushModalAsync(new EditTricyclePage(_tricycleInfo));
            }
            catch
            {
                await ShowModal("Error", "Failed to open edit page", "error", false);
            }
        }

        private async Task RefreshRidesList()
        {
            try
            {
                IsRefreshing = true;
                await LoadRides();
            }
            catch { }
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

                _currentDriverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    await ShowModal("Error", "Please login to view your rides", "warning", false);
                    return;
                }

                var allRideRequests = await _firebaseConnection.GetAllRideRequestsAsync();

                if (allRideRequests == null || !allRideRequests.Any())
                {
                    ShowEmptyState();
                    return;
                }

                _allRides = allRideRequests
                    .Where(r => r.DriverId == _currentDriverId)
                    .OrderByDescending(r => r.RequestTime)
                    .ToList();

                if (!_allRides.Any())
                {
                    ShowEmptyState();
                    return;
                }

                DisplayRides();
            }
            catch
            {
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
                catch
                {
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

            DateTime displayTime;
            if (ride.Status == "Completed" && ride.CompletedTime.HasValue)
            {
                displayTime = ride.CompletedTime.Value;
            }
            else
            {
                displayTime = ride.RequestTime;
            }

            DateTime phTime = ToPhilippineTime(displayTime);
            string formattedDate = phTime.ToString("MMM dd, yyyy");
            string formattedTime = phTime.ToString("hh:mm tt");
            string dayOfWeek = phTime.ToString("ddd").ToUpper();

            var nameLabel = new Label
            {
                Text = ride.CommuterName,
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#333333"),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            infoStack.Children.Add(nameLabel);

            var dateTimeText = $"{dayOfWeek} • {formattedDate} • {formattedTime}";
            var dateTimeLabel = new Label
            {
                Text = dateTimeText,
                FontSize = 12,
                TextColor = Color.FromArgb("#666666")
            };
            infoStack.Children.Add(dateTimeLabel);

            var locationLabel = new Label
            {
                Text = FormatAddress(ride.DropoffLocation, 30),
                FontSize = 12,
                TextColor = Color.FromArgb("#666666"),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            infoStack.Children.Add(locationLabel);

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
                    Text = "PAYMENT",
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
                    Text = "VIEW",
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
                    Text = "DETAILS",
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
                actionButton.Clicked += async (sender, e) => await OnRideCardTapped(ride);
            }
            else if (ride.Status == "Cancelled")
            {
                actionButton = new Button
                {
                    Text = "DETAILS",
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
                await Navigation.PushModalAsync(new Driver_WaitingPayment(ride.FirebaseKey));
            }
            catch
            {
                await ShowModal("Error", "Failed to open payment page", "error", false);
            }
        }

        private async Task OnActiveRideCardTapped(RideRequestWithKey ride)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_StartedTrip(ride.FirebaseKey));
            }
            catch
            {
                await ShowModal("Error", "Failed to open ride details", "error", false);
            }
        }

        private async Task OnRideCardTapped(RideRequestWithKey ride)
        {
            try
            {
                await Navigation.PushModalAsync(new Driver_RideDetails(ride));
            }
            catch
            {
                await ShowModal("Error", "Failed to open ride details", "error", false);
            }
        }

        private string GetStatusShortText(string status)
        {
            return status switch
            {
                "Completed" => "COMPLETED",
                "Cancelled" => "CANCELLED",
                "Pending" => "PENDING",
                "Accepted" => "ACCEPTED",
                "Picking Up" => "PICKING UP",
                "Arrived at Pickup" => "ARRIVED",
                "Trip Started" => "IN PROGRESS",
                "Payment Pending" => "PAYMENT",
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
            try
            {
                await Navigation.PushModalAsync(new Driver_RidesHistory());
            }
            catch
            {
                await ShowModal("Error", "Failed to open ride history", "error", false);
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

            var modalPage = new DriverRidesModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

    public class DriverRidesModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_Rides _parentPage;

        public DriverRidesModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_Rides parentPage)
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