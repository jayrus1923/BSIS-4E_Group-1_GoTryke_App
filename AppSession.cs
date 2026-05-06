using Microsoft.Maui.Storage;
using System;

namespace ServiceCo
{
    /// <summary>
    /// Manages user sessions for both commuters and drivers.
    /// Handles login state, preferences storage, and session expiration.
    /// </summary>
    public static class AppSession
    {
        #region Preference Keys

        private const string IsCommuterLoggedInKey = "IsCommuterLoggedIn";
        private const string CommuterIdKey = "CommuterId";
        private const string CommuterMobileNumberKey = "CommuterMobileNumber";
        private const string CommuterLoginTimeKey = "CommuterLoginTime";
        private const string CommuterUserTypeKey = "CommuterUserType";

        private const string IsDriverLoggedInKey = "IsDriverLoggedIn";
        private const string DriverIdKey = "DriverId";
        private const string DriverMobileNumberKey = "DriverMobileNumber";
        private const string DriverLoginTimeKey = "DriverLoginTime";
        private const string DriverUserTypeKey = "DriverUserType";

        private const string IsTypingNumberKey = "IsTypingNumber";

        // Vehicle information
        private const string DriverPlateNumberKey = "DriverPlateNumber";
        private const string DriverLicenseNumberKey = "DriverLicenseNumber";
        private const string TricycleStatusKey = "TricycleStatus";

        // Cancellation timer session
        private const string LastCancellationTimerKey = "LastCancellationTimer";
        private const string LastDriverAcceptedTimeKey = "LastDriverAcceptedTime";
        private const string LastRideRequestIdKey = "LastRideRequestId";
        private const string LastTimerCheckedKey = "LastTimerChecked";

        private static readonly TimeSpan SessionDuration = TimeSpan.FromDays(7);

        #endregion

        #region Number Entry State

        /// <summary>
        /// Marks that user is currently typing their phone number.
        /// Used to prevent auto-login during OTP verification.
        /// </summary>
        public static void StartNumberEntry()
        {
            Preferences.Set(IsTypingNumberKey, true);
        }

        /// <summary>
        /// Marks that user finished typing their phone number.
        /// </summary>
        public static void FinishNumberEntry()
        {
            Preferences.Set(IsTypingNumberKey, false);
        }

        /// <summary>
        /// Checks if user is currently entering their phone number.
        /// </summary>
        public static bool IsTypingNumber()
        {
            return Preferences.Get(IsTypingNumberKey, false);
        }

        /// <summary>
        /// Verifies if user is actually logged in, considering number entry state.
        /// </summary>
        public static bool IsActuallyLoggedIn()
        {
            if (IsTypingNumber())
            {
                return false;
            }
            return IsLoggedIn();
        }

        #endregion

        #region Commuter Session

        /// <summary>
        /// Checks if a commuter is currently logged in with valid session.
        /// </summary>
        public static bool IsCommuterLoggedIn()
        {
            try
            {
                if (!Preferences.Get(IsCommuterLoggedInKey, false))
                    return false;

                var loginTimeStr = Preferences.Get(CommuterLoginTimeKey, "");
                if (string.IsNullOrEmpty(loginTimeStr))
                    return false;

                if (DateTime.TryParse(loginTimeStr, out DateTime loginTime))
                {
                    var timeElapsed = DateTime.UtcNow - loginTime;
                    return timeElapsed <= SessionDuration;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking commuter login: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the stored commuter ID.
        /// </summary>
        public static string GetCommuterId()
        {
            return Preferences.Get(CommuterIdKey, "");
        }

        /// <summary>
        /// Gets the stored commuter mobile number.
        /// </summary>
        public static string GetCommuterMobileNumber()
        {
            return Preferences.Get(CommuterMobileNumberKey, "");
        }

        /// <summary>
        /// Gets the commuter login timestamp.
        /// </summary>
        public static DateTime? GetCommuterLoginTime()
        {
            var loginTimeStr = Preferences.Get(CommuterLoginTimeKey, "");
            if (DateTime.TryParse(loginTimeStr, out DateTime loginTime))
                return loginTime;
            return null;
        }

        /// <summary>
        /// Logs in a commuter and clears any existing driver session.
        /// </summary>
        public static void LoginCommuter(string commuterId, string mobileNumber)
        {
            // Clear any existing driver session first
            if (IsDriverLoggedIn())
            {
                LogoutDriver();
            }

            // Remove old ride data before new login
            ClearAllRidePreferences();

            Preferences.Set(IsCommuterLoggedInKey, true);
            Preferences.Set(CommuterIdKey, commuterId);
            Preferences.Set(CommuterMobileNumberKey, mobileNumber);
            Preferences.Set(CommuterLoginTimeKey, DateTime.UtcNow.ToString("o"));
            Preferences.Set(CommuterUserTypeKey, "commuter");

            FinishNumberEntry();
        }

        /// <summary>
        /// Logs out the current commuter.
        /// </summary>
        public static void LogoutCommuter()
        {
            FinishNumberEntry();

            Preferences.Remove(IsCommuterLoggedInKey);
            Preferences.Remove(CommuterIdKey);
            Preferences.Remove(CommuterMobileNumberKey);
            Preferences.Remove(CommuterLoginTimeKey);
            Preferences.Remove(CommuterUserTypeKey);
        }

        #endregion

        #region Driver Session

        /// <summary>
        /// Checks if a driver is currently logged in with valid session.
        /// </summary>
        public static bool IsDriverLoggedIn()
        {
            try
            {
                if (!Preferences.Get(IsDriverLoggedInKey, false))
                    return false;

                var loginTimeStr = Preferences.Get(DriverLoginTimeKey, "");
                if (string.IsNullOrEmpty(loginTimeStr))
                    return false;

                if (DateTime.TryParse(loginTimeStr, out DateTime loginTime))
                {
                    var timeElapsed = DateTime.UtcNow - loginTime;
                    return timeElapsed <= SessionDuration;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking driver login: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the stored driver ID.
        /// </summary>
        public static string GetDriverId()
        {
            return Preferences.Get(DriverIdKey, "");
        }

        /// <summary>
        /// Gets the stored driver mobile number.
        /// </summary>
        public static string GetDriverMobileNumber()
        {
            return Preferences.Get(DriverMobileNumberKey, "");
        }

        /// <summary>
        /// Gets the driver login timestamp.
        /// </summary>
        public static DateTime? GetDriverLoginTime()
        {
            var loginTimeStr = Preferences.Get(DriverLoginTimeKey, "");
            if (DateTime.TryParse(loginTimeStr, out DateTime loginTime))
                return loginTime;
            return null;
        }

        /// <summary>
        /// Logs in a driver and clears any existing commuter session.
        /// </summary>
        public static void LoginDriver(string driverId, string mobileNumber)
        {
            // Clear any existing commuter session first
            if (IsCommuterLoggedIn())
            {
                LogoutCommuter();
            }

            // Remove old ride data before new login
            ClearAllRidePreferences();

            Preferences.Set(IsDriverLoggedInKey, true);
            Preferences.Set(DriverIdKey, driverId);
            Preferences.Set(DriverMobileNumberKey, mobileNumber);
            Preferences.Set(DriverLoginTimeKey, DateTime.UtcNow.ToString("o"));
            Preferences.Set(DriverUserTypeKey, "driver");

            FinishNumberEntry();
        }

        /// <summary>
        /// Logs out the current driver and clears vehicle info.
        /// </summary>
        public static void LogoutDriver()
        {
            FinishNumberEntry();

            Preferences.Remove(IsDriverLoggedInKey);
            Preferences.Remove(DriverIdKey);
            Preferences.Remove(DriverMobileNumberKey);
            Preferences.Remove(DriverLoginTimeKey);
            Preferences.Remove(DriverUserTypeKey);

            // Clear vehicle info on logout
            Preferences.Remove(DriverPlateNumberKey);
            Preferences.Remove(DriverLicenseNumberKey);
            Preferences.Remove(TricycleStatusKey);
        }

        #endregion

        #region Vehicle Information

        /// <summary>
        /// Stores the driver's plate number.
        /// </summary>
        public static void SaveDriverPlateNumber(string plateNumber)
        {
            Preferences.Set(DriverPlateNumberKey, plateNumber ?? "");
        }

        /// <summary>
        /// Retrieves the driver's plate number.
        /// </summary>
        public static string GetDriverPlateNumber()
        {
            return Preferences.Get(DriverPlateNumberKey, "");
        }

        /// <summary>
        /// Stores the driver's license number.
        /// </summary>
        public static void SaveDriverLicenseNumber(string licenseNumber)
        {
            Preferences.Set(DriverLicenseNumberKey, licenseNumber ?? "");
        }

        /// <summary>
        /// Retrieves the driver's license number.
        /// </summary>
        public static string GetDriverLicenseNumber()
        {
            return Preferences.Get(DriverLicenseNumberKey, "");
        }

        /// <summary>
        /// Stores the tricycle approval status.
        /// </summary>
        public static void SaveTricycleStatus(string status)
        {
            Preferences.Set(TricycleStatusKey, status ?? "");
        }

        /// <summary>
        /// Retrieves the tricycle approval status.
        /// </summary>
        public static string GetTricycleStatus()
        {
            return Preferences.Get(TricycleStatusKey, "");
        }

        /// <summary>
        /// Checks if driver has all required vehicle information.
        /// </summary>
        public static bool HasRequiredVehicleInfo()
        {
            string plateNumber = GetDriverPlateNumber();
            string licenseNumber = GetDriverLicenseNumber();
            string status = GetTricycleStatus();

            bool hasPlate = !string.IsNullOrWhiteSpace(plateNumber) &&
                           plateNumber != "Not Set" &&
                           plateNumber != "Unknown";

            bool hasLicense = !string.IsNullOrWhiteSpace(licenseNumber) &&
                             licenseNumber != "Unknown";

            bool hasActiveStatus = status?.ToLower() == "active";

            return hasPlate && hasLicense && hasActiveStatus;
        }

        /// <summary>
        /// Returns a formatted message of missing vehicle requirements.
        /// </summary>
        public static string GetMissingVehicleRequirements()
        {
            string message = "";
            string plateNumber = GetDriverPlateNumber();
            string licenseNumber = GetDriverLicenseNumber();
            string status = GetTricycleStatus();

            bool hasPlate = !string.IsNullOrWhiteSpace(plateNumber) &&
                           plateNumber != "Not Set" &&
                           plateNumber != "Unknown";

            bool hasLicense = !string.IsNullOrWhiteSpace(licenseNumber) &&
                             licenseNumber != "Unknown";

            bool hasActiveStatus = status?.ToLower() == "active";

            if (!hasPlate) message += "• Plate Number is missing or not set\n";
            if (!hasLicense) message += "• Driver's License is missing or not set\n";
            if (!hasActiveStatus) message += $"• Vehicle status is '{status}'. Must be 'Active'\n";

            return message;
        }

        #endregion

        #region General Session Methods

        /// <summary>
        /// Checks if any user (commuter or driver) is logged in.
        /// </summary>
        public static bool IsLoggedIn()
        {
            return IsCommuterLoggedIn() || IsDriverLoggedIn();
        }

        /// <summary>
        /// Gets the type of currently logged in user.
        /// </summary>
        public static string GetUserType()
        {
            if (IsCommuterLoggedIn()) return "commuter";
            if (IsDriverLoggedIn()) return "driver";
            return "";
        }

        /// <summary>
        /// Gets the current user ID (commuter or driver).
        /// </summary>
        public static string GetCurrentUserId()
        {
            if (IsCommuterLoggedIn()) return GetCommuterId();
            if (IsDriverLoggedIn()) return GetDriverId();
            return "";
        }

        /// <summary>
        /// Gets the current user's mobile number.
        /// </summary>
        public static string GetCurrentMobileNumber()
        {
            if (IsCommuterLoggedIn()) return GetCommuterMobileNumber();
            if (IsDriverLoggedIn()) return GetDriverMobileNumber();
            return "";
        }

        /// <summary>
        /// Alias for GetCurrentMobileNumber.
        /// </summary>
        public static string GetMobileNumber()
        {
            return GetCurrentMobileNumber();
        }

        /// <summary>
        /// Gets the current user type (commuter or driver).
        /// </summary>
        public static string GetCurrentUserType()
        {
            if (IsCommuterLoggedIn()) return "commuter";
            if (IsDriverLoggedIn()) return "driver";
            return "";
        }

        // ========== ADD THESE TWO PROPERTIES TO FIX THE ERROR ==========

        /// <summary>
        /// Gets or sets the current driver ID.
        /// Used by Driver_SubmitRequirements and other driver pages.
        /// </summary>
        public static string CurrentDriverId
        {
            get => GetDriverId();
            set => Preferences.Set(DriverIdKey, value);
        }

        /// <summary>
        /// Gets or sets the current driver phone number.
        /// Used by Driver_SubmitRequirements and other driver pages.
        /// </summary>
        public static string CurrentDriverPhone
        {
            get => GetDriverMobileNumber();
            set => Preferences.Set(DriverMobileNumberKey, value);
        }

        // ========== END OF ADDED PROPERTIES ==========

        /// <summary>
        /// Gets the remaining time before session expires.
        /// </summary>
        public static TimeSpan GetSessionTimeRemaining()
        {
            DateTime? loginTime = null;

            if (IsCommuterLoggedIn())
                loginTime = GetCommuterLoginTime();
            else if (IsDriverLoggedIn())
                loginTime = GetDriverLoginTime();

            if (loginTime.HasValue)
            {
                var elapsed = DateTime.UtcNow - loginTime.Value;
                var remaining = SessionDuration - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }

            return TimeSpan.Zero;
        }

        /// <summary>
        /// Returns a string summary of the current session.
        /// </summary>
        public static string GetSessionInfo()
        {
            if (IsCommuterLoggedIn())
            {
                var timeRemaining = GetSessionTimeRemaining();
                return $"Commuter - ID: {GetCommuterId()}, Mobile: {GetCommuterMobileNumber()}, Session expires in: {timeRemaining.Days}d {timeRemaining.Hours}h";
            }
            else if (IsDriverLoggedIn())
            {
                var timeRemaining = GetSessionTimeRemaining();
                return $"Driver - ID: {GetDriverId()}, Mobile: {GetDriverMobileNumber()}, Session expires in: {timeRemaining.Days}d {timeRemaining.Hours}h";
            }
            return "No active session";
        }

        /// <summary>
        /// Logs out both commuter and driver sessions.
        /// </summary>
        public static void LogoutAll()
        {
            FinishNumberEntry();

            Preferences.Remove(IsCommuterLoggedInKey);
            Preferences.Remove(CommuterIdKey);
            Preferences.Remove(CommuterMobileNumberKey);
            Preferences.Remove(CommuterLoginTimeKey);
            Preferences.Remove(CommuterUserTypeKey);

            Preferences.Remove(IsDriverLoggedInKey);
            Preferences.Remove(DriverIdKey);
            Preferences.Remove(DriverMobileNumberKey);
            Preferences.Remove(DriverLoginTimeKey);
            Preferences.Remove(DriverUserTypeKey);

            // Clear vehicle info
            Preferences.Remove(DriverPlateNumberKey);
            Preferences.Remove(DriverLicenseNumberKey);
            Preferences.Remove(TricycleStatusKey);

            // Clear cancellation timer
            ClearCancellationTimer();
        }

        /// <summary>
        /// Validates sessions on app startup and removes expired ones.
        /// </summary>
        public static bool ValidateSessionOnStartup()
        {
            bool hadCommuterSession = Preferences.Get(IsCommuterLoggedInKey, false);
            bool hadDriverSession = Preferences.Get(IsDriverLoggedInKey, false);

            bool commuterValid = IsCommuterLoggedIn();
            bool driverValid = IsDriverLoggedIn();

            // Clean up expired commuter session
            if (hadCommuterSession && !commuterValid)
            {
                Preferences.Remove(IsCommuterLoggedInKey);
                Preferences.Remove(CommuterIdKey);
                Preferences.Remove(CommuterMobileNumberKey);
                Preferences.Remove(CommuterLoginTimeKey);
                Preferences.Remove(CommuterUserTypeKey);
            }

            // Clean up expired driver session
            if (hadDriverSession && !driverValid)
            {
                Preferences.Remove(IsDriverLoggedInKey);
                Preferences.Remove(DriverIdKey);
                Preferences.Remove(DriverMobileNumberKey);
                Preferences.Remove(DriverLoginTimeKey);
                Preferences.Remove(DriverUserTypeKey);
            }

            return commuterValid || driverValid;
        }

        /// <summary>
        /// Gets current user ID (alias for GetCurrentUserId).
        /// </summary>
        public static string GetUserId()
        {
            return GetCurrentUserId();
        }

        /// <summary>
        /// Clears all preferences (use with caution).
        /// </summary>
        public static void ClearAllSessions()
        {
            Preferences.Clear();
        }

        /// <summary>
        /// Prints all current preferences for debugging.
        /// </summary>
        public static void PrintAllPreferences()
        {
            System.Diagnostics.Debug.WriteLine("Current Preferences:");
            System.Diagnostics.Debug.WriteLine($"IsTypingNumber: {Preferences.Get(IsTypingNumberKey, false)}");
            System.Diagnostics.Debug.WriteLine($"IsCommuterLoggedIn: {Preferences.Get(IsCommuterLoggedInKey, false)}");
            System.Diagnostics.Debug.WriteLine($"CommuterId: {Preferences.Get(CommuterIdKey, "")}");
            System.Diagnostics.Debug.WriteLine($"CommuterMobile: {Preferences.Get(CommuterMobileNumberKey, "")}");
            System.Diagnostics.Debug.WriteLine($"CommuterLoginTime: {Preferences.Get(CommuterLoginTimeKey, "")}");
            System.Diagnostics.Debug.WriteLine($"IsDriverLoggedIn: {Preferences.Get(IsDriverLoggedInKey, false)}");
            System.Diagnostics.Debug.WriteLine($"DriverId: {Preferences.Get(DriverIdKey, "")}");
            System.Diagnostics.Debug.WriteLine($"DriverMobile: {Preferences.Get(DriverMobileNumberKey, "")}");
            System.Diagnostics.Debug.WriteLine($"DriverLoginTime: {Preferences.Get(DriverLoginTimeKey, "")}");
            System.Diagnostics.Debug.WriteLine($"DriverPlateNumber: {Preferences.Get(DriverPlateNumberKey, "")}");
            System.Diagnostics.Debug.WriteLine($"DriverLicenseNumber: {Preferences.Get(DriverLicenseNumberKey, "")}");
            System.Diagnostics.Debug.WriteLine($"TricycleStatus: {Preferences.Get(TricycleStatusKey, "")}");
            System.Diagnostics.Debug.WriteLine($"LastCancellationTimer: {Preferences.Get(LastCancellationTimerKey, false)}");
            System.Diagnostics.Debug.WriteLine($"LastRideRequestId: {Preferences.Get(LastRideRequestIdKey, "")}");
            System.Diagnostics.Debug.WriteLine($"LastDriverAcceptedTime: {Preferences.Get(LastDriverAcceptedTimeKey, "")}");
            System.Diagnostics.Debug.WriteLine($"LastTimerChecked: {Preferences.Get(LastTimerCheckedKey, "")}");
        }

        /// <summary>
        /// Checks if any session exists (regardless of validity).
        /// </summary>
        public static bool CheckSessionExists()
        {
            bool commuterExists = Preferences.Get(IsCommuterLoggedInKey, false);
            bool driverExists = Preferences.Get(IsDriverLoggedInKey, false);
            return commuterExists || driverExists;
        }

        #endregion

        #region User Name Storage

        /// <summary>
        /// Stores commuter's first and last name.
        /// </summary>
        public static void SaveCommuterName(string firstName, string lastName)
        {
            try
            {
                if (!string.IsNullOrEmpty(firstName))
                    Preferences.Set("CommuterFirstName", firstName);

                if (!string.IsNullOrEmpty(lastName))
                    Preferences.Set("CommuterLastName", lastName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving commuter name: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the full name of the logged-in commuter.
        /// </summary>
        public static string GetCommuterName()
        {
            try
            {
                var firstName = Preferences.Get("CommuterFirstName", "");
                var lastName = Preferences.Get("CommuterLastName", "");
                return $"{firstName} {lastName}".Trim();
            }
            catch
            {
                return "Commuter";
            }
        }

        /// <summary>
        /// Gets commuter's first name only.
        /// </summary>
        public static string GetCommuterFirstName() => Preferences.Get("CommuterFirstName", "");

        /// <summary>
        /// Gets commuter's last name only.
        /// </summary>
        public static string GetCommuterLastName() => Preferences.Get("CommuterLastName", "");

        /// <summary>
        /// Alias for GetCommuterMobileNumber.
        /// </summary>
        public static string GetCommuterPhone() => GetCommuterMobileNumber();

        /// <summary>
        /// Stores driver's first and last name.
        /// </summary>
        public static void SaveDriverName(string firstName, string lastName)
        {
            try
            {
                if (!string.IsNullOrEmpty(firstName))
                    Preferences.Set("DriverFirstName", firstName);

                if (!string.IsNullOrEmpty(lastName))
                    Preferences.Set("DriverLastName", lastName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving driver name: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the full name of the logged-in driver.
        /// </summary>
        public static string GetDriverName()
        {
            try
            {
                var firstName = Preferences.Get("DriverFirstName", "");
                var lastName = Preferences.Get("DriverLastName", "");
                return $"{firstName} {lastName}".Trim();
            }
            catch
            {
                return "Driver";
            }
        }

        /// <summary>
        /// Gets driver's full name (alias for GetDriverName).
        /// </summary>
        public static string GetDriverFullName() => GetDriverName();

        /// <summary>
        /// Gets driver's first name only.
        /// </summary>
        public static string GetDriverFirstName() => Preferences.Get("DriverFirstName", "");

        /// <summary>
        /// Gets driver's last name only.
        /// </summary>
        public static string GetDriverLastName() => Preferences.Get("DriverLastName", "");

        /// <summary>
        /// Checks if commuter name is stored.
        /// </summary>
        public static bool HasCommuterName()
        {
            return !string.IsNullOrEmpty(Preferences.Get("CommuterFirstName", "")) ||
                   !string.IsNullOrEmpty(Preferences.Get("CommuterLastName", ""));
        }

        /// <summary>
        /// Checks if driver name is stored.
        /// </summary>
        public static bool HasDriverName()
        {
            return !string.IsNullOrEmpty(Preferences.Get("DriverFirstName", "")) ||
                   !string.IsNullOrEmpty(Preferences.Get("DriverLastName", ""));
        }

        #endregion

        #region Cancellation Timer Session

        /// <summary>
        /// Saves cancellation timer data to session for persistence.
        /// </summary>
        public static void SaveCancellationTimer(string rideRequestId, DateTime driverAcceptedTime)
        {
            try
            {
                Preferences.Set(LastRideRequestIdKey, rideRequestId);
                Preferences.Set(LastDriverAcceptedTimeKey, driverAcceptedTime.ToString("o"));
                Preferences.Set(LastCancellationTimerKey, true);
                Preferences.Set(LastTimerCheckedKey, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving cancellation timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieves saved cancellation timer data.
        /// </summary>
        public static (bool hasTimer, string rideRequestId, DateTime? driverAcceptedTime) GetCancellationTimer()
        {
            try
            {
                var hasTimer = Preferences.Get(LastCancellationTimerKey, false);
                var rideRequestId = Preferences.Get(LastRideRequestIdKey, "");
                var driverAcceptedTimeStr = Preferences.Get(LastDriverAcceptedTimeKey, "");

                DateTime? driverAcceptedTime = null;
                if (!string.IsNullOrEmpty(driverAcceptedTimeStr) &&
                    DateTime.TryParse(driverAcceptedTimeStr, out DateTime parsedTime))
                {
                    driverAcceptedTime = parsedTime;
                }

                return (hasTimer, rideRequestId, driverAcceptedTime);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting cancellation timer: {ex.Message}");
                return (false, "", null);
            }
        }

        /// <summary>
        /// Removes saved cancellation timer data.
        /// </summary>
        public static void ClearCancellationTimer()
        {
            try
            {
                Preferences.Remove(LastCancellationTimerKey);
                Preferences.Remove(LastRideRequestIdKey);
                Preferences.Remove(LastDriverAcceptedTimeKey);
                Preferences.Remove(LastTimerCheckedKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing cancellation timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if cancellation is still allowed based on saved session timer.
        /// </summary>
        public static bool IsWithin3MinutesFromSession(string currentRideRequestId)
        {
            try
            {
                var (hasTimer, rideRequestId, driverAcceptedTime) = GetCancellationTimer();

                if (!hasTimer || string.IsNullOrEmpty(rideRequestId) || !driverAcceptedTime.HasValue)
                {
                    return true;
                }

                if (rideRequestId != currentRideRequestId)
                {
                    ClearCancellationTimer();
                    return true;
                }

                var timeSinceAcceptance = DateTime.UtcNow - driverAcceptedTime.Value;
                return timeSinceAcceptance.TotalMinutes <= 3;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking session timer: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Checks if there's an active cancellation timer for a specific ride.
        /// </summary>
        public static bool HasActiveCancellationTimer(string rideRequestId)
        {
            try
            {
                var (hasTimer, storedRideRequestId, driverAcceptedTime) = GetCancellationTimer();

                if (!hasTimer || string.IsNullOrEmpty(storedRideRequestId) || !driverAcceptedTime.HasValue)
                {
                    return false;
                }

                if (storedRideRequestId != rideRequestId)
                {
                    return false;
                }

                var timeSinceAcceptance = DateTime.UtcNow - driverAcceptedTime.Value;
                return timeSinceAcceptance.TotalMinutes <= 3;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking active timer: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Updates the last timer check timestamp.
        /// </summary>
        public static void MarkTimerAsChecked()
        {
            try
            {
                Preferences.Set(LastTimerCheckedKey, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking timer check: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the last time the timer was checked.
        /// </summary>
        public static DateTime? GetLastTimerCheckTime()
        {
            try
            {
                var lastCheckStr = Preferences.Get(LastTimerCheckedKey, "");
                if (DateTime.TryParse(lastCheckStr, out DateTime lastCheck))
                {
                    return lastCheck;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting last timer check: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Updates or creates cancellation timer if needed.
        /// </summary>
        public static void UpdateCancellationTimerIfNeeded(string rideRequestId, DateTime? newAcceptedTime)
        {
            try
            {
                var (hasTimer, storedRideRequestId, _) = GetCancellationTimer();

                if (!hasTimer || storedRideRequestId != rideRequestId)
                {
                    if (newAcceptedTime.HasValue)
                    {
                        SaveCancellationTimer(rideRequestId, newAcceptedTime.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating cancellation timer: {ex.Message}");
            }
        }

        #endregion

        #region Ride Preferences Cleanup

        /// <summary>
        /// Removes all ride-related preferences when a new session starts.
        /// Prevents stale ride data from showing up after login.
        /// </summary>
        public static void ClearAllRidePreferences()
        {
            try
            {
                string[] ridePrefs = {
                    "LastRideStatus", "LastDriverId", "LastDriverName", "LastDriverVehicle",
                    "LastDriverPlate", "LastDriverContact", "LastDriverRating", "LastPickup",
                    "LastDropoff", "LastFare", "LastRideId", "LastDriverLicense", "LastDistance",
                    "LastTime", "LastDriverLat", "LastDriverLng", "LastButtonState",
                    "LastCancellationAllowed", "LastPanelExpanded", "LastSearchingSectionVisible",
                    "LastDriverFoundSectionVisible", "LastSearchingButtonsVisible",
                    "LastDriverFoundButtonsVisible", "LastArrivedButtonsVisible",
                    "LastTripStartedButtonsVisible", "LastTripCompletedButtonsVisible",
                    "LastPaymentPendingButtonsVisible", "LastCancelBookingButtonVisible",
                    "LastCancelDriverFoundButtonVisible", "LastTrackDriverButtonVisible",
                    "LastDriverLocationInfoVisible", "LastDriverStatusFrameVisible",
                    "LastArrivedStatusFrameVisible", "LastTripStartedStatusFrameVisible",
                    "LastTripCompletedStatusFrameVisible", "LastPaymentPendingStatusFrameVisible",
                    "LastPanelTitle", "LastPanelSubtitle", "LastDriverFoundHeader",
                    "LastDriverLocationText", "LastDriverStatusText", "LastArrivedStatusText",
                    "LastTripStartedStatusText", "LastTripCompletedStatusText",
                    "LastPaymentPendingStatusText"
                };

                int clearedCount = 0;
                foreach (string key in ridePrefs)
                {
                    if (Preferences.ContainsKey(key))
                    {
                        Preferences.Remove(key);
                        clearedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing ride preferences: {ex.Message}");
            }
        }

        #endregion
    }
}