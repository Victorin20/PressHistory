using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PressHistory.Models;

public sealed class ClipboardEntry : INotifyPropertyChanged
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private DateTimeOffset _capturedAtUtc;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public DateTimeOffset CapturedAtUtc
    {
        get => _capturedAtUtc;
        set
        {
            if (_capturedAtUtc == value)
            {
                return;
            }

            _capturedAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CapturedAtLabel));
        }
    }

    [JsonIgnore]
    public string Preview => Text.Trim();

    [JsonIgnore]
    public string CapturedAtLabel
    {
        get
        {
            var local = CapturedAtUtc.ToLocalTime();
            var now = DateTimeOffset.Now;

            if (local.Date == now.Date)
            {
                return $"Aujourd’hui · {local:HH:mm}";
            }

            if (local.Date == now.Date.AddDays(-1))
            {
                return $"Hier · {local:HH:mm}";
            }

            return local.Year == now.Year
                ? local.ToString("dd MMM · HH:mm", FrenchCulture)
                : local.ToString("dd MMM yyyy · HH:mm", FrenchCulture);
        }
    }

    [JsonIgnore]
    public string CharacterCountLabel => Text.Length switch
    {
        0 => "Vide",
        1 => "1 caractère",
        _ => $"{Text.Length:N0} caractères"
    };

    public void Touch(DateTimeOffset capturedAtUtc)
    {
        CapturedAtUtc = capturedAtUtc;
    }

    public void RefreshTimeLabel() => OnPropertyChanged(nameof(CapturedAtLabel));

    public ClipboardEntry Snapshot()
    {
        return new ClipboardEntry
        {
            Id = Id,
            Text = Text,
            Hash = Hash,
            CapturedAtUtc = CapturedAtUtc
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
