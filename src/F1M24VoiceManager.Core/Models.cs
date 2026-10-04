namespace F1M24VoiceManager.Core;

public enum VoiceRole
{
    Driver,
    Engineer,
}

public sealed record StaffMember(
    int StaffId,
    string FirstName,
    string LastName,
    VoiceRole Role,
    bool IsGenerated,
    int? TeamId,
    string TeamName,
    int? Formula,
    int? PosInTeam)
{
    public string Name => $"{FirstName} {LastName}".Trim();

    public bool HasRecordedVoice => VoiceCatalog.HasRecordedVoice(Role, StaffId);

    /// <summary>Race seat (1-2), reserve (3) or academy (4+) for drivers; engineer slot otherwise.</summary>
    public bool IsRaceSeat => PosInTeam is 1 or 2;
}

public sealed record CareerSave(string Path, DateTime LastWriteTime)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record CareerData(
    int? PlayerTeamId,
    IReadOnlyList<StaffMember> Staff,
    IReadOnlyDictionary<int, int> EngineerOfDriver);

public sealed record VoiceMapping(VoiceRole Role, int SourceStaffId, int DonorStaffId);
