using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ServiceCo
{
    public partial class Driver_Notification : ContentPage
    {
        private readonly FirebaseConnection _firebase = new FirebaseConnection();
        private IDisposable _rideListener;
        private IDisposable _documentListener;
        private IDisposable _accountStatusListener;
        private IDisposable _tricycleStatusListener;
        private IDisposable _announcementListener;
        private string _currentDriverId;
        private DriverNotificationViewModel _viewModel;
        private HashSet<string> _clearedIds;
        private const string ClearedNotificationsKey = "ClearedNotifications";

        public Driver_Notification()
        {
            InitializeComponent();

            _currentDriverId = AppSession.GetDriverId();
            _clearedIds = GetClearedNotificationIds();
            _viewModel = new DriverNotificationViewModel();
            BindingContext = _viewModel;

            LoadNotifications();
            StartRealTimeListeners();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopAllListeners();
        }

        private void StopAllListeners()
        {
            try
            {
                _rideListener?.Dispose();
                _documentListener?.Dispose();
                _accountStatusListener?.Dispose();
                _tricycleStatusListener?.Dispose();
                _announcementListener?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping listeners: {ex.Message}");
            }
        }

        private async void LoadNotifications()
        {
            try
            {
                _viewModel.IsLoading = true;
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;

                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    Console.WriteLine("No driver ID found");
                    return;
                }

                _viewModel.Notifications.Clear();
                _clearedIds = GetClearedNotificationIds();

                var allNotifications = new List<DriverNotificationModel>();

                var accountNotifications = await GetAccountStatusNotifications(includeCleared: false);
                allNotifications.AddRange(accountNotifications);

                var documentNotifications = await GetDocumentNotifications(includeCleared: false);
                allNotifications.AddRange(documentNotifications);

                var tricycleNotifications = await GetTricycleNotifications(includeCleared: false);
                allNotifications.AddRange(tricycleNotifications);

                var rideNotifications = await GetRideNotifications(includeCleared: false);
                allNotifications.AddRange(rideNotifications);

                var messageNotifications = await GetMessageNotifications(includeCleared: false);
                allNotifications.AddRange(messageNotifications);

                var paymentNotifications = await GetPaymentNotifications(includeCleared: false);
                allNotifications.AddRange(paymentNotifications);

                var ratingNotifications = await GetRatingNotifications(includeCleared: false);
                allNotifications.AddRange(ratingNotifications);

                var announcements = await _firebase.GetAnnouncementsByAudienceAsync("driver");
                foreach (var announcement in announcements.OrderByDescending(a => a.CreatedAt))
                {
                    string notificationId = $"announcement_{announcement.Id}";

                    if (!_clearedIds.Contains(notificationId))
                    {
                        allNotifications.Add(new DriverNotificationModel
                        {
                            Id = notificationId,
                            Type = "announcement",
                            Title = announcement.Title,
                            Message = announcement.Message,
                            Timestamp = announcement.CreatedAt,
                            IsUnread = true,
                            Icon = "📢",
                            IconColor = Color.FromArgb("#E3F2FD"),
                            AnnouncementId = announcement.Id,
                            CardBackgroundColor = Colors.White
                        });
                    }
                }

                var sortedNotifications = allNotifications
                    .OrderByDescending(n => n.Timestamp)
                    .ToList();

                foreach (var notification in sortedNotifications)
                {
                    _viewModel.Notifications.Add(notification);
                }

                _viewModel.UpdateEmptyState();
                EmptyStateGrid.IsVisible = _viewModel.IsEmpty;
                NotificationsCollectionView.IsVisible = _viewModel.HasNotifications;

                _viewModel.IsLoading = false;
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;

                UpdateHomePageBadge();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading notifications: {ex.Message}");
                _viewModel.IsLoading = false;
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private async Task<List<DriverNotificationModel>> GetAccountStatusNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var driver = await _firebase.GetDriverByIdAsync(_currentDriverId);
                if (driver == null) return notifications;

                if (driver.AccountStatus == "Suspended" && !string.IsNullOrEmpty(driver.SuspensionReason))
                {
                    var suspendId = $"suspend_{_currentDriverId}_current";
                    if (!_clearedIds.Contains(suspendId))
                    {
                        string untilText = "";
                        if (!string.IsNullOrEmpty(driver.SuspendedUntil))
                        {
                            try
                            {
                                DateTime untilDate = DateTime.Parse(driver.SuspendedUntil);
                                untilText = $" until {untilDate:MMMM dd, yyyy}";
                            }
                            catch { }
                        }

                        notifications.Add(CreateNotification(
                            suspendId,
                            "suspension",
                            "Account Suspended",
                            $"Your account has been suspended{untilText}.\nReason: {driver.SuspensionReason}",
                            driver.SuspendedDate ?? DateTime.UtcNow,
                            "⏸️",
                            "#FEF3C7",
                            _clearedIds.Contains(suspendId)
                        ));
                    }
                }

                if (driver.AccountStatus == "Deactivated" && !string.IsNullOrEmpty(driver.DeactivationReason))
                {
                    var deactivateId = $"deactivate_{_currentDriverId}_current";
                    if (!_clearedIds.Contains(deactivateId))
                    {
                        notifications.Add(CreateNotification(
                            deactivateId,
                            "deactivation",
                            "Account Deactivated",
                            $"Your account has been deactivated.\nReason: {driver.DeactivationReason}",
                            driver.DeactivatedDate ?? DateTime.UtcNow,
                            "🚫",
                            "#FEE2E2",
                            _clearedIds.Contains(deactivateId)
                        ));
                    }
                }

                if (driver.AccountStatus == "Active" && !string.IsNullOrEmpty(driver.ReactivationReason))
                {
                    var reactivateId = $"reactivate_{_currentDriverId}";
                    if (!_clearedIds.Contains(reactivateId))
                    {
                        notifications.Add(CreateNotification(
                            reactivateId,
                            "reactivation",
                            "Account Reactivated",
                            $"Your account has been reactivated by admin.\nReason: {driver.ReactivationReason}",
                            driver.ReactivationDate ?? DateTime.UtcNow,
                            "✅",
                            "#E8F5E9",
                            _clearedIds.Contains(reactivateId)
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting account notifications: {ex.Message}");
            }

            return notifications;
        }

        private async Task<List<DriverNotificationModel>> GetDocumentNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var driver = await _firebase.GetDriverByIdAsync(_currentDriverId);
                if (driver == null) return notifications;

                if (driver.DocumentStatus == "Rejected" && !string.IsNullOrEmpty(driver.RejectionReason))
                {
                    var rejectId = $"reject_{_currentDriverId}";
                    if (!_clearedIds.Contains(rejectId))
                    {
                        List<string> docsToFix = driver.RejectedDocuments ?? new List<string>();

                        if (docsToFix.Count == 0)
                        {
                            string singleDoc = ParseDocumentFromReason(driver.RejectionReason);
                            docsToFix = new List<string> { singleDoc };
                        }

                        string displayDocNames = string.Join(", ", docsToFix.Select(d => GetDisplayDocumentName(d)));

                        notifications.Add(CreateNotification(
                            rejectId,
                            "rejection",
                            "Application Rejected",
                            $"{driver.RejectionReason}\n\nTap to re-upload: {displayDocNames}.",
                            driver.RejectedDate ?? driver.LastUpdated ?? DateTime.UtcNow,
                            "❌",
                            "#FFEBEE",
                            _clearedIds.Contains(rejectId),
                            docsToFix
                        ));
                    }
                }

                if (driver.RegistrationCompleted == true && driver.DocumentStatus == "Approved")
                {
                    var approveId = $"approve_{_currentDriverId}";
                    if (!_clearedIds.Contains(approveId))
                    {
                        notifications.Add(CreateNotification(
                            approveId,
                            "approval",
                            "Application Approved!",
                            "Congratulations! Your driver account has been approved. Please complete your tricycle details and wait for admin approval.",
                            driver.ApprovedDate ?? driver.LastUpdated ?? DateTime.UtcNow,
                            "✅",
                            "#E8F5E9",
                            _clearedIds.Contains(approveId)
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting document notifications: {ex.Message}");
            }

            return notifications;
        }

        private async Task<List<DriverNotificationModel>> GetTricycleNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var tricycle = await _firebase.GetTricycleInfoAsync(_currentDriverId);
                if (tricycle == null) return notifications;

                if (tricycle.TricycleStatus == "Approved")
                {
                    var tricycleId = $"tricycle_approve_{_currentDriverId}";
                    if (!_clearedIds.Contains(tricycleId))
                    {
                        notifications.Add(CreateNotification(
                            tricycleId,
                            "tricycle_approval",
                            "Tricycle Approved!",
                            "Congratulations! Your tricycle profile has been approved. You can now go online and accept rides.",
                            tricycle.UpdatedAt ?? DateTime.UtcNow,
                            "🚲",
                            "#E8F5E9",
                            _clearedIds.Contains(tricycleId)
                        ));
                    }
                }

                if (tricycle.TricycleStatus == "Rejected" && !string.IsNullOrEmpty(tricycle.RejectionReason))
                {
                    var tricycleRejectId = $"tricycle_reject_{_currentDriverId}";
                    if (!_clearedIds.Contains(tricycleRejectId))
                    {
                        notifications.Add(CreateNotification(
                            tricycleRejectId,
                            "tricycle_rejection",
                            "Tricycle Rejected",
                            $"Your tricycle details were rejected.\nReason: {tricycle.RejectionReason}",
                            tricycle.UpdatedAt ?? DateTime.UtcNow,
                            "❌",
                            "#FFEBEE",
                            _clearedIds.Contains(tricycleRejectId)
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting tricycle notifications: {ex.Message}");
            }

            return notifications;
        }

        private async Task<List<DriverNotificationModel>> GetRideNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var rideRequests = await _firebase.GetDriverRideRequestsAsync(_currentDriverId);

                foreach (var ride in rideRequests.OrderByDescending(r => r.RequestTime).Take(50))
                {
                    var notificationId = $"ride_{ride.FirebaseKey}_{ride.Status}";

                    if (!_clearedIds.Contains(notificationId))
                    {
                        notifications.Add(new DriverNotificationModel
                        {
                            Id = notificationId,
                            Type = "ride",
                            Title = GetRideStatusTitle(ride.Status),
                            Message = GetRideStatusMessage(ride),
                            Timestamp = ride.RequestTime,
                            IsUnread = true,
                            Icon = GetRideIcon(ride.Status),
                            IconColor = GetIconColor(ride.Status),
                            RideId = ride.FirebaseKey,
                            CardBackgroundColor = Colors.White
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting ride notifications: {ex.Message}");
            }

            return notifications;
        }

        private async Task<List<DriverNotificationModel>> GetMessageNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var conversations = await _firebase.GetUserConversationsAsync(_currentDriverId, "driver");

                foreach (var conversation in conversations.Where(c => c.UnreadCount > 0).Take(10))
                {
                    var notificationId = $"msg_{conversation.RideRequestId}";

                    if (!_clearedIds.Contains(notificationId))
                    {
                        var recentMessages = await _firebase.GetRecentMessagesAsync(conversation.RideRequestId, 1);

                        if (recentMessages.Any())
                        {
                            var message = recentMessages.First();
                            var preview = message.Message.Length > 50
                                ? message.Message.Substring(0, 50) + "..."
                                : message.Message;

                            notifications.Add(CreateNotification(
                                notificationId,
                                "message",
                                $"Message from {conversation.CommuterName}",
                                preview,
                                message.Timestamp,
                                "💬",
                                "#E3F2FD",
                                _clearedIds.Contains(notificationId),
                                null,
                                conversation.RideRequestId
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting message notifications: {ex.Message}");
            }

            return notifications;
        }

        private async Task<List<DriverNotificationModel>> GetPaymentNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var rideRequests = await _firebase.GetDriverRideRequestsAsync(_currentDriverId);

                var recentCompleted = rideRequests
                    .Where(r => r.Status == "Completed")
                    .OrderByDescending(r => r.RequestTime)
                    .Take(20);

                foreach (var ride in recentCompleted)
                {
                    var notificationId = $"pay_{ride.FirebaseKey}";

                    if (!_clearedIds.Contains(notificationId))
                    {
                        notifications.Add(CreateNotification(
                            notificationId,
                            "payment",
                            "Payment Received",
                            $"You earned ₱{ride.Fare:N2} for the ride",
                            ride.RequestTime,
                            "💰",
                            "#FFF3E0",
                            _clearedIds.Contains(notificationId),
                            null,
                            ride.FirebaseKey
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting payment notifications: {ex.Message}");
            }

            return notifications;
        }

        private async Task<List<DriverNotificationModel>> GetRatingNotifications(bool includeCleared = false)
        {
            var notifications = new List<DriverNotificationModel>();

            try
            {
                var rideRequests = await _firebase.GetDriverRideRequestsAsync(_currentDriverId);

                var ratedRides = rideRequests
                    .Where(r => r.Status == "Completed")
                    .OrderByDescending(r => r.RequestTime)
                    .Take(20);

                foreach (var ride in ratedRides)
                {
                    var rideDetails = await _firebase.GetRideRequestByIdAsync(ride.FirebaseKey);

                    if (rideDetails != null && rideDetails.Rating > 0)
                    {
                        var notificationId = $"rate_{ride.FirebaseKey}";

                        if (!_clearedIds.Contains(notificationId))
                        {
                            notifications.Add(CreateNotification(
                                notificationId,
                                "rating",
                                "New Rating Received",
                                $"{ride.CommuterName} rated you {rideDetails.Rating} ⭐",
                                !string.IsNullOrEmpty(rideDetails.RatingTime)
                                    ? DateTime.Parse(rideDetails.RatingTime)
                                    : ride.RequestTime,
                                "⭐",
                                "#FFF9C4",
                                _clearedIds.Contains(notificationId),
                                null,
                                ride.FirebaseKey
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting rating notifications: {ex.Message}");
            }

            return notifications;
        }

        private DriverNotificationModel CreateNotification(
            string id, string type, string title, string message,
            DateTime timestamp, string icon, string iconColor,
            bool isRead, List<string> documentsToReupload = null, string rideId = null)
        {
            return new DriverNotificationModel
            {
                Id = id,
                Type = type,
                Title = title,
                Message = message,
                Timestamp = timestamp,
                IsUnread = !isRead,
                Icon = icon,
                IconColor = Color.FromArgb(iconColor),
                DocumentsToReupload = documentsToReupload ?? new List<string>(),
                RideId = rideId,
                CardBackgroundColor = !isRead ? Color.FromArgb("#F5F5F5") : Colors.White
            };
        }

        private string ParseDocumentFromReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Driver's_License";
            reason = reason.ToLower();

            if (reason.Contains("license") || reason.Contains("driver's")) return "Driver's_License";
            if (reason.Contains("2x2") || reason.Contains("picture")) return "2x2_Picture";
            if (reason.Contains("cedula")) return "Cedula";
            if (reason.Contains("barangay") || reason.Contains("clearance")) return "Barangay_Clearance";
            if (reason.Contains("orcr") || reason.Contains("or/cr")) return "ORCR";
            if (reason.Contains("plate")) return "Plate_Number";
            if (reason.Contains("gcash") || reason.Contains("qr")) return "GCash_QR_Code";

            return "Driver's_License";
        }

        private string GetDisplayDocumentName(string docKey)
        {
            return docKey switch
            {
                "2x2_Picture" => "2x2 ID Picture",
                "Cedula" => "Cedula",
                "Barangay_Clearance" => "Barangay Clearance",
                "Driver's_License" => "Driver's License",
                "ORCR" => "OR/CR",
                "Plate_Number" => "Plate Number",
                "GCash_QR_Code" => "GCash QR Code",
                _ => docKey.Replace("_", " ")
            };
        }

        private string GetRideStatusTitle(string status)
        {
            return status switch
            {
                "Pending" => "New Ride Request",
                "Accepted" => "Ride Accepted",
                "Picking Up" => "Pickup Started",
                "Arrived at Pickup" => "Arrived at Pickup",
                "Trip Started" => "Trip Started",
                "Completed" => "Ride Completed",
                "Cancelled" => "Ride Cancelled",
                "Payment Pending" => "Payment Pending",
                _ => "Ride Update"
            };
        }

        private string GetRideStatusMessage(Firebase.RideRequestWithKey ride)
        {
            return ride.Status switch
            {
                "Pending" => $"New ride request from {ride.CommuterName ?? "a passenger"}",
                "Accepted" => $"You accepted a ride to {ShortenAddress(ride.DropoffLocation, 30)}",
                "Picking Up" => $"Head to {ShortenAddress(ride.PickupLocation, 30)}",
                "Arrived at Pickup" => $"Waiting for passenger to board",
                "Trip Started" => $"En route to {ShortenAddress(ride.DropoffLocation, 30)}",
                "Completed" => $"Ride completed. Fare: ₱{ride.Fare:N2}",
                "Cancelled" => "This ride has been cancelled",
                "Payment Pending" => $"Payment of ₱{ride.Fare:N2} is pending",
                _ => $"From {ShortenAddress(ride.PickupLocation, 20)} to {ShortenAddress(ride.DropoffLocation, 20)}"
            };
        }

        private string GetRideIcon(string status)
        {
            return status switch
            {
                "Pending" => "🚖",
                "Accepted" => "✅",
                "Picking Up" => "🚗",
                "Arrived at Pickup" => "📍",
                "Trip Started" => "🛵",
                "Completed" => "🏁",
                "Cancelled" => "❌",
                "Payment Pending" => "💰",
                _ => "🚖"
            };
        }

        private Color GetIconColor(string status)
        {
            return status switch
            {
                "Pending" => Color.FromArgb("#E8F5E9"),
                "Accepted" => Color.FromArgb("#E8F5E9"),
                "Picking Up" => Color.FromArgb("#E3F2FD"),
                "Arrived at Pickup" => Color.FromArgb("#E3F2FD"),
                "Trip Started" => Color.FromArgb("#FFF3E0"),
                "Completed" => Color.FromArgb("#E8F5E9"),
                "Cancelled" => Color.FromArgb("#FFEBEE"),
                "Payment Pending" => Color.FromArgb("#FFF3E0"),
                _ => Color.FromArgb("#F5F5F5")
            };
        }

        private string ShortenAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Location";
            if (address.Length <= maxLength) return address;
            return address.Substring(0, maxLength - 3) + "...";
        }

        private void StartRealTimeListeners()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentDriverId)) return;

                _documentListener = _firebase.ListenForDriverDocumentStatus(
                    _currentDriverId,
                    async (docStatus, regCompleted, reason) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            AddNewNotification($"Document status changed to {docStatus}");
                        });
                    });

                _accountStatusListener = _firebase.ListenForDriverDocumentStatus(
                    _currentDriverId,
                    async (docStatus, regCompleted, reason) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            var driver = await _firebase.GetDriverByIdAsync(_currentDriverId);
                            if (driver != null)
                            {
                                string lastStatus = Preferences.Get($"LastAccountStatus_{_currentDriverId}", "");

                                if (lastStatus != driver.AccountStatus)
                                {
                                    if (driver.AccountStatus == "Suspended" && !string.IsNullOrEmpty(driver.SuspensionReason))
                                    {
                                        AddNewNotification($"Account suspended: {driver.SuspensionReason}");
                                    }
                                    else if (driver.AccountStatus == "Deactivated" && !string.IsNullOrEmpty(driver.DeactivationReason))
                                    {
                                        AddNewNotification($"Account deactivated: {driver.DeactivationReason}");
                                    }
                                    else if (driver.AccountStatus == "Active" && !string.IsNullOrEmpty(driver.ReactivationReason))
                                    {
                                        AddNewNotification($"Account reactivated: {driver.ReactivationReason}");
                                    }

                                    Preferences.Set($"LastAccountStatus_{_currentDriverId}", driver.AccountStatus);
                                }
                            }
                        });
                    });

                _tricycleStatusListener = _firebase.ListenForTricycleStatusChanges(
                    _currentDriverId,
                    async (status) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            string lastStatus = Preferences.Get($"LastTricycleStatus_{_currentDriverId}", "");
                            if (lastStatus != status)
                            {
                                if (status == "Approved")
                                {
                                    AddNewNotification("Your tricycle has been approved!");
                                }
                                else if (status == "Rejected")
                                {
                                    AddNewNotification("Your tricycle application was rejected.");
                                }
                                Preferences.Set($"LastTricycleStatus_{_currentDriverId}", status);
                            }
                        });
                    });

                _rideListener = _firebase.ListenForRideStatusChangeRealTimeForDriver(
                    _currentDriverId,
                    async (ride) =>
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            var notificationId = $"ride_{ride.Id}_{ride.Status}";

                            if (!_clearedIds.Contains(notificationId))
                            {
                                var rideWithKey = new Firebase.RideRequestWithKey
                                {
                                    FirebaseKey = ride.Id,
                                    Status = ride.Status,
                                    CommuterName = ride.CommuterName,
                                    PickupLocation = ride.PickupLocation,
                                    DropoffLocation = ride.DropoffLocation,
                                    Fare = ride.Fare,
                                    RequestTime = ride.RequestTime
                                };

                                var notification = new DriverNotificationModel
                                {
                                    Id = notificationId,
                                    Type = "ride",
                                    Title = GetRideStatusTitle(ride.Status),
                                    Message = GetRideStatusMessage(rideWithKey),
                                    Timestamp = DateTime.UtcNow,
                                    IsUnread = true,
                                    Icon = GetRideIcon(ride.Status),
                                    IconColor = GetIconColor(ride.Status),
                                    RideId = ride.Id,
                                    CardBackgroundColor = Color.FromArgb("#F5F5F5")
                                };

                                _viewModel.Notifications.Insert(0, notification);
                                _viewModel.UpdateEmptyState();
                                EmptyStateGrid.IsVisible = _viewModel.IsEmpty;
                                NotificationsCollectionView.IsVisible = _viewModel.HasNotifications;

                                UpdateHomePageBadge();
                            }
                        });
                    });

                StartAnnouncementListener();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up real-time listeners: {ex.Message}");
            }
        }

        private void StartAnnouncementListener()
        {
            try
            {
                Console.WriteLine($"🎧 Starting announcement listener for driver...");

                _announcementListener?.Dispose();

                _announcementListener = _firebase.ListenForAnnouncements("driver", (announcement) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        HandleNewAnnouncement(announcement);
                    });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting announcement listener: {ex.Message}");
            }
        }

        private void HandleNewAnnouncement(Announcement announcement)
        {
            try
            {
                if (announcement == null || _viewModel == null) return;

                Console.WriteLine($"📨 Handling announcement: {announcement.Title} | Status: {announcement.Status}");

                var clearedIds = GetClearedNotificationIds();
                string notificationId = $"announcement_{announcement.Id}";

                if (clearedIds.Contains(notificationId))
                {
                    Console.WriteLine($"⏭️ Announcement is cleared, skipping");
                    return;
                }

                var existing = _viewModel.Notifications
                    .FirstOrDefault(n => n.Id == notificationId && n.Type == "announcement");

                if (announcement.Status == "deleted" || announcement.Status == "inactive")
                {
                    if (existing != null)
                    {
                        Console.WriteLine($"🗑️ Removing inactive/deleted announcement: {announcement.Id}");
                        _viewModel.Notifications.Remove(existing);
                        _viewModel.UpdateEmptyState();
                        EmptyStateGrid.IsVisible = _viewModel.IsEmpty;
                        NotificationsCollectionView.IsVisible = _viewModel.HasNotifications;
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

                        var notification = new DriverNotificationModel
                        {
                            Id = notificationId,
                            Type = "announcement",
                            Title = announcement.Title,
                            Message = announcement.Message,
                            Timestamp = announcement.CreatedAt,
                            IsUnread = true,
                            Icon = "📢",
                            IconColor = Color.FromArgb("#E3F2FD"),
                            AnnouncementId = announcement.Id,
                            CardBackgroundColor = Color.FromArgb("#F5F5F5")
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
                    EmptyStateGrid.IsVisible = _viewModel.IsEmpty;
                    NotificationsCollectionView.IsVisible = _viewModel.HasNotifications;
                    UpdateHomePageBadge();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error handling announcement: {ex.Message}");
            }
        }

        private void AddNewNotification(string message)
        {
            try
            {
                var notification = new DriverNotificationModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "system",
                    Title = "System Update",
                    Message = message,
                    Timestamp = DateTime.UtcNow,
                    IsUnread = true,
                    Icon = "ℹ️",
                    IconColor = Color.FromArgb("#E3F2FD"),
                    CardBackgroundColor = Color.FromArgb("#F5F5F5")
                };

                _viewModel.Notifications.Insert(0, notification);
                _viewModel.UpdateEmptyState();
                EmptyStateGrid.IsVisible = _viewModel.IsEmpty;
                NotificationsCollectionView.IsVisible = _viewModel.HasNotifications;

                UpdateHomePageBadge();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding notification: {ex.Message}");
            }
        }

        private HashSet<string> GetClearedNotificationIds()
        {
            try
            {
                var serialized = Preferences.Get($"{ClearedNotificationsKey}_{_currentDriverId}", "[]");
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
                Preferences.Set($"{ClearedNotificationsKey}_{_currentDriverId}", serialized);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving cleared notifications: {ex.Message}");
            }
        }

        private void UpdateHomePageBadge()
        {
            try
            {
                int unreadCount = _viewModel.Notifications.Count(n => n.IsUnread);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MessagingCenter.Send(this, "UpdateNotificationBadge", unreadCount);
                });
            }
            catch { }
        }

        private async void OnNotificationTapped(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is DriverNotificationModel notification)
            {
                notification.IsUnread = false;

                var clearedIds = GetClearedNotificationIds();
                clearedIds.Add(notification.Id);
                SaveClearedNotificationIds(clearedIds);

                NotificationsCollectionView.SelectedItem = null;
                _viewModel.UpdateEmptyState();

                UpdateHomePageBadge();

                if (notification.Type == "rejection" && notification.DocumentsToReupload != null && notification.DocumentsToReupload.Count > 0)
                {
                    await Navigation.PushModalAsync(new Driver_UploadRejected(_currentDriverId, notification.DocumentsToReupload));
                }
                else if (notification.Type == "suspension" || notification.Type == "deactivation" || notification.Type == "reactivation")
                {
                    await Navigation.PopModalAsync();
                }
                else if (notification.Type == "approval" || notification.Type == "tricycle_approval")
                {
                    await Navigation.PopModalAsync();
                }
                else if (!string.IsNullOrEmpty(notification.RideId))
                {
                    try
                    {
                        await Navigation.PushModalAsync(new Driver_StartedTrip(notification.RideId));
                    }
                    catch { }
                }
            }
        }

        private async void OnClearAllClicked(object sender, EventArgs e)
        {
            try
            {
                bool confirm = await DisplayAlert("Clear All", "Clear all notifications? This will hide them but they won't be deleted.", "Clear", "Cancel");

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
                    EmptyStateGrid.IsVisible = _viewModel.IsEmpty;
                    NotificationsCollectionView.IsVisible = _viewModel.HasNotifications;

                    _clearedIds = GetClearedNotificationIds();

                    await DisplayAlert("Cleared", "All notifications have been cleared.", "OK");

                    UpdateHomePageBadge();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Clear All: {ex.Message}");
                await DisplayAlert("Error", "Failed to clear notifications.", "OK");
            }
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }

    public class DriverNotificationViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<DriverNotificationModel> Notifications { get; set; }
        public ICommand RefreshCommand { get; }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                _isRefreshing = value;
                OnPropertyChanged(nameof(IsRefreshing));
            }
        }

        private bool _isEmpty;
        public bool IsEmpty
        {
            get => _isEmpty;
            set
            {
                _isEmpty = value;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private bool _hasNotifications;
        public bool HasNotifications
        {
            get => _hasNotifications;
            set
            {
                _hasNotifications = value;
                OnPropertyChanged(nameof(HasNotifications));
            }
        }

        public DriverNotificationViewModel()
        {
            Notifications = new ObservableCollection<DriverNotificationModel>();
            RefreshCommand = new Command(async () => await RefreshNotifications());
            UpdateEmptyState();
        }

        private async Task RefreshNotifications()
        {
            IsRefreshing = true;
            await Task.Delay(500);
            IsRefreshing = false;
        }

        public void UpdateEmptyState()
        {
            HasNotifications = Notifications.Any();
            IsEmpty = !HasNotifications;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DriverNotificationModel : INotifyPropertyChanged
    {
        private bool _isUnread;

        public string Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }

        public bool IsUnread
        {
            get => _isUnread;
            set
            {
                _isUnread = value;
                OnPropertyChanged(nameof(IsUnread));
                OnPropertyChanged(nameof(CardBackgroundColor));
            }
        }

        public string Icon { get; set; }
        public Color IconColor { get; set; }
        public string RideId { get; set; }
        public string AnnouncementId { get; set; }
        public List<string> DocumentsToReupload { get; set; } = new List<string>();

        private Color _cardBackgroundColor = Colors.White;
        public Color CardBackgroundColor
        {
            get => IsUnread ? Color.FromArgb("#F5F5F5") : Colors.White;
            set => _cardBackgroundColor = value;
        }

        public string TimeAgo
        {
            get
            {
                var timeSpan = DateTime.UtcNow - Timestamp;

                if (timeSpan.TotalDays >= 7)
                    return $"{(int)(timeSpan.TotalDays / 7)}w ago";
                if (timeSpan.TotalDays >= 1)
                    return $"{(int)timeSpan.TotalDays}d ago";
                if (timeSpan.TotalHours >= 1)
                    return $"{(int)timeSpan.TotalHours}h ago";
                if (timeSpan.TotalMinutes >= 1)
                    return $"{(int)timeSpan.TotalMinutes}m ago";

                return "Just now";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}