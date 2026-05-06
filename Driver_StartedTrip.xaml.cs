using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using ServiceCo.Firebase;
using ServiceCo.Services;  
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

namespace ServiceCo
{
    public partial class Driver_StartedTrip : ContentPage
    {
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string _rideRequestId;
        private RideRequest _currentRideRequest;
        private Location _currentLocation;
        private Location _pickupLocation;
        private Location _dropoffLocation;
        private CancellationTokenSource _gpsCancellationTokenSource;
        private bool _isTrackingLocation = false;
        private bool _isMapLoaded = false;
        private bool _isModalOpen = false;
        private double _currentBearing = 0;
        private Location _previousLocation;
        private enum TripState { GoingToPickup, ArrivedAtPickup, GoingToDropoff, Completed }
        private TripState _currentState = TripState.GoingToPickup;
        private const string GOOGLE_MAPS_API_KEY = Constants.GoogleMapsApiKey;
        private Timer _etaUpdateTimer;
        private Timer _locationUpdateTimer;
        private bool _showPickup = true;
        private bool _isSatelliteView = false;
        private int _loadAttempts = 0;
        private const int MAX_LOAD_ATTEMPTS = 3;

        public Driver_StartedTrip(string rideRequestId)
        {
            InitializeComponent();
            _rideRequestId = rideRequestId;
            SetupUI();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadRideDataWithRetry();
        }

        private void SetupUI()
        {
            MapBlurOverlay.IsVisible = true;
            UpdateButtonTexts();
        }

        private async Task LoadRideDataWithRetry()
        {
            _loadAttempts = 0;
            bool loaded = false;

            while (_loadAttempts < MAX_LOAD_ATTEMPTS && !loaded)
            {
                _loadAttempts++;
                try
                {
                    await LoadRideData();
                    loaded = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Attempt {_loadAttempts} failed: {ex.Message}");
                    if (_loadAttempts >= MAX_LOAD_ATTEMPTS)
                    {
                        await ShowModal("Connection Error", "Unable to load ride data. Please check your internet connection and try again.", "error", true);
                        await Navigation.PopModalAsync();
                        return;
                    }
                    else
                    {
                        await Task.Delay(1500);
                    }
                }
            }
        }

        private async Task LoadRideData()
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                _currentRideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId).WaitAsync(cts.Token);
            }
            catch (TimeoutException)
            {
                throw new Exception("Request timeout");
            }
            catch (TaskCanceledException)
            {
                throw new Exception("Request cancelled");
            }

            if (_currentRideRequest != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PickupDisplayLabel.Text = FormatAddress(_currentRideRequest.PickupLocation, 18);
                    DropoffDisplayLabel.Text = FormatAddress(_currentRideRequest.DropoffLocation, 18);
                });

                _pickupLocation = new Location(_currentRideRequest.PickupLat, _currentRideRequest.PickupLng);
                _dropoffLocation = new Location(_currentRideRequest.DropoffLat, _currentRideRequest.DropoffLng);

                if (_currentRideRequest.Status == "Arrived at Pickup")
                {
                    _currentState = TripState.ArrivedAtPickup;
                }
                else if (_currentRideRequest.Status == "Trip Started")
                {
                    _currentState = TripState.GoingToDropoff;
                }

                UpdateButtonTexts();

                await GetInitialLocation();
                await LoadGoogleMapsWithDrivingMode();
                await StartRealTimeTracking();
            }
            else
            {
                throw new Exception("Ride request not found");
            }
        }

        private string FormatAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";
            var cleanAddress = address.Replace(", Malolos, Bulacan", "").Replace(", Bulacan", "").Replace("Malolos, ", "").Trim();
            return cleanAddress.Length > maxLength ? cleanAddress.Substring(0, maxLength - 3) + "..." : cleanAddress;
        }

        private async Task GetInitialLocation()
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
                    var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5));
                    _currentLocation = await Geolocation.GetLocationAsync(request);
                    if (_currentLocation == null) _currentLocation = await Geolocation.GetLastKnownLocationAsync();
                    if (_currentLocation == null) _currentLocation = _pickupLocation;
                }
                else
                {
                    _currentLocation = _pickupLocation;
                }
            }
            catch
            {
                _currentLocation = _pickupLocation;
            }
        }

        private async Task LoadGoogleMapsWithDrivingMode()
        {
            try
            {
                string mapHtml = GenerateDrivingModeMapHtml();
                RideMapView.Source = new HtmlWebViewSource { Html = mapHtml };

                var loadCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Task.Delay(1000, loadCts.Token);

                await CheckMapReady();
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("Map load timeout, continuing anyway");
                MainThread.BeginInvokeOnMainThread(() => MapBlurOverlay.IsVisible = false);
            }
            catch
            {
                MainThread.BeginInvokeOnMainThread(() => MapBlurOverlay.IsVisible = false);
            }
        }

        private string GenerateDrivingModeMapHtml()
        {
            string driverLat = _currentLocation?.Latitude.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) ?? "14.656764";
            string driverLng = _currentLocation?.Longitude.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) ?? "120.972180";
            string pickupLat = _pickupLocation.Latitude.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            string pickupLng = _pickupLocation.Longitude.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            string dropoffLat = _dropoffLocation.Latitude.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            string dropoffLng = _dropoffLocation.Longitude.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            string destLat = _currentState == TripState.GoingToPickup ? pickupLat : dropoffLat;
            string destLng = _currentState == TripState.GoingToPickup ? pickupLng : dropoffLng;

            return $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>
                <style>body,html,#map{{margin:0;padding:0;height:100%;width:100%;}}</style>
                <script src='https://maps.googleapis.com/maps/api/js?key={GOOGLE_MAPS_API_KEY}&libraries=geometry'></script>
                <script>
                    let map, driverMarker, pickupMarker, dropoffMarker, directionsService, orangeRouteRenderer, blueRouteRenderer;
                    let currentMapType = 'roadmap';
                    
                    const driverPos = {{ lat: {driverLat}, lng: {driverLng} }};
                    const pickupPos = {{ lat: {pickupLat}, lng: {pickupLng} }};
                    const dropoffPos = {{ lat: {dropoffLat}, lng: {dropoffLng} }};
                    const destinationPos = {{ lat: {destLat}, lng: {destLng} }};

                    function initMap() {{
                        map = new google.maps.Map(document.getElementById('map'), {{
                            center: driverPos,
                            zoom: 17,
                            mapTypeId: 'roadmap',
                            disableDefaultUI: true,
                            zoomControl: true
                        }});
                        
                        directionsService = new google.maps.DirectionsService();
                        
                        driverMarker = new google.maps.Marker({{
                            position: driverPos,
                            map: map,
                            icon: {{ url: 'https://maps.google.com/mapfiles/ms/icons/orange-dot.png', scaledSize: new google.maps.Size(44, 44) }},
                            title: 'Your Location'
                        }});
                        
                        pickupMarker = new google.maps.Marker({{
                            position: pickupPos,
                            map: map,
                            icon: {{ url: 'https://maps.google.com/mapfiles/ms/icons/green-dot.png', scaledSize: new google.maps.Size(44, 44) }},
                            title: 'Pickup Location'
                        }});
                        
                        dropoffMarker = new google.maps.Marker({{
                            position: dropoffPos,
                            map: map,
                            icon: {{ url: 'https://maps.google.com/mapfiles/ms/icons/red-dot.png', scaledSize: new google.maps.Size(44, 44) }},
                            title: 'Dropoff Location'
                        }});
                        
                        window.map = map;
                        window.driverMarker = driverMarker;
                        window.pickupMarker = pickupMarker;
                        window.dropoffMarker = dropoffMarker;
                        
                        drawRoutes();
                        window.isMapReady = true;
                    }}
                    
                    function drawRoutes() {{
                        directionsService.route({{
                            origin: driverMarker.getPosition(),
                            destination: pickupPos,
                            travelMode: 'DRIVING'
                        }}, (result, status) => {{
                            if (status === 'OK') {{
                                if (orangeRouteRenderer) orangeRouteRenderer.setMap(null);
                                orangeRouteRenderer = new google.maps.DirectionsRenderer({{
                                    map: map,
                                    suppressMarkers: true,
                                    polylineOptions: {{ strokeColor: '#FF9800', strokeWeight: 6 }}
                                }});
                                orangeRouteRenderer.setDirections(result);
                            }}
                        }});
                        
                        directionsService.route({{
                            origin: pickupPos,
                            destination: dropoffPos,
                            travelMode: 'DRIVING'
                        }}, (result, status) => {{
                            if (status === 'OK') {{
                                if (blueRouteRenderer) blueRouteRenderer.setMap(null);
                                blueRouteRenderer = new google.maps.DirectionsRenderer({{
                                    map: map,
                                    suppressMarkers: true,
                                    polylineOptions: {{ strokeColor: '#2196F3', strokeWeight: 6 }}
                                }});
                                blueRouteRenderer.setDirections(result);
                            }}
                        }});
                    }}
                    
                    window.updateDriverLocation = function(lat, lng, bearing) {{
                        if (!window.isMapReady || !driverMarker) return;
                        const newPos = new google.maps.LatLng(lat, lng);
                        driverMarker.setPosition(newPos);
                        directionsService.route({{
                            origin: newPos,
                            destination: pickupPos,
                            travelMode: 'DRIVING'
                        }}, (result, status) => {{
                            if (status === 'OK' && orangeRouteRenderer) orangeRouteRenderer.setDirections(result);
                        }});
                    }};
                    
                    window.changeRouteToDropoff = function() {{
                        if (!window.isMapReady) return;
                        directionsService.route({{
                            origin: driverMarker.getPosition(),
                            destination: dropoffPos,
                            travelMode: 'DRIVING'
                        }}, (result, status) => {{
                            if (status === 'OK' && orangeRouteRenderer) orangeRouteRenderer.setDirections(result);
                        }});
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
                    
                    window.recenterOnDriver = function() {{
                        if (!window.isMapReady || !driverMarker) return;
                        map.setCenter(driverMarker.getPosition());
                        map.setZoom(17);
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
                    
                    google.maps.event.addDomListener(window, 'load', initMap);
                </script>
            </head>
            <body><div id='map'></div></body>
            </html>";
        }

        private async Task CheckMapReady()
        {
            try
            {
                int attempts = 0;
                var mapLoadCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                while (attempts < 15 && !mapLoadCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await RideMapView.EvaluateJavaScriptAsync("window.isMapReady === true").WaitAsync(TimeSpan.FromSeconds(2));
                        if (result?.ToString() == "true")
                        {
                            _isMapLoaded = true;
                            MainThread.BeginInvokeOnMainThread(() => MapBlurOverlay.IsVisible = false);
                            Debug.WriteLine("Map loaded successfully");

                            _etaUpdateTimer = new Timer(async (state) => await UpdateETADisplay(), null, 0, 5000);
                            if (_currentLocation != null) await UpdateLocationOnMap();
                            return;
                        }
                    }
                    catch { }

                    attempts++;
                    await Task.Delay(500);
                }

                _isMapLoaded = true;
                MainThread.BeginInvokeOnMainThread(() => MapBlurOverlay.IsVisible = false);
            }
            catch
            {
                _isMapLoaded = true;
                MainThread.BeginInvokeOnMainThread(() => MapBlurOverlay.IsVisible = false);
            }
        }

        private async Task StartRealTimeTracking()
        {
            try
            {
                _isTrackingLocation = true;
                _gpsCancellationTokenSource = new CancellationTokenSource();
                _locationUpdateTimer = new Timer(async (state) =>
                {
                    if (_isTrackingLocation && !_gpsCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await UpdateCurrentLocationRealTime();
                    }
                }, null, 0, 2000);
            }
            catch { }
        }

        private async Task UpdateCurrentLocationRealTime()
        {
            try
            {
                var request = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(2));
                var location = await Geolocation.GetLocationAsync(request);
                if (location != null)
                {
                    if (_previousLocation != null)
                    {
                        _currentBearing = CalculateBearing(_previousLocation.Latitude, _previousLocation.Longitude, location.Latitude, location.Longitude);
                    }
                    _previousLocation = _currentLocation;
                    _currentLocation = location;
                    if (_isMapLoaded)
                    {
                        await UpdateLocationOnMap();
                        await UpdateDriverLocationInFirebase();
                    }
                    await UpdateETADisplay();
                }
            }
            catch { }
        }

        private double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
        {
            var dLon = (lon2 - lon1) * Math.PI / 180;
            lat1 = lat1 * Math.PI / 180;
            lat2 = lat2 * Math.PI / 180;
            var y = Math.Sin(dLon) * Math.Cos(lat2);
            var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            var brng = Math.Atan2(y, x);
            return (brng * 180 / Math.PI + 360) % 360;
        }

        private async Task UpdateLocationOnMap()
        {
            if (_currentLocation == null || !_isMapLoaded) return;
            try
            {
                string jsCode = $@"
                    if (window.updateDriverLocation && window.isMapReady) {{
                        window.updateDriverLocation({_currentLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                   {_currentLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                   {_currentBearing.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                    }}";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch { }
        }

        private async Task UpdateETADisplay()
        {
            if (_currentLocation == null || !_isMapLoaded) return;
            try
            {
                Location destination = _currentState == TripState.GoingToPickup ? _pickupLocation : _dropoffLocation;
                if (destination == null) return;
                double distance = CalculateDistance(_currentLocation.Latitude, _currentLocation.Longitude, destination.Latitude, destination.Longitude);
                double avgSpeed = distance < 1.0 ? 10.0 : (distance > 5.0 ? 25.0 : 20.0);
                double etaMinutes = (distance / avgSpeed) * 60 * 1.2;
                string etaText = etaMinutes < 1 ? "<1 min" : $"{Math.Ceiling(etaMinutes)} mins";
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    EtaLabel.Text = $"ETA: {etaText}";
                    DistanceLabel.Text = $"{distance:F1} km";
                });
            }
            catch { }
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private async Task UpdateDriverLocationInFirebase()
        {
            if (_currentLocation == null) return;
            try
            {
                string driverId = AppSession.GetDriverId();
                if (!string.IsNullOrEmpty(driverId))
                {
                    await _firebaseConnection.UpdateDriverLocationAsync(driverId, _currentLocation.Latitude, _currentLocation.Longitude, _currentBearing);
                }
            }
            catch { }
        }

        private void UpdateButtonTexts()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (_currentState)
                {
                    case TripState.GoingToPickup:
                        StatusLabel.Text = "Going to Pickup Location";
                        StatusSubLabel.Text = "Follow the navigation route";
                        LeftActionButton.Text = "Cancel";
                        LeftActionButton.BackgroundColor = Color.FromArgb("#F44336");
                        RightActionButton.Text = "Arrived";
                        RightActionButton.BackgroundColor = Color.FromArgb("#0288D1");
                        break;
                    case TripState.ArrivedAtPickup:
                        StatusLabel.Text = "Arrived at Pickup";
                        StatusSubLabel.Text = "Passenger should be boarding";
                        LeftActionButton.Text = "Cancel";
                        LeftActionButton.BackgroundColor = Color.FromArgb("#F44336");
                        RightActionButton.Text = "Start Ride";
                        RightActionButton.BackgroundColor = Color.FromArgb("#2E7D32");
                        break;
                    case TripState.GoingToDropoff:
                        StatusLabel.Text = "Trip in Progress";
                        StatusSubLabel.Text = "Enroute to destination";
                        LeftActionButton.Text = "Emergency";
                        LeftActionButton.BackgroundColor = Color.FromArgb("#FF9800");
                        RightActionButton.Text = "End Ride";
                        RightActionButton.BackgroundColor = Color.FromArgb("#2E7D32");
                        break;
                    case TripState.Completed:
                        StatusLabel.Text = "Trip Completed";
                        StatusSubLabel.Text = "Proceed to payment";
                        LeftActionButton.Text = "Summary";
                        LeftActionButton.BackgroundColor = Color.FromArgb("#2196F3");
                        RightActionButton.Text = "Go to Payment";
                        RightActionButton.BackgroundColor = Color.FromArgb("#4CAF50");
                        break;
                }
            });
        }

        private async void OnLeftActionButtonClicked(object sender, EventArgs e)
        {
            switch (_currentState)
            {
                case TripState.GoingToPickup:
                case TripState.ArrivedAtPickup:
                    await ShowCancelConfirmationModal();
                    break;
                case TripState.GoingToDropoff:
                    await ShowEmergencyCallConfirmation();
                    break;
                case TripState.Completed:
                    await ShowRideSummaryModal();
                    break;
            }
        }

        private async void OnRightActionButtonClicked(object sender, EventArgs e)
        {
            switch (_currentState)
            {
                case TripState.GoingToPickup:
                    await ShowArrivedConfirmationModal();
                    break;
                case TripState.ArrivedAtPickup:
                    await ShowStartRideConfirmationModal();
                    break;
                case TripState.GoingToDropoff:
                    await ShowEndRideConfirmationModal();
                    break;
                case TripState.Completed:
                    await GoToPaymentPage();
                    break;
            }
        }

        private async Task ShowCancelConfirmationModal()
        {
            _isModalOpen = true;
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = "complaint.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Cancel Ride?", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#DC3545"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Are you sure you want to cancel this ride?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "NO", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(0),
                                new Button { Text = "YES, CANCEL", BackgroundColor = Color.FromArgb("#DC3545"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(1)
                            }
                        }
                    }
                }
            };
            var modalPage = new StartedTripCancelModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public async void CancelRideConfirmed()
        {
            _isModalOpen = false;
            StopAllTracking();
            string driverId = AppSession.GetDriverId();
            bool success = await _firebaseConnection.CancelRideRequestAsync(_rideRequestId, $"Driver: {driverId}");
            if (success)
            {
                await ShowModal("Ride Cancelled", "The ride has been cancelled successfully.", "success", true);
                await Navigation.PopModalAsync();
            }
            else
            {
                await ShowModal("Error", "Failed to cancel ride. Please try again.", "error", false);
            }
        }

        private async Task ShowEmergencyCallConfirmation()
        {
            _isModalOpen = true;
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = "signal.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Emergency", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#FF9800"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "This will call emergency services (911). Are you sure?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "CANCEL", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(0),
                                new Button { Text = "CALL 911", BackgroundColor = Color.FromArgb("#FF9800"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(1)
                            }
                        }
                    }
                }
            };
            var modalPage = new StartedTripEmergencyModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public async void CallEmergency()
        {
            _isModalOpen = false;
            try
            {
                if (PhoneDialer.Default.IsSupported)
                {
                    PhoneDialer.Open("911");
                    await ShowModal("Emergency", "Calling 911...", "info", false);
                }
                else
                {
                    await ShowModal("Not Supported", "Phone calling is not supported on this device.", "warning", false);
                }
            }
            catch
            {
                await ShowModal("Error", "Cannot make call at this time.", "error", false);
            }
        }

        private async Task ShowRideSummaryModal()
        {
            var summaryPage = new RideSummaryPage(_currentRideRequest);
            await Navigation.PushModalAsync(summaryPage);
        }

        private async Task ShowArrivedConfirmationModal()
        {
            _isModalOpen = true;
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = "happy.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Arrived at Pickup", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2E7D32"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Have you arrived at the pickup location?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "NOT YET", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(0),
                                new Button { Text = "YES, ARRIVED", BackgroundColor = Color.FromArgb("#2E7D32"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(1)
                            }
                        }
                    }
                }
            };
            var modalPage = new StartedTripArrivedModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public async void ConfirmArrived()
        {
            _isModalOpen = false;
            bool success = await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "Status", "Arrived at Pickup");
            if (success)
            {
                _currentState = TripState.ArrivedAtPickup;
                UpdateButtonTexts();
                await ShowModal("Arrived", "You have arrived at pickup location.", "success", false);
            }
            else
            {
                await ShowModal("Error", "Failed to update status.", "error", false);
            }
        }

        private async Task ShowStartRideConfirmationModal()
        {
            _isModalOpen = true;
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = "tricycle.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Start Ride", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2E7D32"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Ready to start the trip?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "NOT YET", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(0),
                                new Button { Text = "START RIDE", BackgroundColor = Color.FromArgb("#2E7D32"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(1)
                            }
                        }
                    }
                }
            };
            var modalPage = new StartedTripStartRideModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public async void ConfirmStartRide()
        {
            _isModalOpen = false;
            bool success = await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "Status", "Trip Started");
            if (success)
            {
                _currentState = TripState.GoingToDropoff;
                UpdateButtonTexts();
                await ChangeNavigationToDropoff();
                await ShowModal("Ride Started", "Trip has started. Drive safely.", "success", false);
            }
            else
            {
                await ShowModal("Error", "Failed to start ride.", "error", false);
            }
        }

        private async Task ShowEndRideConfirmationModal()
        {
            _isModalOpen = true;
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = "complete.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "End Ride", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2E7D32"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Have you arrived at the destination?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "NOT YET", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(0),
                                new Button { Text = "END RIDE", BackgroundColor = Color.FromArgb("#2E7D32"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(1)
                            }
                        }
                    }
                }
            };
            var modalPage = new StartedTripEndRideModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public async void ConfirmEndRide()
        {
            _isModalOpen = false;
            bool success = await _firebaseConnection.UpdateRideRequestFieldAsync(_rideRequestId, "Status", "Payment Pending");
            if (success)
            {
                _currentState = TripState.Completed;
                UpdateButtonTexts();
                StopAllTracking();
                await ShowModal("Ride Completed", "Trip completed successfully.", "success", false);
            }
            else
            {
                await ShowModal("Error", "Failed to end ride.", "error", false);
            }
        }

        private async Task GoToPaymentPage()
        {
            await Navigation.PushModalAsync(new Driver_WaitingPayment(_rideRequestId));
        }

        private async Task ChangeNavigationToDropoff()
        {
            if (!_isMapLoaded) return;
            try
            {
                string jsCode = "if (window.changeRouteToDropoff) window.changeRouteToDropoff();";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
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
                Debug.WriteLine($"Map type toggle error: {ex.Message}");
            }
        }

        private async void OnHeaderTapped(object sender, EventArgs e)
        {
            try
            {
                if (_showPickup)
                {
                    string jsCode = $@"
                        if (window.flyToLocation) {{
                            window.flyToLocation({_pickupLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                 {_pickupLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true);
                        }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    _showPickup = false;
                }
                else
                {
                    string jsCode = $@"
                        if (window.flyToLocation) {{
                            window.flyToLocation({_dropoffLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                 {_dropoffLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, false);
                        }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    _showPickup = true;
                }
            }
            catch { }
        }

        private async void OnLocationButtonTapped(object sender, EventArgs e)
        {
            try
            {
                string jsCode = "if (window.recenterOnDriver) window.recenterOnDriver();";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch { }
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            try
            {
                bool hasActiveRide = _currentRideRequest != null && _currentRideRequest.Status != "Completed" && _currentRideRequest.Status != "Cancelled";
                if (hasActiveRide)
                {
                    await ShowBackConfirmationModal();
                }
                else
                {
                    StopAllTracking();
                    await Navigation.PopModalAsync();
                }
            }
            catch
            {
                await Navigation.PopModalAsync();
            }
        }

        private async Task ShowBackConfirmationModal()
        {
            _isModalOpen = true;
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = "complaint.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Active Ride", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#FF9800"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "You have an active ride. Go back?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "NO, STAY", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(0),
                                new Button { Text = "YES, GO BACK", BackgroundColor = Color.FromArgb("#FF9800"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }.WithStartedTripGridColumn(1)
                            }
                        }
                    }
                }
            };
            var modalPage = new StartedTripBackModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public async void ConfirmBack()
        {
            _isModalOpen = false;
            StopAllTracking();
            await Navigation.PopModalAsync();
        }

        private void StopAllTracking()
        {
            _isTrackingLocation = false;
            _gpsCancellationTokenSource?.Cancel();
            _etaUpdateTimer?.Dispose();
            _locationUpdateTimer?.Dispose();
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            if (_isModalOpen) return;
            _isModalOpen = true;
            string iconSource = type == "success" ? "happy.png" : type == "warning" ? "signal.png" : type == "info" ? "phone.png" : "sad.png";
            Color iconColor = type == "success" ? Color.FromArgb("#2E7D32") : type == "warning" ? Color.FromArgb("#FF9800") : type == "info" ? Color.FromArgb("#2196F3") : Color.FromArgb("#D32F2F");
            var blurOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), Opacity = 0 };
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
                        new Image { Source = iconSource, HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = iconColor, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = message, FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center },
                        new Button { Text = "OK", BackgroundColor = iconColor, TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45 }
                    }
                }
            };
            var modalPage = new StartedTripModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modalPage);
            await blurOverlay.FadeTo(1, 250);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public void CloseModal(bool closePage)
        {
            _isModalOpen = false;
            if (closePage) MainThread.BeginInvokeOnMainThread(async () => await Navigation.PopModalAsync());
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopAllTracking();
        }
    }

    public class StartedTripModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_StartedTrip _parentPage;

        public StartedTripModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var button = FindButton(modalContent.Content);
            if (button != null)
            {
                button.Clicked += async (s, e) => { button.IsEnabled = false; await AnimateModalExit(); };
            }
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private Button FindButton(object element)
        {
            if (element is Button btn) return btn;
            if (element is Layout layout)
            {
                foreach (var child in layout.Children)
                {
                    var result = FindButton(child);
                    if (result != null) return result;
                }
            }
            if (element is ContentView cv && cv.Content != null) return FindButton(cv.Content);
            if (element is Frame frame && frame.Content != null) return FindButton(frame.Content);
            return null;
        }

        private async Task AnimateModalExit()
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            _parentPage.CloseModal(_closePageOnOk);
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit());
            return true;
        }
    }

    public class StartedTripCancelModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_StartedTrip _parentPage;

        public StartedTripCancelModalPage(Frame modalContent, Grid blurOverlay, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;
            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            confirmButton.Clicked += async (s, e) => { confirmButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool cancel)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            if (cancel) _parentPage.CancelRideConfirmed();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public class StartedTripEmergencyModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_StartedTrip _parentPage;

        public StartedTripEmergencyModalPage(Frame modalContent, Grid blurOverlay, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var callButton = buttonGrid.Children[1] as Button;
            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            callButton.Clicked += async (s, e) => { callButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool call)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            if (call) _parentPage.CallEmergency();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public class StartedTripArrivedModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_StartedTrip _parentPage;

        public StartedTripArrivedModalPage(Frame modalContent, Grid blurOverlay, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;
            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            confirmButton.Clicked += async (s, e) => { confirmButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            if (confirm) _parentPage.ConfirmArrived();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public class StartedTripStartRideModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_StartedTrip _parentPage;

        public StartedTripStartRideModalPage(Frame modalContent, Grid blurOverlay, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;
            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            confirmButton.Clicked += async (s, e) => { confirmButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            if (confirm) _parentPage.ConfirmStartRide();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public class StartedTripEndRideModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_StartedTrip _parentPage;

        public StartedTripEndRideModalPage(Frame modalContent, Grid blurOverlay, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;
            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            confirmButton.Clicked += async (s, e) => { confirmButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            if (confirm) _parentPage.ConfirmEndRide();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public class StartedTripBackModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_StartedTrip _parentPage;

        public StartedTripBackModalPage(Frame modalContent, Grid blurOverlay, Driver_StartedTrip parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;
            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var confirmButton = buttonGrid.Children[1] as Button;
            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            confirmButton.Clicked += async (s, e) => { confirmButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool confirm)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            if (confirm) _parentPage.ConfirmBack();
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public static class StartedTripGridExtensions
    {
        public static Button WithStartedTripGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }

    public class RideSummaryPage : ContentPage
    {
        public RideSummaryPage(RideRequest rideRequest)
        {
            Title = "Ride Summary";
            BackgroundColor = Color.FromArgb("#F5F5F5");

            var tripDetailsGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Star } },
                RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } },
                RowSpacing = 10
            };

            tripDetailsGrid.Add(new Label { Text = "DISTANCE", TextColor = Color.FromArgb("#666666"), FontSize = 12, FontAttributes = FontAttributes.Bold }, 0, 0);
            tripDetailsGrid.Add(new Label { Text = $"{rideRequest.Distance} km", TextColor = Colors.Black, FontSize = 14, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End }, 1, 0);
            tripDetailsGrid.Add(new Label { Text = "FARE", TextColor = Color.FromArgb("#666666"), FontSize = 12, FontAttributes = FontAttributes.Bold }, 0, 1);
            tripDetailsGrid.Add(new Label { Text = $"₱{rideRequest.Fare}", TextColor = Colors.Black, FontSize = 18, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End }, 1, 1);
            tripDetailsGrid.Add(new Label { Text = "STATUS", TextColor = Color.FromArgb("#666666"), FontSize = 12, FontAttributes = FontAttributes.Bold }, 0, 2);
            tripDetailsGrid.Add(new Label { Text = "Waiting For Payment", TextColor = Color.FromArgb("#2E7D32"), FontSize = 14, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End }, 1, 2);

            Content = new ScrollView
            {
                Content = new StackLayout
                {
                    Spacing = 20,
                    Padding = 20,
                    Children =
                    {
                        new Frame
                        {
                            BackgroundColor = Color.FromArgb("#2E7D32"),
                            CornerRadius = 15,
                            Padding = 20,
                            HasShadow = true,
                            Content = new StackLayout
                            {
                                HorizontalOptions = LayoutOptions.Center,
                                Children =
                                {
                                    new Label { Text = "✓ TRIP COMPLETED", TextColor = Colors.White, FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center },
                                    new Label { Text = "Ride Summary", TextColor = Colors.White, FontSize = 16, HorizontalOptions = LayoutOptions.Center }
                                }
                            }
                        },
                        new Frame
                        {
                            BackgroundColor = Colors.White,
                            CornerRadius = 15,
                            Padding = 20,
                            HasShadow = true,
                            Content = new StackLayout
                            {
                                Spacing = 15,
                                Children =
                                {
                                    new StackLayout
                                    {
                                        Spacing = 5,
                                        Children =
                                        {
                                            new Label { Text = "PASSENGER", TextColor = Color.FromArgb("#666666"), FontSize = 12, FontAttributes = FontAttributes.Bold },
                                            new Label { Text = rideRequest.CommuterName ?? "Unknown", TextColor = Colors.Black, FontSize = 16, FontAttributes = FontAttributes.Bold }
                                        }
                                    },
                                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E0E0E0") },
                                    new StackLayout
                                    {
                                        Spacing = 10,
                                        Children =
                                        {
                                            new Label { Text = "ROUTE", TextColor = Color.FromArgb("#666666"), FontSize = 12, FontAttributes = FontAttributes.Bold },
                                            new StackLayout
                                            {
                                                Spacing = 5,
                                                Children =
                                                {
                                                    new StackLayout
                                                    {
                                                        Orientation = StackOrientation.Horizontal,
                                                        Spacing = 10,
                                                        Children =
                                                        {
                                                            new Label { Text = "📍", FontSize = 16 },
                                                            new Label { Text = rideRequest.PickupLocation ?? "Unknown", TextColor = Colors.Black, FontSize = 14, LineBreakMode = LineBreakMode.WordWrap }
                                                        }
                                                    },
                                                    new StackLayout
                                                    {
                                                        Orientation = StackOrientation.Horizontal,
                                                        Spacing = 10,
                                                        Children =
                                                        {
                                                            new Label { Text = "🎯", FontSize = 16 },
                                                            new Label { Text = rideRequest.DropoffLocation ?? "Unknown", TextColor = Colors.Black, FontSize = 14, LineBreakMode = LineBreakMode.WordWrap }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    },
                                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E0E0E0") },
                                    tripDetailsGrid
                                }
                            }
                        },
                        new Button
                        {
                            Text = "PROCEED TO PAYMENT",
                            BackgroundColor = Color.FromArgb("#2E7D32"),
                            TextColor = Colors.White,
                            FontSize = 16,
                            FontAttributes = FontAttributes.Bold,
                            CornerRadius = 12,
                            HeightRequest = 55,
                            Margin = new Thickness(0, 10, 0, 0)
                        }
                    }
                }
            };

            var proceedButton = (Button)((StackLayout)((ScrollView)Content).Content).Children[^1];
            proceedButton.Clicked += async (s, e) =>
            {
                await Navigation.PopModalAsync();
                await Navigation.PushModalAsync(new Driver_WaitingPayment(rideRequest.Id));
            };
        }
    }
}