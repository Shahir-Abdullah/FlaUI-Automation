using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DemoApp.Models;

/// <summary>
/// A row in the People DataGrid. Implements <see cref="INotifyPropertyChanged"/>
/// so two-way grid bindings also propagate back into the source object if it
/// is mutated from code (e.g. the "Toggle all" button).
/// </summary>
public sealed class Person : INotifyPropertyChanged
{
    private int _id;
    private string _name = string.Empty;
    private string _role = string.Empty;
    private bool _isActive;

    public int Id
    {
        get => _id;
        set => Set(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    public string Role
    {
        get => _role;
        set => Set(ref _role, value ?? string.Empty);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
