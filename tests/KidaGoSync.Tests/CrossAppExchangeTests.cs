using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using KidaGoSync.Connection;
using KidaGoSync.Sync;

namespace KidaGoSync.Tests;

/// <summary>
/// The daily file exchange end to end (TASK-030): this repo's real CatalogSender / ListRetriever over MTP on one side, the
/// real KidaGo app on the PDA, driven through adb, on the other. Needs a Pixel in File transfer mode with the debug app
/// installed and adb authorised, so it only runs when KIDAGO_E2E=1; otherwise it returns at once.
/// It changes the phone: it replaces the app's catalog and archives its current list. The second test also wipes the
/// app's data to start from "no catalog ever imported" (TASK-044), and re-grants all-files access through appops.
/// </summary>
[Trait("Category", "Device")]
public class CrossAppExchangeTests
{
    private const string Package = "com.afede.kidago";
    private const string Folder = "/sdcard/afede/kidago";
    private const string KnownA = "1234567890128";
    private const string KnownB = "4006381333931";
    private const string OnlyInTheOlderCatalog = "9999999999999";

    private static bool Enabled => Environment.GetEnvironmentVariable("KIDAGO_E2E") == "1";

    private static string AdbPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe");

    private static string Adb(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(AdbPath, arguments) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Replace("\r\n", "\n");
    }

    private static string Shell(string command) => Adb($"shell \"{command}\"");

    private static async Task WaitFor(Func<bool> condition, string what, int seconds = 30)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (condition()) return;
            await Task.Delay(250);
        }
        Assert.Fail($"Timed out waiting for: {what}");
    }

    private static string Screen()
    {
        Adb("shell uiautomator dump /sdcard/e2e-ui.xml");
        return Adb("shell cat /sdcard/e2e-ui.xml");
    }

    private static (int X, int Y) Centre(string ui, string nodePattern)
    {
        var m = Regex.Match(ui, nodePattern + "[^>]*?bounds=\"\\[(\\d+),(\\d+)\\]\\[(\\d+),(\\d+)\\]\"");
        if (!m.Success) m = Regex.Match(ui, "bounds=\"\\[(\\d+),(\\d+)\\]\\[(\\d+),(\\d+)\\]\"[^>]*?" + nodePattern);
        Assert.True(m.Success, $"No node matching {nodePattern} on the phone's screen");
        int[] b = [.. m.Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value))];
        return ((b[0] + b[2]) / 2, (b[1] + b[3]) / 2);
    }

    private static string PhoneList() => Shell($"cat {Folder}/scanned-list.txt");

    private static void Launch()
    {
        Adb("shell input keyevent 3"); // home: a real return to the foreground follows
        Adb($"shell am start -n {Package}/.MainActivity");
    }

    private static async Task Scan(string code)
    {
        var field = Centre(Screen(), "class=\"android.widget.EditText\"");
        Adb($"shell input tap {field.X} {field.Y}");
        Adb("shell input keyevent 123 " + string.Join(' ', Enumerable.Repeat("67", 14))); // end, then delete: a Not Found keeps its code in the field
        Adb($"shell input text {code}");
        Adb("shell input keyevent 66");
        await Task.Delay(1200);
    }

    [Fact]
    public async Task ACatalogGoesInAndTheListComesOutAndTheAppResetsItself()
    {
        if (!Enabled) return;

        var monitor = new ConnectionMonitor(new MediaDevicesMtpSource());
        monitor.Poll();
        Assert.Equal(ConnectionState.Connected, monitor.Status.State);
        var storage = new MediaDevicesPdaStorage();
        var temp = Directory.CreateTempSubdirectory().FullName;

        // Start from a known phone: no catalog files waiting, and an empty list file so any leftover list is archived on launch.
        Adb($"shell am force-stop {Package}");
        Shell($"rm -f {Folder}/product-catalog-*");
        Shell($"echo -n > {Folder}/scanned-list.txt");

        // --- Importar: the desktop sends an older and a newer catalog under their dated names.
        var older = Path.Combine(temp, "product-catalog-2026-10-05.txt");
        var newer = Path.Combine(temp, "product-catalog-2026-10-06.txt");
        File.WriteAllText(older, OnlyInTheOlderCatalog + "\n");
        File.WriteAllText(newer, $"{KnownA}\r\n{KnownB}\r\n"); // CRLF as the ERP export would have it
        foreach (var file in new[] { older, newer })
        {
            var path = file;
            Assert.True((await new CatalogSender(() => path, () => monitor.Status.Selected, storage).SendAsync(_ => Task.FromResult(false)))!.Success);
        }
        var landed = Shell($"ls {Folder}");
        Assert.Contains("product-catalog-2026-10-05.txt", landed);
        Assert.Contains("product-catalog-2026-10-06.txt", landed);

        // --- The app picks the newest up at launch, imports it and deletes the imported and older files.
        Launch();
        await WaitFor(() => !Shell($"ls {Folder}").Contains("product-catalog-"), "the app to delete the catalog files");
        await WaitFor(() => Screen().Contains("Datos actualizados"), "the status strip to show the catalog is current");

        // --- The newest catalog is the one in use; the older file's code is not.
        await Scan(KnownA);
        await Scan(KnownA);
        await Scan(KnownB);
        await Scan(OnlyInTheOlderCatalog);
        Assert.Contains("Producto no encontrado", Screen());
        await WaitFor(() => PhoneList() == $"{KnownA},2\n{KnownB},1\n", "the app to mirror its list into scanned-list.txt");

        // --- Exportar: the desktop pulls the list, saves it, and empties the PDA's file.
        var saved = Path.Combine(temp, "exported.txt");
        string? shown = null;
        var retriever = new ListRetriever(() => monitor.Status.Selected, storage, () => "afede/kidago/scanned-list.txt", () => saved, t => shown = t);
        Assert.True((await retriever.RetrieveAsync())!.Success);
        Assert.Equal($"{KnownA},2\n{KnownB},1\n", File.ReadAllText(saved));
        Assert.Equal(File.ReadAllText(saved), shown);
        Assert.Equal("0", Shell($"wc -c < {Folder}/scanned-list.txt").Trim());

        // --- The app notices the emptied file at its next launch/resume: archives the list, empties it, and leaves the file empty.
        Launch();
        await WaitFor(() => Screen().Contains("Datos actualizados"), "the app to come back");
        await Task.Delay(2000);
        Assert.Equal("0", Shell($"wc -c < {Folder}/scanned-list.txt").Trim()); // the mirror did not write the old list back
        // The scan field autofocuses and its keyboard covers the tab bar (whose nodes stay in the dump); close it first.
        if (Adb("shell dumpsys input_method").Contains("mInputShown=true")) Adb("shell input keyevent 4");
        await Task.Delay(800);
        var tab = Centre(Screen(), "text=\"Productos\"");
        Adb($"shell input tap {tab.X} {tab.Y}");
        await WaitFor(() => Screen().Contains("No hay productos escaneados"), "Productos to show its empty state");

        Directory.Delete(temp, recursive: true);
    }

    /// <summary>The catalog normalization seam (TASK-044): desktop check and Importar, then the app's import and lookup.</summary>
    [Fact]
    public async Task ADirtyCatalogIsNormalizedOnTheDesktopImportedByTheAppAndAPaddedUpcIsFound()
    {
        if (!Enabled) return;

        const string Upc = "012345678905";        // 12 digits, becomes 0012345678905
        const string PaddedUpc = "0012345678905";
        var monitor = new ConnectionMonitor(new MediaDevicesMtpSource());
        monitor.Poll();
        Assert.Equal(ConnectionState.Connected, monitor.Status.State);
        var storage = new MediaDevicesPdaStorage();
        var temp = Directory.CreateTempSubdirectory().FullName;

        // --- A phone that has never imported a catalog: its data is wiped, so the strip must say so (C22).
        Adb($"shell am force-stop {Package}");
        Shell($"rm -f {Folder}/product-catalog-*");
        Adb($"shell pm clear {Package}");
        Adb($"shell appops set {Package} MANAGE_EXTERNAL_STORAGE allow");
        Launch();
        await WaitFor(() => Screen().Contains("No hay datos en el cat"), "the strip to show No Catalog");
        Assert.DoesNotContain("Datos actualizados", Screen());

        // --- The desktop checks a dirty catalog and Importar sends only the normalized lines under the original name.
        var original = $"{KnownA}\r\n{Upc}\r\n4444\r\nX0016SA1Z7\r\n12345678901234\r\n\r\n{KnownB}\r\n";
        var path = Path.Combine(temp, "product-catalog-2026-10-07.txt");
        File.WriteAllText(path, original);
        CatalogAnalysis? asked = null;
        var sender = new CatalogSender(() => path, () => monitor.Status.Selected, storage);
        Assert.True((await sender.SendAsync(a => { asked = a; return Task.FromResult(true); }))!.Success);
        Assert.NotNull(asked);
        Assert.Equal([CatalogRule.Padded, CatalogRule.Blank, CatalogRule.NonDigit, CatalogRule.TooShort, CatalogRule.TooLong], asked!.Result.Rules.Select(r => r.Rule));
        Assert.Equal(original, File.ReadAllText(path)); // the original on the computer is untouched
        Assert.Equal($"{KnownA}\r\n{PaddedUpc}\r\n{KnownB}\r\n", Shell($"cat {Folder}/product-catalog-2026-10-07.txt").Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));

        // --- The app imports the normalized file and deletes it.
        Adb($"shell am force-stop {Package}");
        Launch();
        await WaitFor(() => !Shell($"ls {Folder}").Contains("product-catalog-"), "the app to delete the catalog file");
        await WaitFor(() => Screen().Contains("Datos actualizados"), "the status strip to show the catalog is current");

        // --- The padded UPC code is found, and so are the 13-digit ones; nothing the desktop removed is in the catalog.
        await Scan(PaddedUpc);
        Assert.Contains("Producto encontrado", Screen());
        await Scan(KnownB);
        Assert.Contains("Producto encontrado", Screen());
        await Scan("1234567890123");
        Assert.Contains("Producto no encontrado", Screen());

        // --- An empty catalog file leaves the previous catalog alone and shows Error; nothing is deleted.
        Shell($"echo -n > {Folder}/product-catalog-2026-10-08.txt");
        Launch();
        await WaitFor(() => Screen().Contains("Error al sincronizar"), "the strip to show Error for the empty file");
        Assert.Contains("product-catalog-2026-10-08.txt", Shell($"ls {Folder}"));
        await Scan(PaddedUpc);
        Assert.Contains("Producto encontrado", Screen()); // the previous catalog is exactly as it was

        Shell($"rm -f {Folder}/product-catalog-2026-10-08.txt");
        Directory.Delete(temp, recursive: true);
    }
}
