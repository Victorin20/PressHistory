using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PressHistory.Models;
using PressHistory.Services;
using PressHistory.ViewModels;

namespace PressHistory.Tests;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Construction et rendu de l’interface", TestInterfaceRenderingAsync),
            ("Ajout, déduplication et promotion", TestDeduplicationAsync),
            ("Limite de rétention", TestRetentionLimitAsync),
            ("Limite globale de stockage", TestStorageBudgetAsync),
            ("Persistance Unicode", TestPersistenceRoundTripAsync),
            ("Récupération depuis la sauvegarde", TestBackupRecoveryAsync),
            ("Purge physique de l’historique", TestPhysicalPurgeAsync),
            ("Paramètres corrompus", TestCorruptedSettingsAsync),
            ("Marqueurs de confidentialité", TestPrivacyMarkersAsync),
            ("Commande de démarrage citée", TestStartupCommandAsync)
        };

        var failures = new List<string>();
        Console.OutputEncoding = Encoding.UTF8;

        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"✓ {test.Name}");
            }
            catch (Exception exception)
            {
                failures.Add($"{test.Name}: {exception.Message}");
                Console.WriteLine($"✗ {test.Name}");
                Console.WriteLine($"  {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests réussis");

        return failures.Count == 0 ? 0 : 1;
    }

    private static Task TestInterfaceRenderingAsync()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            TemporaryScope? scope = null;
            HistoryService? history = null;
            MainViewModel? viewModel = null;

            try
            {
                var previewApplication = new App();
                previewApplication.InitializeComponent();
                scope = new TemporaryScope();
                history = scope.CreateHistoryService();
                var now = DateTimeOffset.UtcNow;
                history.AddOrPromote(
                    "Bonjour ! Voici un texte copié depuis votre navigateur.\nIl reste disponible pour plus tard.",
                    now);
                history.AddOrPromote("https://exemple.fr/un-lien-pratique", now.AddMinutes(-3));
                history.AddOrPromote(
                    "Référence client : PH-2026-0817 — à conserver",
                    now.AddHours(-1));

                var dispatcher = Dispatcher.CurrentDispatcher;
                viewModel = new MainViewModel(
                    history,
                    new ClipboardService(dispatcher),
                    new SettingsStore(scope.Paths),
                    new StartupManager(),
                    new AppSettings(),
                    dispatcher)
                {
                    HotkeyAvailable = true
                };

                var window = new MainWindow(viewModel);
                window.AllowClose = true;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20_000;
                window.Top = -20_000;
                window.ShowInTaskbar = false;
                window.Show();
                window.UpdateLayout();

                using (var monitor = new ClipboardMonitor())
                {
                    monitor.Initialize(new WindowInteropHelper(window).Handle);
                }

                var root = (FrameworkElement)window.Content;
                var historyList = (ListBox)window.FindName("HistoryList");
                var emptyState = (Border)window.FindName("EmptyState");
                var headerPanel = (FrameworkElement)window.FindName("HeaderPanel");
                var searchPanel = (FrameworkElement)window.FindName("SearchPanel");
                var historyPanel = (FrameworkElement)window.FindName("HistoryPanel");
                var footerPanel = (FrameworkElement)window.FindName("FooterPanel");
                Assert.True(root.IsMeasureValid);
                Assert.True(root.IsArrangeValid);
                Assert.True(root.ActualWidth > 450);
                Assert.True(root.ActualHeight > 600);
                Assert.Equal("#FFF5F6FA", window.Background.ToString().ToUpperInvariant());
                Assert.Equal(3, historyList.Items.Count);
                Assert.Equal(Visibility.Visible, historyList.Visibility);
                Assert.Equal(Visibility.Collapsed, emptyState.Visibility);
                var headerTop = headerPanel.TranslatePoint(new Point(0, 0), root).Y;
                var searchTop = searchPanel.TranslatePoint(new Point(0, 0), root).Y;
                var historyTop = historyPanel.TranslatePoint(new Point(0, 0), root).Y;
                var footerTop = footerPanel.TranslatePoint(new Point(0, 0), root).Y;
                Assert.True(searchTop >= headerTop + headerPanel.ActualHeight - 1);
                Assert.True(historyTop >= searchTop + searchPanel.ActualHeight - 1);
                Assert.True(footerTop >= historyTop + historyPanel.ActualHeight - 1);

                var previewPath = Environment.GetEnvironmentVariable("PRESSHISTORY_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    VirtualizingPanel.SetIsVirtualizing(historyList, false);
                    ScrollViewer.SetCanContentScroll(historyList, false);
                    historyList.ClipToBounds = true;
                    window.UpdateLayout();
                    var pixelWidth = (int)Math.Ceiling(window.ActualWidth);
                    var pixelHeight = (int)Math.Ceiling(window.ActualHeight);
                    var contentBitmap = new RenderTargetBitmap(
                        pixelWidth,
                        pixelHeight,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    contentBitmap.Render(window);

                    var composite = new DrawingVisual();
                    using (var drawingContext = composite.RenderOpen())
                    {
                        drawingContext.DrawRectangle(
                            new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xFA)),
                            null,
                            new Rect(0, 0, pixelWidth, pixelHeight));
                        drawingContext.DrawImage(
                            contentBitmap,
                            new Rect(0, 0, pixelWidth, pixelHeight));
                    }

                    var bitmap = new RenderTargetBitmap(
                        pixelWidth,
                        pixelHeight,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(composite);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
                    using var stream = File.Create(previewPath);
                    encoder.Save(stream);
                }

                viewModel.ClearHistoryAsync().GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, historyList.Visibility);
                Assert.Equal(Visibility.Visible, emptyState.Visibility);

                history.FlushAsync().GetAwaiter().GetResult();
                window.Close();
                previewApplication.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                viewModel?.Dispose();
                history?.Dispose();
                scope?.Dispose();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("Le rendu de l’interface n’a pas terminé à temps.");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(
                $"Le rendu WPF a échoué : {failure.Message}",
                failure);
        }

        return Task.CompletedTask;
    }

    private static Task TestDeduplicationAsync()
    {
        using var scope = new TemporaryScope();
        using var history = scope.CreateHistoryService();

        var firstTime = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
        var laterTime = firstTime.AddMinutes(5);
        var first = history.AddOrPromote("Bonjour 👋", firstTime);
        history.AddOrPromote("Deuxième texte", firstTime.AddMinutes(1));
        var promoted = history.AddOrPromote("Bonjour 👋", laterTime);

        Assert.NotNull(first);
        Assert.Same(first, promoted);
        Assert.Equal(2, history.Entries.Count);
        Assert.Equal("Bonjour 👋", history.Entries[0].Text);
        Assert.Equal(laterTime, history.Entries[0].CapturedAtUtc);
        Assert.Null(history.AddOrPromote("   \r\n"));
        return Task.CompletedTask;
    }

    private static Task TestRetentionLimitAsync()
    {
        using var scope = new TemporaryScope();
        using var history = scope.CreateHistoryService(maxEntries: 25);
        var start = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 30; index++)
        {
            history.AddOrPromote($"Élément {index}", start.AddSeconds(index));
        }

        Assert.Equal(25, history.Entries.Count);
        Assert.Equal("Élément 29", history.Entries[0].Text);
        Assert.Equal("Élément 5", history.Entries[^1].Text);
        return Task.CompletedTask;
    }

    private static Task TestStorageBudgetAsync()
    {
        using var scope = new TemporaryScope();
        using var history = new HistoryService(
            new HistoryStore(scope.Paths),
            maxEntries: 250,
            maximumBytes: 1_600);
        var start = DateTimeOffset.UtcNow;

        history.AddOrPromote(new string('a', 700), start);
        history.AddOrPromote(new string('b', 700), start.AddSeconds(1));
        history.AddOrPromote(new string('c', 700), start.AddSeconds(2));

        Assert.Equal(1, history.Entries.Count);
        Assert.Equal(new string('c', 700), history.Entries[0].Text);
        return Task.CompletedTask;
    }

    private static async Task TestPersistenceRoundTripAsync()
    {
        using var scope = new TemporaryScope();
        var store = new HistoryStore(scope.Paths);
        var timestamp = new DateTimeOffset(2026, 8, 17, 12, 30, 0, TimeSpan.Zero);
        var entries = new[]
        {
            CreateEntry("Accents : été, où, ça — emoji 🧪", timestamp),
            CreateEntry("Plusieurs\nlignes\nconservées", timestamp.AddMinutes(-1))
        };

        await store.SaveAsync(entries);
        var reloaded = await store.LoadAsync();

        Assert.Equal(2, reloaded.Count);
        Assert.Equal(entries[0].Text, reloaded[0].Text);
        Assert.Equal(entries[1].Hash, reloaded[1].Hash);
        Assert.True(File.Exists(scope.Paths.HistoryBackupFile));
    }

    private static async Task TestBackupRecoveryAsync()
    {
        using var scope = new TemporaryScope();
        var store = new HistoryStore(scope.Paths);
        var timestamp = DateTimeOffset.UtcNow;
        var original = new[] { CreateEntry("Version sauvegardée", timestamp) };
        var newer = new[] { CreateEntry("Version plus récente", timestamp.AddMinutes(1)) };

        await store.SaveAsync(original);
        await store.SaveAsync(newer);
        await File.WriteAllTextAsync(scope.Paths.HistoryFile, "{ json invalide", Encoding.UTF8);

        var recovered = await store.LoadAsync();

        Assert.Equal(1, recovered.Count);
        Assert.Equal("Version plus récente", recovered[0].Text);
        Assert.True(Directory.EnumerateFiles(scope.Paths.DataDirectory, "history.corrupt-*.json").Any());
    }

    private static async Task TestCorruptedSettingsAsync()
    {
        using var scope = new TemporaryScope();
        Directory.CreateDirectory(scope.Paths.DataDirectory);
        await File.WriteAllTextAsync(scope.Paths.SettingsFile, "pas du json", Encoding.UTF8);

        var settings = await new SettingsStore(scope.Paths).LoadAsync();

        Assert.True(settings.CaptureEnabled);
        Assert.Equal(250, settings.MaxEntries);
    }

    private static async Task TestPhysicalPurgeAsync()
    {
        using var scope = new TemporaryScope();
        var store = new HistoryStore(scope.Paths);
        await store.SaveAsync(new[]
        {
            CreateEntry("Secret à supprimer", DateTimeOffset.UtcNow)
        });
        var corruptCopy = Path.Combine(
            scope.Paths.DataDirectory,
            "history.corrupt-20260817-120000.json");
        await File.WriteAllTextAsync(corruptCopy, "ancien contenu sensible", Encoding.UTF8);

        await store.SaveAsync(Array.Empty<ClipboardEntry>());

        Assert.False(File.Exists(scope.Paths.HistoryFile));
        Assert.False(File.Exists(scope.Paths.HistoryBackupFile));
        Assert.False(File.Exists(scope.Paths.HistoryFile + ".tmp"));
        Assert.False(Directory.EnumerateFiles(
            scope.Paths.DataDirectory,
            "history.corrupt-*.json").Any());
    }

    private static Task TestPrivacyMarkersAsync()
    {
        var directExclusion = new System.Windows.DataObject();
        directExclusion.SetData("ExcludeClipboardContentFromMonitorProcessing", true);
        Assert.True(ClipboardService.ShouldExcludeFromHistory(directExclusion));

        var legacyExclusion = new System.Windows.DataObject();
        legacyExclusion.SetData("Clipboard Viewer Ignore", true);
        Assert.True(ClipboardService.ShouldExcludeFromHistory(legacyExclusion));

        var historyForbidden = new System.Windows.DataObject();
        historyForbidden.SetData("CanIncludeInClipboardHistory", BitConverter.GetBytes(0u));
        Assert.True(ClipboardService.ShouldExcludeFromHistory(historyForbidden));

        var historyAllowed = new System.Windows.DataObject();
        historyAllowed.SetData("CanIncludeInClipboardHistory", BitConverter.GetBytes(1u));
        historyAllowed.SetData(System.Windows.DataFormats.UnicodeText, "Texte autorisé");
        Assert.False(ClipboardService.ShouldExcludeFromHistory(historyAllowed));

        var unreadablePolicy = new System.Windows.DataObject();
        unreadablePolicy.SetData("CanUploadToCloudClipboard", "valeur inattendue");
        Assert.True(ClipboardService.ShouldExcludeFromHistory(unreadablePolicy));
        return Task.CompletedTask;
    }

    private static Task TestStartupCommandAsync()
    {
        var command = new StartupManager().BuildStartupCommand();
        Assert.True(command.StartsWith('"'));
        Assert.True(command.EndsWith("\" --background", StringComparison.Ordinal));
        Assert.True(command.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    private static ClipboardEntry CreateEntry(string text, DateTimeOffset capturedAtUtc)
    {
        return new ClipboardEntry
        {
            Id = Guid.NewGuid(),
            Text = text,
            Hash = ClipboardTextHasher.Compute(text),
            CapturedAtUtc = capturedAtUtc
        };
    }

    private sealed class TemporaryScope : IDisposable
    {
        public TemporaryScope()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "PressHistory.Tests",
                Guid.NewGuid().ToString("N"));
            Paths = new AppPaths(root);
        }

        public AppPaths Paths { get; }

        public HistoryService CreateHistoryService(int maxEntries = 250)
        {
            return new HistoryService(new HistoryStore(Paths), maxEntries);
        }

        public void Dispose()
        {
            if (Directory.Exists(Paths.DataDirectory))
            {
                Directory.Delete(Paths.DataDirectory, recursive: true);
            }
        }
    }

    private static class Assert
    {
        public static void True(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "La condition devait être vraie.");
            }
        }

        public static void False(bool condition, string? message = null) =>
            True(!condition, message ?? "La condition devait être fausse.");

        public static void Null(object? value)
        {
            if (value is not null)
            {
                throw new InvalidOperationException("La valeur devait être nulle.");
            }
        }

        public static void NotNull(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("La valeur ne devait pas être nulle.");
            }
        }

        public static void Same(object? expected, object? actual)
        {
            if (!ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException("Les références devaient être identiques.");
            }
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Attendu : {expected}; obtenu : {actual}.");
            }
        }
    }
}
