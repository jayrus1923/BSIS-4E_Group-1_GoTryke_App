using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using ServiceCo.Firebase;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using ServiceCo.Services; 
using System.Threading;
using System.Threading.Tasks;
using static ServiceCo.Firebase.FirebaseConnection;

namespace ServiceCo
{
    public partial class Commuter_SearchDriver : ContentPage
    {
        private string pickupLocation, dropoffLocation, rideType, rideID;
        private double fare;
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _rideRequestId;
        private IDisposable _statusListener;
        private IDisposable _driverLocationListener;
        private IDisposable _rideRequestListener;
        private IDisposable _cancellationTimerListener;
        private string _currentDriverId;
        private bool _isDriverFound = false;
        private bool _isCheckingStatus = false;
        private bool _isSatelliteView = false;

        private Location _currentLocation;
        private CancellationTokenSource _gpsCancellationTokenSource;
        private bool _isTrackingLocation = false;
        private Location _driverLocation;
        private bool _isTrackingDriver = false;
        private string _currentStatus = "";
        private bool _isListenerActive = true;
        private string _paymentMethod = "Cash";

        private string _driverProfileImageUrl = string.Empty;
        private Driver _currentDriver = null;

        private double _panelExpandedHeight;
        private double _panelCollapsedHeight = 80;
        private bool _isPanelExpanded = true;
        private double _lastPanelHeight;
        private bool _isDragging = false;
        private double _startY;
        private double _startHeight;

        private double _pickupLat, _pickupLng, _dropoffLat, _dropoffLng;
        private double _originalDistance = 0;
        private double _originalEstimatedTime = 0;
        private string _commuterName = "";
        private const string GOOGLE_MAPS_API_KEY = Constants.GoogleMapsApiKey;

        private bool _isMapLoaded = false;
        private bool _isInitialLoadComplete = false;
        private CancellationTokenSource _initializeCancellationTokenSource;
        private TaskCompletionSource<bool> _mapReadyTcs = new TaskCompletionSource<bool>();

        private bool _isCancellationModalShown = false;
        private CancellationTimer _cancellationTimer = null;
        private bool _isCancellationAllowed = true;
        private DateTime? _driverAcceptedTime = null;
        private bool _isModalOpen = false;
        private double _currentDriverDistance = 0;
        private double _currentDriverEta = 0;
        private bool _driverPinPlaced = false;

        public ObservableCollection<string> StatusUpdates { get; } = new ObservableCollection<string>();

        private const string PREF_LAST_STATUS = "LastRideStatus";
        private const string PREF_LAST_DRIVER_ID = "LastDriverId";
        private const string PREF_LAST_DRIVER_NAME = "LastDriverName";
        private const string PREF_LAST_DRIVER_VEHICLE = "LastDriverVehicle";
        private const string PREF_LAST_DRIVER_PLATE = "LastDriverPlate";
        private const string PREF_LAST_DRIVER_CONTACT = "LastDriverContact";
        private const string PREF_LAST_DRIVER_RATING = "LastDriverRating";
        private const string PREF_LAST_PICKUP = "LastPickup";
        private const string PREF_LAST_DROPOFF = "LastDropoff";
        private const string PREF_LAST_FARE = "LastFare";
        private const string PREF_LAST_RIDE_ID = "LastRideId";
        private const string PREF_LAST_DRIVER_LICENSE = "LastDriverLicense";
        private const string PREF_LAST_DISTANCE = "LastDistance";
        private const string PREF_LAST_TIME = "LastTime";
        private const string PREF_LAST_DRIVER_LAT = "LastDriverLat";
        private const string PREF_LAST_DRIVER_LNG = "LastDriverLng";
        private const string PREF_LAST_BUTTON_STATE = "LastButtonState";

        public Commuter_SearchDriver(string pickup, string dropoff, string rideType, double fare, string rideRequestId = null, string paymentMethod = null)
        {
            InitializeComponent();
            pickupLocation = pickup;
            dropoffLocation = dropoff;
            this.rideType = rideType;
            this.fare = fare;
            _rideRequestId = rideRequestId;
            _paymentMethod = paymentMethod ?? "Cash";
            _mapReadyTcs = new TaskCompletionSource<bool>();
            SetupUI();
        }

        private void SaveCurrentStatusToLocal()
        {
            try
            {
                Preferences.Set(PREF_LAST_STATUS, _currentStatus);
                Preferences.Set(PREF_LAST_DRIVER_ID, _currentDriverId ?? "");
                Preferences.Set(PREF_LAST_DRIVER_NAME, DriverNameLabel.Text ?? "");
                Preferences.Set(PREF_LAST_DRIVER_VEHICLE, DriverVehicleLabel.Text ?? "");
                Preferences.Set(PREF_LAST_DRIVER_PLATE, DriverPlateLabel.Text ?? "");
                Preferences.Set(PREF_LAST_DRIVER_CONTACT, DriverContactLabel.Text ?? "");
                Preferences.Set(PREF_LAST_DRIVER_RATING, DriverRatingLabel.Text ?? "5.0");
                Preferences.Set(PREF_LAST_PICKUP, pickupLocation ?? "");
                Preferences.Set(PREF_LAST_DROPOFF, dropoffLocation ?? "");
                Preferences.Set(PREF_LAST_FARE, fare);
                Preferences.Set(PREF_LAST_RIDE_ID, _rideRequestId ?? "");
                Preferences.Set(PREF_LAST_DRIVER_LICENSE, DriverLicenseLabel.Text ?? "");
                Preferences.Set(PREF_LAST_DISTANCE, DistanceLabel.Text ?? "-- km");
                Preferences.Set(PREF_LAST_TIME, TimeLabel.Text ?? "-- mins");

                if (_driverLocation != null)
                {
                    Preferences.Set(PREF_LAST_DRIVER_LAT, _driverLocation.Latitude);
                    Preferences.Set(PREF_LAST_DRIVER_LNG, _driverLocation.Longitude);
                }

                string buttonState = "searching";
                if (_isDriverFound)
                {
                    if (_currentStatus == "Accepted") buttonState = "driver_found";
                    else if (_currentStatus == "Arrived at Pickup") buttonState = "arrived";
                    else if (_currentStatus == "Trip Started") buttonState = "trip_started";
                    else if (_currentStatus == "Completed") buttonState = "completed";
                    else if (_currentStatus == "Payment Pending") buttonState = "payment_pending";
                }
                Preferences.Set(PREF_LAST_BUTTON_STATE, buttonState);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving to local: {ex.Message}");
            }
        }

        private void LoadLastStatusFromLocal()
        {
            try
            {
                string lastStatus = Preferences.Get(PREF_LAST_STATUS, "");
                string lastDriverId = Preferences.Get(PREF_LAST_DRIVER_ID, "");

                if (string.IsNullOrEmpty(lastStatus) || string.IsNullOrEmpty(lastDriverId))
                    return;

                pickupLocation = Preferences.Get(PREF_LAST_PICKUP, pickupLocation);
                dropoffLocation = Preferences.Get(PREF_LAST_DROPOFF, dropoffLocation);
                fare = Preferences.Get(PREF_LAST_FARE, fare);
                _rideRequestId = Preferences.Get(PREF_LAST_RIDE_ID, _rideRequestId);

                PickupDisplayLabel.Text = FormatAddress(pickupLocation, 20);
                DropoffDisplayLabel.Text = FormatAddress(dropoffLocation, 20);
                PickupMainLabel.Text = FormatAddress(pickupLocation, 30);
                DropoffMainLabel.Text = FormatAddress(dropoffLocation, 30);
                FareLabel.Text = $"₱{fare:F0}";

                string savedRideId = Preferences.Get(PREF_LAST_RIDE_ID, "");
                if (!string.IsNullOrEmpty(savedRideId) && savedRideId.StartsWith("TRC-"))
                {
                    RideIDLabel.Text = savedRideId;
                    rideID = savedRideId;
                }
                else
                {
                    RideIDLabel.Text = "Loading...";
                }

                DistanceLabel.Text = Preferences.Get(PREF_LAST_DISTANCE, "-- km");
                TimeLabel.Text = Preferences.Get(PREF_LAST_TIME, "-- mins");

                double savedLat = Preferences.Get(PREF_LAST_DRIVER_LAT, 0.0);
                double savedLng = Preferences.Get(PREF_LAST_DRIVER_LNG, 0.0);
                if (savedLat != 0 && savedLng != 0)
                {
                    _driverLocation = new Location(savedLat, savedLng);
                }

                if (!string.IsNullOrEmpty(lastDriverId) && lastStatus != "Pending" && lastStatus != "Searching")
                {
                    _isDriverFound = true;
                    _currentDriverId = lastDriverId;
                    _currentStatus = lastStatus;

                    DriverNameLabel.Text = Preferences.Get(PREF_LAST_DRIVER_NAME, "Driver");
                    DriverVehicleLabel.Text = Preferences.Get(PREF_LAST_DRIVER_VEHICLE, "Tricycle");
                    DriverPlateLabel.Text = Preferences.Get(PREF_LAST_DRIVER_PLATE, "Plate: --");
                    DriverContactLabel.Text = Preferences.Get(PREF_LAST_DRIVER_CONTACT, "Contact: --");
                    DriverLicenseLabel.Text = Preferences.Get(PREF_LAST_DRIVER_LICENSE, "License: --");

                    string ratingStr = Preferences.Get(PREF_LAST_DRIVER_RATING, "5.0");
                    if (double.TryParse(ratingStr, out double rating))
                    {
                        UpdateDriverRating(rating);
                    }

                    SearchingSection.IsVisible = false;
                    DriverFoundSection.IsVisible = true;

                    string buttonState = Preferences.Get(PREF_LAST_BUTTON_STATE, "");

                    switch (lastStatus)
                    {
                        case "Accepted":
                            PanelTitleLabel.Text = "Driver Found";
                            PanelSubtitleLabel.Text = "Your driver is on the way";
                            DriverFoundHeaderLabel.Text = "Driver Found";
                            DriverFoundActionButtons.IsVisible = true;
                            ArrivedActionButtons.IsVisible = false;
                            CancelBookingDriverFoundButton.IsVisible = _isCancellationAllowed;
                            CancelBookingButton.IsVisible = false;
                            break;

                        case "Arrived at Pickup":
                            PanelTitleLabel.Text = "Driver Arrived";
                            PanelSubtitleLabel.Text = "Your driver is waiting at pickup location";
                            DriverFoundHeaderLabel.Text = "Driver Arrived";
                            DriverFoundActionButtons.IsVisible = false;
                            ArrivedActionButtons.IsVisible = true;
                            CancelBookingDriverFoundButton.IsVisible = false;
                            CancelBookingButton.IsVisible = false;
                            DriverLocationLabel.Text = "Driver is at pickup location";
                            break;

                        case "Trip Started":
                            PanelTitleLabel.Text = "Trip Started";
                            PanelSubtitleLabel.Text = "Enroute to destination";
                            DriverFoundHeaderLabel.Text = "Trip Started";
                            DriverFoundActionButtons.IsVisible = false;
                            ArrivedActionButtons.IsVisible = false;
                            CancelBookingDriverFoundButton.IsVisible = false;
                            CancelBookingButton.IsVisible = false;
                            DriverLocationLabel.Text = "On the way to dropoff...";
                            break;

                        case "Completed":
                            PanelTitleLabel.Text = "Trip Completed";
                            PanelSubtitleLabel.Text = "Your ride has been completed successfully";
                            TripCompletedStatusFrame.IsVisible = true;
                            TripCompletedActionButtons.IsVisible = true;
                            DriverFoundSection.IsVisible = false;
                            break;

                        case "Payment Pending":
                            PanelTitleLabel.Text = "Payment Required";
                            PanelSubtitleLabel.Text = "Please complete payment for your ride";
                            PaymentPendingStatusFrame.IsVisible = true;
                            PaymentPendingActionButtons.IsVisible = true;
                            DriverFoundSection.IsVisible = false;
                            break;
                    }

                    TrackDriverButton.IsVisible = _isPanelExpanded &&
                        !(lastStatus == "Completed" || lastStatus == "Payment Pending");
                    TrackDriverButton.IsEnabled = true;

                    DriverLocationInfo.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading from local: {ex.Message}");
            }
        }

        private void SetupUI()
        {
            PickupDisplayLabel.Text = FormatAddress(pickupLocation, 20);
            DropoffDisplayLabel.Text = FormatAddress(dropoffLocation, 20);
            PickupMainLabel.Text = FormatAddress(pickupLocation, 30);
            DropoffMainLabel.Text = FormatAddress(dropoffLocation, 30);

            RideTypeLabel.Text = $"Tricycle ({rideType})";
            FareLabel.Text = $"₱{fare:F0}";

            RideIDLabel.Text = rideID ?? "Loading...";

            PaymentMethodLabel.Text = _paymentMethod;
            PaymentMethodIcon.Source = _paymentMethod == "GCash" ? "gcash.png" : "cash.png";

            DistanceLabel.Text = "-- km";
            TimeLabel.Text = "-- mins";

            PanelTitleLabel.Text = "Searching for Driver";
            PanelSubtitleLabel.Text = "We're matching you with the nearest available driver";

            MapBlurOverlay.IsVisible = true;
            TrackDriverButton.IsVisible = false;
            TrackDriverButton.IsEnabled = false;

            TripCompletedStatusFrame.IsVisible = false;
            TripCompletedActionButtons.IsVisible = false;
            PaymentPendingStatusFrame.IsVisible = false;
            PaymentPendingActionButtons.IsVisible = false;

            DriverProfileImage.Source = "profile_icon.png";

            SearchingActionButtons.IsVisible = true;
            DriverFoundActionButtons.IsVisible = false;
            ArrivedActionButtons.IsVisible = false;
            TripCompletedActionButtons.IsVisible = false;
            PaymentPendingActionButtons.IsVisible = false;
            CancelBookingDriverFoundButton.IsVisible = false;

            CancelBookingButton.IsEnabled = false;
            AddNoteButton.IsEnabled = false;
            CancelBookingButton.Opacity = 0.5;
            AddNoteButton.Opacity = 0.5;
        }

        private string FormatAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";
            var cleanAddress = address.Replace(", Malolos, Bulacan", "")
                                     .Replace(", Bulacan", "")
                                     .Replace("Malolos, ", "")
                                     .Trim();
            if (cleanAddress.Length > maxLength)
                return cleanAddress.Substring(0, maxLength - 3) + "...";
            return cleanAddress;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _isListenerActive = true;
            _isCancellationModalShown = false;
            _isModalOpen = false;
            _driverPinPlaced = false;

            _initializeCancellationTokenSource?.Cancel();
            _initializeCancellationTokenSource = new CancellationTokenSource();

            try
            {
                StatusUpdates.Clear();
                LoadLastStatusFromLocal();
                await LoadOrCreateCancellationTimer();
                await InitializeEverythingAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnAppearing: {ex.Message}");
            }
        }

        private async Task LoadOrCreateCancellationTimer()
        {
            try
            {
                _cancellationTimer = await _firebaseConnection.GetCancellationTimerAsync(_rideRequestId);

                if (_cancellationTimer == null)
                {
                    var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
                    if (rideRequest != null)
                    {
                        await _firebaseConnection.SaveCancellationTimerOnRequestAsync(_rideRequestId, rideRequest.CommuterId);
                        _cancellationTimer = await _firebaseConnection.GetCancellationTimerAsync(_rideRequestId);
                    }
                }

                if (_cancellationTimer != null)
                {
                    if (_cancellationTimer.DriverAcceptedTime.HasValue)
                    {
                        _driverAcceptedTime = _cancellationTimer.DriverAcceptedTime.Value;
                    }

                    _isCancellationAllowed = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);
                }

                StartCancellationTimerListener();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading cancellation timer: {ex.Message}");
                _isCancellationAllowed = true;
            }
        }

        private void StartCancellationTimerListener()
        {
            try
            {
                _cancellationTimerListener?.Dispose();

                _cancellationTimerListener = _firebaseConnection.ListenForCancellationTimer(_rideRequestId, async (timer) =>
                {
                    if (!_isListenerActive || timer == null) return;

                    _cancellationTimer = timer;

                    if (timer.DriverAcceptedTime.HasValue)
                    {
                        _driverAcceptedTime = timer.DriverAcceptedTime.Value;
                    }

                    bool newAllowedStatus = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (_isCancellationAllowed != newAllowedStatus)
                        {
                            _isCancellationAllowed = newAllowedStatus;
                            UpdateCancellationButtonVisibility();
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting timer listener: {ex.Message}");
            }
        }

        private void UpdateCancellationButtonVisibility()
        {
            try
            {
                if (!_isDriverFound)
                {
                    CancelBookingButton.IsVisible = true;
                    CancelBookingDriverFoundButton.IsVisible = false;
                }
                else if (_currentStatus == "Accepted")
                {
                    CancelBookingButton.IsVisible = false;

                    _ = Task.Run(async () =>
                    {
                        var currentAllowed = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            CancelBookingDriverFoundButton.IsVisible = currentAllowed;
                        });
                    });
                }
                else
                {
                    CancelBookingButton.IsVisible = false;
                    CancelBookingDriverFoundButton.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating cancellation button: {ex.Message}");
            }
        }

        private void EnableButtonsAfterSetup()
        {
            CancelBookingButton.IsEnabled = true;
            AddNoteButton.IsEnabled = true;
            CancelBookingButton.Opacity = 1;
            AddNoteButton.Opacity = 1;
        }

        private async Task InitializeEverythingAsync()
        {
            try
            {
                await LoadRideIdFromFirebase();

                var routeTask = LoadRouteAndMap();

                StartRealTimeRideListener();
                StartFastStatusCheck();

                await StartRealTimeLocationTracking();

                await routeTask;

                _isInitialLoadComplete = true;

                if (!_isDriverFound)
                {
                    _isCancellationAllowed = true;
                    UpdateCancellationButtonVisibility();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InitializeEverythingAsync: {ex.Message}");
            }
        }

        private async Task LoadRideIdFromFirebase()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RideIDLabel.Text = "Loading...";
                });

                string firebaseUrl = $"https://serviceco-37c60-default-rtdb.firebaseio.com/RideRequests/{_rideRequestId}.json";

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = await client.GetStringAsync(firebaseUrl);

                    if (!string.IsNullOrEmpty(response) && response != "null")
                    {
                        var jsonDoc = JsonDocument.Parse(response);
                        var root = jsonDoc.RootElement;

                        string[] possibleFieldNames = { "Id", "ID", "id", "rideId", "rideID", "RideId", "RideID" };

                        foreach (var fieldName in possibleFieldNames)
                        {
                            if (root.TryGetProperty(fieldName, out var idElement))
                            {
                                string foundId = idElement.ToString();
                                if (!string.IsNullOrEmpty(foundId) && foundId.StartsWith("TRC-"))
                                {
                                    rideID = foundId.Trim('"');
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        RideIDLabel.Text = rideID;
                                    });
                                    return;
                                }
                            }
                        }

                        rideID = _rideRequestId;
                    }
                    else
                    {
                        rideID = $"TRC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
                    }
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RideIDLabel.Text = rideID;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR LOADING RIDE ID: {ex.Message}");
                rideID = $"TRC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RideIDLabel.Text = rideID;
                });
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            _isListenerActive = false;
            _initializeCancellationTokenSource?.Cancel();
            StopAllTracking();

            _statusListener?.Dispose();
            _driverLocationListener?.Dispose();
            _rideRequestListener?.Dispose();
            _cancellationTimerListener?.Dispose();
            _isCheckingStatus = false;
        }

        private void StopAllTracking()
        {
            _isTrackingLocation = false;
            _gpsCancellationTokenSource?.Cancel();
            _isTrackingDriver = false;
        }

        private async Task LoadRouteAndMap()
        {
            try
            {
                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);

                if (rideRequest != null)
                {
                    if (!string.IsNullOrEmpty(rideRequest.Id) && rideRequest.Id.StartsWith("TRC-"))
                    {
                        rideID = rideRequest.Id;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            RideIDLabel.Text = rideID;
                        });
                    }

                    _pickupLat = rideRequest.PickupLat;
                    _pickupLng = rideRequest.PickupLng;
                    _dropoffLat = rideRequest.DropoffLat;
                    _dropoffLng = rideRequest.DropoffLng;
                    _originalDistance = rideRequest.Distance;
                    _originalEstimatedTime = rideRequest.EstimatedTime;
                    _commuterName = rideRequest.CommuterName;

                    UpdateRouteInfo(_originalDistance, _originalEstimatedTime);

                    if (rideRequest.Fare > 0 && rideRequest.Fare != fare)
                    {
                        fare = rideRequest.Fare;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            FareLabel.Text = $"₱{fare:F0}";
                        });
                    }

                    await LoadMap((_pickupLat, _pickupLng), (_dropoffLat, _dropoffLng));

                    _ = StartMapCountdown();
                    _ = StartDriverSearchAnimation();

                    if (!string.IsNullOrEmpty(rideRequest.DriverId) && rideRequest.Status != "Pending")
                    {
                        _currentStatus = rideRequest.Status;
                        await HandleInitialStatus(rideRequest);
                    }
                }
                else
                {
                    rideID = $"TRC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        RideIDLabel.Text = rideID;
                    });

                    var pickupCoord = await GetCoordinatesAsync(pickupLocation);
                    var dropoffCoord = await GetCoordinatesAsync(dropoffLocation);
                    _pickupLat = pickupCoord.Item1; _pickupLng = pickupCoord.Item2;
                    _dropoffLat = dropoffCoord.Item1; _dropoffLng = dropoffCoord.Item2;

                    var routeInfo = await GetRouteInfoAsync(pickupCoord, dropoffCoord);
                    _originalDistance = routeInfo.distanceKm;
                    _originalEstimatedTime = routeInfo.durationMinutes;

                    UpdateRouteInfo(_originalDistance, _originalEstimatedTime);
                    await LoadMap(pickupCoord, dropoffCoord);

                    _ = StartMapCountdown();
                    _ = StartDriverSearchAnimation();
                }

                MapBlurOverlay.IsVisible = false;
                EnableButtonsAfterSetup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in LoadRouteAndMap: {ex.Message}");
                rideID = $"TRC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RideIDLabel.Text = rideID;
                    MapBlurOverlay.IsVisible = false;
                    EnableButtonsAfterSetup();
                });
            }
        }

        private async Task HandleInitialStatus(RideRequest rideRequest)
        {
            if (rideRequest.Status == "Accepted" ||
                rideRequest.Status == "Arrived at Pickup" ||
                rideRequest.Status == "Trip Started")
            {
                await ShowDriverFoundUI(rideRequest.DriverId, rideRequest.Status);
            }
            else if (rideRequest.Status == "Completed")
            {
                await ShowTripCompletedUI();
            }
            else if (rideRequest.Status == "Payment Pending")
            {
                await ShowPaymentPendingUI();
            }
            else if (rideRequest.Status == "Cancelled")
            {
                await ShowCancellationModalDirectly(rideRequest.CancelledBy);
            }
        }

        private async Task<(double, double)> GetCoordinatesAsync(string place)
        {
            if (place.Equals("Current Location", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var location = await Geolocation.GetLastKnownLocationAsync() ??
                                  await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(3)));
                    if (location != null)
                        return (location.Latitude, location.Longitude);
                }
                catch { }
                return (14.8433, 120.8105);
            }

            try
            {
                var locations = await Geocoding.Default.GetLocationsAsync(place + ", Malolos, Bulacan");
                var location = locations?.FirstOrDefault();
                if (location != null) return (location.Latitude, location.Longitude);
            }
            catch { }
            return (14.8433, 120.8105);
        }

        private async Task<(double distanceKm, double durationMinutes)> GetRouteInfoAsync((double, double) pickupCoord, (double, double) dropoffCoord)
        {
            try
            {
                string url = $"https://maps.googleapis.com/maps/api/directions/json?origin={pickupCoord.Item1},{pickupCoord.Item2}&destination={dropoffCoord.Item1},{dropoffCoord.Item2}&key={GOOGLE_MAPS_API_KEY}";

                using var client = new HttpClient();
                var response = await client.GetStringAsync(url);
                var jsonDoc = JsonDocument.Parse(response);
                var routes = jsonDoc.RootElement.GetProperty("routes");

                if (routes.GetArrayLength() > 0)
                {
                    var legs = routes[0].GetProperty("legs");
                    if (legs.GetArrayLength() > 0)
                    {
                        var leg = legs[0];
                        double distanceMeters = leg.GetProperty("distance").GetProperty("value").GetDouble();
                        double durationSeconds = leg.GetProperty("duration").GetProperty("value").GetDouble();

                        double distanceKm = Math.Round(distanceMeters / 1000.0, 1);
                        double durationMinutes = Math.Ceiling(durationSeconds / 60.0);

                        return (distanceKm, durationMinutes);
                    }
                }
            }
            catch { }

            var dist = CalculateDistance(pickupCoord.Item1, pickupCoord.Item2, dropoffCoord.Item1, dropoffCoord.Item2);
            double fallbackDistance = Math.Round(dist, 1);
            double fallbackTime = Math.Ceiling(dist * 3 + 5);
            return (fallbackDistance, fallbackTime);
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private void UpdateRouteInfo(double distance, double duration)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DistanceLabel.Text = $"{distance:F1} km";
                TimeLabel.Text = $"{duration} mins";
                FareLabel.Text = $"₱{fare:F0}";

                SaveCurrentStatusToLocal();
            });
        }

        private async Task LoadMap((double, double) pickupCoord, (double, double) dropoffCoord)
        {
            try
            {
                if (App.HasDriverLocation && !string.IsNullOrEmpty(App.LastDriverId) && App.LastDriverId == _currentDriverId && _driverLocation == null)
                {
                    _driverLocation = new Location(App.SavedDriverLat, App.SavedDriverLng);
                    _currentDriverDistance = App.SavedDriverDistance;
                    _currentDriverEta = App.SavedDriverEta;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (_currentDriverDistance > 0)
                        {
                            DriverLocationLabel.Text = $"Driver is {_currentDriverDistance:F1} km away • ETA: {_currentDriverEta} min";
                        }
                    });
                }

                var html = GenerateGoogleMapHtml(pickupCoord, dropoffCoord);
                RideMapView.Source = new HtmlWebViewSource { Html = html };

                await Task.Delay(3000);
                _isMapLoaded = true;
                _mapReadyTcs.TrySetResult(true);

                if (_driverLocation != null && _isDriverFound)
                {
                    await PlaceDriverPinOnMap();
                }

                await UpdateLocationOnMap();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading map: {ex.Message}");
                _mapReadyTcs.TrySetResult(false);
            }
        }

        private async Task PlaceDriverPinOnMap()
        {
            if (_driverLocation == null || !_isMapLoaded) return;

            try
            {
                double lat = _driverLocation.Latitude;
                double lng = _driverLocation.Longitude;
                string latStr = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string lngStr = lng.ToString(System.Globalization.CultureInfo.InvariantCulture);

                string jsCode = $@"
                (function() {{
                    if (typeof map !== 'undefined' && map) {{
                        if (window.driverMarker && window.driverMarker.setMap) {{
                            window.driverMarker.setMap(null);
                        }}
                        
                        window.driverMarker = new google.maps.Marker({{
                            position: {{ lat: {latStr}, lng: {lngStr} }},
                            map: map,
                            icon: {{
                                url: 'https://maps.google.com/mapfiles/ms/icons/orange-dot.png',
                                scaledSize: new google.maps.Size(48, 48)
                            }},
                            title: 'Driver Location'
                        }});
                        
                        console.log('Driver pin placed at: {latStr}, {lngStr}');
                        return 'SUCCESS';
                    }}
                    return 'FAIL';
                }})();";

                await RideMapView.EvaluateJavaScriptAsync(jsCode);
                _driverPinPlaced = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error placing driver pin: {ex.Message}");
            }
        }

        private async Task StartRealTimeLocationTracking()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status == PermissionStatus.Granted)
                {
                    _isTrackingLocation = true;
                    _gpsCancellationTokenSource = new CancellationTokenSource();

                    _ = Task.Run(async () =>
                    {
                        while (_isTrackingLocation && !_gpsCancellationTokenSource.Token.IsCancellationRequested && _isListenerActive)
                        {
                            try
                            {
                                await UpdateLocationOnMap();
                                await Task.Delay(5000);
                            }
                            catch { await Task.Delay(5000); }
                        }
                    });
                }
            }
            catch { }
        }

        private async Task UpdateLocationOnMap()
        {
            try
            {
                var location = await Geolocation.GetLastKnownLocationAsync() ??
                              await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(2)));
                if (location != null)
                {
                    _currentLocation = location;
                    string jsCode = $@"
                    if (window.updateLiveLocation) {{
                        window.updateLiveLocation({location.Latitude}, {location.Longitude});
                    }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                }
            }
            catch { }
        }

        private async Task StartMapCountdown()
        {
            for (int i = 5; i >= 0; i--)
            {
                if (!_isListenerActive) return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (MapCountdownLabel != null)
                    {
                        if (i > 0)
                        {
                            MapCountdownLabel.Text = $"Ready in {i} seconds...";
                        }
                        else
                        {
                            MapCountdownLabel.Text = "Route Found!";
                            MapCountdownLabel.TextColor = Color.FromArgb("#2E7D32");
                        }
                    }
                });

                await Task.Delay(1000);
            }

            await Task.Delay(500);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MapBlurOverlay.IsVisible = false;
            });
        }

        private async Task StartDriverSearchAnimation()
        {
            string[] searchMessages = {
                "Scanning nearby drivers...",
                "Checking driver availability...",
                "Matching you with the best driver...",
                "Almost there..."
            };

            for (int i = 0; i < searchMessages.Length; i++)
            {
                if (!_isListenerActive || _isDriverFound) return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SearchingStatusLabel.Text = searchMessages[i];
                });

                await Task.Delay(2000);
            }

            if (!_isDriverFound && _isListenerActive)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SearchingStatusLabel.Text = "Waiting for driver to accept...";
                });
            }
        }

        private void StartRealTimeRideListener()
        {
            try
            {
                _rideRequestListener?.Dispose();

                _rideRequestListener = _firebaseConnection.ListenForRideStatusChangeRealTime(_rideRequestId, async (rideRequest) =>
                {
                    if (!_isListenerActive || rideRequest == null) return;

                    _currentStatus = rideRequest.Status;

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            MapBlurOverlay.IsVisible = false;
                            EnableButtonsAfterSetup();

                            switch (rideRequest.Status)
                            {
                                case "Pending":
                                case "Searching":
                                    ShowSearchingUI();
                                    break;

                                case "Accepted":
                                    await HandleDriverAccepted(rideRequest);
                                    break;

                                case "Arrived at Pickup":
                                    await HandleDriverArrived(rideRequest);
                                    break;

                                case "Trip Started":
                                    await HandleTripStarted(rideRequest);
                                    break;

                                case "Completed":
                                    await HandleTripCompleted(rideRequest);
                                    break;

                                case "Payment Pending":
                                    await HandlePaymentPending(rideRequest);
                                    break;

                                case "Cancelled":
                                    await HandleRideCancelled(rideRequest);
                                    break;
                            }

                            SaveCurrentStatusToLocal();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error in status handler: {ex.Message}");
                        }
                    });
                });

                _ = CheckInitialStatusOnLoad();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting real-time ride listener: {ex.Message}");
            }
        }

        private async Task CheckInitialStatusOnLoad()
        {
            try
            {
                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
                if (rideRequest != null && !string.IsNullOrEmpty(rideRequest.Status))
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        MapBlurOverlay.IsVisible = false;
                        EnableButtonsAfterSetup();

                        switch (rideRequest.Status)
                        {
                            case "Accepted":
                                await HandleDriverAccepted(rideRequest);
                                break;
                            case "Arrived at Pickup":
                                await HandleDriverArrived(rideRequest);
                                break;
                            case "Trip Started":
                                await HandleTripStarted(rideRequest);
                                break;
                            case "Completed":
                                await HandleTripCompleted(rideRequest);
                                break;
                            case "Payment Pending":
                                await HandlePaymentPending(rideRequest);
                                break;
                            case "Cancelled":
                                await HandleRideCancelled(rideRequest);
                                break;
                            default:
                                ShowSearchingUI();
                                break;
                        }
                    });
                }
                else
                {
                    ShowSearchingUI();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking initial status: {ex.Message}");
                ShowSearchingUI();
            }
        }

        private void ShowSearchingUI()
        {
            SearchingSection.IsVisible = true;
            DriverFoundSection.IsVisible = false;
            SearchingActionButtons.IsVisible = true;
            DriverFoundActionButtons.IsVisible = false;
            ArrivedActionButtons.IsVisible = false;
            TripCompletedActionButtons.IsVisible = false;
            PaymentPendingActionButtons.IsVisible = false;
            CancelBookingButton.IsVisible = true;
            CancelBookingDriverFoundButton.IsVisible = false;

            PanelTitleLabel.Text = "Searching for Driver";
            PanelSubtitleLabel.Text = "We're matching you with the nearest available driver";
        }

        private async Task HandleDriverAccepted(RideRequest rideRequest)
        {
            _isDriverFound = true;
            _currentDriverId = rideRequest.DriverId;

            SearchingSection.IsVisible = false;
            DriverFoundSection.IsVisible = true;

            PanelTitleLabel.Text = "Driver Found";
            PanelSubtitleLabel.Text = "Your driver is on the way";
            DriverFoundHeaderLabel.Text = "Driver Found";

            if (!string.IsNullOrEmpty(rideRequest.DriverName))
                DriverNameLabel.Text = rideRequest.DriverName;

            DriverVehicleLabel.Text = rideRequest.DriverVehicleType ?? "Tricycle";
            DriverPlateLabel.Text = $"Plate: {rideRequest.VehiclePlateNumber ?? "--"}";
            DriverLicenseLabel.Text = $"License: {rideRequest.DriverLicenseNumber ?? "--"}";
            DriverContactLabel.Text = $"Contact: {rideRequest.DriverMobileNumber ?? "--"}";
            UpdateDriverRating(rideRequest.DriverRating);

            SearchingActionButtons.IsVisible = false;
            DriverFoundActionButtons.IsVisible = true;
            ArrivedActionButtons.IsVisible = false;

            _isCancellationAllowed = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);
            UpdateCancellationButtonVisibility();

            TrackDriverButton.IsVisible = true;
            TrackDriverButton.IsEnabled = true;

            UpdateStatusDisplay("Accepted");
            StartTrackingDriverLocation(rideRequest.DriverId);

            var driver = await _firebaseConnection.GetDriverByIdAsync(rideRequest.DriverId);
            if (driver != null)
            {
                await LoadDriverProfilePicture(driver);
            }

            SaveCurrentStatusToLocal();
            await GetDriverLocationFromFirebase();
        }

        private async Task HandleDriverArrived(RideRequest rideRequest)
        {
            _isDriverFound = true;
            _currentDriverId = rideRequest.DriverId;

            SearchingSection.IsVisible = false;
            DriverFoundSection.IsVisible = true;

            PanelTitleLabel.Text = "Driver Arrived";
            PanelSubtitleLabel.Text = "Your driver is waiting at pickup location";
            DriverFoundHeaderLabel.Text = "Driver Arrived";

            if (!string.IsNullOrEmpty(rideRequest.DriverName))
                DriverNameLabel.Text = rideRequest.DriverName;

            DriverVehicleLabel.Text = rideRequest.DriverVehicleType ?? "Tricycle";
            DriverPlateLabel.Text = $"Plate: {rideRequest.VehiclePlateNumber ?? "--"}";
            DriverLicenseLabel.Text = $"License: {rideRequest.DriverLicenseNumber ?? "--"}";
            DriverContactLabel.Text = $"Contact: {rideRequest.DriverMobileNumber ?? "--"}";
            UpdateDriverRating(rideRequest.DriverRating);

            SearchingActionButtons.IsVisible = false;
            DriverFoundActionButtons.IsVisible = false;
            ArrivedActionButtons.IsVisible = true;
            CancelBookingDriverFoundButton.IsVisible = false;
            CancelBookingButton.IsVisible = false;

            TrackDriverButton.IsVisible = true;
            TrackDriverButton.IsEnabled = true;

            UpdateStatusDisplay("Arrived at Pickup");
            DriverLocationLabel.Text = "Driver is at pickup location";

            var driver = await _firebaseConnection.GetDriverByIdAsync(rideRequest.DriverId);
            if (driver != null)
            {
                await LoadDriverProfilePicture(driver);
            }

            SaveCurrentStatusToLocal();
            await GetDriverLocationFromFirebase();
        }

        private async Task HandleTripStarted(RideRequest rideRequest)
        {
            _isDriverFound = true;
            _currentDriverId = rideRequest.DriverId;

            SearchingSection.IsVisible = false;
            DriverFoundSection.IsVisible = true;

            PanelTitleLabel.Text = "Trip Started";
            PanelSubtitleLabel.Text = "Enroute to destination";
            DriverFoundHeaderLabel.Text = "Trip Started";

            if (!string.IsNullOrEmpty(rideRequest.DriverName))
                DriverNameLabel.Text = rideRequest.DriverName;

            SearchingActionButtons.IsVisible = false;
            DriverFoundActionButtons.IsVisible = false;
            ArrivedActionButtons.IsVisible = false;
            CancelBookingDriverFoundButton.IsVisible = false;
            CancelBookingButton.IsVisible = false;

            TrackDriverButton.IsVisible = true;
            TrackDriverButton.IsEnabled = true;

            UpdateStatusDisplay("Trip Started");
            DriverLocationLabel.Text = "On the way to dropoff...";

            var driver = await _firebaseConnection.GetDriverByIdAsync(rideRequest.DriverId);
            if (driver != null)
            {
                await LoadDriverProfilePicture(driver);
            }

            SaveCurrentStatusToLocal();
            await GetDriverLocationFromFirebase();
        }

        private async Task HandleTripCompleted(RideRequest rideRequest)
        {
            PanelTitleLabel.Text = "Trip Completed";
            PanelSubtitleLabel.Text = "Your ride has been completed successfully";

            TripCompletedStatusFrame.IsVisible = true;
            TripCompletedStatusLabel.Text = "Trip completed! Please proceed to payment";

            SearchingActionButtons.IsVisible = false;
            DriverFoundActionButtons.IsVisible = false;
            ArrivedActionButtons.IsVisible = false;
            CancelBookingDriverFoundButton.IsVisible = false;
            CancelBookingButton.IsVisible = false;
            TripCompletedActionButtons.IsVisible = true;

            TrackDriverButton.IsVisible = false;
            TrackDriverButton.IsEnabled = false;

            SaveCurrentStatusToLocal();
        }

        private async Task HandlePaymentPending(RideRequest rideRequest)
        {
            PanelTitleLabel.Text = "Payment Required";
            PanelSubtitleLabel.Text = "Please complete payment for your ride";

            PaymentPendingStatusFrame.IsVisible = true;
            PaymentPendingActionButtons.IsVisible = true;

            SearchingActionButtons.IsVisible = false;
            DriverFoundActionButtons.IsVisible = false;
            ArrivedActionButtons.IsVisible = false;
            TripCompletedActionButtons.IsVisible = false;
            CancelBookingDriverFoundButton.IsVisible = false;
            CancelBookingButton.IsVisible = false;

            TrackDriverButton.IsVisible = false;
            TrackDriverButton.IsEnabled = false;

            SaveCurrentStatusToLocal();
        }

        private async Task HandleRideCancelled(RideRequest rideRequest)
        {
            await ShowCancellationModalDirectly(rideRequest.CancelledBy);
        }

        private async Task ShowCancellationModalDirectly(string cancelledBy)
        {
            if (_isCancellationModalShown || _isModalOpen) return;

            _isCancellationModalShown = true;
            _isModalOpen = true;

            string iconSource = "complaint.png";
            Color iconColor = Color.FromArgb("#DC3545");
            string message = cancelledBy == "Commuter" ? "You cancelled the ride." : "The driver cancelled the ride.";

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var closeButton = new Button
            {
                Text = "OK",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
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
                            Text = "Ride Cancelled",
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
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        closeButton
                    }
                }
            };

            closeButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, true);
                await GoToCommuterShell();
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private void StartFastStatusCheck()
        {
            if (_isCheckingStatus) return;

            _isCheckingStatus = true;

            _ = Task.Run(async () =>
            {
                while (_isCheckingStatus && _isListenerActive)
                {
                    try
                    {
                        await Task.Delay(500);

                        if (!_isListenerActive) break;

                        var currentRide = await _firebaseConnection.GetRideStatusFastAsync(_rideRequestId);
                        if (currentRide != null && currentRide.Status != _currentStatus)
                        {
                            _currentStatus = currentRide.Status;

                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                await CheckAndUpdateCurrentStatus(currentRide);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Fast check error: {ex.Message}");
                        await Task.Delay(1000);
                    }
                }
            });
        }

        private async Task CheckAndUpdateCurrentStatus(RideRequest rideRequest)
        {
            if (rideRequest == null) return;

            switch (rideRequest.Status)
            {
                case "Accepted":
                    if (!string.IsNullOrEmpty(rideRequest.DriverId) && !_isDriverFound)
                    {
                        await ShowDriverFoundUI(rideRequest.DriverId, "Accepted");
                    }
                    else if (_isDriverFound && rideRequest.Status == "Accepted")
                    {
                        UpdateStatusDisplay("Accepted");
                    }
                    break;

                case "Arrived at Pickup":
                    if (!string.IsNullOrEmpty(rideRequest.DriverId))
                    {
                        if (!_isDriverFound)
                        {
                            await ShowDriverFoundUI(rideRequest.DriverId, "Arrived at Pickup");
                        }
                        await UpdateStatusUI("Arrived at Pickup", rideRequest.DriverId);
                    }
                    break;

                case "Trip Started":
                    if (!string.IsNullOrEmpty(rideRequest.DriverId))
                    {
                        if (!_isDriverFound)
                        {
                            await ShowDriverFoundUI(rideRequest.DriverId, "Trip Started");
                        }
                        await UpdateStatusUI("Trip Started", rideRequest.DriverId);
                    }
                    break;

                case "Completed":
                    await ShowTripCompletedUI();
                    break;

                case "Payment Pending":
                    await ShowPaymentPendingUI();
                    break;

                case "Cancelled":
                    await ShowCancellationModalDirectly(rideRequest.CancelledBy);
                    break;
            }
        }

        private async Task ShowDriverFoundUI(string driverId, string status)
        {
            if (_isDriverFound && status == "Accepted") return;

            _isDriverFound = true;
            _currentDriverId = driverId;
            _currentStatus = status;

            var driverTask = _firebaseConnection.GetDriverByIdAsync(driverId);
            var rideRequestTask = _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);

            await Task.WhenAll(driverTask, rideRequestTask);

            var driver = driverTask.Result;
            var rideRequest = rideRequestTask.Result;

            if (driver != null && rideRequest != null)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        SearchingSection.IsVisible = false;
                        DriverFoundSection.IsVisible = true;

                        switch (status)
                        {
                            case "Arrived at Pickup":
                                PanelTitleLabel.Text = "Driver Arrived";
                                PanelSubtitleLabel.Text = "Your driver is waiting at pickup location";
                                DriverFoundHeaderLabel.Text = "Driver Arrived";
                                break;
                            case "Trip Started":
                                PanelTitleLabel.Text = "Trip Started";
                                PanelSubtitleLabel.Text = "Enroute to destination";
                                DriverFoundHeaderLabel.Text = "Trip Started";
                                break;
                            default:
                                PanelTitleLabel.Text = "Driver Found";
                                PanelSubtitleLabel.Text = "Your driver is on the way";
                                DriverFoundHeaderLabel.Text = "Driver Found";
                                break;
                        }

                        string driverDisplayName = !string.IsNullOrEmpty(rideRequest.DriverName)
                            ? rideRequest.DriverName
                            : $"{driver.FirstName} {driver.LastName}";

                        string vehicleInfo = !string.IsNullOrEmpty(rideRequest.DriverVehicleType)
                            ? rideRequest.DriverVehicleType
                            : driver.VehicleType;

                        string plateNumber = !string.IsNullOrEmpty(rideRequest.VehiclePlateNumber)
                            ? rideRequest.VehiclePlateNumber
                            : driver.PlateNumber;

                        string licenseNumber = !string.IsNullOrEmpty(rideRequest.DriverLicenseNumber)
                            ? rideRequest.DriverLicenseNumber
                            : driver.LicenseNumber;

                        string mobileNumber = !string.IsNullOrEmpty(rideRequest.DriverMobileNumber)
                            ? rideRequest.DriverMobileNumber
                            : driver.MobileNumber;

                        double driverRating = 5.0;
                        if (driver.Rating > 0) driverRating = driver.Rating;
                        else if (rideRequest.DriverRating > 0) driverRating = rideRequest.DriverRating;

                        DriverNameLabel.Text = driverDisplayName;
                        DriverVehicleLabel.Text = vehicleInfo;
                        UpdateDriverRating(driverRating);
                        DriverPlateLabel.Text = $"Plate: {plateNumber ?? "--"}";
                        DriverLicenseLabel.Text = $"License: {licenseNumber ?? "--"}";
                        DriverContactLabel.Text = $"Contact: {mobileNumber ?? "--"}";

                        await LoadDriverProfilePicture(driver);
                        UpdateStatusDisplay(status);

                        DriverLocationInfo.IsVisible = true;

                        if (status == "Arrived at Pickup")
                        {
                            DriverLocationLabel.Text = "Driver is at pickup location";
                        }
                        else if (status == "Trip Started")
                        {
                            DriverLocationLabel.Text = "On the way to dropoff...";
                        }
                        else
                        {
                            DriverLocationLabel.Text = "Getting driver location...";
                        }

                        SearchingActionButtons.IsVisible = false;

                        if (status == "Arrived at Pickup" || status == "Trip Started")
                        {
                            DriverFoundActionButtons.IsVisible = false;
                            ArrivedActionButtons.IsVisible = true;
                            CancelBookingDriverFoundButton.IsVisible = false;
                            CancelBookingButton.IsVisible = false;

                            _isCancellationAllowed = false;

                            UpdateCancellationButtonVisibility();
                        }
                        else if (status == "Accepted")
                        {
                            DriverFoundActionButtons.IsVisible = true;
                            ArrivedActionButtons.IsVisible = false;

                            if (!string.IsNullOrEmpty(driverId))
                            {
                                await _firebaseConnection.UpdateCancellationTimerOnAcceptAsync(_rideRequestId, driverId);
                                await Task.Delay(1000);
                            }

                            _isCancellationAllowed = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);

                            UpdateCancellationButtonVisibility();

                            _ = StartCancellationTimerCheck();
                        }

                        PaymentPendingStatusFrame.IsVisible = false;
                        PaymentPendingActionButtons.IsVisible = false;

                        TrackDriverButton.IsVisible = _isPanelExpanded && !(status == "Completed" || status == "Payment Pending");
                        TrackDriverButton.IsEnabled = true;

                        await GetDriverCurrentLocationImmediately();

                        SaveCurrentStatusToLocal();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in UI update: {ex.Message}");
                    }
                });

                StartTrackingDriverLocation(driverId);
                UpdateCommuterHomeStatus(status);
            }
        }

        private void UpdateDriverRating(double rating)
        {
            try
            {
                DriverRatingLabel.Text = rating.ToString("F1");

                int filledStars = (int)Math.Round(rating, MidpointRounding.AwayFromZero);
                filledStars = Math.Max(0, Math.Min(5, filledStars));

                var star1 = this.FindByName<Image>("Star1");
                var star2 = this.FindByName<Image>("Star2");
                var star3 = this.FindByName<Image>("Star3");
                var star4 = this.FindByName<Image>("Star4");
                var star5 = this.FindByName<Image>("Star5");

                if (star1 != null) star1.Source = filledStars >= 1 ? "star_filled.png" : "star_outline.png";
                if (star2 != null) star2.Source = filledStars >= 2 ? "star_filled.png" : "star_outline.png";
                if (star3 != null) star3.Source = filledStars >= 3 ? "star_filled.png" : "star_outline.png";
                if (star4 != null) star4.Source = filledStars >= 4 ? "star_filled.png" : "star_outline.png";
                if (star5 != null) star5.Source = filledStars >= 5 ? "star_filled.png" : "star_outline.png";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating stars: {ex.Message}");
            }
        }

        private async Task ShowPaymentPendingUI()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    SearchingSection.IsVisible = false;
                    DriverFoundSection.IsVisible = false;
                    TripCompletedStatusFrame.IsVisible = false;

                    PaymentPendingStatusFrame.IsVisible = true;
                    PaymentPendingStatusLabel.Text = "Payment pending. Please complete payment.";

                    PanelTitleLabel.Text = "Payment Required";
                    PanelSubtitleLabel.Text = "Please complete payment for your ride";

                    SearchingActionButtons.IsVisible = false;
                    DriverFoundActionButtons.IsVisible = false;
                    ArrivedActionButtons.IsVisible = false;
                    TripCompletedActionButtons.IsVisible = false;
                    CancelBookingDriverFoundButton.IsVisible = false;
                    CancelBookingButton.IsVisible = false;

                    PaymentPendingActionButtons.IsVisible = true;

                    TrackDriverButton.IsVisible = false;
                    TrackDriverButton.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing payment pending UI: {ex.Message}");
                }
            });
        }

        private async Task LoadDriverProfilePicture(Driver driver)
        {
            try
            {
                ProfileLoadingIndicator.IsRunning = true;
                ProfileLoadingIndicator.IsVisible = true;

                if (driver != null && !string.IsNullOrEmpty(driver.ProfileImageUrl))
                {
                    _driverProfileImageUrl = driver.ProfileImageUrl;

                    if (Uri.IsWellFormedUriString(_driverProfileImageUrl, UriKind.Absolute))
                    {
                        using var httpClient = new HttpClient();
                        httpClient.Timeout = TimeSpan.FromSeconds(10);

                        var imageBytes = await httpClient.GetByteArrayAsync(_driverProfileImageUrl);
                        var imageSource = ImageSource.FromStream(() => new MemoryStream(imageBytes));

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            DriverProfileImage.Source = imageSource;
                            ProfileLoadingIndicator.IsRunning = false;
                            ProfileLoadingIndicator.IsVisible = false;
                        });
                    }
                    else
                    {
                        SetDefaultProfilePicture();
                    }
                }
                else
                {
                    SetDefaultProfilePicture();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading profile picture: {ex.Message}");
                SetDefaultProfilePicture();
            }
        }

        private void SetDefaultProfilePicture()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DriverProfileImage.Source = "profile_icon.png";
                ProfileLoadingIndicator.IsRunning = false;
                ProfileLoadingIndicator.IsVisible = false;
            });
        }

        private async Task GetDriverCurrentLocationImmediately()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentDriverId)) return;
                await GetDriverLocationFromFirebase();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting driver location: {ex.Message}");
            }
        }

        private async Task GetDriverLocationFromFirebase()
        {
            try
            {
                string firebaseUrl = $"https://serviceco-37c60-default-rtdb.firebaseio.com/DriverLocations/{_currentDriverId}.json";

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = await client.GetStringAsync(firebaseUrl);

                    if (!string.IsNullOrEmpty(response) && response != "null")
                    {
                        var jsonDoc = JsonDocument.Parse(response);
                        var root = jsonDoc.RootElement;

                        double lat = 0, lng = 0;

                        if (root.TryGetProperty("latitude", out var latElement)) lat = latElement.GetDouble();
                        if (root.TryGetProperty("longitude", out var lngElement)) lng = lngElement.GetDouble();

                        if (lat != 0 && lng != 0)
                        {
                            _driverLocation = new Location(lat, lng);
                            _currentDriverDistance = CalculateDistance(lat, lng, _pickupLat, _pickupLng);
                            _currentDriverEta = Math.Ceiling(_currentDriverDistance * 3);

                            App.SavedDriverLat = lat;
                            App.SavedDriverLng = lng;
                            App.SavedDriverDistance = _currentDriverDistance;
                            App.SavedDriverEta = _currentDriverEta;
                            App.HasDriverLocation = true;
                            App.LastDriverId = _currentDriverId;

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                TrackDriverButton.IsEnabled = true;
                                TrackDriverButton.IsVisible = _isPanelExpanded;

                                if (_currentDriverDistance > 0)
                                {
                                    DriverLocationLabel.Text = $"Driver is {_currentDriverDistance:F1} km away • ETA: {_currentDriverEta} min";
                                }

                                if (_isMapLoaded && !_driverPinPlaced)
                                {
                                    _ = PlaceDriverPinOnMap();
                                }
                            });

                            SaveCurrentStatusToLocal();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching driver location: {ex.Message}");
            }
        }

        private async Task UpdateStatusUI(string status, string driverId)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    UpdateStatusDisplay(status);

                    switch (status)
                    {
                        case "Arrived at Pickup":
                            PanelTitleLabel.Text = "Driver Arrived";
                            PanelSubtitleLabel.Text = "Your driver is waiting at pickup location";
                            DriverFoundHeaderLabel.Text = "Driver Arrived";
                            DriverLocationLabel.Text = "Driver is at pickup location";

                            SearchingActionButtons.IsVisible = false;
                            DriverFoundActionButtons.IsVisible = false;
                            ArrivedActionButtons.IsVisible = true;
                            CancelBookingDriverFoundButton.IsVisible = false;
                            CancelBookingButton.IsVisible = false;
                            TrackDriverButton.IsVisible = _isPanelExpanded;
                            break;

                        case "Trip Started":
                            PanelTitleLabel.Text = "Trip Started";
                            PanelSubtitleLabel.Text = "Enroute to destination";
                            DriverFoundHeaderLabel.Text = "Trip Started";
                            DriverLocationLabel.Text = "On the way to dropoff...";

                            SearchingActionButtons.IsVisible = false;
                            DriverFoundActionButtons.IsVisible = false;
                            ArrivedActionButtons.IsVisible = false;
                            CancelBookingDriverFoundButton.IsVisible = false;
                            CancelBookingButton.IsVisible = false;
                            TrackDriverButton.IsVisible = _isPanelExpanded;
                            break;
                    }

                    UpdateCommuterHomeStatus(status);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in UpdateStatusUI: {ex.Message}");
                }
            });
        }

        private async Task ShowTripCompletedUI()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    PanelTitleLabel.Text = "Trip Completed";
                    PanelSubtitleLabel.Text = "Your ride has been completed successfully";

                    DriverFoundHeaderLabel.Text = "Trip Completed";

                    DriverStatusFrame.IsVisible = false;
                    ArrivedStatusFrame.IsVisible = false;
                    TripStartedStatusFrame.IsVisible = false;

                    TripCompletedStatusFrame.IsVisible = true;
                    TripCompletedStatusLabel.Text = "Trip completed! Please proceed to payment";

                    SearchingActionButtons.IsVisible = false;
                    DriverFoundActionButtons.IsVisible = false;
                    ArrivedActionButtons.IsVisible = false;
                    CancelBookingDriverFoundButton.IsVisible = false;
                    CancelBookingButton.IsVisible = false;
                    TripCompletedActionButtons.IsVisible = true;

                    TrackDriverButton.IsVisible = false;
                    TrackDriverButton.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing trip completed UI: {ex.Message}");
                }
            });
        }

        private async Task NavigateToPaymentPage()
        {
            try
            {
                StopAllTracking();
                _isListenerActive = false;

                _statusListener?.Dispose();
                _driverLocationListener?.Dispose();
                _rideRequestListener?.Dispose();
                _cancellationTimerListener?.Dispose();
                _isCheckingStatus = false;

                await Navigation.PushModalAsync(new Commuter_Payment(_rideRequestId));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error navigating to payment: {ex.Message}");
                await ShowErrorModal("Error", $"Failed to navigate to payment: {ex.Message}");
            }
        }

        private async Task HandleSearchNewDriver()
        {
            try
            {
                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
                if (rideRequest == null)
                {
                    await ShowErrorModal("Error", "Ride request not found.");
                    return;
                }

                await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "Status", "Pending");
                await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "DriverId", "");
                await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "CancelledBy", "");
                await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "CancellationReason", "");

                await _firebaseConnection.ResetCancellationTimerForNewSearchAsync(_rideRequestId);

                _isDriverFound = false;
                _currentDriverId = null;
                _currentDriver = null;
                _driverProfileImageUrl = string.Empty;
                _isCancellationModalShown = false;
                _isCancellationAllowed = true;
                _driverAcceptedTime = null;
                _driverPinPlaced = false;

                _driverLocationListener?.Dispose();
                _driverLocationListener = null;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SearchingSection.IsVisible = true;
                    DriverFoundSection.IsVisible = false;
                    TrackDriverButton.IsVisible = false;
                    TrackDriverButton.IsEnabled = false;

                    PanelTitleLabel.Text = "Searching for New Driver";
                    PanelSubtitleLabel.Text = "Finding a replacement driver for you...";

                    SearchingStatusLabel.Text = "Searching for available drivers...";

                    SearchingActionButtons.IsVisible = true;
                    DriverFoundActionButtons.IsVisible = false;
                    ArrivedActionButtons.IsVisible = false;
                    TripCompletedActionButtons.IsVisible = false;
                    PaymentPendingActionButtons.IsVisible = false;

                    CancelBookingButton.IsVisible = true;
                    CancelBookingDriverFoundButton.IsVisible = false;

                    DriverStatusFrame.IsVisible = false;
                    ArrivedStatusFrame.IsVisible = false;
                    TripStartedStatusFrame.IsVisible = false;
                    PaymentPendingStatusFrame.IsVisible = false;

                    DriverProfileImage.Source = "profile_icon.png";

                    DistanceLabel.Text = $"{_originalDistance:F1} km";
                    TimeLabel.Text = $"{_originalEstimatedTime} mins";
                    FareLabel.Text = $"₱{fare:F0}";
                    RideTypeLabel.Text = $"Tricycle ({rideType})";
                    PaymentMethodLabel.Text = _paymentMethod;
                });

                _currentStatus = "Pending";

                _rideRequestListener?.Dispose();
                StartRealTimeRideListener();

                await ShowSuccessModal("Searching", "We're now searching for a new driver for you.");

                _ = StartDriverSearchAnimation();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HandleSearchNewDriver: {ex.Message}");
                await ShowErrorModal("Error", "Failed to search for new driver. Please try again.");
                await GoToCommuterShell();
            }
        }

        private async Task GoToCommuterShell()
        {
            try
            {
                StopAllTracking();
                _isListenerActive = false;
                _isCancellationModalShown = false;

                _statusListener?.Dispose();
                _driverLocationListener?.Dispose();
                _rideRequestListener?.Dispose();
                _cancellationTimerListener?.Dispose();

                Application.Current.MainPage = new CommuterShell();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GoToCommuterShell: {ex.Message}");
                Application.Current.MainPage = new CommuterShell();
            }
        }

        private void UpdateStatusDisplay(string status)
        {
            try
            {
                DriverStatusFrame.IsVisible = false;
                ArrivedStatusFrame.IsVisible = false;
                TripStartedStatusFrame.IsVisible = false;
                TripCompletedStatusFrame.IsVisible = false;
                PaymentPendingStatusFrame.IsVisible = false;

                switch (status)
                {
                    case "Accepted":
                        DriverStatusFrame.IsVisible = true;
                        DriverStatusLabel.Text = "Driver has accepted your ride request";
                        DriverStatusIndicator.IsRunning = true;
                        break;

                    case "Arrived at Pickup":
                        ArrivedStatusFrame.IsVisible = true;
                        ArrivedStatusLabel.Text = "Driver has arrived at pickup location";
                        break;

                    case "Trip Started":
                        TripStartedStatusFrame.IsVisible = true;
                        TripStartedStatusLabel.Text = "Trip has started - enroute to destination";
                        break;

                    case "Completed":
                        TripCompletedStatusFrame.IsVisible = true;
                        TripCompletedStatusLabel.Text = "Trip completed! Please proceed to payment";
                        break;

                    case "Payment Pending":
                        PaymentPendingStatusFrame.IsVisible = true;
                        PaymentPendingStatusLabel.Text = "Payment pending. Please complete payment.";
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateStatusDisplay: {ex.Message}");
            }
        }

        private void UpdateCommuterHomeStatus(string status)
        {
            try
            {
                var homePage = Application.Current.MainPage.Navigation.NavigationStack
                    .FirstOrDefault(x => x is Commuter_Home) as Commuter_Home;

                if (homePage != null)
                {
                    homePage.UpdateRideStatus(status, pickupLocation, dropoffLocation);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Commuter_Home status: {ex.Message}");
            }
        }

        private void StartTrackingDriverLocation(string driverId)
        {
            try
            {
                _driverLocationListener?.Dispose();

                _driverLocationListener = _firebaseConnection.ListenForDriverLocation(driverId, async (driverLocation) =>
                {
                    if (!_isListenerActive || driverLocation == null) return;

                    _driverLocation = new Location(driverLocation.latitude, driverLocation.longitude);
                    _currentDriverDistance = CalculateDistance(driverLocation.latitude, driverLocation.longitude, _pickupLat, _pickupLng);
                    _currentDriverEta = Math.Ceiling(_currentDriverDistance * 3);

                    App.SavedDriverLat = driverLocation.latitude;
                    App.SavedDriverLng = driverLocation.longitude;
                    App.SavedDriverDistance = _currentDriverDistance;
                    App.SavedDriverEta = _currentDriverEta;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!TrackDriverButton.IsVisible && _isDriverFound && _isPanelExpanded)
                        {
                            TrackDriverButton.IsVisible = true;
                            TrackDriverButton.IsEnabled = true;
                        }

                        if (DriverLocationLabel != null)
                        {
                            if (_currentDriverDistance < 0.05)
                            {
                                DriverLocationLabel.Text = "Driver is at your location!";
                            }
                            else if (_currentDriverDistance < 0.1)
                            {
                                DriverLocationLabel.Text = "Driver is very close!";
                            }
                            else
                            {
                                DriverLocationLabel.Text = $"Driver is {_currentDriverDistance:F1} km away • ETA: {_currentDriverEta} min";
                            }
                        }

                        SaveCurrentStatusToLocal();
                    });

                    if (_isMapLoaded)
                    {
                        await UpdateDriverLocationOnMap(driverLocation.latitude, driverLocation.longitude);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting driver location tracking: {ex.Message}");
            }
        }

        private async Task UpdateDriverLocationOnMap(double latitude, double longitude)
        {
            if (!_isMapLoaded) return;

            try
            {
                string latStr = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string lngStr = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);

                string jsCode = $@"
                (function() {{
                    if (typeof window.driverMarker !== 'undefined' && window.driverMarker) {{
                        window.driverMarker.setPosition({{ lat: {latStr}, lng: {lngStr} }});
                    }} else if (typeof map !== 'undefined' && map) {{
                        window.driverMarker = new google.maps.Marker({{
                            position: {{ lat: {latStr}, lng: {lngStr} }},
                            map: map,
                            icon: {{
                                url: 'https://maps.google.com/mapfiles/ms/icons/orange-dot.png',
                                scaledSize: new google.maps.Size(48, 48)
                            }},
                            title: 'Driver Location'
                        }});
                    }}
                }})();";

                await RideMapView.EvaluateJavaScriptAsync(jsCode);
                _driverPinPlaced = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating driver location on map: {ex.Message}");
            }
        }

        private async Task StartCancellationTimerCheck()
        {
            while (_isListenerActive && _isDriverFound && _currentStatus == "Accepted")
            {
                try
                {
                    await Task.Delay(10000);

                    if (!_isListenerActive || !_isDriverFound || _currentStatus != "Accepted") break;

                    bool newStatus = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);

                    if (_isCancellationAllowed != newStatus)
                    {
                        _isCancellationAllowed = newStatus;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            UpdateCancellationButtonVisibility();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in timer check: {ex.Message}");
                }
            }
        }

        private async Task ShowAddNoteModal()
        {
            if (_isModalOpen) return;
            _isModalOpen = true;

            string iconSource = "document.png";
            Color iconColor = Color.FromArgb("#2196F3");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var noteEditor = new Editor
            {
                Placeholder = "e.g., I'm wearing a red shirt",
                PlaceholderColor = Color.FromArgb("#9E9E9E"),
                BackgroundColor = Color.FromArgb("#F5F5F5"),
                FontSize = 14,
                TextColor = Color.FromArgb("#212121"),
                HeightRequest = 100,
                MaxLength = 100
            };

            var charCountLabel = new Label
            {
                Text = "0/100",
                FontSize = 11,
                TextColor = Color.FromArgb("#9E9E9E"),
                HorizontalOptions = LayoutOptions.End,
                Margin = new Thickness(0, -15, 5, 0)
            };

            var sendButton = new Button
            {
                Text = "SEND",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                IsEnabled = false,
                Opacity = 0.5
            };

            var cancelButton = new Button
            {
                Text = "CANCEL",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            noteEditor.TextChanged += (s, e) =>
            {
                int charCount = e.NewTextValue?.Length ?? 0;
                charCountLabel.Text = $"{charCount}/100";
                sendButton.IsEnabled = charCount > 0;
                sendButton.Opacity = charCount > 0 ? 1 : 0.5;
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttonGrid.Children.Add(cancelButton);
            Grid.SetColumn(cancelButton, 0);
            buttonGrid.Children.Add(sendButton);
            Grid.SetColumn(sendButton, 1);

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
                            Text = "Add Note to Driver",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "Add a note for your driver",
                            FontSize = 13,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center
                        },
                        noteEditor,
                        charCountLabel,
                        buttonGrid
                    }
                }
            };

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            sendButton.Clicked += async (s, e) =>
            {
                string note = noteEditor.Text?.Trim();

                if (string.IsNullOrEmpty(note))
                {
                    await ShowInfoModal("Empty Note", "Please enter a note for the driver.");
                    return;
                }

                sendButton.IsEnabled = false;

                await AnimateModalExit(modalContent, blurOverlay, false);

                bool success = await _firebaseConnection.SaveNoteToRideRequestAsync(_rideRequestId, note);
                if (success)
                {
                    await ShowSuccessModal("Note Sent", "Your note has been sent to the driver.");
                }
                else
                {
                    await ShowErrorModal("Error", "Failed to send note. Please try again.");
                }
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            noteEditor.Focus();
        }

        private async Task ShowInfoModal(string title, string message)
        {
            if (_isModalOpen) return;
            _isModalOpen = true;

            string iconSource = "signal.png";
            Color iconColor = Color.FromArgb("#FF9800");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var okButton = new Button
            {
                Text = "OK",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
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
                        okButton
                    }
                }
            };

            okButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task ShowSuccessModal(string title, string message)
        {
            if (_isModalOpen) return;
            _isModalOpen = true;

            string iconSource = "happy.png";
            Color iconColor = Color.FromArgb("#2E7D32");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var okButton = new Button
            {
                Text = "OK",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
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
                        okButton
                    }
                }
            };

            okButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task ShowErrorModal(string title, string message)
        {
            if (_isModalOpen) return;
            _isModalOpen = true;

            string iconSource = "sad.png";
            Color iconColor = Color.FromArgb("#DC3545");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var okButton = new Button
            {
                Text = "OK",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
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
                        okButton
                    }
                }
            };

            okButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task AnimateModalExit(Frame modalContent, Grid blurOverlay, bool closePage)
        {
            await Task.WhenAll(
                modalContent.FadeTo(0, 200, Easing.CubicIn),
                modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );

            await Navigation.PopModalAsync();
            _isModalOpen = false;
        }

        private async void OnAddNoteClicked(object sender, EventArgs e)
        {
            if (MapBlurOverlay.IsVisible)
            {
                await ShowInfoModal("Please wait", "Please wait while we set up your ride.");
                return;
            }

            await ShowAddNoteModal();
        }

        private async void OnCancelBookingClicked(object sender, EventArgs e)
        {
            if (_isModalOpen) return;

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

            var cancelButton = new Button
            {
                Text = "CANCEL",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var confirmButton = new Button
            {
                Text = "YES",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttonGrid.Children.Add(cancelButton);
            Grid.SetColumn(cancelButton, 0);
            buttonGrid.Children.Add(confirmButton);
            Grid.SetColumn(confirmButton, 1);

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
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to cancel this ride request?",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        buttonGrid
                    }
                }
            };

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(modalContent, blurOverlay, false);

                bool success = await _firebaseConnection.CancelRideRequestAsync(_rideRequestId, "Commuter");
                if (success)
                {
                    await GoToCommuterShell();
                }
                else
                {
                    await ShowErrorModal("Error", "Failed to cancel ride.");
                }
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async void OnCancelBookingDriverFoundClicked(object sender, EventArgs e)
        {
            if (_isModalOpen) return;

            bool isAllowed = await _firebaseConnection.IsCancellationAllowedAsync(_rideRequestId);

            if (!isAllowed)
            {
                await ShowInfoModal("Cannot Cancel", "Cannot cancel ride after 3 minutes of driver acceptance.");
                return;
            }

            var currentRide = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
            if (currentRide != null &&
                (currentRide.Status == "Arrived at Pickup" || currentRide.Status == "Trip Started"))
            {
                await ShowInfoModal("Cannot Cancel", "Cannot cancel ride after driver has arrived or trip has started.");
                return;
            }

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

            var cancelButton = new Button
            {
                Text = "CANCEL",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var confirmButton = new Button
            {
                Text = "YES",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttonGrid.Children.Add(cancelButton);
            Grid.SetColumn(cancelButton, 0);
            buttonGrid.Children.Add(confirmButton);
            Grid.SetColumn(confirmButton, 1);

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
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to cancel this ride?",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        buttonGrid
                    }
                }
            };

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            confirmButton.Clicked += async (s, e) =>
            {
                confirmButton.IsEnabled = false;
                await AnimateModalExit(modalContent, blurOverlay, false);

                bool success = await _firebaseConnection.CancelRideRequestAsync(_rideRequestId, "Commuter");
                if (success)
                {
                    await GoToCommuterShell();
                }
                else
                {
                    await ShowErrorModal("Error", "Failed to cancel ride.");
                }
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async void OnCallDriverClicked(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentDriverId))
            {
                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver != null && !string.IsNullOrEmpty(driver.MobileNumber))
                {
                    try
                    {
                        PhoneDialer.Open(driver.MobileNumber);
                    }
                    catch
                    {
                        await ShowErrorModal("Not Supported", "Unable to open dialer on this device");
                    }
                }
            }
        }

        private async void OnMessageDriverClicked(object sender, EventArgs e)
        {
            try
            {
                if (!_isDriverFound || string.IsNullOrEmpty(_currentDriverId))
                {
                    await ShowInfoModal("Info", "Wait for driver to accept your ride");
                    return;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(_currentDriverId);
                if (driver == null) return;

                await Navigation.PushModalAsync(new Commuter_Chat(
                    _rideRequestId,
                    _currentDriverId,
                    $"{driver.FirstName} {driver.LastName}"
                ));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Message button error: {ex.Message}");
            }
        }

        private async void OnProceedToPaymentClicked(object sender, EventArgs e)
        {
            await NavigateToPaymentPage();
        }

        private async void OnProceedToPaymentPendingClicked(object sender, EventArgs e)
        {
            await NavigateToPaymentPage();
        }

        private async void OnViewReceiptClicked(object sender, EventArgs e)
        {
            await ShowInfoModal("Receipt", "Receipt feature coming soon!");
        }

        private async void OnRateDriverClicked(object sender, EventArgs e)
        {
            if (_isModalOpen) return;

            _isModalOpen = true;

            string iconSource = "star_filled.png";
            Color iconColor = Color.FromArgb("#FF9800");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var star1 = new Button { Text = "⭐", FontSize = 32, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#FFC107"), WidthRequest = 60, HeightRequest = 60, CornerRadius = 30 };
            var star2 = new Button { Text = "⭐", FontSize = 32, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#FFC107"), WidthRequest = 60, HeightRequest = 60, CornerRadius = 30 };
            var star3 = new Button { Text = "⭐", FontSize = 32, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#FFC107"), WidthRequest = 60, HeightRequest = 60, CornerRadius = 30 };
            var star4 = new Button { Text = "⭐", FontSize = 32, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#FFC107"), WidthRequest = 60, HeightRequest = 60, CornerRadius = 30 };
            var star5 = new Button { Text = "⭐", FontSize = 32, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#FFC107"), WidthRequest = 60, HeightRequest = 60, CornerRadius = 30 };

            var closeButton = new Button
            {
                Text = "LATER",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var starsLayout = new HorizontalStackLayout
            {
                Spacing = 5,
                HorizontalOptions = LayoutOptions.Center,
                Children = { star1, star2, star3, star4, star5 }
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
                WidthRequest = 300,
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
                            Text = "Rate Your Driver",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "How was your ride experience?",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center
                        },
                        starsLayout,
                        closeButton
                    }
                }
            };

            int selectedRating = 0;

            star1.Clicked += (s, e) => { selectedRating = 1; UpdateStars(1); };
            star2.Clicked += (s, e) => { selectedRating = 2; UpdateStars(2); };
            star3.Clicked += (s, e) => { selectedRating = 3; UpdateStars(3); };
            star4.Clicked += (s, e) => { selectedRating = 4; UpdateStars(4); };
            star5.Clicked += (s, e) => { selectedRating = 5; UpdateStars(5); };

            void UpdateStars(int rating)
            {
                star1.Text = rating >= 1 ? "⭐" : "☆";
                star2.Text = rating >= 2 ? "⭐" : "☆";
                star3.Text = rating >= 3 ? "⭐" : "☆";
                star4.Text = rating >= 4 ? "⭐" : "☆";
                star5.Text = rating >= 5 ? "⭐" : "☆";
            }

            closeButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            while (selectedRating == 0 && modalPage.Parent != null)
            {
                await Task.Delay(100);
            }

            if (selectedRating > 0 && !string.IsNullOrEmpty(_currentDriverId))
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
                bool success = await _firebaseConnection.SaveDriverRating(_currentDriverId, selectedRating);
                if (success)
                {
                    await ShowSuccessModal("Thank You!", $"You rated the driver {selectedRating} stars!");
                    await NavigateToPaymentPage();
                }
                else
                {
                    await ShowErrorModal("Error", "Failed to save rating.");
                }
            }
        }

        private async void OnCopyRideIDTapped(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(rideID) && rideID != "TRC-LOADING" && rideID.StartsWith("TRC-"))
            {
                await Clipboard.SetTextAsync(rideID);
                await ShowSuccessModal("Copied", "Ride ID copied to clipboard.");
            }
            else
            {
                await ShowErrorModal("Error", "Ride ID not available yet.");
            }
        }

        private bool _showPickup = true;

        private async void OnHeaderTapped(object sender, EventArgs e)
        {
            try
            {
                if (_showPickup)
                {
                    string jsCode = $@"
                        if (window.flyToLocation) {{
                            window.flyToLocation({_pickupLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                 {_pickupLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true);
                        }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    _showPickup = false;
                }
                else
                {
                    string jsCode = $@"
                        if (window.flyToLocation) {{
                            window.flyToLocation({_dropoffLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                 {_dropoffLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}, false);
                        }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    _showPickup = true;
                }
            }
            catch { }
        }

        private async void OnMapTypeTapped(object sender, EventArgs e)
        {
            _isSatelliteView = !_isSatelliteView;
            MapTypeIcon.Source = _isSatelliteView ? "two_dimentional.png" : "three_dimentional.png";

            try
            {
                string jsCode = $@"
                    if (window.toggleMapType) {{
                        window.toggleMapType();
                    }} else if (window.map) {{
                        window.map.setMapTypeId('{(_isSatelliteView ? "satellite" : "roadmap")}');
                        window.map.setTilt({(_isSatelliteView ? 60 : 0)});
                    }}";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Map type toggle error: {ex.Message}");
            }
        }

        private async void OnLocationButtonTapped(object sender, EventArgs e)
        {
            bool hasPermission = await RequestLocationPermission();
            if (!hasPermission) return;

            try
            {
                var originalSource = ((Image)LocationButton.Content).Source;
                ((Image)LocationButton.Content).Source = "selected_rides.png";

                try
                {
                    Location location = null;
                    bool gotLocation = false;

                    try
                    {
                        var request = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(5));
                        location = await Geolocation.GetLocationAsync(request);
                        gotLocation = location != null;
                    }
                    catch
                    {
                        location = await Geolocation.GetLastKnownLocationAsync();
                        gotLocation = location != null;
                    }

                    if (gotLocation && location != null)
                    {
                        _currentLocation = location;

                        // Wait for map to be ready
                        await _mapReadyTcs.Task;
                        await Task.Delay(300);

                        string latStr = location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string lngStr = location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);

                        string jsCode = $@"
                (function() {{
                    if (typeof map !== 'undefined' && map) {{
                        // Center map on current location
                        map.panTo({{ lat: {latStr}, lng: {lngStr} }});
                        map.setZoom(17);
                        
                        // Update or create user marker with bounce animation
                        if (window.userMarker) {{
                            window.userMarker.setPosition({{ lat: {latStr}, lng: {lngStr} }});
                            window.userMarker.setAnimation(google.maps.Animation.BOUNCE);
                            setTimeout(function() {{ 
                                if (window.userMarker) window.userMarker.setAnimation(null); 
                            }}, 1500);
                        }} else {{
                            window.userMarker = new google.maps.Marker({{
                                position: {{ lat: {latStr}, lng: {lngStr} }},
                                map: map,
                                icon: {{
                                    url: 'https://maps.google.com/mapfiles/ms/icons/blue-dot.png',
                                    scaledSize: new google.maps.Size(44, 44)
                                }},
                                title: 'Your Location',
                                animation: google.maps.Animation.BOUNCE
                            }});
                            setTimeout(function() {{ 
                                if (window.userMarker) window.userMarker.setAnimation(null); 
                            }}, 1500);
                        }}
                        
                        return 'SUCCESS';
                    }}
                    return 'NOT_READY';
                }})();";

                        var result = await RideMapView.EvaluateJavaScriptAsync(jsCode);
                        string resultStr = result?.ToString() ?? "";

                        if (resultStr == "NOT_READY")
                        {
                            await Task.Delay(500);
                            await FlyToCurrentLocation(location);
                        }

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            ((Image)LocationButton.Content).Source = originalSource;
                        });
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            ((Image)LocationButton.Content).Source = originalSource;
                        });
                        await ShowInfoModal("Location Unavailable", "Unable to get your current location.");
                    }
                }
                catch
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ((Image)LocationButton.Content).Source = originalSource;
                    });
                }
            }
            catch { }
        }

        private async Task FlyToCurrentLocation(Location location)
        {
            try
            {
                string latStr = location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string lngStr = location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);

                string jsCode = $@"
        (function() {{
            if (typeof map !== 'undefined' && map) {{
                map.panTo({{ lat: {latStr}, lng: {lngStr} }});
                map.setZoom(17);
                
                if (window.userMarker) {{
                    window.userMarker.setPosition({{ lat: {latStr}, lng: {lngStr} }});
                    window.userMarker.setAnimation(google.maps.Animation.BOUNCE);
                    setTimeout(function() {{ 
                        if (window.userMarker) window.userMarker.setAnimation(null); 
                    }}, 1500);
                }} else {{
                    window.userMarker = new google.maps.Marker({{
                        position: {{ lat: {latStr}, lng: {lngStr} }},
                        map: map,
                        icon: {{
                            url: 'https://maps.google.com/mapfiles/ms/icons/blue-dot.png',
                            scaledSize: new google.maps.Size(44, 44)
                        }},
                        title: 'Your Location',
                        animation: google.maps.Animation.BOUNCE
                    }});
                    setTimeout(function() {{ 
                        if (window.userMarker) window.userMarker.setAnimation(null); 
                    }}, 1500);
                }}
                
                return 'SUCCESS';
            }}
            return 'FAIL';
        }})();";

                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error flying to current location: {ex.Message}");
            }
        }

        private async Task<bool> RequestLocationPermission()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status == PermissionStatus.Granted) return true;
                if (status == PermissionStatus.Denied) return false;

                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                return status == PermissionStatus.Granted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Permission error: {ex.Message}");
                return false;
            }
        }

        private async void OnTrackDriverTapped(object sender, EventArgs e)
        {
            if (_isModalOpen) return;

            try
            {
                if (string.IsNullOrEmpty(_currentDriverId))
                {
                    await ShowInfoModal("Info", "No driver assigned yet.");
                    return;
                }

                if (_driverLocation == null)
                {
                    await GetDriverLocationFromFirebase();
                    if (_driverLocation == null)
                    {
                        await ShowInfoModal("Location Unavailable", "Driver location is not available yet. Please wait...");
                        return;
                    }
                }

                await FlyToDriverLocation();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnTrackDriverTapped: {ex.Message}");
                await ShowErrorModal("Error", $"Could not track driver: {ex.Message}");
            }
        }

        private async Task FlyToDriverLocation()
        {
            try
            {
                await _mapReadyTcs.Task;
                await Task.Delay(500);

                double lat = _driverLocation.Latitude;
                double lng = _driverLocation.Longitude;

                string latStr = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string lngStr = lng.ToString(System.Globalization.CultureInfo.InvariantCulture);

                string jsCode = $@"
                (function() {{
                    if (typeof map !== 'undefined' && map) {{
                        map.panTo({{ lat: {latStr}, lng: {lngStr} }});
                        map.setZoom(16);
                        
                        if (window.driverMarker) {{
                            window.driverMarker.setAnimation(google.maps.Animation.BOUNCE);
                            setTimeout(() => window.driverMarker.setAnimation(null), 1500);
                        }}
                        
                        return 'SUCCESS';
                    }} else {{
                        return 'RETRY';
                    }}
                }})();";

                var result = await RideMapView.EvaluateJavaScriptAsync(jsCode);
                string resultStr = result?.ToString() ?? "";

                if (resultStr == "RETRY")
                {
                    await Task.Delay(1000);
                    await FlyToDriverLocation();
                }
                else if (resultStr == "SUCCESS")
                {
                    _driverPinPlaced = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error flying to driver: {ex.Message}");
                await Task.Delay(1000);
                await FlyToDriverLocation();
            }
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            if (_isModalOpen) return;
            await ShowExitConfirmation();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_isModalOpen)
                {
                    await ShowExitConfirmation();
                }
            });
            return true;
        }

        private async Task ShowExitConfirmation()
        {
            if (_isModalOpen) return;

            bool hasActiveRide = false;
            if (!string.IsNullOrEmpty(_rideRequestId))
            {
                var rideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);
                if (rideRequest != null &&
                    (rideRequest.Status == "Accepted" ||
                     rideRequest.Status == "Arrived at Pickup" ||
                     rideRequest.Status == "Trip Started" ||
                     rideRequest.Status == "Payment Pending"))
                {
                    hasActiveRide = true;
                }
            }

            string message = hasActiveRide
                ? "You have an active ride. Are you sure you want to exit?"
                : "Are you sure you want to exit?";

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

            var cancelButton = new Button
            {
                Text = "CANCEL",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var exitButton = new Button
            {
                Text = "EXIT",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttonGrid.Children.Add(cancelButton);
            Grid.SetColumn(cancelButton, 0);
            buttonGrid.Children.Add(exitButton);
            Grid.SetColumn(exitButton, 1);

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
                            Text = "Exit?",
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
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap
                        },
                        buttonGrid
                    }
                }
            };

            cancelButton.Clicked += async (s, e) =>
            {
                await AnimateModalExit(modalContent, blurOverlay, false);
            };

            exitButton.Clicked += async (s, e) =>
            {
                exitButton.IsEnabled = false;
                await AnimateModalExit(modalContent, blurOverlay, false);
                await GoToCommuterShell();
            };

            var mainLayout = new Grid
            {
                Children = { blurOverlay, modalContent }
            };

            var modalPage = new ContentPage
            {
                BackgroundColor = Colors.Transparent,
                Content = mainLayout
            };

            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            _panelExpandedHeight = height * 0.55;
            _panelCollapsedHeight = 80;

            if (_isPanelExpanded)
            {
                SelectionPanel.HeightRequest = _panelExpandedHeight;
                DetailsContent.IsVisible = true;
                ShowDetailsButton.IsVisible = false;

                TrackDriverButton.IsVisible = _isDriverFound && !(_currentStatus == "Completed" || _currentStatus == "Payment Pending");

                UpdateButtonPositions(120, 200);
            }
            else
            {
                SelectionPanel.HeightRequest = _panelCollapsedHeight;
                DetailsContent.IsVisible = false;
                ShowDetailsButton.IsVisible = true;
                TrackDriverButton.IsVisible = false;
                UpdateButtonPositions(90, 0);
            }
            _lastPanelHeight = SelectionPanel.HeightRequest;
        }

        private void UpdateButtonPositions(double locationBackOffset, double mapTypeOffset)
        {
            var showDetailsOffset = locationBackOffset - 30;
            AbsoluteLayout.SetLayoutBounds(ShowDetailsButton, new Rect(0.5, 0.95, -1, showDetailsOffset));
            AbsoluteLayout.SetLayoutBounds(BackButton, new Rect(0.05, 0.95, -1, locationBackOffset));
            AbsoluteLayout.SetLayoutBounds(LocationButton, new Rect(0.95, 0.95, -1, locationBackOffset));
            AbsoluteLayout.SetLayoutBounds(MapTypeButton, new Rect(0.95, 0.80, -1, mapTypeOffset));

            TrackDriverButton.IsVisible = _isPanelExpanded && _isDriverFound && !(_currentStatus == "Completed" || _currentStatus == "Payment Pending");
            AbsoluteLayout.SetLayoutBounds(TrackDriverButton, new Rect(0.5, 0.90, -1, -1));
        }

        private void OnPanelPanned(object sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _isDragging = true;
                    _startY = e.TotalY;
                    _startHeight = _lastPanelHeight;
                    break;
                case GestureStatus.Running:
                    if (!_isDragging) return;
                    var deltaY = e.TotalY - _startY;
                    var newHeight = Math.Max(_panelCollapsedHeight, Math.Min(_panelExpandedHeight, _startHeight - deltaY));
                    SelectionPanel.HeightRequest = newHeight;
                    var progress = (newHeight - _panelCollapsedHeight) / (_panelExpandedHeight - _panelCollapsedHeight);
                    var locationBackOffset = 90 + (30 * progress);
                    var mapTypeOffset = 0 + (200 * progress);
                    UpdateButtonPositions(locationBackOffset, mapTypeOffset);
                    DetailsContent.IsVisible = newHeight > _panelCollapsedHeight + 50;
                    ShowDetailsButton.IsVisible = newHeight < _panelCollapsedHeight + 50;
                    break;
                case GestureStatus.Completed:
                    _isDragging = false;
                    _lastPanelHeight = SelectionPanel.HeightRequest;
                    var threshold = (_panelExpandedHeight + _panelCollapsedHeight) / 2;
                    var shouldExpand = _lastPanelHeight > threshold;
                    AnimatePanel(shouldExpand);
                    break;
            }
        }

        private void AnimatePanel(bool expand)
        {
            _isPanelExpanded = expand;
            var targetHeight = expand ? _panelExpandedHeight : _panelCollapsedHeight;
            var targetLocationBackOffset = expand ? 120 : 90;
            var targetMapTypeOffset = expand ? 200 : 0;

            var panelAnimation = new Animation(h => SelectionPanel.HeightRequest = h, _lastPanelHeight, targetHeight, easing: Easing.CubicOut);
            var backBounds = AbsoluteLayout.GetLayoutBounds(BackButton);
            var locationBounds = AbsoluteLayout.GetLayoutBounds(LocationButton);
            var mapTypeBounds = AbsoluteLayout.GetLayoutBounds(MapTypeButton);
            var currentLocationBackOffset = backBounds.Height;
            var currentMapTypeOffset = mapTypeBounds.Height;

            var buttonAnimation = new Animation(t =>
            {
                var locationBackOffset = currentLocationBackOffset + (targetLocationBackOffset - currentLocationBackOffset) * t;
                var mapTypeOffset = currentMapTypeOffset + (targetMapTypeOffset - currentMapTypeOffset) * t;
                UpdateButtonPositions(locationBackOffset, mapTypeOffset);
            }, 0, 1, easing: Easing.CubicOut);

            panelAnimation.Commit(SelectionPanel, "PanelAnimation", length: 300, finished: (v, c) =>
            {
                _lastPanelHeight = targetHeight;
                DetailsContent.IsVisible = expand;
                ShowDetailsButton.IsVisible = !expand;

                TrackDriverButton.IsVisible = expand && _isDriverFound && !(_currentStatus == "Completed" || _currentStatus == "Payment Pending");

                UpdateButtonPositions(targetLocationBackOffset, targetMapTypeOffset);
            });
            buttonAnimation.Commit(this, "ButtonAnimation", length: 300);
        }

        private void OnDragHandleTapped(object sender, EventArgs e)
        {
            if (!_isDragging) TogglePanel(!_isPanelExpanded);
        }

        private void OnShowDetailsTapped(object sender, EventArgs e)
        {
            TogglePanel(true);
        }

        private void TogglePanel(bool expand)
        {
            AnimatePanel(expand);
        }

        private string GenerateGoogleMapHtml((double, double) pickupCoord, (double, double) dropoffCoord)
        {
            string mapType = _isSatelliteView ? "satellite" : "roadmap";
            string tilt = _isSatelliteView ? "60" : "0";

            string pickupLat = pickupCoord.Item1.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string pickupLng = pickupCoord.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string dropoffLat = dropoffCoord.Item1.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string dropoffLng = dropoffCoord.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture);

            string currentLat = _currentLocation?.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? pickupLat;
            string currentLng = _currentLocation?.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? pickupLng;

            return $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>
                <style>body,html,#map{{margin:0;padding:0;height:100%;width:100%;}}</style>
                <script src='https://maps.googleapis.com/maps/api/js?key={GOOGLE_MAPS_API_KEY}&libraries=geometry'></script>
                <script>
                    let map, userMarker, pickupMarker, dropoffMarker, driverMarker, directionsService, routeRenderer;
                    let currentMapType = '{mapType}';
                    
                    const userPos = {{ lat: {currentLat}, lng: {currentLng} }};
                    const pickupPos = {{ lat: {pickupLat}, lng: {pickupLng} }};
                    const dropoffPos = {{ lat: {dropoffLat}, lng: {dropoffLng} }};

                    function initMap() {{
                        const bounds = new google.maps.LatLngBounds();
                        bounds.extend(pickupPos);
                        bounds.extend(dropoffPos);
                        
                        map = new google.maps.Map(document.getElementById('map'), {{
                            center: userPos,
                            zoom: 15,
                            mapTypeId: currentMapType,
                            tilt: {tilt},
                            disableDefaultUI: true,
                            zoomControl: true,
                            mapTypeControl: false
                        }});
                        
                        directionsService = new google.maps.DirectionsService();
                        
                        userMarker = new google.maps.Marker({{
                            position: userPos,
                            map: map,
                            icon: {{
                                url: 'https://maps.google.com/mapfiles/ms/icons/blue-dot.png',
                                scaledSize: new google.maps.Size(40, 40)
                            }},
                            title: 'Your Location'
                        }});
                        
                        pickupMarker = new google.maps.Marker({{
                            position: pickupPos,
                            map: map,
                            icon: {{
                                url: 'https://maps.google.com/mapfiles/ms/icons/green-dot.png',
                                scaledSize: new google.maps.Size(40, 40)
                            }},
                            title: 'Pickup Location'
                        }});
                        
                        dropoffMarker = new google.maps.Marker({{
                            position: dropoffPos,
                            map: map,
                            icon: {{
                                url: 'https://maps.google.com/mapfiles/ms/icons/red-dot.png',
                                scaledSize: new google.maps.Size(40, 40)
                            }},
                            title: 'Dropoff Location'
                        }});
                        
                        window.map = map;
                        window.userMarker = userMarker;
                        window.pickupMarker = pickupMarker;
                        window.dropoffMarker = dropoffMarker;
                        
                        drawRoute();
                        window.isMapReady = true;
                    }}
                    
                    function drawRoute() {{
                        directionsService.route({{
                            origin: pickupPos,
                            destination: dropoffPos,
                            travelMode: 'DRIVING'
                        }}, (result, status) => {{
                            if (status === 'OK') {{
                                if (routeRenderer) routeRenderer.setMap(null);
                                routeRenderer = new google.maps.DirectionsRenderer({{
                                    map: map,
                                    suppressMarkers: true,
                                    polylineOptions: {{ strokeColor: '#2196F3', strokeWeight: 5 }}
                                }});
                                routeRenderer.setDirections(result);
                            }}
                        }});
                    }}
                    
                    window.updateLiveLocation = function(lat, lng) {{
                        if (!window.isMapReady || !userMarker) return;
                        const newPos = new google.maps.LatLng(lat, lng);
                        userMarker.setPosition(newPos);
                    }};
                    
                    window.flyToLocation = function(lat, lng, isPickup) {{
                        if (!window.isMapReady) return;
                        map.panTo(new google.maps.LatLng(lat, lng));
                        map.setZoom(16);
                        const marker = isPickup ? pickupMarker : dropoffMarker;
                        if (marker) {{
                            marker.setAnimation(google.maps.Animation.BOUNCE);
                            setTimeout(() => marker.setAnimation(null), 1500);
                        }}
                    }};
                    
                    window.flyToDriver = function() {{
                        if (!window.isMapReady || !driverMarker) return;
                        map.panTo(driverMarker.getPosition());
                        map.setZoom(16);
                        driverMarker.setAnimation(google.maps.Animation.BOUNCE);
                        setTimeout(() => driverMarker.setAnimation(null), 1500);
                    }};
                    
                    window.toggleMapType = function() {{ 
                        if (!window.isMapReady) return false;
                        try {{
                            currentMapType = currentMapType === 'roadmap' ? 'satellite' : 'roadmap';
                            map.setMapTypeId(currentMapType);
                            map.setTilt(currentMapType === 'satellite' ? 60 : 0);
                            return currentMapType === 'satellite';
                        }} catch(e) {{
                            return false;
                        }}
                    }};
                    
                    window.updateDriverLocation = function(lat, lng) {{
                        if (!window.isMapReady) return;
                        if (!driverMarker) {{
                            driverMarker = new google.maps.Marker({{
                                position: {{ lat: lat, lng: lng }},
                                map: map,
                                icon: {{
                                    url: 'https://maps.google.com/mapfiles/ms/icons/orange-dot.png',
                                    scaledSize: new google.maps.Size(48, 48)
                                }},
                                title: 'Driver Location'
                            }});
                        }} else {{
                            driverMarker.setPosition({{ lat: lat, lng: lng }});
                        }}
                    }};
                    
                    google.maps.event.addDomListener(window, 'load', initMap);
                </script>
            </head>
            <body><div id='map'></div></body>
            </html>";
        }
    }
}