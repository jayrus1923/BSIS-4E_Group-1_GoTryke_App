using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using ServiceCo.Firebase;
using ServiceCo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class Commuter_SelectRide : ContentPage
    {
        private string selectedRide = "12";
        private string passengerType = "Regular";
        private string paymentMethod = "Cash";
        private string pickupLocation, dropoffLocation;
        private bool trackingActive = false;
        private double actualDistance = 0;
        private double actualDuration = 0;
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();

        private double _panelExpandedHeight;
        private double _panelCollapsedHeight = 80;
        private bool _isPanelExpanded = true;
        private double _lastPanelHeight;
        private bool _isDragging = false;
        private bool _isSatelliteView = false;
        private double _startY;
        private double _startHeight;

        private double _pickupLat, _pickupLng, _dropoffLat, _dropoffLng;
        private Location _currentLocation;
        private CancellationTokenSource _gpsCancellationTokenSource;
        private bool _isTrackingLocation = false;

        private FareConfiguration _fareConfig;
        private IDisposable _fareConfigListener;
        private bool _hasShownUpdateNotification = false;
        private bool _isModalOpen = false;

        private const string GOOGLE_MAPS_API_KEY = Constants.GoogleMapsApiKey;
        private string commuterId;
        private bool isStudentSelected = false;
        private bool isSeniorSelected = false;
        private bool isPWDSelected = false;
        private string currentSelectedType = "";

        public Commuter_SelectRide(string pickup, string dropoff, double pickupLat, double pickupLng, double dropoffLat, double dropoffLng)
        {
            InitializeComponent();
            pickupLocation = pickup;
            dropoffLocation = dropoff;
            _pickupLat = pickupLat;
            _pickupLng = pickupLng;
            _dropoffLat = dropoffLat;
            _dropoffLng = dropoffLng;
            commuterId = AppSession.GetCommuterId();
            SetupUI();
        }

        private async void SetupUI()
        {
            PickupDisplayLabel.Text = FormatAddress(pickupLocation, 20);
            DropoffDisplayLabel.Text = FormatAddress(dropoffLocation, 20);
            PanelDistanceLabel.Text = "-- km";
            PanelTimeLabel.Text = "-- mins";
            TotalFareLabel.Text = "₱--";
            Fare12Label.Text = "₱--";
            Fare34Label.Text = "₱--";

            DeselectAllPassengerTypes();

            await LoadFareConfiguration();
            StartFareConfigListener();

            await LoadDefaultPaymentMethod();
            MapBlurOverlay.IsVisible = true;
        }

        private async Task LoadFareConfiguration()
        {
            try
            {
                _fareConfig = await _firebaseConnection.GetFareConfigurationAsync();
                UpdateDiscountDisplay();
                UpdateFareDisplay();
            }
            catch
            {
                _fareConfig = new FareConfiguration();
                UpdateDiscountDisplay();
            }
        }

        private void UpdateDiscountDisplay()
        {
            if (_fareConfig == null) return;

            int studentDiscount = _fareConfig.studentDiscount > 0 ? (int)_fareConfig.studentDiscount : 20;
            int seniorDiscount = _fareConfig.seniorDiscount > 0 ? (int)_fareConfig.seniorDiscount : 20;
            int pwdDiscount = _fareConfig.pwdDiscount > 0 ? (int)_fareConfig.pwdDiscount : 20;

            StudentDiscountLabel.Text = $"{studentDiscount}% off";
            SeniorDiscountLabel.Text = $"{seniorDiscount}% off";
            PWDDiscountLabel.Text = $"{pwdDiscount}% off";
        }

        private void StartFareConfigListener()
        {
            try
            {
                _fareConfigListener?.Dispose();

                _firebaseConnection.ListenForFareConfigurationChanges((updatedConfig) =>
                {
                    var oldConfig = _fareConfig;
                    _fareConfig = updatedConfig;

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        UpdateDiscountDisplay();
                        UpdateFareDisplay();

                        bool hasChanges = false;
                        var changes = new List<string>();

                        if (oldConfig?.minDistanceKm != updatedConfig.minDistanceKm)
                        {
                            hasChanges = true;
                            changes.Add($"Free km: {oldConfig?.minDistanceKm ?? 3}km → {updatedConfig.minDistanceKm}km");
                        }
                        if (oldConfig?.minDistanceFare != updatedConfig.minDistanceFare)
                        {
                            hasChanges = true;
                            changes.Add($"Min fare: ₱{oldConfig?.minDistanceFare ?? 35} → ₱{updatedConfig.minDistanceFare}");
                        }
                        if (oldConfig?.excessRatePerKm != updatedConfig.excessRatePerKm)
                        {
                            hasChanges = true;
                            changes.Add($"Rate/km: ₱{oldConfig?.excessRatePerKm ?? 10} → ₱{updatedConfig.excessRatePerKm}");
                        }
                        if (oldConfig?.bookingFee != updatedConfig.bookingFee)
                        {
                            hasChanges = true;
                            changes.Add($"Booking fee: ₱{oldConfig?.bookingFee ?? 5} → ₱{updatedConfig.bookingFee}");
                        }
                        if (oldConfig?.multipliers?.seater12 != updatedConfig.multipliers?.seater12)
                        {
                            hasChanges = true;
                            changes.Add($"1-2 Seater: {oldConfig?.multipliers?.seater12 ?? 1.0}x → {updatedConfig.multipliers?.seater12}x");
                        }
                        if (oldConfig?.multipliers?.seater34 != updatedConfig.multipliers?.seater34)
                        {
                            hasChanges = true;
                            changes.Add($"3-4 Seater: {oldConfig?.multipliers?.seater34 ?? 1.2}x → {updatedConfig.multipliers?.seater34}x");
                        }
                        if (oldConfig?.surge?.enabled != updatedConfig.surge?.enabled)
                        {
                            hasChanges = true;
                            changes.Add($"Surge: {(oldConfig?.surge?.enabled == true ? "On" : "Off")} → {(updatedConfig.surge?.enabled == true ? "On" : "Off")}");
                        }
                        if (oldConfig?.night?.enabled != updatedConfig.night?.enabled)
                        {
                            hasChanges = true;
                            changes.Add($"Night: {(oldConfig?.night?.enabled == true ? "On" : "Off")} → {(updatedConfig.night?.enabled == true ? "On" : "Off")}");
                        }
                        if (oldConfig?.holiday?.enabled != updatedConfig.holiday?.enabled)
                        {
                            hasChanges = true;
                            changes.Add($"Holiday: {(oldConfig?.holiday?.enabled == true ? "On" : "Off")} → {(updatedConfig.holiday?.enabled == true ? "On" : "Off")}");
                        }

                        if (hasChanges && !_hasShownUpdateNotification)
                        {
                            _hasShownUpdateNotification = true;
                            await ShowFareUpdateModal(changes);
                            await Task.Delay(5000);
                            _hasShownUpdateNotification = false;
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting fare listener: {ex.Message}");
            }
        }

        private void DeselectAllPassengerTypes()
        {
            isStudentSelected = false;
            isSeniorSelected = false;
            isPWDSelected = false;
            UpdatePassengerTypeUI();
            passengerType = "Regular";
        }

        private void UpdatePassengerTypeUI()
        {
            StudentButton.BackgroundColor = isStudentSelected ? Color.FromArgb("#2E7D32") : Color.FromArgb("#F5F5F5");
            StudentButton.BorderColor = isStudentSelected ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0");
            StudentLabel.TextColor = isStudentSelected ? Colors.White : Colors.Black;
            StudentDiscountLabel.TextColor = isStudentSelected ? Colors.White : Color.FromArgb("#666666");

            SeniorButton.BackgroundColor = isSeniorSelected ? Color.FromArgb("#2E7D32") : Color.FromArgb("#F5F5F5");
            SeniorButton.BorderColor = isSeniorSelected ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0");
            SeniorLabel.TextColor = isSeniorSelected ? Colors.White : Colors.Black;
            SeniorDiscountLabel.TextColor = isSeniorSelected ? Colors.White : Color.FromArgb("#666666");

            PWDSeniorButton.BackgroundColor = isPWDSelected ? Color.FromArgb("#2E7D32") : Color.FromArgb("#F5F5F5");
            PWDSeniorButton.BorderColor = isPWDSelected ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0");
            PWDLabel.TextColor = isPWDSelected ? Colors.White : Colors.Black;
            PWDDiscountLabel.TextColor = isPWDSelected ? Colors.White : Color.FromArgb("#666666");

            bool hasSelection = isStudentSelected || isSeniorSelected || isPWDSelected;
            RegularIndicator.IsVisible = !hasSelection;

            if (isStudentSelected)
                passengerType = "Student";
            else if (isSeniorSelected)
                passengerType = "Senior";
            else if (isPWDSelected)
                passengerType = "PWD";
            else
                passengerType = "Regular";

            UpdateFareDisplay();
        }

        private async Task LoadDefaultPaymentMethod()
        {
            try
            {
                if (!string.IsNullOrEmpty(commuterId))
                {
                    string defaultMethod = await _firebaseConnection.GetDefaultPaymentMethodAsync(commuterId);
                    paymentMethod = defaultMethod;
                    UpdatePaymentDisplay();
                }
            }
            catch { }
        }

        private void UpdatePaymentDisplay()
        {
            switch (paymentMethod)
            {
                case "Cash":
                    PaymentIcon.Source = "cash.png";
                    SelectedPaymentLabel.Text = "Cash";
                    PaymentDescriptionLabel.Text = "Pay cash to the driver";
                    break;
                case "GCash":
                    PaymentIcon.Source = "gcash.png";
                    SelectedPaymentLabel.Text = "GCash";
                    PaymentDescriptionLabel.Text = "Scan QR code to pay";
                    break;
            }
        }

        private string FormatAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";
            var cleanAddress = address.Replace(", Malolos, Bulacan", "").Replace(", Bulacan", "").Replace("Malolos, ", "").Trim();
            if (cleanAddress.Length > maxLength)
                return cleanAddress.Substring(0, maxLength - 3) + "...";
            return cleanAddress;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                await LoadDefaultPaymentMethod();
                await LoadFareConfiguration();
                StartFareConfigListener();
                await LoadRouteAndMap();
                await StartRealTimeLocationTracking();
            }
            catch { }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopRealTimeLocationTracking();
            _fareConfigListener?.Dispose();
            _firebaseConnection.StopListeningForFareChanges();
            trackingActive = false;
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
                UpdateButtonPositions(120, 200);
            }
            else
            {
                SelectionPanel.HeightRequest = _panelCollapsedHeight;
                DetailsContent.IsVisible = false;
                ShowDetailsButton.IsVisible = true;
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
        }

        private async Task LoadRouteAndMap()
        {
            try
            {
                var routeInfo = await GetRouteInfoAsync((_pickupLat, _pickupLng), (_dropoffLat, _dropoffLng));
                actualDistance = routeInfo.distanceKm;
                actualDuration = routeInfo.durationMinutes;
                UpdateRouteInfo(actualDistance, actualDuration);
                await LoadMap((_pickupLat, _pickupLng), (_dropoffLat, _dropoffLng));
                MapBlurOverlay.IsVisible = false;
            }
            catch
            {
                MapBlurOverlay.IsVisible = false;
            }
        }

        private async Task<Location> GetCurrentLocationAsync()
        {
            try
            {
                var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5));
                return await Geolocation.GetLocationAsync(request);
            }
            catch
            {
                return await Geolocation.GetLastKnownLocationAsync();
            }
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
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private void UpdateRouteInfo(double distance, double duration)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PanelDistanceLabel.Text = $"{distance:F1} km";
                PanelTimeLabel.Text = $"{duration} mins";
                UpdateFareDisplay();
            });
        }

        private async Task LoadMap((double, double) pickupCoord, (double, double) dropoffCoord)
        {
            try
            {
                var html = GenerateGoogleMapHtml(pickupCoord, dropoffCoord);
                RideMapView.Source = new HtmlWebViewSource { Html = html };
                await Task.Delay(1000);
            }
            catch { }
        }

        private string GenerateGoogleMapHtml((double, double) pickupCoord, (double, double) dropoffCoord)
        {
            string mapType = _isSatelliteView ? "satellite" : "roadmap";
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />
    <script src='https://maps.googleapis.com/maps/api/js?key={GOOGLE_MAPS_API_KEY}&libraries=places'></script>
    <style>
        html, body {{ margin:0; padding:0; height:100%; width:100%; overflow:hidden; }}
        #map {{ height:100%; width:100%; }}
        .gm-style .gm-style-cc {{ display:none; }}
        a[href^='https://maps.google.com/maps'] {{ display:none !important; }}
    </style>
</head>
<body>
    <div id='map'></div>
    <script>
        let map;
        let userMarker = null;
        let pickupMarker = null;
        let dropoffMarker = null;
        let routeLayer = null;
        
        const pickupLat = {pickupCoord.Item1};
        const pickupLng = {pickupCoord.Item2};
        const dropoffLat = {dropoffCoord.Item1};
        const dropoffLng = {dropoffCoord.Item2};
        
        function initMap() {{
            const bounds = new google.maps.LatLngBounds();
            bounds.extend({{ lat: pickupLat, lng: pickupLng }});
            bounds.extend({{ lat: dropoffLat, lng: dropoffLng }});
            
            map = new google.maps.Map(document.getElementById('map'), {{
                center: {{ lat: (pickupLat + dropoffLat) / 2, lng: (pickupLng + dropoffLng) / 2 }},
                zoom: 13,
                mapTypeId: '{mapType}',
                mapTypeControl: false,
                fullscreenControl: false,
                streetViewControl: false,
                zoomControl: true,
                zoomControlOptions: {{
                    position: google.maps.ControlPosition.RIGHT_TOP
                }}
            }});
            
            map.fitBounds(bounds);
            map.setZoom(map.getZoom() - 0.5);
            
            pickupMarker = new google.maps.Marker({{
                position: {{ lat: pickupLat, lng: pickupLng }},
                map: map,
                icon: {{
                    url: 'https://maps.google.com/mapfiles/ms/icons/green-dot.png',
                    scaledSize: new google.maps.Size(44, 44)
                }},
                title: 'Pickup Location'
            }});
            
            dropoffMarker = new google.maps.Marker({{
                position: {{ lat: dropoffLat, lng: dropoffLng }},
                map: map,
                icon: {{
                    url: 'https://maps.google.com/mapfiles/ms/icons/red-dot.png',
                    scaledSize: new google.maps.Size(44, 44)
                }},
                title: 'Dropoff Location'
            }});
            
            window.map = map;
            window.pickupMarker = pickupMarker;
            window.dropoffMarker = dropoffMarker;
            
            drawRoute();
        }}
        
        function drawRoute() {{
            const directionsService = new google.maps.DirectionsService();
            const request = {{
                origin: {{ lat: pickupLat, lng: pickupLng }},
                destination: {{ lat: dropoffLat, lng: dropoffLng }},
                travelMode: google.maps.TravelMode.DRIVING
            }};
            
            directionsService.route(request, function(result, status) {{
                if (status === 'OK') {{
                    if (routeLayer) {{
                        routeLayer.setMap(null);
                    }}
                    
                    const routePath = result.routes[0].overview_path;
                    
                    routeLayer = new google.maps.Polyline({{
                        path: routePath,
                        geodesic: true,
                        strokeColor: '#1976D2',
                        strokeOpacity: 0.9,
                        strokeWeight: 6
                    }});
                    routeLayer.setMap(map);
                }}
            }});
        }}
        
        window.updateLiveLocation = function(lat, lng) {{
            if (userMarker) {{
                userMarker.setPosition({{ lat: lat, lng: lng }});
            }} else {{
                userMarker = new google.maps.Marker({{
                    position: {{ lat: lat, lng: lng }},
                    map: map,
                    icon: {{
                        url: 'https://maps.google.com/mapfiles/ms/icons/blue-dot.png',
                        scaledSize: new google.maps.Size(40, 40)
                    }},
                    title: 'Your Location'
                }});
            }}
        }};
        
        window.centerOnUserLocation = function(lat, lng) {{
            if (!userMarker) {{
                userMarker = new google.maps.Marker({{
                    position: {{ lat: lat, lng: lng }},
                    map: map,
                    icon: {{
                        url: 'https://maps.google.com/mapfiles/ms/icons/blue-dot.png',
                        scaledSize: new google.maps.Size(40, 40)
                    }}
                }});
            }} else {{
                userMarker.setPosition({{ lat: lat, lng: lng }});
            }}
            userMarker.setAnimation(google.maps.Animation.BOUNCE);
            setTimeout(function() {{ userMarker.setAnimation(null); }}, 1500);
            map.panTo({{ lat: lat, lng: lng }});
            map.setZoom(17);
        }};
        
        window.flyToPickupDropoff = function(showPickup) {{
            if (showPickup && window.pickupMarker) {{
                map.panTo(window.pickupMarker.getPosition());
                map.setZoom(16);
                window.pickupMarker.setAnimation(google.maps.Animation.BOUNCE);
                setTimeout(function() {{ window.pickupMarker.setAnimation(null); }}, 1500);
            }} else if (!showPickup && window.dropoffMarker) {{
                map.panTo(window.dropoffMarker.getPosition());
                map.setZoom(16);
                window.dropoffMarker.setAnimation(google.maps.Animation.BOUNCE);
                setTimeout(function() {{ window.dropoffMarker.setAnimation(null); }}, 1500);
            }}
        }};
        
        window.toggleSatelliteView = function(enableSatellite) {{
            if (enableSatellite) {{
                map.setMapTypeId('satellite');
                map.setTilt(60);
            }} else {{
                map.setMapTypeId('roadmap');
                map.setTilt(0);
            }}
        }};
        
        google.maps.event.addDomListener(window, 'load', initMap);
    </script>
</body>
</html>";
        }

        private async void OnMapTypeTapped(object sender, EventArgs e)
        {
            _isSatelliteView = !_isSatelliteView;

            if (_isSatelliteView)
            {
                MapTypeIcon.Source = "two_dimentional.png";
            }
            else
            {
                MapTypeIcon.Source = "three_dimentional.png";
            }

            try
            {
                string jsCode = $"if (window.toggleSatelliteView) {{ window.toggleSatelliteView({_isSatelliteView.ToString().ToLower()}); }}";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch { }
        }

        private async Task StartRealTimeLocationTracking()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                if (status == PermissionStatus.Granted)
                {
                    await UpdateLocationOnMap();
                    _isTrackingLocation = true;
                    _gpsCancellationTokenSource = new CancellationTokenSource();
                    _ = Task.Run(async () =>
                    {
                        while (_isTrackingLocation && !_gpsCancellationTokenSource.Token.IsCancellationRequested)
                        {
                            try
                            {
                                await UpdateLocationOnMap();
                                await Task.Delay(3000);
                            }
                            catch
                            {
                                await Task.Delay(5000);
                            }
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
                var location = await GetCurrentLocationAsync();
                if (location != null)
                {
                    _currentLocation = location;
                    string jsCode = $"if (window.updateLiveLocation) {{ window.updateLiveLocation({location.Latitude}, {location.Longitude}); }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                }
            }
            catch { }
        }

        private void StopRealTimeLocationTracking()
        {
            _isTrackingLocation = false;
            _gpsCancellationTokenSource?.Cancel();
        }

        private bool _showPickup = true;
        private async void OnHeaderTapped(object sender, EventArgs e)
        {
            try
            {
                string jsCode = $"if (window.flyToPickupDropoff) {{ window.flyToPickupDropoff({(_showPickup ? "true" : "false")}); }}";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
                _showPickup = !_showPickup;
            }
            catch { }
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
            catch
            {
                return false;
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

                        string jsCode = $@"
                if (window.flyToDriver) {{
                    window.flyToDriver();
                }} else if (window.map) {{
                    window.map.panTo({{ lat: {location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)} }});
                    window.map.setZoom(17);
                    
                    if (window.userMarker) {{
                        window.userMarker.setPosition({{ lat: {location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)} }});
                        window.userMarker.setAnimation(google.maps.Animation.BOUNCE);
                        setTimeout(function() {{ if (window.userMarker) window.userMarker.setAnimation(null); }}, 1500);
                    }} else {{
                        window.userMarker = new google.maps.Marker({{
                            position: {{ lat: {location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)} }},
                            map: window.map,
                            icon: {{
                                url: 'https://maps.google.com/mapfiles/ms/icons/blue-dot.png',
                                scaledSize: new google.maps.Size(40, 40)
                            }},
                            title: 'Your Location'
                        }});
                        window.userMarker.setAnimation(google.maps.Animation.BOUNCE);
                        setTimeout(function() {{ if (window.userMarker) window.userMarker.setAnimation(null); }}, 1500);
                    }}
                }}";

                        await RideMapView.EvaluateJavaScriptAsync(jsCode);

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
                            Text = "Exit?",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Are you sure you want to exit?",
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
                                }.WithSelectRideGridColumn(0),
                                new Button
                                {
                                    Text = "EXIT",
                                    BackgroundColor = iconColor,
                                    TextColor = Colors.White,
                                    CornerRadius = 10,
                                    HeightRequest = 45,
                                    FontSize = 14,
                                    FontAttributes = FontAttributes.Bold
                                }.WithSelectRideGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new SelectRideExitModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(exitModal);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        public void CloseModal(bool closePage = false)
        {
            _isModalOpen = false;
            if (closePage)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    StopRealTimeLocationTracking();
                    _fareConfigListener?.Dispose();
                    _firebaseConnection.StopListeningForFareChanges();
                    if (Navigation.ModalStack.Count > 0)
                        await Navigation.PopModalAsync();
                    else
                        await Navigation.PopAsync();
                });
            }
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
                UpdateButtonPositions(targetLocationBackOffset, targetMapTypeOffset);
            });
            buttonAnimation.Commit(this, "ButtonAnimation", length: 300);
        }

        private void OnDragHandleTapped(object sender, EventArgs e)
        {
            if (!_isDragging) TogglePanel(!_isPanelExpanded);
        }

        private void OnShowDetailsTapped(object sender, EventArgs e) => TogglePanel(true);
        private void TogglePanel(bool expand) => AnimatePanel(expand);

        private void OnRideOptionTapped(object sender, EventArgs e)
        {
            if (sender is Frame tappedFrame) SelectRideOption(tappedFrame == RideOption12 ? "12" : "34");
        }

        private void SelectRideOption(string rideId)
        {
            selectedRide = rideId;
            RideOption12.BorderColor = rideId == "12" ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0");
            RideOption34.BorderColor = rideId == "34" ? Color.FromArgb("#2E7D32") : Color.FromArgb("#E0E0E0");
            BookButton.Text = rideId == "12" ? "BOOK 1-2 SEATER TRICYCLE" : "BOOK 3-4 SEATER TRICYCLE";
            UpdateFareDisplay();
        }

        private async void OnPassengerTypeTapped(object sender, EventArgs e)
        {
            var tappedType = sender == StudentButton ? "Student" :
                             sender == SeniorButton ? "Senior" : "PWD";

            if (tappedType == "Student") currentSelectedType = "Student";
            else if (tappedType == "Senior") currentSelectedType = "Senior";
            else if (tappedType == "PWD") currentSelectedType = "PWD";

            bool isVerified = await _firebaseConnection.IsUserVerifiedAsync(commuterId, tappedType);

            if (isVerified)
            {
                if (sender is Frame tappedFrame)
                {
                    if (tappedFrame == StudentButton)
                    {
                        isStudentSelected = !isStudentSelected;
                        if (isStudentSelected)
                        {
                            isSeniorSelected = false;
                            isPWDSelected = false;
                        }
                    }
                    else if (tappedFrame == SeniorButton)
                    {
                        isSeniorSelected = !isSeniorSelected;
                        if (isSeniorSelected)
                        {
                            isStudentSelected = false;
                            isPWDSelected = false;
                        }
                    }
                    else if (tappedFrame == PWDSeniorButton)
                    {
                        isPWDSelected = !isPWDSelected;
                        if (isPWDSelected)
                        {
                            isStudentSelected = false;
                            isSeniorSelected = false;
                        }
                    }
                    UpdatePassengerTypeUI();
                }
                return;
            }

            var pendingRequest = await _firebaseConnection.GetPendingVerificationAsync(commuterId);

            if (pendingRequest != null && pendingRequest.UserType == tappedType && pendingRequest.Status == "pending")
            {
                await ShowPendingVerificationModal(tappedType);
                return;
            }

            if (pendingRequest != null && pendingRequest.UserType != tappedType && pendingRequest.Status == "pending")
            {
                await ShowOtherTypePendingModal(pendingRequest.UserType, tappedType);
                return;
            }

            await ShowVerificationRequiredModal(tappedType);
        }

        private async Task ShowOtherTypePendingModal(string pendingType, string requestedType)
        {
            await ShowModal(
                "Verification in Progress",
                $"You have a pending verification for {pendingType}. Please wait for it to be approved before applying for {requestedType}.",
                "info",
                false);
        }

        private async void OnChangePaymentClicked(object sender, EventArgs e)
        {
            try
            {
                StopRealTimeLocationTracking();
                await Navigation.PushModalAsync(new Commuter_PaymentOption());
                await StartRealTimeLocationTracking();
            }
            catch { }
        }

        private double CalculateFare(string rideType)
        {
            double distance = actualDistance > 0 ? actualDistance : 0;

            double minDistanceKm = 3;
            double minDistanceFare = 35;
            double excessRatePerKm = 10;
            double bookingFee = 5;
            double maxFare = 500;
            double multiplier = 1.0;

            if (_fareConfig != null)
            {
                minDistanceKm = _fareConfig.minDistanceKm > 0 ? _fareConfig.minDistanceKm : 3;
                minDistanceFare = _fareConfig.minDistanceFare > 0 ? _fareConfig.minDistanceFare : 35;
                excessRatePerKm = _fareConfig.excessRatePerKm > 0 ? _fareConfig.excessRatePerKm : 10;
                bookingFee = _fareConfig.bookingFee;
                maxFare = _fareConfig.maxFare > 0 ? _fareConfig.maxFare : 500;

                if (rideType == "12" && _fareConfig.multipliers?.seater12 > 0)
                    multiplier = _fareConfig.multipliers.seater12;
                else if (rideType == "34" && _fareConfig.multipliers?.seater34 > 0)
                    multiplier = _fareConfig.multipliers.seater34;
                else
                    multiplier = rideType == "34" ? 1.2 : 1.0;
            }

            double fare;
            if (distance <= minDistanceKm)
            {
                fare = minDistanceFare;
            }
            else
            {
                double excessKm = distance - minDistanceKm;
                fare = minDistanceFare + (excessKm * excessRatePerKm);
            }

            fare = fare * multiplier;

            if (IsSurgeTime() && _fareConfig?.surge?.multiplier > 0)
            {
                fare = fare * _fareConfig.surge.multiplier;
            }

            if (IsNightSurchargeTime() && _fareConfig?.night?.amount > 0)
            {
                fare = fare + _fareConfig.night.amount;
            }

            if (IsHolidaySurchargeTime() && _fareConfig?.holiday?.percentage > 0)
            {
                fare = fare * (1 + (_fareConfig.holiday.percentage / 100.0));
            }

            fare = fare + bookingFee;

            if (passengerType == "Student" || passengerType == "Senior" || passengerType == "PWD")
            {
                double discount = 0.80;
                if (_fareConfig != null)
                {
                    if (passengerType == "Senior")
                        discount = 1 - (_fareConfig.seniorDiscount / 100.0);
                    else if (passengerType == "PWD")
                        discount = 1 - (_fareConfig.pwdDiscount / 100.0);
                    else if (passengerType == "Student")
                        discount = 1 - (_fareConfig.studentDiscount / 100.0);
                }
                fare *= discount;
            }

            fare = Math.Min(fare, maxFare);
            return Math.Ceiling(fare);
        }

        private bool IsSurgeTime()
        {
            if (_fareConfig?.surge?.enabled != true) return false;

            var now = DateTime.Now;
            var currentTime = now.TimeOfDay;

            if (TimeSpan.TryParse(_fareConfig.surge.startTime, out var startTime) &&
                TimeSpan.TryParse(_fareConfig.surge.endTime, out var endTime))
            {
                if (endTime < startTime)
                {
                    return currentTime >= startTime || currentTime < endTime;
                }
                return currentTime >= startTime && currentTime < endTime;
            }
            return false;
        }

        private bool IsNightSurchargeTime()
        {
            if (_fareConfig?.night?.enabled != true) return false;

            var now = DateTime.Now;
            var currentTime = now.TimeOfDay;

            if (TimeSpan.TryParse(_fareConfig.night.startTime, out var startTime))
            {
                return currentTime >= startTime || currentTime < TimeSpan.FromHours(4);
            }
            return false;
        }

        private bool IsHolidaySurchargeTime()
        {
            return _fareConfig?.holiday?.enabled == true;
        }

        private void UpdateFareDisplay()
        {
            double fare12 = CalculateFare("12");
            double fare34 = CalculateFare("34");
            double currentFare = CalculateFare(selectedRide);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TotalFareLabel.Text = $"₱{currentFare:F0}";
                Fare12Label.Text = $"₱{fare12:F0}";
                Fare34Label.Text = $"₱{fare34:F0}";
            });
        }

        private async Task<RideRequestWithKey> CheckActiveRideWithPaymentPendingAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(commuterId)) return null;

                var activeRide = await _firebaseConnection.GetActiveRideForCommuterAsync(commuterId);
                if (activeRide != null)
                {
                    string[] activeStatuses = { "Pending", "Searching", "Accepted", "Picking Up", "Trip Started" };
                    if (activeStatuses.Contains(activeRide.Status))
                    {
                        return activeRide;
                    }

                    if (activeRide.Status == "Payment Pending")
                    {
                        return activeRide;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private async void OnBookRideClicked(object sender, EventArgs e)
        {
            if (_isModalOpen) return;

            try
            {
                var activeRide = await CheckActiveRideWithPaymentPendingAsync();
                if (activeRide != null)
                {
                    if (activeRide.Status == "Payment Pending")
                    {
                        await ShowModal(
                            "Payment Required",
                            "You have an unpaid ride. Please complete the payment for your previous ride before booking a new one.",
                            "warning",
                            false);
                    }
                    else
                    {
                        await ShowModal(
                            "Active Ride",
                            "You already have an active ride in progress. Please wait until it's completed before booking a new ride.",
                            "warning",
                            false);
                    }
                    return;
                }

                string commuterId = AppSession.GetCommuterId();
                if (string.IsNullOrEmpty(commuterId))
                {
                    await ShowModal("Error", "Please login first", "error", true);
                    await Navigation.PopModalAsync();
                    return;
                }

                if (paymentMethod == "GCash")
                {
                    bool proceed = await ShowConfirmModal("GCash Payment", "You selected GCash payment. Continue?", "warning");
                    if (!proceed) return;
                }

                string rideType = selectedRide == "12" ? "1-2 seater" : "3-4 seater";
                double fare = CalculateFare(selectedRide);

                var rideRequest = new RideRequest
                {
                    CommuterId = commuterId,
                    CommuterName = await GetCommuterNameAsync(),
                    CommuterMobile = AppSession.GetCommuterMobileNumber(),
                    PickupLocation = pickupLocation,
                    DropoffLocation = dropoffLocation,
                    PickupLat = _pickupLat,
                    PickupLng = _pickupLng,
                    DropoffLat = _dropoffLat,
                    DropoffLng = _dropoffLng,
                    VehicleType = "Tricycle",
                    SeatingCapacity = rideType,
                    PassengerType = passengerType,
                    PaymentMethod = paymentMethod,
                    Fare = fare,
                    Distance = actualDistance,
                    EstimatedTime = actualDuration,
                    Status = "Pending",
                    RequestTime = DateTime.UtcNow
                };

                string requestId = await _firebaseConnection.CreateRideRequestAsync(rideRequest);
                if (!string.IsNullOrEmpty(requestId))
                {
                    StopRealTimeLocationTracking();
                    _fareConfigListener?.Dispose();
                    _firebaseConnection.StopListeningForFareChanges();
                    await Navigation.PushModalAsync(new Commuter_SearchDriver(pickupLocation, dropoffLocation, rideType, fare, requestId, paymentMethod));
                }
                else
                {
                    await ShowModal("Error", "Failed to create ride request.", "error", false);
                }
            }
            catch (Exception ex)
            {
                await ShowModal("Error", $"Booking failed: {ex.Message}", "error", false);
            }
        }

        private async Task<string> GetCommuterNameAsync()
        {
            try
            {
                string commuterId = AppSession.GetCommuterId();
                if (!string.IsNullOrEmpty(commuterId))
                {
                    var commuter = await _firebaseConnection.GetCommuterByIdAsync(commuterId);
                    if (commuter != null) return $"{commuter.FirstName} {commuter.LastName}".Trim();
                }
            }
            catch { }
            return "Commuter";
        }

        private async Task ShowVerificationRequiredModal(string userType)
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

            var laterButton = new Button
            {
                Text = "Later",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            laterButton.Clicked += async (s, e) =>
            {
                await CloseModal(s);
            };

            var verifyButton = new Button
            {
                Text = "Verify Now",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            verifyButton.Clicked += async (s, e) =>
            {
                await CloseModal(s);
                await Navigation.PushModalAsync(new Commuter_IDVerification(currentSelectedType));
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10
            };
            buttonGrid.Children.Add(laterButton);
            Grid.SetColumn(laterButton, 0);
            buttonGrid.Children.Add(verifyButton);
            Grid.SetColumn(verifyButton, 1);

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
                            Text = "Verification Required",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = iconColor,
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = $"You need to verify your {userType} ID to get discount. Verify now?",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        buttonGrid
                    }
                }
            };

            var modalPage = new SelectRideModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task ShowPendingVerificationModal(string userType)
        {
            await ShowModal(
                "Verification Pending",
                $"Your {userType} ID verification is still under review. Please wait for approval.",
                "info",
                false);
        }

        private async Task ShowFareUpdateModal(List<string> changes)
        {
            _isModalOpen = true;

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var changesStack = new VerticalStackLayout { Spacing = 4 };
            foreach (var change in changes)
            {
                changesStack.Children.Add(new Label
                {
                    Text = $"• {change}",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#333333"),
                    Margin = new Thickness(0, 2)
                });
            }

            var okButton = new Button
            {
                Text = "Got it",
                BackgroundColor = Color.FromArgb("#2E7D32"),
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            okButton.Clicked += async (s, e) =>
            {
                await CloseModal(s);
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
                WidthRequest = 300,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Label
                        {
                            Text = "💰",
                            FontSize = 40,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Fare Updated!",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#2E7D32"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "The fare rates have been updated by the admin:",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Frame
                        {
                            BackgroundColor = Color.FromArgb("#F5F5F5"),
                            CornerRadius = 8,
                            Padding = 12,
                            HasShadow = false,
                            Content = new ScrollView
                            {
                                HeightRequest = 120,
                                Content = changesStack
                            }
                        },
                        okButton
                    }
                }
            };

            var modalPage = new SelectRideModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk = false)
        {
            _isModalOpen = true;

            string iconSource = type == "success" ? "happy.png" :
                               type == "warning" ? "signal.png" :
                               type == "info" ? "document.png" : "sad.png";
            Color iconColor = type == "success" ? Color.FromArgb("#2E7D32") :
                             type == "warning" ? Color.FromArgb("#FF9800") :
                             type == "info" ? Color.FromArgb("#2196F3") : Color.FromArgb("#DC3545");

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
            okButton.Clicked += async (s, e) =>
            {
                await CloseModal(s);
                if (closePageOnOk)
                {
                    StopRealTimeLocationTracking();
                    _fareConfigListener?.Dispose();
                    _firebaseConnection.StopListeningForFareChanges();
                    if (Navigation.ModalStack.Count > 0)
                        await Navigation.PopModalAsync();
                    else
                        await Navigation.PopAsync();
                }
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
                        okButton
                    }
                }
            };

            var modalPage = new SelectRideModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );
        }

        private async Task<bool> ShowConfirmModal(string title, string message, string type)
        {
            var tcs = new TaskCompletionSource<bool>();
            _isModalOpen = true;

            string iconSource = type == "warning" ? "signal.png" : "document.png";
            Color iconColor = type == "warning" ? Color.FromArgb("#FF9800") : Color.FromArgb("#2196F3");

            var blurOverlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#CC000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Opacity = 0
            };

            var noButton = new Button
            {
                Text = "No",
                BackgroundColor = Color.FromArgb("#E0E0E0"),
                TextColor = Color.FromArgb("#666666"),
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            noButton.Clicked += async (s, e) =>
            {
                await CloseModal(s);
                tcs.SetResult(false);
            };

            var yesButton = new Button
            {
                Text = "Yes",
                BackgroundColor = iconColor,
                TextColor = Colors.White,
                CornerRadius = 10,
                HeightRequest = 45,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            yesButton.Clicked += async (s, e) =>
            {
                await CloseModal(s);
                tcs.SetResult(true);
            };

            var buttonGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10
            };
            buttonGrid.Children.Add(noButton);
            Grid.SetColumn(noButton, 0);
            buttonGrid.Children.Add(yesButton);
            Grid.SetColumn(yesButton, 1);

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
                        buttonGrid
                    }
                }
            };

            var modalPage = new SelectRideModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(modalPage);

            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(
                modalContent.FadeTo(1, 350, Easing.SpringOut),
                modalContent.ScaleTo(1, 450, Easing.SpringOut)
            );

            return await tcs.Task;
        }

        private async Task CloseModal(object sender)
        {
            var modalPage = (sender as Button)?.Parent?.Parent?.Parent?.Parent?.Parent as SelectRideModalPage;
            if (modalPage == null)
            {
                var page = sender as SelectRideModalPage;
                if (page != null)
                {
                    await page.AnimateModalExit();
                }
                else
                {
                    var btn = sender as Button;
                    if (btn != null)
                    {
                        var parent = btn.Parent?.Parent?.Parent?.Parent as SelectRideModalPage;
                        if (parent != null)
                        {
                            await parent.AnimateModalExit();
                        }
                    }
                }
            }
            else
            {
                await modalPage.AnimateModalExit();
            }
        }

        public void CloseModal()
        {
            _isModalOpen = false;
        }
    }

    public class SelectRideModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_SelectRide _parentPage;

        public SelectRideModalPage(Frame modalContent, Grid blurOverlay, Commuter_SelectRide parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;

            BackgroundColor = Colors.Transparent;
            Content = new Grid
            {
                Children = { blurOverlay, modalContent }
            };
        }

        public async Task AnimateModalExit()
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );
            await Navigation.PopModalAsync();
            _parentPage.CloseModal();
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

    public class SelectRideExitModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Commuter_SelectRide _parentPage;

        public SelectRideExitModalPage(Frame modalContent, Grid blurOverlay, Commuter_SelectRide parentPage)
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

        private async Task AnimateModalExit(bool exit)
        {
            await Task.WhenAll(
                _modalContent.TranslateTo(0, 300, 250, Easing.CubicIn),
                _modalContent.FadeTo(0, 200, Easing.CubicIn),
                _modalContent.ScaleTo(0.5, 200, Easing.CubicIn),
                _blurOverlay.FadeTo(0, 200, Easing.CubicIn)
            );
            await Navigation.PopModalAsync();
            _parentPage.CloseModal(exit);
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

    public static class SelectRideGridExtensions
    {
        public static Button WithSelectRideGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}