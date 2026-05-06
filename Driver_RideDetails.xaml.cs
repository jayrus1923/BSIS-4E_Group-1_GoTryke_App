using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.Linq;

namespace ServiceCo
{
    public partial class Driver_RideDetails : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private RideRequestWithKey _ride;
        private string _rideId;
        private string _firebaseKey;
        private RideRequest _rideDetails;
        private string _driverId;
        private Commuter _commuterInfo;
        private string _commuterId;
        private bool _isModalOpen = false;

        public Driver_RideDetails(RideRequestWithKey ride)
        {
            InitializeComponent();
            _ride = ride;
            _firebaseKey = ride.FirebaseKey;
            _driverId = AppSession.GetDriverId();
            LoadRideDetails();
        }

        private DateTime ToPhilippineTime(DateTime utcDateTime)
        {
            if (utcDateTime == DateTime.MinValue) return DateTime.MinValue;
            return utcDateTime.AddHours(8);
        }

        private string FormatPhilippineTime(DateTime? utcDateTime)
        {
            if (!utcDateTime.HasValue || utcDateTime.Value == DateTime.MinValue)
                return "--:--";
            DateTime phTime = ToPhilippineTime(utcDateTime.Value);
            return phTime.ToString("hh:mm tt");
        }

        private string FormatPhilippineDate(DateTime? utcDateTime)
        {
            if (!utcDateTime.HasValue || utcDateTime.Value == DateTime.MinValue)
                return "--";
            DateTime phTime = ToPhilippineTime(utcDateTime.Value);
            return phTime.ToString("MMMM dd, yyyy");
        }

        private async void LoadRideDetails()
        {
            try
            {
                _rideDetails = await _firebaseConnection.GetRideRequestByIdAsync(_firebaseKey);

                if (_rideDetails == null)
                {
                    await ShowModal("Error", "Ride details not found", "error", true);
                    return;
                }

                _commuterId = _rideDetails.CommuterId;
                _rideId = !string.IsNullOrEmpty(_rideDetails.Id) ? _rideDetails.Id : "TRC-UNKNOWN";
                RideIdLabel.Text = _rideId;

                SetDateBasedOnStatus();
                SetStatusBadge(_rideDetails.Status);
                await LoadPassengerInfo();

                PickupLabel.Text = FormatAddress(_ride.PickupLocation, 40);
                DropoffLabel.Text = FormatAddress(_ride.DropoffLocation, 40);
                CalculateAndSetTimes();

                FareLabel.Text = $"₱{_ride.Fare:F0}";
                DistanceLabel.Text = _rideDetails.Distance > 0 ? $"{_rideDetails.Distance:F1} km" : "-- km";

                SetVehicleInfo();
                SetPaymentInfo();
                UpdateActionButtons(_rideDetails.Status, _rideDetails.PaymentStatus);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ride details: {ex.Message}");
                await ShowModal("Error", "Failed to load ride details", "error", true);
            }
        }

        private void SetDateBasedOnStatus()
        {
            try
            {
                if (_rideDetails.Status == "Completed")
                {
                    if (_rideDetails.CompletedTime.HasValue)
                    {
                        DateTimeLabel.Text = FormatPhilippineDate(_rideDetails.CompletedTime.Value);
                    }
                    else if (_rideDetails.PaymentConfirmedDate.HasValue)
                    {
                        DateTimeLabel.Text = FormatPhilippineDate(_rideDetails.PaymentConfirmedDate.Value);
                    }
                    else if (!string.IsNullOrEmpty(_rideDetails.PaymentReceivedTime))
                    {
                        try
                        {
                            if (DateTime.TryParse(_rideDetails.PaymentReceivedTime, out DateTime paymentTime))
                            {
                                DateTimeLabel.Text = FormatPhilippineDate(paymentTime);
                            }
                            else
                            {
                                DateTimeLabel.Text = FormatPhilippineDate(_ride.RequestTime);
                            }
                        }
                        catch
                        {
                            DateTimeLabel.Text = FormatPhilippineDate(_ride.RequestTime);
                        }
                    }
                    else
                    {
                        DateTimeLabel.Text = FormatPhilippineDate(_ride.RequestTime);
                    }
                }
                else if (_rideDetails.Status == "Cancelled")
                {
                    if (_rideDetails.CompletedTime.HasValue)
                    {
                        DateTimeLabel.Text = FormatPhilippineDate(_rideDetails.CompletedTime.Value);
                    }
                    else
                    {
                        DateTimeLabel.Text = FormatPhilippineDate(_ride.RequestTime);
                    }
                }
                else
                {
                    DateTimeLabel.Text = FormatPhilippineDate(_ride.RequestTime);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting date: {ex.Message}");
                DateTimeLabel.Text = FormatPhilippineDate(_ride.RequestTime);
            }
        }

        private void CalculateAndSetTimes()
        {
            try
            {
                PickupTimeLabel.Text = FormatPhilippineTime(_ride.RequestTime);

                if (_rideDetails.Status == "Completed" && _rideDetails.CompletedTime.HasValue)
                {
                    DropoffTimeLabel.Text = FormatPhilippineTime(_rideDetails.CompletedTime.Value);
                }
                else
                {
                    if (!string.IsNullOrEmpty(_rideDetails.PaymentReceivedTime))
                    {
                        try
                        {
                            if (DateTime.TryParse(_rideDetails.PaymentReceivedTime, out DateTime paymentTime))
                            {
                                DropoffTimeLabel.Text = FormatPhilippineTime(paymentTime) + " ✓";
                            }
                            else
                            {
                                DropoffTimeLabel.Text = "--:--";
                            }
                        }
                        catch
                        {
                            DropoffTimeLabel.Text = "--:--";
                        }
                    }
                    else if (_rideDetails.PaymentConfirmedDate.HasValue)
                    {
                        DropoffTimeLabel.Text = FormatPhilippineTime(_rideDetails.PaymentConfirmedDate.Value) + " ✓";
                    }
                    else
                    {
                        DropoffTimeLabel.Text = "--:--";
                    }
                }

                DurationLabel.Text = _rideDetails.EstimatedTime > 0 ? $"{_rideDetails.EstimatedTime:F0} mins" : "-- mins";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating times: {ex.Message}");
                PickupTimeLabel.Text = FormatPhilippineTime(_ride.RequestTime);
                DropoffTimeLabel.Text = "--:--";
                DurationLabel.Text = _rideDetails.EstimatedTime > 0 ? $"{_rideDetails.EstimatedTime:F0} mins" : "-- mins";
            }
        }

        private void SetVehicleInfo()
        {
            VehicleTypeLabel.Text = _rideDetails.VehicleType ?? "Tricycle";

            SeatingCapacityLabel.Text = _rideDetails.VehicleType?.ToLower() switch
            {
                "tricycle" => "1-2 passengers + driver",
                "car" => "3-4 passengers",
                "motorcycle" => "1 passenger",
                "jeepney" => "10-12 passengers",
                _ => _rideDetails.SeatingCapacity ?? "1-2 passengers"
            };

            VehicleIcon.Source = "tricycle.png";
            PlateNumberLabel.Text = !string.IsNullOrEmpty(_rideDetails.VehiclePlateNumber)
                ? _rideDetails.VehiclePlateNumber
                : "No plate number";
        }

        private void SetPaymentInfo()
        {
            string paymentMethod = _rideDetails.PaymentMethod ?? "Cash";

            if (paymentMethod == "Cash")
            {
                PaymentMethodIcon.Source = "cash.png";
            }
            else if (paymentMethod == "GCash")
            {
                PaymentMethodIcon.Source = "gcash.png";
            }
            else
            {
                PaymentMethodIcon.Source = "cash.png";
            }

            PaymentMethodLabel.Text = paymentMethod;

            string paymentStatus = _rideDetails.PaymentStatus ?? "";
            string rideStatus = _rideDetails.Status ?? "";

            var (statusText, statusColor, bgColor) = GetPaymentStatusDisplay(paymentStatus, rideStatus);
            PaymentStatusLabel.Text = statusText;
            PaymentStatusLabel.TextColor = Color.FromArgb(statusColor);
            PaymentStatusFrame.BackgroundColor = Color.FromArgb(bgColor);
        }

        private (string text, string textColor, string bgColor) GetPaymentStatusDisplay(string paymentStatus, string rideStatus)
        {
            if (!string.IsNullOrEmpty(paymentStatus))
            {
                return paymentStatus.ToUpper() switch
                {
                    "PAID" => ("PAID", "#2E7D32", "#E8F5E9"),
                    "CONFIRMED" => ("CONFIRMED", "#2E7D32", "#E8F5E9"),
                    "PENDING" => ("PENDING", "#FFB300", "#FFF8E1"),
                    "WAITING TO CONFIRM" => ("WAITING", "#FFB300", "#FFF8E1"),
                    "FAILED" => ("FAILED", "#F44336", "#FFEBEE"),
                    "REFUNDED" => ("REFUNDED", "#666666", "#F5F5F5"),
                    _ => (paymentStatus.ToUpper(), "#666666", "#F5F5F5")
                };
            }

            return rideStatus == "Completed" ? ("PAID", "#2E7D32", "#E8F5E9") : ("PENDING", "#FFB300", "#FFF8E1");
        }

        private async Task LoadPassengerInfo()
        {
            try
            {
                _commuterInfo = await _firebaseConnection.GetCommuterByIdAsync(_rideDetails.CommuterId);

                if (_commuterInfo != null)
                {
                    PassengerNameLabel.Text = !string.IsNullOrEmpty(_rideDetails.CommuterName)
                        ? _rideDetails.CommuterName
                        : $"{_commuterInfo.FirstName} {_commuterInfo.LastName}".Trim();

                    PassengerMobileLabel.Text = !string.IsNullOrEmpty(_rideDetails.CommuterMobile)
                        ? _rideDetails.CommuterMobile
                        : _commuterInfo.MobileNumber ?? "N/A";

                    var passengerType = _rideDetails.PassengerType ?? "Regular";
                    PassengerTypeLabel.Text = GetPassengerTypeDisplayText(passengerType);

                    await LoadPassengerProfilePicture();
                }
                else
                {
                    PassengerNameLabel.Text = _ride.CommuterName ?? "Passenger";
                    PassengerMobileLabel.Text = _ride.CommuterMobile ?? "N/A";
                    var passengerType = _rideDetails.PassengerType ?? "Regular";
                    PassengerTypeLabel.Text = GetPassengerTypeDisplayText(passengerType);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading passenger info: {ex.Message}");
                PassengerNameLabel.Text = _ride.CommuterName ?? "Passenger";
                PassengerMobileLabel.Text = _ride.CommuterMobile ?? "N/A";
            }
        }

        private async Task LoadPassengerProfilePicture()
        {
            try
            {
                if (_commuterInfo != null && !string.IsNullOrEmpty(_commuterInfo.ProfileImageUrl))
                {
                    try
                    {
                        PassengerProfileImage.Source = ImageSource.FromUri(new Uri(_commuterInfo.ProfileImageUrl));
                    }
                    catch
                    {
                        PassengerProfileImage.Source = "profile_icon.png";
                    }
                }
                else
                {
                    PassengerProfileImage.Source = "profile_icon.png";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading passenger profile picture: {ex.Message}");
                PassengerProfileImage.Source = "profile_icon.png";
            }
        }

        private string GetPassengerTypeDisplayText(string passengerType)
        {
            return passengerType?.ToLower() switch
            {
                "student" => "Student",
                "senior" => "Senior Citizen",
                "pwd" => "PWD",
                "regular" => "Regular Passenger",
                _ => passengerType ?? "Regular Passenger"
            };
        }

        private void UpdateActionButtons(string status, string paymentStatus)
        {
            ConfirmPaymentButton.IsVisible = false;
            UpdateStatusButton.IsVisible = false;
            ShareReceiptButton.IsVisible = false;
            StartTripButton.IsVisible = false;
            ReceiptButton.IsVisible = false;

            switch (status)
            {
                case "Payment Pending":
                    ConfirmPaymentButton.IsVisible = true;
                    ConfirmPaymentButton.Text = "Confirm";
                    break;

                case "Completed":
                    ReceiptButton.IsVisible = true;
                    break;

                case "Accepted":
                    UpdateStatusButton.IsVisible = true;
                    UpdateStatusButton.Text = "Start Pickup";
                    UpdateStatusButton.BackgroundColor = Color.FromArgb("#4CAF50");
                    break;

                case "Picking Up":
                    UpdateStatusButton.IsVisible = true;
                    UpdateStatusButton.Text = "Arrived";
                    UpdateStatusButton.BackgroundColor = Color.FromArgb("#0288D1");
                    break;

                case "Arrived at Pickup":
                    UpdateStatusButton.IsVisible = true;
                    UpdateStatusButton.Text = "Start Trip";
                    UpdateStatusButton.BackgroundColor = Color.FromArgb("#F57C00");
                    break;

                case "Trip Started":
                    UpdateStatusButton.IsVisible = true;
                    UpdateStatusButton.Text = "Complete";
                    UpdateStatusButton.BackgroundColor = Color.FromArgb("#2E7D32");
                    break;
            }
        }

        private async void OnUpdateStatusButtonClicked(object sender, EventArgs e)
        {
            try
            {
                string currentStatus = _rideDetails.Status;
                string nextStatus = "";
                string confirmMessage = "";

                switch (currentStatus)
                {
                    case "Accepted":
                        nextStatus = "Picking Up";
                        confirmMessage = "Start picking up the passenger?";
                        break;
                    case "Picking Up":
                        nextStatus = "Arrived at Pickup";
                        confirmMessage = "Mark as arrived at pickup location?";
                        break;
                    case "Arrived at Pickup":
                        nextStatus = "Trip Started";
                        confirmMessage = "Start the trip now?";
                        break;
                    case "Trip Started":
                        nextStatus = "Payment Pending";
                        confirmMessage = "Complete the ride and proceed to payment?";
                        break;
                    default:
                        await ShowModal("Error", "Cannot update status from current state", "error", false);
                        return;
                }

                await ShowUpdateStatusConfirmation(nextStatus, confirmMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating status: {ex.Message}");
                await ShowModal("Error", "Failed to update status", "error", false);
            }
        }

        private async Task ShowUpdateStatusConfirmation(string nextStatus, string confirmMessage)
        {
            _isModalOpen = true;

            string iconSource = "update.png";
            Color iconColor = Color.FromArgb("#2196F3");

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
                            Text = "Update Status",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = confirmMessage,
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
                                }.WithDriverRideDetailsGridColumn(0),
                                new Button
                                {
                                    Text = "YES",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverRideDetailsGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modal = new DriverRideDetailsStatusModalPage(modalContent, blurOverlay, nextStatus, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async Task ExecuteStatusUpdate(string nextStatus)
        {
            try
            {
                bool success = await _firebaseConnection.UpdateRideStatusWithDriverActionAsync(
                    _firebaseKey, nextStatus, _driverId);

                if (success)
                {
                    await ShowModal("Success", $"Status updated to {nextStatus}", "success", false);

                    _rideDetails = await _firebaseConnection.GetRideRequestByIdAsync(_firebaseKey);
                    if (_rideDetails != null)
                    {
                        SetDateBasedOnStatus();
                        SetStatusBadge(_rideDetails.Status);
                        UpdateActionButtons(_rideDetails.Status, _rideDetails.PaymentStatus);
                        CalculateAndSetTimes();
                    }
                }
                else
                {
                    await ShowModal("Error", "Failed to update status", "error", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating status: {ex.Message}");
                await ShowModal("Error", "Failed to update status", "error", false);
            }
        }

        private async void OnConfirmPaymentButtonClicked(object sender, EventArgs e)
        {
            await ShowPaymentConfirmation();
        }

        private async Task ShowPaymentConfirmation()
        {
            _isModalOpen = true;

            string iconSource = "cash.png";
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
                            Text = "Confirm Payment",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = $"Confirm that you have received ₱{_ride.Fare:F0} from the passenger?",
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
                                    Text = "NO",
                                    BackgroundColor = Color.FromArgb("#E0E0E0"),
                                    TextColor = Color.FromArgb("#666666"),
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverRideDetailsGridColumn(0),
                                new Button
                                {
                                    Text = "YES, RECEIVED",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithDriverRideDetailsGridColumn(1)
                            }
                        }
                    }
                }
            };

            var modal = new DriverRideDetailsPaymentModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async Task ExecutePaymentConfirmation()
        {
            try
            {
                bool success = await _firebaseConnection.ConfirmPaymentWithFeedbackAsync(
                    _firebaseKey, _ride.Fare, _driverId);

                if (success)
                {
                    await ShowModal("Success", "Payment confirmed successfully!", "success", false);

                    _rideDetails = await _firebaseConnection.GetRideRequestByIdAsync(_firebaseKey);
                    if (_rideDetails != null)
                    {
                        SetDateBasedOnStatus();
                        SetStatusBadge(_rideDetails.Status);
                        SetPaymentInfo();
                        UpdateActionButtons(_rideDetails.Status, _rideDetails.PaymentStatus);
                        CalculateAndSetTimes();
                    }
                }
                else
                {
                    await ShowModal("Error", "Failed to confirm payment", "error", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error confirming payment: {ex.Message}");
                await ShowModal("Error", "Failed to confirm payment", "error", false);
            }
        }

        private async void OnComplaintButtonClicked(object sender, EventArgs e)
        {
            try
            {
                string passengerName = "Unknown Passenger";
                string passengerPhone = "";
                string rideId = _rideId;
                string rideFirebaseKey = _firebaseKey;
                string passengerUserId = _commuterId ?? "";

                if (_commuterInfo != null)
                {
                    passengerName = $"{_commuterInfo.FirstName} {_commuterInfo.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(passengerName))
                        passengerName = _commuterInfo.MobileNumber ?? "Unknown Passenger";

                    passengerPhone = _commuterInfo.MobileNumber ?? "";
                    passengerUserId = _commuterId;
                }
                else if (!string.IsNullOrEmpty(_rideDetails.CommuterName))
                {
                    passengerName = _rideDetails.CommuterName;
                    passengerPhone = _rideDetails.CommuterMobile ?? "";
                }

                await Navigation.PushModalAsync(new Driver_FileAComplaint(
                    accusedName: passengerName,
                    accusedId: passengerUserId,
                    accusedPhone: passengerPhone,
                    rideId: rideId,
                    rideFirebaseKey: rideFirebaseKey,
                    complainantType: "driver"
                ));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening complaint: {ex.Message}");
                await ShowModal("Error", "Failed to open complaint form", "error", false);
            }
        }

        private void SetStatusBadge(string status)
        {
            switch (status)
            {
                case "Completed":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E8F5E9");
                    StatusIcon.Source = "complete.png";
                    StatusLabel.Text = "Ride Completed";
                    StatusLabel.TextColor = Color.FromArgb("#2E7D32");
                    break;
                case "Cancelled":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#FFEBEE");
                    StatusIcon.Source = "cancelled.png";
                    StatusLabel.Text = "Ride Cancelled";
                    StatusLabel.TextColor = Color.FromArgb("#F44336");
                    break;
                case "Pending":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#FFF8E1");
                    StatusIcon.Source = "pending.png";
                    StatusLabel.Text = "Pending Acceptance";
                    StatusLabel.TextColor = Color.FromArgb("#FFB300");
                    break;
                case "Accepted":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E3F2FD");
                    StatusIcon.Source = "complete.png";
                    StatusLabel.Text = "Ride Accepted";
                    StatusLabel.TextColor = Color.FromArgb("#2196F3");
                    break;
                case "Picking Up":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E8F5E9");
                    StatusIcon.Source = "pickup.png";
                    StatusLabel.Text = "Picking Up Passenger";
                    StatusLabel.TextColor = Color.FromArgb("#4CAF50");
                    break;
                case "Arrived at Pickup":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E1F5FE");
                    StatusIcon.Source = "arrived.png";
                    StatusLabel.Text = "Arrived at Pickup";
                    StatusLabel.TextColor = Color.FromArgb("#0288D1");
                    break;
                case "Trip Started":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#FFF3E0");
                    StatusIcon.Source = "trip_started.png";
                    StatusLabel.Text = "Trip In Progress";
                    StatusLabel.TextColor = Color.FromArgb("#F57C00");
                    break;
                case "Payment Pending":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#FFF3E0");
                    StatusIcon.Source = "payment.png";
                    StatusLabel.Text = "Payment Pending";
                    StatusLabel.TextColor = Color.FromArgb("#F57C00");
                    break;
                default:
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#F5F5F5");
                    StatusIcon.Source = "ride_icon.png";
                    StatusLabel.Text = status?.ToUpper() ?? "UNKNOWN";
                    StatusLabel.TextColor = Color.FromArgb("#666666");
                    break;
            }

            if (StatusIcon.Source == null)
            {
                SetStatusEmojiFallback(status);
            }
        }

        private void SetStatusEmojiFallback(string status)
        {
            switch (status)
            {
                case "Completed":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Label { Text = "✓", FontSize = 26, TextColor = Color.FromArgb("#2E7D32") };
                    break;
                case "Cancelled":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Label { Text = "✕", FontSize = 26, TextColor = Color.FromArgb("#F44336") };
                    break;
                case "Pending":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Label { Text = "⏳", FontSize = 26 };
                    break;
                case "Accepted":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Label { Text = "✓", FontSize = 26, TextColor = Color.FromArgb("#2196F3") };
                    break;
                case "Picking Up":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Image { Source = "pickup.png", HeightRequest = 26, WidthRequest = 26 };
                    break;
                case "Arrived at Pickup":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Image { Source = "arrived.png", HeightRequest = 26, WidthRequest = 26 };
                    break;
                case "Trip Started":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Image { Source = "trip_started.png", HeightRequest = 26, WidthRequest = 26 };
                    break;
                case "Payment Pending":
                    StatusIcon.Source = null;
                    StatusIconFrame.Content = new Image { Source = "payment.png", HeightRequest = 26, WidthRequest = 26 };
                    break;
            }
        }

        private string FormatAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown Location";
            var cleanAddress = address.Replace(", Malolos, Bulacan", "")
                                     .Replace(", Bulacan", "")
                                     .Replace("Malolos, ", "")
                                     .Replace("Philippines", "")
                                     .Trim();
            if (cleanAddress.Length > maxLength)
                return cleanAddress.Substring(0, maxLength - 3) + "...";
            return cleanAddress;
        }

        private async void OnCopyRideIdClicked(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_rideId))
                {
                    await Clipboard.Default.SetTextAsync(_rideId);
                    await ShowModal("Copied", $"Ride ID {_rideId} copied to clipboard!", "success", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying Ride ID: {ex.Message}");
                await ShowModal("Error", "Failed to copy Ride ID", "error", false);
            }
        }

        private async void OnShareButtonClicked(object sender, EventArgs e)
        {
            try
            {
                if (_rideDetails.Status == "Completed")
                {
                    await ShareReceipt();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in share button: {ex.Message}");
                await ShowModal("Error", "Operation failed", "error", false);
            }
        }

        private async Task ShareReceipt()
        {
            try
            {
                string receiptText = CreateReceiptText();
                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Text = receiptText,
                    Title = "ServiceCo Ride Receipt"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sharing receipt: {ex.Message}");
                await ShowModal("Error", "Failed to share receipt", "error", false);
            }
        }

        private string CreateReceiptText()
        {
            var passengerType = _rideDetails?.PassengerType ?? "Regular";
            var passengerTypeDisplay = GetPassengerTypeDisplayText(passengerType);

            string driverName = AppSession.GetDriverName();
            string vehicleInfo = $"{VehicleTypeLabel.Text} • {PlateNumberLabel.Text ?? "N/A"}";

            string paymentTimeText = "";
            string displayDate = DateTimeLabel.Text;
            DateTime phRequestTime = ToPhilippineTime(_ride.RequestTime);

            if (!string.IsNullOrEmpty(_rideDetails.PaymentReceivedTime))
            {
                try
                {
                    if (DateTime.TryParse(_rideDetails.PaymentReceivedTime, out DateTime paymentTime))
                    {
                        DateTime phPaymentTime = ToPhilippineTime(paymentTime);
                        paymentTimeText = $"\nPAYMENT TIME: {phPaymentTime:hh:mm tt}";
                    }
                }
                catch { }
            }
            else if (_rideDetails.PaymentConfirmedDate.HasValue)
            {
                DateTime phConfirmedTime = ToPhilippineTime(_rideDetails.PaymentConfirmedDate.Value);
                paymentTimeText = $"\nPAYMENT TIME: {phConfirmedTime:hh:mm tt}";
            }

            return $"══════════════════════════════\n" +
                   $"     SERVICECO RIDE RECEIPT    \n" +
                   $"══════════════════════════════\n\n" +
                   $"RIDE ID: {_rideId}\n" +
                   $"STATUS: {StatusLabel.Text}\n" +
                   $"DATE: {displayDate}\n" +
                   $"BOOKED: {phRequestTime:hh:mm tt}\n" +
                   $"{paymentTimeText}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"PASSENGER: {PassengerNameLabel.Text}\n" +
                   $"CONTACT: {PassengerMobileLabel.Text}\n" +
                   $"TYPE: {passengerTypeDisplay}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"FROM: {PickupLabel.Text}\n" +
                   $"  {PickupTimeLabel.Text}\n\n" +
                   $"TO: {DropoffLabel.Text}\n" +
                   $"  {DropoffTimeLabel.Text}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"DISTANCE: {DistanceLabel.Text}\n" +
                   $"DURATION: {DurationLabel.Text}\n" +
                   $"FARE: {FareLabel.Text}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"VEHICLE: {vehicleInfo}\n" +
                   $"CAPACITY: {SeatingCapacityLabel.Text}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"PAYMENT: {PaymentMethodLabel.Text}\n" +
                   $"STATUS: {PaymentStatusLabel.Text}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"DRIVER: {driverName}\n\n" +
                   $"══════════════════════════════\n" +
                   $"  Thank you for riding with us!\n" +
                   $"  ServiceCo - Safe & Reliable Rides\n" +
                   $"══════════════════════════════";
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await this.FadeTo(0, 250, Easing.CubicIn);
            await Navigation.PopModalAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await this.FadeTo(1, 300, Easing.CubicOut);
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

            var modal = new DriverRideDetailsCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
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
    }

    public class DriverRideDetailsCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_RideDetails _parentPage;

        public DriverRideDetailsCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_RideDetails parentPage)
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

    public class DriverRideDetailsStatusModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private string _nextStatus;
        private Driver_RideDetails _parentPage;

        public DriverRideDetailsStatusModalPage(Frame modalContent, Grid blurOverlay, string nextStatus, Driver_RideDetails parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _nextStatus = nextStatus;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal(false);

            if (confirm)
            {
                await _parentPage.ExecuteStatusUpdate(_nextStatus);
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

    public class DriverRideDetailsPaymentModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_RideDetails _parentPage;

        public DriverRideDetailsPaymentModalPage(Frame modalContent, Grid blurOverlay, Driver_RideDetails parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal(false);

            if (confirm)
            {
                await _parentPage.ExecutePaymentConfirmation();
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

    public static class DriverRideDetailsGridExtensions
    {
        public static Button WithDriverRideDetailsGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}