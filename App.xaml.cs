using Microsoft.Maui.Controls;
using ServiceCo.Firebase;
using System;

namespace ServiceCo
{
    public partial class App : Application
    {
        public static double SavedDriverLat { get; set; }
        public static double SavedDriverLng { get; set; }
        public static double SavedDriverDistance { get; set; }
        public static double SavedDriverEta { get; set; }
        public static bool HasDriverLocation { get; set; } = false;
        public static string LastDriverId { get; set; } = "";

        public App()
        {
            InitializeComponent();

            Console.WriteLine("📱 App starting...");

            StartAutoReactivationListener();

            bool hasValidSession = AppSession.IsActuallyLoggedIn();
            Console.WriteLine($"📱 Has valid session: {hasValidSession}");

            if (hasValidSession)
            {
                RedirectToLoggedInUser();
            }
            else
            {
                Console.WriteLine("📍 No valid session - going to GetStarted");
                MainPage = new GetStarted();
            }
        }

        private void StartAutoReactivationListener()
        {
            try
            {
                var firebaseConnection = new FirebaseConnection();
                firebaseConnection.StartAutoReactivationListener();
                Console.WriteLine("✅ Auto-reactivation listener started");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to start auto-reactivation listener: {ex.Message}");
            }
        }

        public class BoolInverseConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return !(bool)value;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return !(bool)value;
            }
        }

        private void RedirectToLoggedInUser()
        {
            string userType = AppSession.GetCurrentUserType();
            string userId = AppSession.GetCurrentUserId();
            string mobileNumber = AppSession.GetCurrentMobileNumber();

            Console.WriteLine($"🔄 Auto-login detected: {userType} - {mobileNumber}");

            if (userType == "commuter")
            {
                MainPage = new CommuterShell();
                Console.WriteLine($"📍 Redirected to CommuterShell");
            }
            else if (userType == "driver")
            {
                MainPage = new DriverShell();
                Console.WriteLine($"📍 Redirected to DriverShell");
            }
            else
            {
                MainPage = new GetStarted();
                Console.WriteLine($"⚠️ Invalid user type, going to GetStarted");
            }
        }

        protected override void OnStart()
        {
            Console.WriteLine("🚀 App started");
            AppSession.PrintAllPreferences();
        }

        protected override void OnSleep()
        {
            Console.WriteLine("😴 App going to sleep");
        }

        protected override void OnResume()
        {
            Console.WriteLine("↩️ App resuming");

            try
            {
                var firebaseConnection = new FirebaseConnection();
                _ = firebaseConnection.CheckAndUpdateExpiredSuspensionsAsync();
                Console.WriteLine("✅ Checked expired suspensions on resume");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to check expired suspensions: {ex.Message}");
            }
        }

        public static void PerformLogout()
        {
            Console.WriteLine("🚪 App-wide logout");
            AppSession.LogoutAll();

            HasDriverLocation = false;
            LastDriverId = "";

            Current.MainPage = new GetStarted();
            Console.WriteLine("📍 Now on GetStarted page");
        }
    }
}