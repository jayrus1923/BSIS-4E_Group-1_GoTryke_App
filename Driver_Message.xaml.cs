using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ServiceCo
{
    public partial class Driver_Message : ContentPage, INotifyPropertyChanged
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private ObservableCollection<ConversationDisplay> _conversations = new ObservableCollection<ConversationDisplay>();
        private string _currentDriverId;
        private IDisposable _accountStatusListener;
        private IDisposable _documentStatusListener;
        private IDisposable _conversationsListener;
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

        public ObservableCollection<ConversationDisplay> Conversations
        {
            get => _conversations;
            set
            {
                _conversations = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasConversations));
                OnPropertyChanged(nameof(HasNoConversations));
            }
        }

        public bool HasConversations => _conversations.Count > 0;
        public bool HasNoConversations => _conversations.Count == 0;

        public ICommand RefreshCommand { get; }

        public Driver_Message()
        {
            InitializeComponent();
            RefreshCommand = new Command(async () => await LoadConversations());
            BindingContext = this;
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
            _conversationsListener?.Dispose();
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
                    await LoadConversations();
                    StartConversationsListener();
                }
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
                                await LoadConversations();
                                StartConversationsListener();
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

        private void StartConversationsListener()
        {
            try
            {
                _conversationsListener?.Dispose();
                _conversationsListener = _firebaseConnection.ListenForConversationsForDriver(
                    _currentDriverId,
                    async () =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await LoadConversations();
                        });
                    });
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
                    WaitingView.IsVisible = false;
                    RejectedView.IsVisible = false;
                    SuspendedView.IsVisible = false;
                    DeactivatedView.IsVisible = false;
                    ApprovedView.IsVisible = false;
                    ActiveMessagesView.IsVisible = false;
                    TricyclePendingSection.IsVisible = false;
                    TricycleRejectedSection.IsVisible = false;

                    if (_accountStatus == "Suspended")
                    {
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
                        DeactivatedView.IsVisible = true;

                        DeactivatedMessageLabel.Text = !string.IsNullOrEmpty(_deactivationReason)
                            ? $"Reason: {_deactivationReason}"
                            : "Your account has been deactivated by admin.";
                    }
                    else if (regCompleted && docStatus == "Approved")
                    {
                        ApprovedView.IsVisible = true;

                        if (_tricycleStatus == "Rejected")
                        {
                            TricycleRejectedSection.IsVisible = true;
                            TricycleRejectionReasonLabel.Text = !string.IsNullOrEmpty(_tricycleRejectionReason)
                                ? $"Reason: {_tricycleRejectionReason}"
                                : "Your tricycle details were rejected by admin.";
                        }
                        else if (_isTricycleApproved)
                        {
                            ActiveMessagesView.IsVisible = true;
                        }
                        else
                        {
                            TricyclePendingSection.IsVisible = true;

                            if (_tricycleStatus == "Not Set" || string.IsNullOrEmpty(_tricycleInfo?.PlateNumber))
                            {
                                GoToEditTricycleButton.Text = "Complete Now";
                            }
                        }
                    }
                    else if (docStatus == "Rejected" || docStatus == "Pending Review")
                    {
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

        private async Task LoadConversations()
        {
            try
            {
                Conversations.Clear();

                var conversations = await _firebaseConnection.GetUserConversationsAsync(_currentDriverId, "driver");

                if (conversations != null && conversations.Count > 0)
                {
                    foreach (var conversation in conversations.OrderByDescending(c => c.UpdatedAt))
                    {
                        var display = await ConvertToDisplayModel(conversation);
                        if (display != null)
                        {
                            Conversations.Add(display);
                        }
                    }
                }

                OnPropertyChanged(nameof(HasConversations));
                OnPropertyChanged(nameof(HasNoConversations));
            }
            catch { }
        }

        private async Task<ConversationDisplay> ConvertToDisplayModel(ConversationInfo conversation)
        {
            try
            {
                var display = new ConversationDisplay
                {
                    RideRequestId = conversation.RideRequestId,
                    CommuterId = conversation.CommuterId,
                    CommuterName = conversation.CommuterName,
                    LastMessage = conversation.LastMessage,
                    LastMessageTime = conversation.LastMessageTime,
                    UnreadCount = conversation.UnreadCount,
                    IsLastMessageFromMe = conversation.LastMessageSender == "driver"
                };

                if (!string.IsNullOrEmpty(conversation.CommuterId))
                {
                    var commuter = await _firebaseConnection.GetCommuterByIdAsync(conversation.CommuterId);
                    if (commuter != null && !string.IsNullOrEmpty(commuter.ProfileImageUrl))
                    {
                        display.ProfileImageUrl = commuter.ProfileImageUrl;
                    }
                    else
                    {
                        display.ProfileImageUrl = "profile_icon.png";
                    }
                }

                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(conversation.RideRequestId);
                if (rideRequest != null)
                {
                    if (!string.IsNullOrEmpty(rideRequest.PickupLocation) && !string.IsNullOrEmpty(rideRequest.DropoffLocation))
                    {
                        display.RideInfo = $"{rideRequest.PickupLocation} → {rideRequest.DropoffLocation}";
                    }
                    else if (!string.IsNullOrEmpty(rideRequest.PickupLocation))
                    {
                        display.RideInfo = rideRequest.PickupLocation;
                    }
                    else if (!string.IsNullOrEmpty(rideRequest.DropoffLocation))
                    {
                        display.RideInfo = rideRequest.DropoffLocation;
                    }
                    else
                    {
                        display.RideInfo = "Ride details";
                    }

                    display.RideStatus = rideRequest.Status;
                }
                else
                {
                    display.RideInfo = "Ride information";
                    display.RideStatus = "Unknown";
                }

                if (string.IsNullOrEmpty(display.LastMessage))
                {
                    display.LastMessage = "No messages yet";
                }

                return display;
            }
            catch
            {
                return null;
            }
        }

        private async void OnConversationSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (e.CurrentSelection.FirstOrDefault() is ConversationDisplay selectedConversation)
                {
                    ConversationsListView.SelectedItem = null;

                    await _firebaseConnection.MarkMessagesAsReadAsync(selectedConversation.RideRequestId, "driver");

                    await Navigation.PushAsync(new Driver_Chat(
                        selectedConversation.RideRequestId,
                        selectedConversation.CommuterId,
                        selectedConversation.CommuterName));
                }
            }
            catch
            {
                await ShowModal("Error", "Failed to open conversation", "error", false);
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

            var modalPage = new DriverMessageModalPage(modalContent, blurOverlay, closePageOnOk, this);
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

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DriverMessageModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_Message _parentPage;

        public DriverMessageModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_Message parentPage)
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

    public class ConversationDisplay
    {
        public string RideRequestId { get; set; }
        public string CommuterId { get; set; }
        public string CommuterName { get; set; }
        public string ProfileImageUrl { get; set; }
        public string LastMessage { get; set; }
        public DateTime LastMessageTime { get; set; }
        public int UnreadCount { get; set; }
        public bool IsLastMessageFromMe { get; set; }
        public string RideInfo { get; set; }
        public string RideStatus { get; set; }

        public bool HasUnread => UnreadCount > 0;

        public string LastMessageColor => HasUnread ? "#333333" : "#666666";

        public string LastMessageTimeDisplay
        {
            get
            {
                if (LastMessageTime == DateTime.MinValue)
                    return "";

                var now = DateTime.UtcNow;
                var diff = now - LastMessageTime;

                if (diff.TotalDays >= 365)
                    return $"{diff.Days / 365}y";
                if (diff.TotalDays >= 30)
                    return $"{diff.Days / 30}mo";
                if (diff.TotalDays >= 7)
                    return $"{diff.Days / 7}w";
                if (diff.TotalDays >= 1)
                    return $"{diff.Days}d";
                if (diff.TotalHours >= 1)
                    return $"{diff.Hours}h";
                if (diff.TotalMinutes >= 1)
                    return $"{diff.Minutes}m";
                return "Just now";
            }
        }
    }

    public class ImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    {
                        return ImageSource.FromUri(new Uri(url));
                    }
                    else if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return url;
                    }
                }
                catch { }
            }

            return "profile_icon.png";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}