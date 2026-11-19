using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// SQL is authoritative for clinic details shared by all stations. The prior
/// local clinic.json is a one-time seed only if SQL has no clinic details.
/// </summary>
internal sealed class SharedClinicService(ConnectionSettingsStore connectionStore)
{
    internal sealed record Snapshot(ClinicSettings Settings, string Revision);

    internal static bool HasDetails(ClinicSettings value) =>
        !string.IsNullOrWhiteSpace(value.Name) ||
        !string.IsNullOrWhiteSpace(value.Address) ||
        !string.IsNullOrWhiteSpace(value.Doctor) ||
        !string.IsNullOrWhiteSpace(value.Phone) ||
        value.UseClinicStamp || value.UseDoctorStamp ||
        value.ClinicStampPng is { Length: > 0 } ||
        value.DoctorStampPng is { Length: > 0 };

    internal static bool ShouldImportLocal(ClinicSettings? shared, ClinicSettings local) =>
        HasDetails(local) && (shared is null || !HasDetails(shared));

    internal static string Revision(ClinicSettings value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    internal static void VerifyStamps(ClinicSettings value)
    {
        ValidatePng(value.ClinicStampPng, "lecznicy");
        ValidatePng(value.DoctorStampPng, "lekarza");
        if (value.UseClinicStamp && value.ClinicStampPng is not { Length: > 0 })
            throw new InvalidOperationException("Wybierz pieczątkę lecznicy PNG albo wyłącz jej używanie.");
        if (value.UseDoctorStamp && value.DoctorStampPng is not { Length: > 0 })
            throw new InvalidOperationException("Wybierz pieczątkę lekarza PNG albo wyłącz jej używanie.");
    }

    internal static void ValidatePng(byte[]? png, string subject)
    {
        if (png is null) return;
        if (png.Length < 8 || png.Length > 2 * 1024 * 1024 || !CardSchema.HasPngHeader(png))
            throw new InvalidDataException("Pieczątka " + subject + " musi być plikiem PNG nie większym niż 2 MB.");
    }

    private SqlConnection Open()
    {
        var options = connectionStore.Load().Options ??
            throw new InvalidOperationException("Skonfiguruj archiwum SQL przed odczytem danych lecznicy.");
        var connection = new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia")) { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal Snapshot Load()
    {
        var local = ClinicSettingsStore.Load();
        using var db = Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        var shared = Read(db, tx);
        if (ShouldImportLocal(shared, local))
        {
            VerifyStamps(local);
            Write(db, tx, local);
            shared = local;
        }
        tx.Commit();
        var result = shared ?? new ClinicSettings();
        CacheLocally(result);
        return new(result, Revision(result));
    }

    internal Snapshot Save(ClinicSettings updated, string expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision))
            throw new InvalidOperationException("Najpierw odśwież dane lecznicy z SQL.");
        VerifyStamps(updated);
        using var db = Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        var before = Read(db, tx) ?? new ClinicSettings();
        if (!string.Equals(expectedRevision, Revision(before), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Dane lecznicy zostały zmienione na innym stanowisku. Odśwież je przed ponownym zapisem.");
        Write(db, tx, updated);
        tx.Commit();
        CacheLocally(updated);
        return new(updated, Revision(updated));
    }

    private static void CacheLocally(ClinicSettings clinic)
    {
        // Kept solely for migration/backward compatibility; SQL remains the source.
        try { ClinicSettingsStore.Save(clinic); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ReactKL.Clinic.LocalCache", ex);
        }
    }

    private static ClinicSettings? Read(SqlConnection db, SqlTransaction tx)
    {
        using var command = new SqlCommand("""
            SELECT name, address, doctor, use_doctor_stamp, doctor_stamp_png,
                   use_clinic_stamp, clinic_stamp_png, phone
            FROM dbo.clinic_settings WITH (UPDLOCK, HOLDLOCK) WHERE id = 1;
            """, db, tx) { CommandTimeout = 20 };
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        string StringAt(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
        bool BoolAt(int i) => !reader.IsDBNull(i) && reader.GetBoolean(i);
        byte[]? BytesAt(int i) => reader.IsDBNull(i) ? null : reader.GetFieldValue<byte[]>(i);
        return new ClinicSettings(StringAt(0), StringAt(1), StringAt(2))
        {
            UseDoctorStamp = BoolAt(3),
            DoctorStampPng = BytesAt(4),
            UseClinicStamp = BoolAt(5),
            ClinicStampPng = BytesAt(6),
            Phone = StringAt(7)
        };
    }

    private static void Write(SqlConnection db, SqlTransaction tx, ClinicSettings value)
    {
        using var command = new SqlCommand("""
            UPDATE dbo.clinic_settings SET name = @name, address = @address,
                doctor = @doctor, phone = @phone, use_doctor_stamp = @doctorEnabled,
                doctor_stamp_png = @doctorPng, use_clinic_stamp = @clinicEnabled,
                clinic_stamp_png = @clinicPng WHERE id = 1;
            IF @@ROWCOUNT = 0
                INSERT dbo.clinic_settings
                    (id, name, address, doctor, phone, use_doctor_stamp,
                     doctor_stamp_png, use_clinic_stamp, clinic_stamp_png)
                VALUES (1, @name, @address, @doctor, @phone,
                        @doctorEnabled, @doctorPng, @clinicEnabled, @clinicPng);
            """, db, tx) { CommandTimeout = 20 };
        command.Parameters.Add("@name", SqlDbType.NVarChar, -1).Value = value.Name;
        command.Parameters.Add("@address", SqlDbType.NVarChar, -1).Value = value.Address;
        command.Parameters.Add("@doctor", SqlDbType.NVarChar, -1).Value = value.Doctor;
        command.Parameters.Add("@phone", SqlDbType.NVarChar, -1).Value = value.Phone;
        command.Parameters.Add("@doctorEnabled", SqlDbType.Bit).Value = value.UseDoctorStamp;
        command.Parameters.Add("@doctorPng", SqlDbType.VarBinary, -1).Value =
            (object?)value.DoctorStampPng ?? DBNull.Value;
        command.Parameters.Add("@clinicEnabled", SqlDbType.Bit).Value = value.UseClinicStamp;
        command.Parameters.Add("@clinicPng", SqlDbType.VarBinary, -1).Value =
            (object?)value.ClinicStampPng ?? DBNull.Value;
        command.ExecuteNonQuery();
    }
}
