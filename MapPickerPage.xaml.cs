using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using System;
using ServiceCo.Services;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ServiceCo
{
    public partial class MapPickerPage : ContentPage
    {
        private string googleMapsApiKey = Constants.GoogleMapsApiKey;
        private double _selectedLatitude = 14.8445;
        private double _selectedLongitude = 120.8115;
        private string _selectedAddress = "Malolos, Bulacan";
        private bool _isLocationValid = true;
        private bool _isManualMode = true;

        private bool _isProcessing = false;
        private bool _isSwitchingMode = false;
        private bool _isGettingLocation = false;
        private bool _isModalOpen = false;

        private readonly double MALOLOS_NORTH = 14.8950;
        private readonly double MALOLOS_SOUTH = 14.8200;
        private readonly double MALOLOS_EAST = 120.8450;
        private readonly double MALOLOS_WEST = 120.7600;

        public static string SelectedLocationResult { get; set; }
        public Action<string> OnLocationSelected { get; set; }

        public MapPickerPage(string fieldType, string token)
        {
            InitializeComponent();
            UpdateModeIndicator();
            LoadMap();
        }

        private void UpdateModeIndicator()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isManualMode)
                {
                    ModeIcon.Source = "tap_icon.png";
                    ModeIndicator.Text = "Tap anywhere on map to select";
                    CenterPin.IsVisible = false;
                    PulsingDot.IsVisible = false;
                }
                else
                {
                    ModeIcon.Source = "move_icon.png";
                    ModeIndicator.Text = "Move map to adjust position";
                    CenterPin.IsVisible = true;
                    PulsingDot.IsVisible = true;
                }
            });
        }

        private bool IsInMalolos(double lat, double lng)
        {
            return lat >= MALOLOS_SOUTH &&
                   lat <= MALOLOS_NORTH &&
                   lng >= MALOLOS_WEST &&
                   lng <= MALOLOS_EAST;
        }

        private void LoadMap()
        {
            string html = $@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />
            <script src='https://maps.googleapis.com/maps/api/js?key={googleMapsApiKey}&libraries=places'></script>
            <style>
                html, body {{ margin:0; padding:0; height:100%; width:100%; overflow:hidden; }}
                #map {{ height:100%; width:100%; }}
                .gm-style .gm-style-cc {{ display:none; }}
                a[href^='https://maps.google.com/maps'] {{ display:none !important; }}
                .gm-style .gm-style-cc {{ display:none; }}
            </style>
            </head>
            <body>
            <div id='map'></div>
            <script>
                let map;
                let marker = null;
                let currentLocationMarker = null;
                let updateTimer;
                let isManualMode = {(_isManualMode ? "true" : "false")};
                
                const malolosBounds = {{
                    north: {MALOLOS_NORTH},
                    south: {MALOLOS_SOUTH},
                    east: {MALOLOS_EAST},
                    west: {MALOLOS_WEST}
                }};
                
                function isInMalolos(lat, lng) {{
                    return lat >= malolosBounds.south && 
                           lat <= malolosBounds.north && 
                           lng >= malolosBounds.west && 
                           lng <= malolosBounds.east;
                }}
                
                function initMap() {{
                    map = new google.maps.Map(document.getElementById('map'), {{
                        center: {{ lat: {_selectedLatitude}, lng: {_selectedLongitude} }},
                        zoom: 12,
                        mapTypeId: 'roadmap',
                        mapTypeControl: false,
                        fullscreenControl: false,
                        streetViewControl: false,
                        zoomControl: true,
                        zoomControlOptions: {{
                            position: google.maps.ControlPosition.RIGHT_TOP
                        }}
                    }});
                    
                    if (isManualMode) {{
                        map.addListener('click', function(e) {{
                            const lat = e.latLng.lat();
                            const lng = e.latLng.lng();
                            
                            if (marker) {{
                                marker.setMap(null);
                            }}
                            
                            marker = new google.maps.Marker({{
                                position: {{ lat: lat, lng: lng }},
                                map: map,
                                animation: google.maps.Animation.DROP
                            }});
                            
                            window.location.href = 'mappicker://update?lat=' + lat + '&lng=' + lng;
                        }});
                        
                        map.getDiv().style.cursor = 'crosshair';
                    }} else {{
                        map.addListener('dragend', function() {{
                            updateLocation();
                        }});
                        
                        map.addListener('idle', function() {{
                            updateLocation();
                        }});
                    }}
                    
                    if (!isManualMode) {{
                        updateLocation();
                    }}
                }}
                
                function updateLocation() {{
                    const center = map.getCenter();
                    const lat = center.lat();
                    const lng = center.lng();
                    window.location.href = 'mappicker://update?lat=' + lat + '&lng=' + lng;
                }}
                
                function setCurrentLocation(lat, lng) {{
                    if (currentLocationMarker) {{
                        currentLocationMarker.setMap(null);
                    }}
                    
                    currentLocationMarker = new google.maps.Marker({{
                        position: {{ lat: lat, lng: lng }},
                        map: map,
                        icon: {{
                            path: google.maps.SymbolPath.CIRCLE,
                            scale: 10,
                            fillColor: '#1a73e8',
                            fillOpacity: 1,
                            strokeColor: '#ffffff',
                            strokeWeight: 3
                        }},
                        title: 'Your Location'
                    }});
                    
                    map.panTo({{ lat: lat, lng: lng }});
                    map.setZoom(15);
                }}
                
                document.addEventListener('DOMContentLoaded', initMap);
            </script>
            </body>
            </html>";

            MapWebView.Source = new HtmlWebViewSource { Html = html };
            MapWebView.Navigating += OnWebViewNavigating;
        }

        private void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
        {
            if (e.Url.StartsWith("mappicker://update"))
            {
                e.Cancel = true;

                try
                {
                    var uri = new Uri(e.Url);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                    if (double.TryParse(query["lat"], out double lat) &&
                        double.TryParse(query["lng"], out double lng))
                    {
                        _selectedLatitude = lat;
                        _selectedLongitude = lng;
                        _isLocationValid = IsInMalolos(lat, lng);

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            string latDirection = lat >= 0 ? "N" : "S";
                            string lngDirection = lng >= 0 ? "E" : "W";

                            CoordinatesLabel.Text = $"{Math.Abs(lat):F4}° {latDirection}, {Math.Abs(lng):F4}° {lngDirection}";

                            if (_isLocationValid)
                            {
                                ConfirmButton.BackgroundColor = Color.FromArgb("#1a73e8");
                                ConfirmButton.Text = "Confirm Location";
                                GetAddressForCoordinates(lat, lng);
                            }
                            else
                            {
                                ConfirmButton.BackgroundColor = Color.FromArgb("#999999");
                                ConfirmButton.Text = "Outside Malolos";
                                AddressLine1Label.Text = "Location not in";
                                AddressLine2Label.Text = "Malolos, Bulacan";
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private async void GetAddressForCoordinates(double lat, double lng)
        {
            try
            {
                string url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={lat},{lng}&key={googleMapsApiKey}";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var response = await client.GetStringAsync(url);
                var jsonDoc = JsonDocument.Parse(response);

                if (jsonDoc.RootElement.TryGetProperty("results", out var results) &&
                    results.GetArrayLength() > 0)
                {
                    var result = results[0];

                    if (result.TryGetProperty("formatted_address", out var address))
                    {
                        _selectedAddress = address.GetString();
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var parts = _selectedAddress.Split(',');
                        if (parts.Length >= 2)
                        {
                            AddressLine1Label.Text = parts[0].Trim();
                            AddressLine2Label.Text = string.Join(",", parts.Skip(1).Take(2)).Trim();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Geocoding error: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AddressLine1Label.Text = "Malolos";
                    AddressLine2Label.Text = "Bulacan";
                });
            }
        }

        private async void OnSwitchModeClicked(object sender, EventArgs e)
        {
            if (_isSwitchingMode || _isModalOpen) return;

            try
            {
                _isSwitchingMode = true;

                var frame = (Frame)((Image)sender).Parent;
                frame.Opacity = 0.5;
                frame.IsEnabled = false;

                _isManualMode = !_isManualMode;
                UpdateModeIndicator();
                LoadMap();

                await Task.Delay(500);
            }
            finally
            {
                var frame = (Frame)((Image)sender).Parent;
                frame.Opacity = 1;
                frame.IsEnabled = true;
                _isSwitchingMode = false;
            }
        }

        private async void OnConfirmClicked(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            if (!_isLocationValid)
            {
                await DisplayMalolosOnlyAlert();
                return;
            }

            try
            {
                _isProcessing = true;

                ConfirmButton.IsEnabled = false;
                ConfirmButton.Opacity = 0.5;

                var result = $"{_selectedAddress}|{_selectedLatitude:F6}|{_selectedLongitude:F6}";
                SelectedLocationResult = result;
                OnLocationSelected?.Invoke(result);

                await Navigation.PopModalAsync();
            }
            catch
            {
                await Shell.Current.GoToAsync("..");
            }
            finally
            {
                ConfirmButton.IsEnabled = true;
                ConfirmButton.Opacity = 1;
                _isProcessing = false;
            }
        }

        private async void OnCurrentLocationTapped(object sender, EventArgs e)
        {
            if (_isGettingLocation || _isModalOpen) return;

            try
            {
                _isGettingLocation = true;

                var frame = (Frame)((Image)sender).Parent;
                frame.Opacity = 0.5;
                frame.IsEnabled = false;

                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
                var location = await Geolocation.GetLocationAsync(request);

                if (location != null)
                {
                    _selectedLatitude = location.Latitude;
                    _selectedLongitude = location.Longitude;
                    _isLocationValid = IsInMalolos(_selectedLatitude, _selectedLongitude);

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        CurrentLocationIndicator.IsVisible = true;

                        Task.Delay(2000).ContinueWith(_ =>
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                CurrentLocationIndicator.IsVisible = false;
                            });
                        });
                    });

                    string updateScript = $"setCurrentLocation({_selectedLatitude}, {_selectedLongitude});";
                    await MapWebView.EvaluateJavaScriptAsync(updateScript);

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        string latDirection = _selectedLatitude >= 0 ? "N" : "S";
                        string lngDirection = _selectedLongitude >= 0 ? "E" : "W";
                        CoordinatesLabel.Text = $"{Math.Abs(_selectedLatitude):F4}° {latDirection}, {Math.Abs(_selectedLongitude):F4}° {lngDirection}";

                        if (_isLocationValid)
                        {
                            ConfirmButton.BackgroundColor = Color.FromArgb("#1a73e8");
                            ConfirmButton.Text = "Confirm Location";
                            GetAddressForCoordinates(_selectedLatitude, _selectedLongitude);
                        }
                        else
                        {
                            ConfirmButton.BackgroundColor = Color.FromArgb("#999999");
                            ConfirmButton.Text = "Outside Malolos";
                            AddressLine1Label.Text = "Location not in";
                            AddressLine2Label.Text = "Malolos, Bulacan";

                            await DisplayMalolosOnlyAlert();
                        }
                    });
                }
            }
            catch
            {
                if (!_isModalOpen)
                {
                    await DisplayAlert("Error", "Unable to get current location.", "OK");
                }
            }
            finally
            {
                await Task.Delay(1000);

                var frame = (Frame)((Image)sender).Parent;
                frame.Opacity = 1;
                frame.IsEnabled = true;
                _isGettingLocation = false;
            }
        }

        private async Task DisplayMalolosOnlyAlert()
        {
            if (_isModalOpen) return;

            try
            {
                _isModalOpen = true;

                var alertView = new StackLayout
                {
                    Spacing = 20,
                    Padding = 20,
                    BackgroundColor = Colors.White,
                    Children =
                    {
                        new Image
                        {
                            Source = "sad.png",
                            HeightRequest = 50,
                            WidthRequest = 50,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Location Not Supported",
                            FontSize = 18,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#d32f2f"),
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Please select a location within Malolos, Bulacan only.",
                            FontSize = 14,
                            TextColor = Color.FromArgb("#666666"),
                            HorizontalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Button
                        {
                            Text = "OK",
                            BackgroundColor = Color.FromArgb("#1a73e8"),
                            TextColor = Colors.White,
                            CornerRadius = 8,
                            HeightRequest = 40,
                            FontSize = 14
                        }
                    }
                };

                var okButton = (Button)alertView.Children[3];

                bool isClosing = false;

                okButton.Clicked += async (s, args) =>
                {
                    if (isClosing) return;
                    isClosing = true;

                    okButton.IsEnabled = false;
                    okButton.Opacity = 0.5;

                    var modalPage = (s as Button)?.Parent?.Parent?.Parent as ContentPage;
                    if (modalPage != null)
                    {
                        await modalPage.FadeTo(0, 200, Easing.CubicOut);
                    }

                    await Navigation.PopModalAsync();
                    _isModalOpen = false;
                };

                var modalPage = new ContentPage
                {
                    Content = new Border
                    {
                        StrokeThickness = 0,
                        Padding = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 20 },
                        Content = alertView,
                        WidthRequest = 280,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        BackgroundColor = Colors.White
                    },
                    BackgroundColor = Color.FromArgb("#88000000")
                };

                modalPage.Opacity = 0;
                await Navigation.PushModalAsync(modalPage);
                await modalPage.FadeTo(1, 200, Easing.CubicOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Modal error: {ex.Message}");
                _isModalOpen = false;
            }
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            if (_isProcessing || _isModalOpen) return;

            try
            {
                _isProcessing = true;

                var frame = (Frame)((Image)sender).Parent?.Parent;
                if (frame != null)
                {
                    frame.Opacity = 0.5;
                    frame.IsEnabled = false;
                }

                await Navigation.PopModalAsync();
            }
            catch
            {
                await Shell.Current.GoToAsync("..");
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }
}