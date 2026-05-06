using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ServiceCo.Firebase;
using Firebase.Database;
using Firebase.Database.Query;
using System.Reactive.Linq;

namespace ServiceCo
{
    public partial class Commuter_Notification : ContentPage
    {
        private readonly FirebaseConnection _firebase;
        private readonly FirebaseClient _firebaseClient;
        private IDisposable _rideListener;
        private IDisposable _announcementListener;
        private IDisposable _verificationListener;
        private string _currentCommuterId;
        private const string NotificationsKey = "UserNotifications";
        private const string ClearedNotificationsKey = "ClearedNotifications";
        private NotificationViewModel _viewModel;
        private HashSet<string> _clearedIds;

        public Commuter_Notification()
        {
            try
            {
                InitializeComponent();
                _firebase = new FirebaseConnection();
                _firebaseClient = new FirebaseClient("https://serviceco-37c60-default-rtdb.firebaseio.com/");
                _currentCommuterId = Preferences.Get("CommuterId", "");
                _clearedIds = GetClearedNotificationIds();
                _viewModel = new NotificationViewModel();
                BindingContext = _viewModel;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in constructor: {ex.Message}");
            }
        }

        protected override async void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                Console.WriteLine("📱 Notification page appearing");

                if (_viewModel != null)
                {
                    _viewModel.IsLoading = true;
                    _clearedIds = GetClearedNotificationIds();
                    await LoadNotifications();
                    StartRideListener();
                    StartAnnouncementListener();
                    StartVerificationListener();
                    _viewModel.IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnAppearing: {ex.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
                Console.WriteLine("📱 Notification page disappearing");
                _rideListener?.Dispose();
                _announcementListener?.Dispose();
                _verificationListener?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnDisappearing: {ex.Message}");
            }
        }

        private void StartVerificationListener()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentCommuterId)) return;

                Console.WriteLine($"👂 Starting verification listener for commuter: {_currentCommuterId}");

                _verificationListener?.Dispose();

                _verificationListener = _firebaseClient
                    .Child("CommuterNotifications")
                    .Child(_currentCommuterId)
                    .AsObservable<NotificationData>()
                    .Subscribe(notificationData =>
                    {
                        if (notificationData.Object != null)
                        {
                            Console.WriteLine($"📨 Verification notification received: {notificationData.Object.Title}");
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                HandleNewVerificationNotification(notificationData.Object);
                            });
                        }
                        else if (notificationData.Key != null)
                        {
                            Console.WriteLine($"🗑️ Notification deleted: {notificationData.Key}");
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                var existing = _viewModel.Notifications.FirstOrDefault(n => n.Id == notificationData.Key);
                                if (existing != null)
                                {
                                    _viewModel.Notifications.Remove(existing);
                                    _viewModel.UpdateEmptyState();
                                    SaveNotificationsToStorage(_viewModel.Notifications.ToList());
                                }
                            });
                        }
                    },
                    ex => Console.WriteLine($"❌ Verification listener error: {ex.Message}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error starting verification listener: {ex.Message}");
            }
        }

        private void HandleNewVerificationNotification(NotificationData notification)
        {
            try
            {
                if (notification == null) return;

                Console.WriteLine($"📋 Processing verification notification: {notification.Title} | Type: {notification.Type}");

                _clearedIds = GetClearedNotificationIds();
                if (_clearedIds.Contains(notification.Id))
                {
                    Console.WriteLine($"⏭️ Notification already cleared: {notification.Id}");
                    return;
                }

                var existing = _viewModel.Notifications.FirstOrDefault(n => n.Id == notification.Id);

                if (existing == null)
                {
                    var notificationModel = new NotificationModel
                    {
                        Id = notification.Id,
                        Title = notification.Title,
                        Message = notification.Message,
                        Timestamp = notification.Timestamp,
                        IsUnread = true,
                        Type = notification.Type ?? "verification"
                    };

                    _viewModel.Notifications.Insert(0, notificationModel);

                    var sorted = _viewModel.Notifications.OrderByDescending(n => n.Timestamp).ToList();
                    _viewModel.Notifications.Clear();
                    foreach (var item in sorted)
                    {
                        _viewModel.Notifications.Add(item);
                    }

                    _viewModel.UpdateEmptyState();
                    SaveNotificationsToStorage(_viewModel.Notifications.ToList());

                    Console.WriteLine($"✅ Verification notification added: {notification.Title}");
                }
                else if (existing.IsUnread != notification.IsRead)
                {
                    existing.IsUnread = !notification.IsRead;
                    SaveNotificationsToStorage(_viewModel.Notifications.ToList());
                    Console.WriteLine($"🔄 Verification notification updated: {notification.Title}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error handling verification notification: {ex.Message}");
            }
        }

        private void StartRideListener()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentCommuterId)) return;

                Console.WriteLine($"Starting ride listener...");

                _rideListener?.Dispose();

                _rideListener = _firebase.ListenForRideStatusChangeRealTimeForCommuter(
                    _currentCommuterId,
                    (ride) =>
                    {
                        try
                        {
                            if (ride != null)
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    HandleRideUpdate(ride);
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error in ride callback: {ex.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting ride listener: {ex.Message}");
            }
        }

        private void StartAnnouncementListener()
        {
            try
            {
                Console.WriteLine($"Starting announcement listener...");

                _announcementListener?.Dispose();

                _announcementListener = _firebase.ListenForAnnouncements("commuter", (announcement) =>
                {
                    try
                    {
                        if (announcement != null)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                HandleNewAnnouncement(announcement);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in announcement callback: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting announcement listener: {ex.Message}");
            }
        }

        private void HandleRideUpdate(RideRequest ride)
        {
            try
            {
                if (ride == null || _viewModel == null) return;

                _clearedIds = GetClearedNotificationIds();
                if (_clearedIds.Contains(ride.Id)) return;

                var existing = _viewModel.Notifications
                    .FirstOrDefault(n => n.RideId == ride.Id && n.Type == "ride");

                if (existing != null)
                {
                    _viewModel.Notifications.Remove(existing);
                }

                var notification = CreateRideNotification(ride);
                notification.Timestamp = DateTime.UtcNow;
                notification.IsUnread = true;

                _viewModel.Notifications.Insert(0, notification);

                var sorted = _viewModel.Notifications.OrderByDescending(n => n.Timestamp).ToList();
                _viewModel.Notifications.Clear();
                foreach (var item in sorted)
                {
                    _viewModel.Notifications.Add(item);
                }

                _viewModel.UpdateEmptyState();
                SaveNotificationsToStorage(_viewModel.Notifications.ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling ride update: {ex.Message}");
            }
        }

        private void HandleNewAnnouncement(Announcement announcement)
        {
            try
            {
                if (announcement == null || _viewModel == null) return;

                Console.WriteLine($"📨 Handling announcement: {announcement.Title} | Status: {announcement.Status}");

                _clearedIds = GetClearedNotificationIds();
                if (_clearedIds.Contains(announcement.Id))
                {
                    Console.WriteLine($"⏭️ Announcement is cleared, skipping");
                    return;
                }

                var existing = _viewModel.Notifications
                    .FirstOrDefault(n => n.AnnouncementId == announcement.Id && n.Type == "announcement");

                if (announcement.Status == "deleted" || announcement.Status == "inactive")
                {
                    if (existing != null)
                    {
                        Console.WriteLine($"🗑️ Removing inactive/deleted announcement: {announcement.Id}");
                        _viewModel.Notifications.Remove(existing);

                        _viewModel.UpdateEmptyState();
                        SaveNotificationsToStorage(_viewModel.Notifications.ToList());
                    }
                    return;
                }

                if (announcement.Status == "active")
                {
                    if (existing != null)
                    {
                        Console.WriteLine($"🔄 Updating existing announcement: {announcement.Title}");
                        existing.Title = announcement.Title;
                        existing.Message = announcement.Message;
                        existing.Timestamp = announcement.UpdatedAt;
                        existing.IsUnread = true;
                    }
                    else
                    {
                        Console.WriteLine($"➕ Adding new announcement: {announcement.Title}");
                        var notification = new NotificationModel
                        {
                            Id = announcement.Id,
                            Title = announcement.Title,
                            Message = announcement.Message,
                            Timestamp = announcement.CreatedAt,
                            IsUnread = true,
                            Type = "announcement",
                            AnnouncementId = announcement.Id
                        };
                        _viewModel.Notifications.Insert(0, notification);
                    }

                    var sorted = _viewModel.Notifications.OrderByDescending(n => n.Timestamp).ToList();
                    _viewModel.Notifications.Clear();
                    foreach (var item in sorted)
                    {
                        _viewModel.Notifications.Add(item);
                    }

                    _viewModel.UpdateEmptyState();
                    SaveNotificationsToStorage(_viewModel.Notifications.ToList());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error handling announcement: {ex.Message}");
            }
        }

        public async Task LoadNotifications()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentCommuterId) || _viewModel == null) return;

                Console.WriteLine($"Loading notifications...");

                _viewModel.Notifications.Clear();
                _clearedIds = GetClearedNotificationIds();
                var allNotifications = new List<NotificationModel>();

                var rideRequests = await _firebase.GetCommuterRideRequestsAsync(_currentCommuterId);

                foreach (var ride in rideRequests.OrderByDescending(r => r.RequestTime))
                {
                    if (!_clearedIds.Contains(ride.Id))
                    {
                        allNotifications.Add(CreateRideNotification(ride));
                    }
                }

                var announcements = await _firebase.GetAnnouncementsByAudienceAsync("commuter");

                foreach (var announcement in announcements.OrderByDescending(a => a.CreatedAt))
                {
                    if (!_clearedIds.Contains(announcement.Id))
                    {
                        allNotifications.Add(new NotificationModel
                        {
                            Id = announcement.Id,
                            Title = announcement.Title,
                            Message = announcement.Message,
                            Timestamp = announcement.CreatedAt,
                            IsUnread = true,
                            Type = "announcement",
                            AnnouncementId = announcement.Id
                        });
                    }
                }

                var verificationNotifications = await LoadVerificationNotificationsAsync();
                allNotifications.AddRange(verificationNotifications);

                var sortedNotifications = allNotifications
                    .OrderByDescending(n => n.Timestamp)
                    .ToList();

                SaveNotificationsToStorage(sortedNotifications);

                foreach (var notification in sortedNotifications)
                {
                    _viewModel.Notifications.Add(notification);
                }

                _viewModel.UpdateEmptyState();
                Console.WriteLine($"Loaded {sortedNotifications.Count} notifications");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading notifications: {ex.Message}");
                LoadNotificationsFromStorage();
            }
        }

        private async Task<List<NotificationModel>> LoadVerificationNotificationsAsync()
        {
            var notifications = new List<NotificationModel>();

            try
            {
                var verificationNotes = await _firebaseClient
                    .Child("CommuterNotifications")
                    .Child(_currentCommuterId)
                    .OnceAsync<NotificationData>();

                foreach (var note in verificationNotes)
                {
                    if (note.Object != null && !_clearedIds.Contains(note.Key))
                    {
                        notifications.Add(new NotificationModel
                        {
                            Id = note.Key,
                            Title = note.Object.Title,
                            Message = note.Object.Message,
                            Timestamp = note.Object.Timestamp,
                            IsUnread = !note.Object.IsRead,
                            Type = note.Object.Type
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading verification notifications: {ex.Message}");
            }

            return notifications;
        }

        private NotificationModel CreateRideNotification(RideRequest ride)
        {
            try
            {
                return new NotificationModel
                {
                    Id = ride.Id ?? Guid.NewGuid().ToString(),
                    Title = GetRideStatusTitle(ride.Status),
                    Message = GetRideStatusMessage(ride),
                    Timestamp = ride.RequestTime,
                    IsUnread = IsRideUnread(ride),
                    RideId = ride.Id,
                    Type = "ride"
                };
            }
            catch
            {
                return new NotificationModel();
            }
        }

        private string GetRideStatusTitle(string status)
        {
            return status switch
            {
                "Pending" => "Ride requested",
                "Searching" => "Finding driver",
                "Accepted" => "Driver found",
                "Picking Up" => "Driver on the way",
                "Trip Started" => "Trip started",
                "Completed" => "Ride completed",
                "Cancelled" => "Ride cancelled",
                _ => "Ride update"
            };
        }

        private string GetRideStatusMessage(RideRequest ride)
        {
            try
            {
                switch (ride.Status)
                {
                    case "Accepted":
                        if (!string.IsNullOrEmpty(ride.DriverName))
                            return $"{ride.DriverName} accepted your ride request. Fare: ₱{ride.Fare:0.00}";
                        return $"A driver accepted your ride. Fare: ₱{ride.Fare:0.00}";

                    case "Picking Up":
                        if (!string.IsNullOrEmpty(ride.DriverName))
                            return $"{ride.DriverName} is on the way to pick you up.";
                        return "Your driver is on the way to pick you up.";

                    case "Trip Started":
                        return $"Your trip to {GetShortAddress(ride.DropoffLocation)} has started.";

                    case "Completed":
                        return $"Your ride is complete. Thank you for riding with ServiceCo.";

                    case "Cancelled":
                        return ride.CancelledBy == "Driver" ?
                            "Your ride was cancelled by the driver." :
                            "Your ride has been cancelled.";

                    default:
                        return $"{GetShortAddress(ride.PickupLocation)} → {GetShortAddress(ride.DropoffLocation)}";
                }
            }
            catch
            {
                return "Ride update";
            }
        }

        private string GetShortAddress(string address)
        {
            try
            {
                if (string.IsNullOrEmpty(address)) return "Location";
                var parts = address.Split(',');
                return parts[0].Trim();
            }
            catch
            {
                return "Location";
            }
        }

        private bool IsRideUnread(RideRequest ride)
        {
            try
            {
                var timeSinceUpdate = DateTime.UtcNow - ride.RequestTime;
                return timeSinceUpdate.TotalHours < 24;
            }
            catch
            {
                return true;
            }
        }

        private void SaveNotificationsToStorage(List<NotificationModel> notifications)
        {
            try
            {
                var serialized = System.Text.Json.JsonSerializer.Serialize(notifications);
                Preferences.Set($"{NotificationsKey}_{_currentCommuterId}", serialized);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving: {ex.Message}");
            }
        }

        private void LoadNotificationsFromStorage()
        {
            try
            {
                if (_viewModel == null) return;

                var serialized = Preferences.Get($"{NotificationsKey}_{_currentCommuterId}", "");
                if (!string.IsNullOrEmpty(serialized))
                {
                    var notifications = System.Text.Json.JsonSerializer.Deserialize<List<NotificationModel>>(serialized);
                    if (notifications != null)
                    {
                        _clearedIds = GetClearedNotificationIds();
                        var filteredNotifications = notifications
                            .Where(n => !_clearedIds.Contains(n.Id))
                            .ToList();

                        foreach (var notification in filteredNotifications)
                        {
                            _viewModel.Notifications.Add(notification);
                        }

                        _viewModel.UpdateEmptyState();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading storage: {ex.Message}");
            }
        }

        private HashSet<string> GetClearedNotificationIds()
        {
            try
            {
                var serialized = Preferences.Get($"{ClearedNotificationsKey}_{_currentCommuterId}", "[]");
                return System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(serialized) ?? new HashSet<string>();
            }
            catch
            {
                return new HashSet<string>();
            }
        }

        private void SaveClearedNotificationIds(HashSet<string> ids)
        {
            try
            {
                var serialized = System.Text.Json.JsonSerializer.Serialize(ids);
                Preferences.Set($"{ClearedNotificationsKey}_{_currentCommuterId}", serialized);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving cleared: {ex.Message}");
            }
        }

        private async void OnClearAllClicked(object sender, EventArgs e)
        {
            try
            {
                bool confirm = await DisplayAlert("Clear all", "Clear all notifications?", "Clear", "Cancel");

                if (confirm && _viewModel != null)
                {
                    var allIds = _viewModel.Notifications
                        .Where(n => !string.IsNullOrEmpty(n.Id))
                        .Select(n => n.Id)
                        .ToHashSet();

                    var clearedIds = GetClearedNotificationIds();
                    foreach (var id in allIds)
                    {
                        clearedIds.Add(id);
                    }
                    SaveClearedNotificationIds(clearedIds);

                    _viewModel.Notifications.Clear();
                    _viewModel.UpdateEmptyState();
                    Preferences.Remove($"{NotificationsKey}_{_currentCommuterId}");

                    _clearedIds = GetClearedNotificationIds();

                    await DisplayAlert("Cleared", "All notifications have been cleared.", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Clear All: {ex.Message}");
                await DisplayAlert("Error", "Failed to clear notifications.", "OK");
            }
        }
    }

    public class NotificationViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<NotificationModel> _notifications;
        public ObservableCollection<NotificationModel> Notifications
        {
            get => _notifications;
            set
            {
                _notifications = value;
                OnPropertyChanged(nameof(Notifications));
                UpdateEmptyState();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(nameof(IsRefreshing)); }
        }

        private bool _isEmpty;
        public bool IsEmpty
        {
            get => _isEmpty;
            set { _isEmpty = value; OnPropertyChanged(nameof(IsEmpty)); }
        }

        private bool _hasNotifications;
        public bool HasNotifications
        {
            get => _hasNotifications;
            set { _hasNotifications = value; OnPropertyChanged(nameof(HasNotifications)); }
        }

        public ICommand RefreshCommand { get; }

        public NotificationViewModel()
        {
            try
            {
                Notifications = new ObservableCollection<NotificationModel>();
                RefreshCommand = new Command(async () => await ExecuteRefreshCommand());
                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ViewModel constructor: {ex.Message}");
            }
        }

        private async Task ExecuteRefreshCommand()
        {
            try
            {
                IsRefreshing = true;

                if (Application.Current?.MainPage?.Navigation?.ModalStack?.FirstOrDefault() is Commuter_Notification page)
                {
                    await page.LoadNotifications();
                }

                await Task.Delay(500);
                IsRefreshing = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RefreshCommand: {ex.Message}");
                IsRefreshing = false;
            }
        }

        public void UpdateEmptyState()
        {
            try
            {
                HasNotifications = Notifications?.Any() ?? false;
                IsEmpty = !HasNotifications;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateEmptyState: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class NotificationModel : INotifyPropertyChanged
    {
        private bool _isUnread;
        public string Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string RideId { get; set; }
        public string AnnouncementId { get; set; }
        public string Type { get; set; } = "ride";

        public bool IsUnread
        {
            get => _isUnread;
            set { _isUnread = value; OnPropertyChanged(nameof(IsUnread)); }
        }

        public string TimeAgo
        {
            get
            {
                try
                {
                    var timeSpan = DateTime.UtcNow - Timestamp;
                    if (timeSpan.TotalDays >= 1) return $"{timeSpan.TotalDays:F0}d ago";
                    if (timeSpan.TotalHours >= 1) return $"{timeSpan.TotalHours:F0}h ago";
                    if (timeSpan.TotalMinutes >= 1) return $"{timeSpan.TotalMinutes:F0}m ago";
                    return "Just now";
                }
                catch
                {
                    return "Just now";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try
            {
                if (value is string type)
                {
                    if (type == "announcement") return Color.FromArgb("#2196F3");
                    if (type == "verification") return Color.FromArgb("#9C27B0");
                    return Color.FromArgb("#2E7D32");
                }
            }
            catch { }
            return Color.FromArgb("#2E7D32");
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}