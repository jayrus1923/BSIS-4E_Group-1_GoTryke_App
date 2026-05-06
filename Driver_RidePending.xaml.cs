using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Layouts;
using ServiceCo.Firebase;
using ServiceCo.Services; 
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ServiceCo
{
    public partial class Driver_RidePending : ContentPage
    {
        private string _rideRequestId;
        private bool _isViewOnly;
        private RideRequest _currentRideRequest;
        private Commuter _commuterData;
        private readonly FirebaseConnection _firebaseConnection = new FirebaseConnection();
        private string googleMapsApiKey = Constants.GoogleMapsApiKey;

        private bool _is3DView = false;
        private Location _currentLocation;
        private Location _commuterLocation;
        private bool _isTrackingCommuter = false;
        private CancellationTokenSource _gpsCancellationTokenSource;
        private bool _isTrackingLocation = false;
        private bool _isMapLoaded = false;

        private double _pickupLat, _pickupLng, _dropoffLat, _dropoffLng;
        private bool _isProcessing = false;
        private bool _isModalOpen = false;

        private double _panelExpandedHeight;
        private double _panelCollapsedHeight = 80;
        private bool _isPanelExpanded = true;
        private double _lastPanelHeight;
        private bool _isDragging = false;
        private double _startY;
        private double _startHeight;

        private bool _showPickup = true;

        public Driver_RidePending(string rideRequestId = null, bool isViewOnly = false)
        {
            InitializeComponent();
            _rideRequestId = rideRequestId;
            _isViewOnly = isViewOnly;
            SetupUI();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!string.IsNullOrEmpty(_rideRequestId) && _currentRideRequest == null)
            {
                await LoadRideRequestData();
            }
        }

        private void SetupUI()
        {
            if (_isViewOnly)
            {
                PanelTitleLabel.Text = "Ride Request Preview";
                PanelSubtitleLabel.Text = "View passenger and route details";
                ViewActionButtons.IsVisible = true;
                ViewOnlyButton.IsVisible = false;
                AcceptButton.IsVisible = false;
                BackToRequestsButton.IsVisible = true;
                ShowDetailsButton.IsVisible = false;
            }
            else
            {
                PanelTitleLabel.Text = "Ride Request Details";
                PanelSubtitleLabel.Text = "Review before accepting";
                ViewActionButtons.IsVisible = true;
                ViewOnlyButton.IsVisible = false;
                AcceptButton.IsVisible = true;
                BackToRequestsButton.IsVisible = false;
                ShowDetailsButton.IsVisible = false;
            }

            MapBlurOverlay.IsVisible = true;
            PassengerProfileImage.Source = "profile_icon.png";
        }

        private async Task LoadRideRequestData()
        {
            try
            {
                Debug.WriteLine($"=== LOADING RIDE DATA ===");
                _currentRideRequest = await _firebaseConnection.GetRideRequestByIdAsync(_rideRequestId);

                if (_currentRideRequest != null)
                {
                    UpdateRideDetailsUI();
                    await LoadCommuterDataAndProfilePicture();

                    _pickupLat = _currentRideRequest.PickupLat;
                    _pickupLng = _currentRideRequest.PickupLng;
                    _dropoffLat = _currentRideRequest.DropoffLat;
                    _dropoffLng = _currentRideRequest.DropoffLng;

                    await LoadMap();
                    StartTrackingCommuterLocation();
                    await StartRealTimeLocationTracking();
                }
                else
                {
                    await ShowModal("Error", "Failed to load ride request data.", "error", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                await ShowModal("Error", $"Failed to load ride data: {ex.Message}", "error", true);
            }
        }

        private async Task LoadCommuterDataAndProfilePicture()
        {
            try
            {
                if (_currentRideRequest == null || string.IsNullOrEmpty(_currentRideRequest.CommuterId)) return;

                _commuterData = await _firebaseConnection.GetCommuterByIdAsync(_currentRideRequest.CommuterId);

                if (_commuterData != null)
                {
                    PassengerNameLabel.Text = GetCommuterDisplayName(_commuterData);
                    await LoadCommuterProfilePicture();
                }
                else
                {
                    PassengerNameLabel.Text = _currentRideRequest.CommuterName ?? "Unknown Passenger";
                }
            }
            catch
            {
                PassengerNameLabel.Text = _currentRideRequest.CommuterName ?? "Unknown Passenger";
            }
        }

        private async Task LoadCommuterProfilePicture()
        {
            try
            {
                if (_commuterData == null) return;

                ProfileImageLoadingIndicator.IsVisible = true;
                ProfileImageLoadingIndicator.IsRunning = true;

                if (!string.IsNullOrEmpty(_commuterData.ProfileImageUrl) && Uri.IsWellFormedUriString(_commuterData.ProfileImageUrl, UriKind.Absolute))
                {
                    PassengerProfileImage.Source = ImageSource.FromUri(new Uri(_commuterData.ProfileImageUrl));
                }
                else
                {
                    PassengerProfileImage.Source = "profile_icon.png";
                }
            }
            catch
            {
                PassengerProfileImage.Source = "profile_icon.png";
            }
            finally
            {
                ProfileImageLoadingIndicator.IsVisible = false;
                ProfileImageLoadingIndicator.IsRunning = false;
            }
        }

        private string GetCommuterDisplayName(Commuter commuter)
        {
            try
            {
                if (commuter == null) return "Passenger";
                if (!string.IsNullOrEmpty(commuter.FirstName) && !string.IsNullOrEmpty(commuter.LastName))
                {
                    string middleInitial = string.IsNullOrEmpty(commuter.MiddleName) ? "" : $"{commuter.MiddleName[0]}.";
                    return $"{commuter.FirstName} {middleInitial} {commuter.LastName}".Trim().Replace("  ", " ");
                }
                return !string.IsNullOrEmpty(commuter.FirstName) ? commuter.FirstName.Trim() : "Passenger";
            }
            catch
            {
                return "Passenger";
            }
        }

        private void UpdateRideDetailsUI()
        {
            if (_currentRideRequest != null)
            {
                PickupDisplayLabel.Text = FormatAddress(_currentRideRequest.PickupLocation, 20);
                DropoffDisplayLabel.Text = FormatAddress(_currentRideRequest.DropoffLocation, 20);
                PickupMainLabel.Text = FormatAddress(_currentRideRequest.PickupLocation, 30);
                DropoffMainLabel.Text = FormatAddress(_currentRideRequest.DropoffLocation, 30);

                PassengerTypeLabel.Text = GetPassengerTypeDisplay(_currentRideRequest.PassengerType);
                PassengerMobileLabel.Text = $"Mobile: {_currentRideRequest.CommuterMobile ?? "--"}";

                RideTypeLabel.Text = $"{_currentRideRequest.VehicleType} ({_currentRideRequest.SeatingCapacity})";
                RideIDLabel.Text = _currentRideRequest.Id ?? $"TRC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
                DistanceLabel.Text = $"{_currentRideRequest.Distance:F1} km";
                TimeLabel.Text = $"{_currentRideRequest.EstimatedTime} mins";
                FareLabel.Text = $"\u20B1{_currentRideRequest.Fare:F0}";
                PaymentMethodLabel.Text = _currentRideRequest.PaymentMethod ?? "Cash";

                if (!string.IsNullOrEmpty(_currentRideRequest.Note))
                {
                    NoteFrame.IsVisible = true;
                    NoteLabel.Text = _currentRideRequest.Note;
                }
            }
        }

        private string FormatAddress(string address, int maxLength)
        {
            if (string.IsNullOrEmpty(address)) return "Unknown";
            var cleanAddress = address.Replace(", Malolos, Bulacan", "").Replace(", Bulacan", "").Replace("Malolos, ", "").Trim();
            return cleanAddress.Length > maxLength ? cleanAddress.Substring(0, maxLength - 3) + "..." : cleanAddress;
        }

        private string GetPassengerTypeDisplay(string passengerType)
        {
            return passengerType?.ToLower() switch
            {
                "student" => "Student (20% off)",
                "senior" => "Senior/PWD (20% off)",
                "senior/pwd" => "Senior/PWD (20% off)",
                _ => "Regular Fare"
            };
        }

        private async Task LoadMap()
        {
            try
            {
                await GetCurrentLocation();
                _commuterLocation = new Location(_pickupLat, _pickupLng);

                var html = GenerateMapHtml();
                RideMapView.Navigating += OnWebViewNavigating;
                RideMapView.Source = new HtmlWebViewSource { Html = html };

                await Task.Delay(3000);
                await CheckMapReady();
                MapBlurOverlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadMap error: {ex.Message}");
                MapBlurOverlay.IsVisible = false;
            }
        }

        private async Task CheckMapReady()
        {
            try
            {
                int attempts = 0;
                while (attempts < 15)
                {
                    var result = await RideMapView.EvaluateJavaScriptAsync("typeof window.isMapReady !== 'undefined' && window.isMapReady === true");
                    if (result?.ToString() == "true")
                    {
                        _isMapLoaded = true;
                        Debug.WriteLine("✅ Map loaded successfully");
                        return;
                    }
                    attempts++;
                    await Task.Delay(500);
                }
                _isMapLoaded = true;
            }
            catch
            {
                _isMapLoaded = true;
            }
        }

        private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
        {
            Debug.WriteLine($"Navigating to: {e.Url}");
        }

        private async Task GetCurrentLocation()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                if (status == PermissionStatus.Granted)
                {
                    var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5));
                    _currentLocation = await Geolocation.GetLocationAsync(request);
                }
            }
            catch { }

            _currentLocation ??= new Location(14.8433, 120.8105);
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
                if (location != null && _isMapLoaded)
                {
                    _currentLocation = location;

                    string driverId = AppSession.GetDriverId();
                    if (!string.IsNullOrEmpty(driverId))
                        await _firebaseConnection.UpdateDriverLocationAsync(driverId, location.Latitude, location.Longitude);

                    string jsCode = $@"
                    if (window.updateDriverLocation && window.isMapReady) {{
                        window.updateDriverLocation({location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                   {location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                    }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                }
            }
            catch { }
        }

        private void StartTrackingCommuterLocation()
        {
            if (string.IsNullOrEmpty(_currentRideRequest?.CommuterId)) return;
            _isTrackingCommuter = true;

            _firebaseConnection.ListenForCommuterLocation(_currentRideRequest.CommuterId, (location) =>
            {
                if (!_isTrackingCommuter) return;
                _commuterLocation = new Location(location.lat, location.lng);
                Device.BeginInvokeOnMainThread(() => UpdateCommuterLocationOnMap());
            });
        }

        private async void UpdateCommuterLocationOnMap()
        {
            try
            {
                if (!_isMapLoaded) return;
                string jsCode = $@"
                if (window.updateCommuterLocation && window.isMapReady) {{
                    window.updateCommuterLocation({_commuterLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 
                                                  {_commuterLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                }}";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch { }
        }

        private string GenerateMapHtml()
        {
            string mapType = _is3DView ? "satellite" : "roadmap";
            double centerLat = (_currentLocation.Latitude + _pickupLat) / 2;
            double centerLng = (_currentLocation.Longitude + _pickupLng) / 2;

            return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />
        <script src='https://maps.googleapis.com/maps/api/js?key={googleMapsApiKey}&libraries=places,geometry'></script>
        <style>html,body,#map{{margin:0;padding:0;height:100%;width:100%;overflow:hidden;}}</style>
    </head>
    <body>
        <div id='map'></div>
        <script>
            let map, pickupMarker, dropoffMarker, driverMarker;
            let directionsService;
            let orangeRouteRenderer, blueRouteRenderer;
            let isMapReady = false;
            
            const pickupPos = {{ lat: {_pickupLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {_pickupLng.ToString(System.Globalization.CultureInfo.InvariantCulture)} }};
            const dropoffPos = {{ lat: {_dropoffLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {_dropoffLng.ToString(System.Globalization.CultureInfo.InvariantCulture)} }};
            const driverPos = {{ lat: {_currentLocation.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {_currentLocation.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)} }};
            
            function initMap() {{
                map = new google.maps.Map(document.getElementById('map'), {{
                    center: {{ lat: {centerLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, lng: {centerLng.ToString(System.Globalization.CultureInfo.InvariantCulture)} }},
                    zoom: 13,
                    mapTypeId: '{mapType}',
                    disableDefaultUI: true,
                    zoomControl: true,
                    zoomControlOptions: {{ position: google.maps.ControlPosition.RIGHT_TOP }}
                }});
                
                directionsService = new google.maps.DirectionsService();
                
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
                
                driverMarker = new google.maps.Marker({{
                    position: driverPos,
                    map: map,
                    icon: {{ url: 'https://maps.google.com/mapfiles/ms/icons/yellow-dot.png', scaledSize: new google.maps.Size(40, 40) }},
                    title: 'Your Location'
                }});
                
                drawRoutes();
                
                window.map = map;
                window.pickupMarker = pickupMarker;
                window.dropoffMarker = dropoffMarker;
                window.driverMarker = driverMarker;
                
                isMapReady = true;
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
                            polylineOptions: {{ strokeColor: '#FF9800', strokeWeight: 6, strokeOpacity: 0.9 }}
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
                            polylineOptions: {{ strokeColor: '#2196F3', strokeWeight: 6, strokeOpacity: 0.9 }}
                        }});
                        blueRouteRenderer.setDirections(result);
                    }}
                }});
            }}
            
            window.updateDriverLocation = function(lat, lng) {{
                if (!isMapReady || !driverMarker) return;
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
            
            window.updateCommuterLocation = function(lat, lng) {{
                console.log('Commuter location updated');
            }};
            
            window.flyToPickupDropoff = function(isPickup) {{
                if (!isMapReady) return;
                const target = isPickup ? pickupPos : dropoffPos;
                map.panTo(new google.maps.LatLng(target.lat, target.lng));
                map.setZoom(16);
                const marker = isPickup ? pickupMarker : dropoffMarker;
                if (marker) {{
                    marker.setAnimation(google.maps.Animation.BOUNCE);
                    setTimeout(() => marker.setAnimation(null), 1500);
                }}
            }};
            
            window.centerOnUserLocation = function(lat, lng) {{
                if (!isMapReady) return;
                map.panTo(new google.maps.LatLng(lat, lng));
                map.setZoom(17);
                if (driverMarker) {{
                    driverMarker.setAnimation(google.maps.Animation.BOUNCE);
                    setTimeout(() => driverMarker.setAnimation(null), 1500);
                }}
            }};
            
            window.toggle3DView = function(enable3D) {{
                if (!isMapReady) return;
                map.setMapTypeId(enable3D ? 'satellite' : 'roadmap');
                map.setTilt(enable3D ? 60 : 0);
            }};
            
            google.maps.event.addDomListener(window, 'load', initMap);
        </script>
    </body>
    </html>";
        }

        private async void OnHeaderTapped(object sender, EventArgs e)
        {
            if (!_isMapLoaded)
            {
                await ShowModal("Info", "Map is still loading. Please wait.", "info", false);
                return;
            }

            try
            {
                if (_showPickup)
                {
                    string jsCode = $@"
                var lat = {_pickupLat.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                var lng = {_pickupLng.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                if (window.map && window.pickupMarker) {{
                    window.map.panTo(new google.maps.LatLng(lat, lng));
                    window.map.setZoom(16);
                    window.pickupMarker.setAnimation(google.maps.Animation.BOUNCE);
                    setTimeout(function() {{ window.pickupMarker.setAnimation(null); }}, 1500);
                }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    _showPickup = false;
                }
                else
                {
                    string jsCode = $@"
                var lat = {_dropoffLat.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                var lng = {_dropoffLng.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                if (window.map && window.dropoffMarker) {{
                    window.map.panTo(new google.maps.LatLng(lat, lng));
                    window.map.setZoom(16);
                    window.dropoffMarker.setAnimation(google.maps.Animation.BOUNCE);
                    setTimeout(function() {{ window.dropoffMarker.setAnimation(null); }}, 1500);
                }}";
                    await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    _showPickup = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Header tap error: {ex.Message}");
            }
        }

        private async void OnLocationButtonTapped(object sender, EventArgs e)
        {
            if (!_isMapLoaded)
            {
                await ShowModal("Info", "Map is still loading. Please wait.", "info", false);
                return;
            }

            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                if (status == PermissionStatus.Granted)
                {
                    var request = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(5));
                    var location = await Geolocation.GetLocationAsync(request);

                    if (location != null)
                    {
                        _currentLocation = location;

                        string jsCode = $@"
                    var lat = {location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                    var lng = {location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                    if (window.map && window.driverMarker) {{
                        window.map.panTo(new google.maps.LatLng(lat, lng));
                        window.map.setZoom(17);
                        window.driverMarker.setAnimation(google.maps.Animation.BOUNCE);
                        setTimeout(function() {{ window.driverMarker.setAnimation(null); }}, 1500);
                    }}";
                        await RideMapView.EvaluateJavaScriptAsync(jsCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Location button error: {ex.Message}");
            }
        }

        private async void OnMapTypeTapped(object sender, EventArgs e)
        {
            _is3DView = !_is3DView;
            MapTypeIcon.Source = _is3DView ? "two_dimentional.png" : "three_dimentional.png";

            try
            {
                string jsCode = $@"
                    if (window.toggle3DView) {{
                        window.toggle3DView({_is3DView.ToString().ToLower()});
                    }} else if (window.map) {{
                        window.map.setMapTypeId('{(_is3DView ? "satellite" : "roadmap")}');
                        window.map.setTilt({(_is3DView ? 60 : 0)});
                    }}";
                await RideMapView.EvaluateJavaScriptAsync(jsCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Map type toggle error: {ex.Message}");
            }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            _panelExpandedHeight = height * 0.55;
            _panelCollapsedHeight = 80;

            if (_isPanelExpanded)
            {
                RidePanel.HeightRequest = _panelExpandedHeight;
                DetailsContent.IsVisible = true;
                ShowDetailsButton.IsVisible = false;
                UpdateButtonPositions(120, 200);
            }
            else
            {
                RidePanel.HeightRequest = _panelCollapsedHeight;
                DetailsContent.IsVisible = false;
                ShowDetailsButton.IsVisible = true;
                UpdateButtonPositions(90, 0);
            }
            _lastPanelHeight = RidePanel.HeightRequest;
        }

        private void UpdateButtonPositions(double locationBackOffset, double mapTypeOffset)
        {
            var showDetailsOffset = locationBackOffset - 30;
            AbsoluteLayout.SetLayoutBounds(BackButton, new Rect(0.05, 0.95, -1, locationBackOffset));
            AbsoluteLayout.SetLayoutBounds(LocationButton, new Rect(0.95, 0.95, -1, locationBackOffset));
            AbsoluteLayout.SetLayoutBounds(MapTypeButton, new Rect(0.95, 0.80, -1, mapTypeOffset));

            if (_isViewOnly)
            {
                AbsoluteLayout.SetLayoutBounds(BackToRequestsButton, new Rect(0.5, 0.95, -1, showDetailsOffset));
                AbsoluteLayout.SetLayoutBounds(ShowDetailsButton, new Rect(0.5, 0.95, -1, showDetailsOffset));
                BackToRequestsButton.IsVisible = true;
                AcceptButton.IsVisible = false;
            }
            else
            {
                AbsoluteLayout.SetLayoutBounds(AcceptButton, new Rect(0.5, 0.95, -1, showDetailsOffset));
                AbsoluteLayout.SetLayoutBounds(ShowDetailsButton, new Rect(0.5, 0.95, -1, showDetailsOffset));
                AcceptButton.IsVisible = true;
                BackToRequestsButton.IsVisible = false;
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
                    RidePanel.HeightRequest = newHeight;
                    var progress = (newHeight - _panelCollapsedHeight) / (_panelExpandedHeight - _panelCollapsedHeight);
                    UpdateButtonPositions(90 + (30 * progress), 0 + (200 * progress));
                    DetailsContent.IsVisible = newHeight > _panelCollapsedHeight + 50;
                    ShowDetailsButton.IsVisible = newHeight < _panelCollapsedHeight + 50;
                    break;
                case GestureStatus.Completed:
                    _isDragging = false;
                    _lastPanelHeight = RidePanel.HeightRequest;
                    AnimatePanel(_lastPanelHeight > (_panelExpandedHeight + _panelCollapsedHeight) / 2);
                    break;
            }
        }

        private void AnimatePanel(bool expand)
        {
            _isPanelExpanded = expand;
            var targetHeight = expand ? _panelExpandedHeight : _panelCollapsedHeight;
            var panelAnimation = new Animation(h => RidePanel.HeightRequest = h, _lastPanelHeight, targetHeight, easing: Easing.CubicOut);
            panelAnimation.Commit(RidePanel, "PanelAnimation", length: 300, finished: (v, c) =>
            {
                _lastPanelHeight = targetHeight;
                DetailsContent.IsVisible = expand;
                ShowDetailsButton.IsVisible = !expand;
                UpdateButtonPositions(expand ? 120 : 90, expand ? 200 : 0);
            });
        }

        private void OnDragHandleTapped(object sender, EventArgs e) => AnimatePanel(!_isPanelExpanded);
        private void OnShowDetailsTapped(object sender, EventArgs e) => AnimatePanel(true);

        private async void OnBackButtonClicked(object sender, EventArgs e) => await NavigateBackAsync();
        private async void OnBackToRequestsClicked(object sender, EventArgs e) => await NavigateBackAsync();
        private async void OnBackToRequestsTapped(object sender, EventArgs e) => await NavigateBackAsync();
        private async void OnAcceptRideTapped(object sender, EventArgs e) => await AcceptRideRequestAsync();
        private async void OnAcceptButtonClicked(object sender, EventArgs e) => await AcceptRideRequestAsync();

        private async Task NavigateBackAsync()
        {
            if (_isProcessing || _isModalOpen) return;
            _isModalOpen = true;

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
                WidthRequest = 280,
                Content = new VerticalStackLayout
                {
                    Spacing = 20,
                    Children =
                    {
                        new Image { Source = "complaint.png", HeightRequest = 60, WidthRequest = 60, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Exit Ride?", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#DC3545"), HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Are you sure you want to exit?", FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center },
                        new Grid
                        {
                            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                            ColumnSpacing = 10,
                            Children =
                            {
                                new Button { Text = "CANCEL", BackgroundColor = Color.FromArgb("#E0E0E0"), TextColor = Color.FromArgb("#666666"), CornerRadius = 10, HeightRequest = 45, FontSize = 14, FontAttributes = FontAttributes.Bold }.WithDriverRidePendingGridColumn(0),
                                new Button { Text = "EXIT", BackgroundColor = Color.FromArgb("#DC3545"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45, FontSize = 14, FontAttributes = FontAttributes.Bold }.WithDriverRidePendingGridColumn(1)
                            }
                        }
                    }
                }
            };

            var exitModal = new DriverRidePendingExitModalPage(modalContent, blurOverlay, this);
            await Navigation.PushModalAsync(exitModal);
            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        private async Task AcceptRideRequestAsync()
        {
            if (_isProcessing || _isModalOpen) return;

            _isProcessing = true;
            _isModalOpen = true;
            ShowProcessing("Accepting ride...");

            try
            {
                string driverId = AppSession.GetDriverId();
                if (string.IsNullOrEmpty(driverId))
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Error", "Driver session not found. Please login again.", "error", true);
                    return;
                }

                var driver = await _firebaseConnection.GetDriverByIdAsync(driverId);
                string driverName = $"{driver?.FirstName} {driver?.LastName}".Trim();

                var activeRide = await _firebaseConnection.GetActiveRideForDriverAsync(driverId);
                if (activeRide != null && activeRide.FirebaseKey != _rideRequestId)
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Active Ride", "You already have an active ride. Please complete your current ride first.", "warning", false);
                    return;
                }

                bool isAlreadyTaken = await _firebaseConnection.CheckIfRideAlreadyTaken(_rideRequestId);
                if (isAlreadyTaken)
                {
                    HideProcessing();
                    _isProcessing = false;
                    _isModalOpen = false;
                    await ShowModal("Ride Taken", "This ride was already taken by another driver.", "warning", true);
                    return;
                }

                AcceptButton.IsEnabled = false;
                BackToRequestsButton.IsEnabled = false;

                bool success = await _firebaseConnection.AcceptRideRequestAsync(_rideRequestId, driverId, driverName);

                HideProcessing();
                _isProcessing = false;
                _isModalOpen = false;

                if (success)
                {
                    await Navigation.PushModalAsync(new Driver_StartedTrip(_rideRequestId));
                }
                else
                {
                    await ShowModal("Error", "Failed to accept ride. The ride may have been taken.", "error", false);
                    AcceptButton.IsEnabled = true;
                    BackToRequestsButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                HideProcessing();
                _isProcessing = false;
                _isModalOpen = false;
                await ShowModal("Error", $"Failed to accept ride: {ex.Message}", "error", false);
                AcceptButton.IsEnabled = true;
                BackToRequestsButton.IsEnabled = true;
            }
        }

        private void ShowProcessing(string message = "Loading...")
        {
            ProcessingLabel.Text = message;
            ProcessingOverlay.IsVisible = true;
            ProcessingIndicator.IsRunning = true;
        }

        private void HideProcessing()
        {
            ProcessingOverlay.IsVisible = false;
            ProcessingIndicator.IsRunning = false;
        }

        private async Task ShowModal(string title, string message, string type, bool closePageOnOk)
        {
            _isModalOpen = true;
            string iconSource = type == "success" ? "happy.png" : type == "warning" ? "signal.png" : type == "info" ? "document.png" : "sad.png";
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
                        new Label { Text = message, FontSize = 14, TextColor = Color.FromArgb("#666666"), HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center },
                        new Button { Text = "OK", BackgroundColor = iconColor, TextColor = Colors.White, CornerRadius = 10, HeightRequest = 45, FontSize = 14, FontAttributes = FontAttributes.Bold }
                    }
                }
            };

            var modal = new DriverRidePendingCustomModalPage(modalContent, blurOverlay, closePageOnOk, this);
            await Navigation.PushModalAsync(modal);
            await blurOverlay.FadeTo(1, 250, Easing.CubicIn);
            await Task.WhenAll(modalContent.FadeTo(1, 350, Easing.SpringOut), modalContent.ScaleTo(1, 450, Easing.SpringOut));
        }

        public void CloseModal(bool closePage)
        {
            _isModalOpen = false;
            if (closePage) MainThread.BeginInvokeOnMainThread(async () => await Navigation.PopModalAsync());
        }

        private void StopAllTracking()
        {
            _isTrackingLocation = false;
            _gpsCancellationTokenSource?.Cancel();
            _isTrackingCommuter = false;
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!_isProcessing && !_isModalOpen) await NavigateBackAsync();
            });
            return true;
        }
    }

    public class DriverRidePendingCustomModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private bool _closePageOnOk;
        private Driver_RidePending _parentPage;

        public DriverRidePendingCustomModalPage(Frame modalContent, Grid blurOverlay, bool closePageOnOk, Driver_RidePending parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _closePageOnOk = closePageOnOk;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;

            var button = (modalContent.Content as VerticalStackLayout).Children[3] as Button;
            button.Clicked += async (s, e) => { button.IsEnabled = false; await AnimateModalExit(); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
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

    public class DriverRidePendingExitModalPage : ContentPage
    {
        private Frame _modalContent;
        private Grid _blurOverlay;
        private Driver_RidePending _parentPage;

        public DriverRidePendingExitModalPage(Frame modalContent, Grid blurOverlay, Driver_RidePending parentPage)
        {
            _modalContent = modalContent;
            _blurOverlay = blurOverlay;
            _parentPage = parentPage;
            BackgroundColor = Colors.Transparent;

            var grid = modalContent.Content as VerticalStackLayout;
            var buttonGrid = grid.Children[3] as Grid;
            var cancelButton = buttonGrid.Children[0] as Button;
            var exitButton = buttonGrid.Children[1] as Button;

            cancelButton.Clicked += async (s, e) => await AnimateModalExit(false);
            exitButton.Clicked += async (s, e) => { exitButton.IsEnabled = false; await AnimateModalExit(true); };
            Content = new Grid { Children = { blurOverlay, modalContent } };
        }

        private async Task AnimateModalExit(bool exit)
        {
            await Task.WhenAll(_modalContent.TranslateTo(0, 300, 250, Easing.CubicIn), _modalContent.FadeTo(0, 200, Easing.CubicIn), _modalContent.ScaleTo(0.5, 200, Easing.CubicIn), _blurOverlay.FadeTo(0, 200, Easing.CubicIn));
            await Navigation.PopModalAsync();
            _parentPage.CloseModal(false);
            if (exit) MainThread.BeginInvokeOnMainThread(async () => await _parentPage.Navigation.PopModalAsync());
        }

        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await AnimateModalExit(false));
            return true;
        }
    }

    public static class DriverRidePendingGridExtensions
    {
        public static Button WithDriverRidePendingGridColumn(this Button button, int column)
        {
            Grid.SetColumn(button, column);
            return button;
        }
    }
}