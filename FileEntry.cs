using System.ComponentModel;
using Avalonia.Media;

namespace CbrToCbz;

public class FileEntry : INotifyPropertyChanged
{
    private string _status = "Queued";

    public string Filename  { get; init; } = "";
    public string FullPath  { get; init; } = "";

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusWeight)));
        }
    }

    public IBrush StatusColor => _status switch
    {
        "Converting" => new SolidColorBrush(Color.Parse("#2563EB")),
        "Done"       => new SolidColorBrush(Color.Parse("#16A34A")),
        "Failed"     => new SolidColorBrush(Color.Parse("#DC2626")),
        _            => new SolidColorBrush(Color.Parse("#9CA3AF")),
    };

    public FontWeight StatusWeight => _status is "Converting" or "Done" or "Failed"
        ? FontWeight.SemiBold
        : FontWeight.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;
}
