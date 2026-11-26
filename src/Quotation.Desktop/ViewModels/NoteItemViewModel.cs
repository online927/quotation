using CommunityToolkit.Mvvm.ComponentModel;
using Quotation.Contracts;

namespace Quotation.Desktop.ViewModels;

/// <summary>Round 39 (Z8): one internal note in the 📝 Notes card (never printed).</summary>
public sealed partial class NoteItemViewModel(QuotationNoteDto note) : ObservableObject
{
    public QuotationNoteDto Note { get; private set; } = note;
    public int Id => Note.Id;
    public string Text => Note.Text;
    public bool Mine => Note.Mine;
    public bool CanDelete => Note.CanDelete;
    public string WhoWhen => $"{Note.DisplayName} · {Note.CreatedUtc.ToLocalTime():dd-MMM HH:mm}{(Note.IsEdited ? " (edited)" : "")}";

    /// <summary>The note is being changed in place (its text in a box with Save / Cancel).</summary>
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";

    public void Replace(QuotationNoteDto note)
    {
        Note = note;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(WhoWhen));
    }
}
