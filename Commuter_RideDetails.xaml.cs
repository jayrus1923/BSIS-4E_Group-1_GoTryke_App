using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.Linq;

namespace ServiceCo
{
    public partial class Commuter_RideDetails : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private readonly FirebaseStorageService _firebaseStorage = new FirebaseStorageService();
        private RideRequestWithKey _ride;
        private string _rideId;
        private string _firebaseKey;
        private RideRequest _rideDetails;
        private Driver _driverInfo;
        private string _driverId;
        private bool _isModalOpen = false;

        public Commuter_RideDetails(RideRequestWithKey ride)
        {
            InitializeComponent();
            _ride = ride;
            _firebaseKey = ride.FirebaseKey;
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

        private string FormatPhilippineDateTime(DateTime? utcDateTime)
        {
            if (!utcDateTime.HasValue || utcDateTime.Value == DateTime.MinValue)
                return "--";
            DateTime phTime = ToPhilippineTime(utcDateTime.Value);
            return phTime.ToString("MMMM dd, yyyy • hh:mm tt");
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

                _driverId = _rideDetails.DriverId;
                _rideId = !string.IsNullOrEmpty(_rideDetails.Id) ? _rideDetails.Id : "TRC-UNKNOWN";
                RideIdLabel.Text = _rideId;

                DateTimeLabel.Text = FormatPhilippineDateTime(_ride.RequestTime);
                SetStatusBadge(_rideDetails.Status);
                PickupLabel.Text = FormatAddress(_ride.PickupLocation, 40);
                DropoffLabel.Text = FormatAddress(_ride.DropoffLocation, 40);
                CalculateAndSetTimes();
                FareLabel.Text = $"₱{_ride.Fare:F0}";
                DistanceLabel.Text = _rideDetails.Distance > 0 ? $"{_rideDetails.Distance:F1} km" : "-- km";
                VehicleTypeLabel.Text = _rideDetails.VehicleType ?? "Tricycle";
                SeatingCapacityLabel.Text = GetSeatingCapacity(_rideDetails.VehicleType, _rideDetails.SeatingCapacity);
                VehicleIcon.Source = "tricycle.png";
                var passengerType = _rideDetails.PassengerType ?? "Regular";
                PassengerTypeLabel.Text = GetPassengerTypeDisplayText(passengerType);
                SetPaymentInfo();
                ConfigureButtonsByStatus(_rideDetails.Status);

                if (!string.IsNullOrEmpty(_rideDetails.DriverId))
                {
                    await LoadDriverInfo();
                }
                else
                {
                    DriverCardFrame.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ride details: {ex.Message}");
                await ShowModal("Error", "Failed to load ride details", "error", true);
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
                    "FAILED" => ("FAILED", "#F44336", "#FFEBEE"),
                    "REFUNDED" => ("REFUNDED", "#666666", "#F5F5F5"),
                    _ => (paymentStatus.ToUpper(), "#666666", "#F5F5F5")
                };
            }

            return rideStatus == "Completed" ? ("PAID", "#2E7D32", "#E8F5E9") : ("PENDING", "#FFB300", "#FFF8E1");
        }

        private string GetSeatingCapacity(string vehicleType, string defaultCapacity)
        {
            return vehicleType?.ToLower() switch
            {
                "tricycle" => "👥 1-2 passengers + driver",
                "car" => "👥 3-4 passengers",
                "motorcycle" => "👤 1 passenger",
                "jeepney" => "👥 10-12 passengers",
                _ => defaultCapacity ?? "👥 1-2 passengers"
            };
        }

        private void ConfigureButtonsByStatus(string status)
        {
            RebookButton.IsVisible = false;
            ShareReceiptButton.IsVisible = false;
            CancelRideButton.IsVisible = false;
            ComplaintButton.IsVisible = false;

            switch (status)
            {
                case "Completed":
                    RebookButton.IsVisible = true;
                    ShareReceiptButton.IsVisible = true;
                    ShareReceiptButton.Text = "Receipt";
                    if (!string.IsNullOrEmpty(_driverId))
                    {
                        ComplaintButton.IsVisible = true;
                    }
                    break;
                case "Cancelled":
                    RebookButton.IsVisible = true;
                    RebookButton.Text = "↻ Book Again";
                    ShareReceiptButton.IsVisible = false;
                    CancelRideButton.IsVisible = false;
                    break;
                case "Pending":
                case "Accepted":
                case "Picking Up":
                case "Arrived at Pickup":
                case "Trip Started":
                    CancelRideButton.IsVisible = true;
                    ShareReceiptButton.IsVisible = false;
                    RebookButton.IsVisible = false;
                    break;
            }
        }

        private async Task LoadDriverInfo()
        {
            try
            {
                _driverInfo = await _firebaseConnection.GetDriverByIdAsync(_rideDetails.DriverId);

                if (_driverInfo != null)
                {
                    DriverCardFrame.IsVisible = true;

                    DriverNameLabel.Text = !string.IsNullOrEmpty(_rideDetails.DriverName)
                        ? _rideDetails.DriverName
                        : $"{_driverInfo.FirstName} {_driverInfo.LastName}".Trim();

                    DriverMobileLabel.Text = !string.IsNullOrEmpty(_rideDetails.DriverMobileNumber)
                        ? _rideDetails.DriverMobileNumber
                        : _driverInfo.MobileNumber ?? "N/A";

                    string vehicleType = !string.IsNullOrEmpty(_rideDetails.DriverVehicleType)
                        ? _rideDetails.DriverVehicleType
                        : _driverInfo.VehicleType ?? "Tricycle";

                    string plateNumber = !string.IsNullOrEmpty(_rideDetails.VehiclePlateNumber)
                        ? _rideDetails.VehiclePlateNumber
                        : _driverInfo.PlateNumber ?? "";

                    DriverVehicleLabel.Text = string.IsNullOrEmpty(plateNumber) ? vehicleType : $"{plateNumber}";

                    double rating = _rideDetails.DriverRating > 0 ? _rideDetails.DriverRating : _driverInfo.Rating > 0 ? _driverInfo.Rating : 5.0;
                    DriverRatingLabel.Text = $"{rating:F1}";

                    int totalRides = _driverInfo.TotalCompletedRides ?? 0;
                    DriverRidesLabel.Text = $"{totalRides} rides";

                    await LoadDriverProfilePicture();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading driver info: {ex.Message}");
            }
        }

        private async Task LoadDriverProfilePicture()
        {
            try
            {
                string profileImageUrl = "";

                if (!string.IsNullOrEmpty(_rideDetails.DriverProfileImageUrl))
                {
                    profileImageUrl = _rideDetails.DriverProfileImageUrl;
                }
                else if (_driverInfo != null && !string.IsNullOrEmpty(_driverInfo.ProfileImageUrl))
                {
                    profileImageUrl = _driverInfo.ProfileImageUrl;
                }

                if (!string.IsNullOrEmpty(profileImageUrl))
                {
                    try
                    {
                        DriverProfileImage.Source = ImageSource.FromUri(new Uri(profileImageUrl));
                    }
                    catch
                    {
                        DriverProfileImage.Source = "profile_icon.png";
                    }
                }
                else
                {
                    DriverProfileImage.Source = "profile_icon.png";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading driver profile picture: {ex.Message}");
                DriverProfileImage.Source = "profile_icon.png";
            }
        }

        private string GetPassengerTypeDisplayText(string passengerType)
        {
            return passengerType?.ToLower() switch
            {
                "student" => "🎓 Student",
                "senior" => "👴 Senior Citizen",
                "pwd" => "♿ PWD",
                "regular" => "👤 Regular Passenger",
                _ => passengerType ?? "👤 Regular Passenger"
            };
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
                    StatusLabel.Text = "Finding Driver...";
                    StatusLabel.TextColor = Color.FromArgb("#FFB300");
                    break;
                case "Accepted":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E3F2FD");
                    StatusIcon.Source = "accepted.png";
                    StatusLabel.Text = "Driver Found";
                    StatusLabel.TextColor = Color.FromArgb("#2196F3");
                    break;
                case "Picking Up":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E8F5E9");
                    StatusIcon.Source = "pickup.png";
                    StatusLabel.Text = "Driver On The Way";
                    StatusLabel.TextColor = Color.FromArgb("#4CAF50");
                    break;
                case "Arrived at Pickup":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#E1F5FE");
                    StatusIcon.Source = "arrived.png";
                    StatusLabel.Text = "Driver Arrived";
                    StatusLabel.TextColor = Color.FromArgb("#0288D1");
                    break;
                case "Trip Started":
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#FFF3E0");
                    StatusIcon.Source = "trip_started.png";
                    StatusLabel.Text = "Trip In Progress";
                    StatusLabel.TextColor = Color.FromArgb("#F57C00");
                    break;
                default:
                    StatusIconFrame.BackgroundColor = Color.FromArgb("#F5F5F5");
                    StatusIcon.Source = "ride_icon.png";
                    StatusLabel.Text = status?.ToUpper() ?? "UNKNOWN";
                    StatusLabel.TextColor = Color.FromArgb("#666666");
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

        private async void OnRebookButtonClicked(object sender, EventArgs e)
        {
            try
            {
                string commuterId = AppSession.GetCommuterId();
                var activeRide = await _firebaseConnection.GetActiveRideForCommuterAsync(commuterId);

                if (activeRide != null && IsActiveStatus(activeRide.Status))
                {
                    await ShowModal("Active Ride",
                        "You already have an active ride. Please complete or cancel it before booking a new one.",
                        "warning", false);
                    return;
                }

                await Navigation.PushModalAsync(new Commuter_SearchLocation(
                    _ride.PickupLocation,
                    _ride.DropoffLocation));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rebooking ride: {ex.Message}");
                await ShowModal("Error", "Failed to rebook ride", "error", false);
            }
        }

        private bool IsActiveStatus(string status)
        {
            return status == "Pending" || status == "Accepted" ||
                   status == "Picking Up" || status == "Arrived at Pickup" ||
                   status == "Trip Started";
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

        private async void OnCancelRideClicked(object sender, EventArgs e)
        {
            await ShowCancelConfirmation();
        }

        private async Task ShowCancelConfirmation()
        {
            _isModalOpen = true;

            string iconSource = "cancelled.png";
            Color iconColor = Color.FromArgb("#F44336");

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
                            Text = "Cancel Ride?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to cancel this ride?",
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
                                }.WithCommuterRideDetailsGridColumn(0),
                                new Button
                                {
                                    Text = "YES, CANCEL",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithCommuterRideDetailsGridColumn(1)
                            }
                        }
                    }
                }
            };

            var cancelModal = new CommuterRideDetailsCancelModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(cancelModal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public async Task ExecuteCancelRide()
        {
            try
            {
                bool success = await _firebaseConnection.CancelRideRequestAsync(_firebaseKey, "Commuter");

                if (success)
                {
                    await ShowModal("Cancelled", "Ride has been cancelled", "success", true);
                }
                else
                {
                    await ShowModal("Error", "Failed to cancel ride", "error", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cancelling ride: {ex.Message}");
                await ShowModal("Error", "Failed to cancel ride", "error", false);
            }
        }

        public void CloseModal()
        {
            _isModalOpen = false;
        }

        private async void OnComplaintButtonClicked(object sender, EventArgs e)
        {
            try
            {
                string driverName = "Unknown Driver";
                string plateNumber = "";
                string driverPhone = "";
                string rideId = _rideId;
                string rideFirebaseKey = _firebaseKey;
                string driverUserId = _driverId ?? "";

                if (_driverInfo != null)
                {
                    driverName = $"{_driverInfo.FirstName} {_driverInfo.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(driverName))
                        driverName = _driverInfo.MobileNumber ?? "Unknown Driver";

                    plateNumber = _driverInfo.PlateNumber ?? "";
                    driverPhone = _driverInfo.MobileNumber ?? "";
                    driverUserId = _driverId;
                }
                else if (!string.IsNullOrEmpty(_rideDetails.DriverName))
                {
                    driverName = _rideDetails.DriverName;
                    plateNumber = _rideDetails.VehiclePlateNumber ?? "";
                    driverPhone = _rideDetails.DriverMobileNumber ?? "";
                }

                await Navigation.PushModalAsync(new Commuter_FileAComplaint(
                    accusedName: driverName,
                    accusedId: driverUserId,
                    accusedPhone: driverPhone,
                    vehicleNumber: plateNumber,
                    rideId: rideId,
                    rideFirebaseKey: rideFirebaseKey,
                    complainantType: "commuter"
                ));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening complaint: {ex.Message}");
                await ShowModal("Error", "Failed to open complaint form", "error", false);
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

            string driverName = DriverNameLabel?.Text ?? "N/A";
            string vehicleInfo = DriverVehicleLabel?.Text ?? "N/A";
            string rating = DriverRatingLabel?.Text ?? "★ 5.0";

            DateTime phRequestTime = ToPhilippineTime(_ride.RequestTime);

            return $"══════════════════════════════\n" +
                   $"     SERVICECO RIDE RECEIPT    \n" +
                   $"══════════════════════════════\n\n" +
                   $"RIDE ID: {_rideId}\n" +
                   $"STATUS: {StatusLabel.Text}\n" +
                   $"DATE: {phRequestTime:MMMM dd, yyyy}\n" +
                   $"TIME: {phRequestTime:hh:mm tt}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"FROM: {PickupLabel.Text}\n" +
                   $"  ⏱️ {PickupTimeLabel.Text}\n\n" +
                   $"TO: {DropoffLabel.Text}\n" +
                   $"  ⏱️ {DropoffTimeLabel.Text}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"📏 DISTANCE: {DistanceLabel.Text}\n" +
                   $"⏱️ DURATION: {DurationLabel.Text}\n" +
                   $"💰 FARE: {FareLabel.Text}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"🛵 VEHICLE: {VehicleTypeLabel.Text}\n" +
                   $"👥 CAPACITY: {SeatingCapacityLabel.Text}\n" +
                   $"👤 PASSENGER: {passengerTypeDisplay}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"DRIVER: {driverName}\n" +
                   $"📞 {DriverMobileLabel?.Text ?? "N/A"}\n" +
                   $"🚗 {vehicleInfo}\n" +
                   $"⭐ {rating}\n\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                   $"💳 PAYMENT: {PaymentMethodLabel.Text}\n" +
                   $"✅ STATUS: {PaymentStatusLabel.Text}\n\n" +
                   $"══════════════════════════════\n" +
                   $"  Thank you for riding with us!\n" +
                   $"  ServiceCo - Safe & Reliable Rides\n" +
                   $"══════════════════════════════";
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await AnimateBackNavigation();
        }

        private async Task AnimateBackNavigation()
        {
            await this.FadeTo(0, 250, Easing.CubicIn);

            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
            else
            {
                await Navigation.PopAsync();
            }
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
                               type == "info" ? "document.png" : "sad.png";
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

            var modal = new CommuterRideDetailsCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }
    }

    public class CommuterRideDetailsCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Commuter_RideDetails _parentPage;

        public CommuterRideDetailsCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Commuter_RideDetails parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var button = (modalContent.Content as VerticalStackLayout).Children[3] as Button;
            button.Clicked += async (s, e) =>
            {
                button.IsEnabled = false;
                await AnimateModalExit();
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
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
            _parentPage.CloseModal();

            if (_closePageOnOk)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await _parentPage.Navigation.PopModalAsync();
                });
            }
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var button = (_modalContent.Content as VerticalStackLayout).Children[3] as Button;
                if (button != null)
                    button.IsEnabled = false;

                await AnimateModalExit();
            });
            return true;
        }
    }

    public class CommuterRideDetailsCancelModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_RideDetails _parentPage;

        public CommuterRideDetailsCancelModalPage(Frame modalContent, Grid blurOverlay, Commuter_RideDetails parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;

            var cancelButton = buttonGrid.Children[0] as Button;
            var exitButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(false);
            };

            exitButton.Clicked += async (s, e) =>
            {
                exitButton.IsEnabled = false;
                await AnimateModalExit(true);
            };

            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        private async Task AnimateModalExit(bool cancel)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _parentPage.CloseModal();

            if (cancel)
            {
                await _parentPage.ExecuteCancelRide();
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

    public static class CommuterRideDetailsGridExtensions
    {
        public static Button WithCommuterRideDetailsGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}