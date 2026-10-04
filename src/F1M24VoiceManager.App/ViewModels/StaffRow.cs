using F1M24VoiceManager.Core;

namespace F1M24VoiceManager.App.ViewModels;

public sealed record DonorOption(int? StaffId, string Label)
{
    public static readonly DonorOption Original = new(null, "— Voix d'origine —");

    public override string ToString() => Label;
}

public sealed class StaffRow : ObservableObject
{
    private DonorOption _selectedDonor;
    private DonorOption _appliedDonor;

    public StaffRow(StaffMember member, bool isPlayerTeam, string position, IReadOnlyList<DonorOption> donorOptions, int? appliedDonorId)
    {
        Member = member;
        IsPlayerTeam = isPlayerTeam;
        Position = position;
        DonorOptions = donorOptions;
        _appliedDonor = Find(appliedDonorId);
        _selectedDonor = _appliedDonor;
    }

    public StaffMember Member { get; }
    public bool IsPlayerTeam { get; }
    public string Position { get; }
    public IReadOnlyList<DonorOption> DonorOptions { get; }

    public string Name => Member.Name;
    public int StaffId => Member.StaffId;
    public string Team => string.IsNullOrEmpty(Member.TeamName) ? "—" : Member.TeamName;
    public string Role => Member.Role == VoiceRole.Driver ? "Pilote" : "Ingénieur";
    public string RecordedVoice => Member.HasRecordedVoice ? "Oui" : "Non";

    public DonorOption SelectedDonor
    {
        get => _selectedDonor;
        set
        {
            if (Set(ref _selectedDonor, value ?? DonorOption.Original))
            {
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    public bool IsChanged => _selectedDonor != _appliedDonor;

    public void MarkApplied()
    {
        _appliedDonor = _selectedDonor;
        OnPropertyChanged(nameof(IsChanged));
    }

    public void ResetTo(int? donorId)
    {
        _appliedDonor = Find(donorId);
        SelectedDonor = _appliedDonor;
        OnPropertyChanged(nameof(IsChanged));
    }

    private DonorOption Find(int? id) =>
        id is null ? DonorOption.Original : DonorOptions.FirstOrDefault(o => o.StaffId == id) ?? DonorOption.Original;
}
