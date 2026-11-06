using System.Text.Json;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    passed++;
}
var options = new ConnectionOptions(@"HOST\INSTANCE", "Firma", false, "reader", "dummy-only-for-test!;Password=x", false);
var builder = new SqlConnectionStringBuilder(options.BuildConnectionString());
Check(builder.DataSource == options.Server && builder.InitialCatalog == "Firma", "Wrong server or database.");
Check(builder.Password == options.Password, "Connection-string escaping failed.");
Check(builder.ApplicationIntent == ApplicationIntent.ReadOnly && !builder.PersistSecurityInfo, "Read-only intent or secret visibility changed.");
Check(!options.ToString().Contains(options.Password), "Password leaked through diagnostic representation.");
Check(new SqlConnectionStringBuilder(options.BuildConnectionString("master")).InitialCatalog == "master", "Discovery must use master, not a stale selected company.");
var windows = new SqlConnectionStringBuilder((options with { WindowsAuthentication = true }).BuildConnectionString());
Check(windows.IntegratedSecurity && windows.UserID == "" && windows.Password == "", "Windows authentication must omit SQL credentials.");
try { (options with { Server = "" }).BuildConnectionString(); throw new Exception("Empty server accepted."); }
catch (InvalidOperationException) { passed++; }
Check(new CompanyDatabase("FirmaDb", "Nazwa firmy").ToString().Contains("FirmaDb"), "Company display must keep database unambiguous.");
string folder = Path.Combine(Path.GetTempPath(), "KartyLeczenia-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string path = Path.Combine(folder, "connection.json");
    var store = new ConnectionSettingsStore(path);
    Check(store.Load().Options is null, "First run must work without a configuration file.");
    store.Save(options, false);
    Check(!File.ReadAllText(path).Contains(options.Password), "Plaintext password persisted.");
    var loaded = store.Load();
    Check(loaded.Options?.Password == "" && !loaded.Remembered, "Opt-out must not restore a password.");
    Check(loaded.Options?.Database == "Firma" && loaded.Options.DrugGroup == "Leki", "Database/group not restored.");
    store.Save(options, true);
    string persisted = File.ReadAllText(path);
    var encrypted = JsonSerializer.Deserialize<SavedConnection>(persisted)!;
    Check(!string.IsNullOrWhiteSpace(encrypted.ProtectedPassword) && !persisted.Contains(options.Password), "DPAPI password not protected.");
    Check(store.Load().Options == options && store.Load().Remembered, "Encrypted credentials did not round-trip.");
    store.Save(options, false);
    Check(JsonSerializer.Deserialize<SavedConnection>(File.ReadAllText(path))!.ProtectedPassword is null, "Unchecking remember must remove the stored secret.");
    store.Save(options with { WindowsAuthentication = true }, true);
    Check(JsonSerializer.Deserialize<SavedConnection>(File.ReadAllText(path))!.ProtectedPassword is null, "SQL password persisted with Windows authentication.");
    File.WriteAllText(path, "corrupted");
    Check(store.Load().Options is null && store.Load().Warning != null, "Corrupt settings must produce a recoverable first-run message.");
}
finally { Directory.Delete(folder, true); }
Console.WriteLine($"Passed {passed} connection checks.");
