using Windows.Security.Credentials.UI;

namespace KartyLeczenia.Windows;

/// <summary>
/// Mandatory local Windows Hello confirmation before privileged SQL backup or
/// restore. It does not collect PINs, change SQL identity or grant privileges.
/// The desktop-specific HWND API keeps the Windows prompt in front of WinForms.
/// Deny by default when the API is unavailable or the user cancels.
/// </summary>
internal static class WindowsHelloConsent
{
    internal static async Task<bool> VerifyAsync(Form owner, string purpose)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
            {
                MessageBox.Show(owner, availability switch
                {
                    UserConsentVerifierAvailability.NotConfiguredForUser =>
                        "Windows Hello nie jest skonfigurowany dla tego konta. Ustaw PIN Windows Hello w Ustawieniach Windows → Konta → Opcje logowania.",
                    UserConsentVerifierAvailability.DisabledByPolicy =>
                        "Windows Hello jest wyłączony przez zasady systemowe.",
                    UserConsentVerifierAvailability.DeviceNotPresent =>
                        "Na tym komputerze nie ma dostępnego mechanizmu Windows Hello.",
                    _ => "Windows Hello jest obecnie niedostępny. Spróbuj ponownie po zalogowaniu do Windows."
                } + "\n\nOperacja SQL nie została rozpoczęta.",
                    "Wymagane Windows Hello", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // RequestVerificationAsync is for UWP; the WinForms desktop API
            // must be given the current window handle.
            var outcome = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(
                owner.Handle, $"SQL KartyLeczenia — potwierdź: {purpose}");
            if (outcome == UserConsentVerificationResult.Verified)
                return true;

            MessageBox.Show(owner, outcome switch
            {
                UserConsentVerificationResult.Canceled => "Anulowano potwierdzenie Windows Hello.",
                UserConsentVerificationResult.RetriesExhausted => "Przekroczono limit prób potwierdzenia Windows Hello.",
                UserConsentVerificationResult.DeviceBusy => "Urządzenie Windows Hello jest zajęte.",
                UserConsentVerificationResult.DeviceNotPresent => "Windows Hello nie jest dostępny.",
                UserConsentVerificationResult.DisabledByPolicy => "Windows Hello jest zablokowany przez zasady systemowe.",
                UserConsentVerificationResult.NotConfiguredForUser => "Skonfiguruj PIN lub biometrię Windows Hello dla tego użytkownika.",
                _ => "Tożsamość nie została potwierdzona przez Windows Hello."
            } + "\n\nOperacja SQL nie została rozpoczęta.",
                "Brak potwierdzenia tożsamości", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CrashLog.Write("WindowsHello.Verify", ex);
            MessageBox.Show(owner,
                "Nie udało się uruchomić systemowego okna Windows Hello. " +
                "Ze względów bezpieczeństwa operacja SQL została anulowana.\n\n" + ex.Message,
                "Błąd Windows Hello", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
