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
        "Converting" => new SolidColorBrush(Color.Parse("#C8821A")),
        "Done"       => new SolidColorBrush(Color.Parse("#22A85A")),
        "Failed"     => new SolidColorBrush(Color.Parse("#D63030")),
        _            => new SolidColorBrush(Color.Parse("#888888")),
    };

    public FontWeight StatusWeight => _status is "Converting" or "Done" or "Failed"
        ? FontWeight.SemiBold
        : FontWeight.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;
}
