using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DemoApp.Models;

namespace DemoApp;

public partial class MainWindow : Window
{
    private int _count;
    private int _nextPersonId;

    public ObservableCollection<Person> People { get; } = new();

    public MainWindow()
    {
        SeedPeople();
        DataContext = this;
        InitializeComponent();
    }

    // ---------- Greeter ------------------------------------------------------

    private void GreetButton_Click(object sender, RoutedEventArgs e)
    {
        var first = FirstNameTextBox.Text?.Trim() ?? string.Empty;
        var last = LastNameTextBox.Text?.Trim() ?? string.Empty;
        var fullName = string.Join(" ", new[] { first, last }).Trim();

        if (string.IsNullOrEmpty(fullName))
        {
            GreetingLabel.Text = "Please enter a name first.";
            StatusLabel.Text = "Missing name";
            return;
        }

        var hello = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
        {
            "Spanish" => "Hola",
            "French"  => "Bonjour",
            _          => "Hello",
        };

        var message = $"{hello}, {fullName}!";
        if (UppercaseCheckBox.IsChecked == true)
        {
            message = message.ToUpperInvariant();
        }

        GreetingLabel.Text = message;
        StatusLabel.Text = "Greeted";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        FirstNameTextBox.Clear();
        LastNameTextBox.Clear();
        GreetingLabel.Text = "(no greeting yet)";
        StatusLabel.Text = "Cleared";
    }

    // ---------- Counter ------------------------------------------------------

    private void IncrementButton_Click(object sender, RoutedEventArgs e)
    {
        _count++;
        UpdateCount();
    }

    private void DecrementButton_Click(object sender, RoutedEventArgs e)
    {
        _count--;
        UpdateCount();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _count = 0;
        UpdateCount();
        StatusLabel.Text = "Counter reset";
    }

    private void UpdateCount()
    {
        CountLabel.Text = _count.ToString();
        StatusLabel.Text = $"Count is {_count}";
    }

    // ---------- People grid --------------------------------------------------

    private void SeedPeople()
    {
        AddPerson("Ada Lovelace",   "Engineer",  isActive: true);
        AddPerson("Alan Turing",    "Scientist", isActive: true);
        AddPerson("Grace Hopper",   "Engineer",  isActive: false);
        AddPerson("Marie Curie",    "Scientist", isActive: true);
    }

    private void AddPerson(string name, string role, bool isActive)
    {
        People.Add(new Person
        {
            Id       = ++_nextPersonId,
            Name     = name,
            Role     = role,
            IsActive = isActive,
        });
    }

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        AddPerson($"New Person {_nextPersonId + 1}", "Unassigned", isActive: false);
        StatusLabel.Text = $"Added row (now {People.Count})";
    }

    private void DeleteRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Person person })
        {
            People.Remove(person);
            StatusLabel.Text = $"Deleted '{person.Name}' (now {People.Count})";
        }
    }

    private void ToggleAllActiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (People.Count == 0)
        {
            StatusLabel.Text = "Grid is empty";
            return;
        }

        // If any are inactive, flip everything to active; otherwise flip everything off.
        var makeActive = People.Any(p => !p.IsActive);
        foreach (var p in People)
        {
            p.IsActive = makeActive;
        }
        StatusLabel.Text = makeActive ? "All marked active" : "All marked inactive";
    }

    private void ClearGridButton_Click(object sender, RoutedEventArgs e)
    {
        var removed = People.Count;
        People.Clear();
        StatusLabel.Text = removed == 0 ? "Grid was already empty" : $"Cleared {removed} rows";
    }
}
