using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using F1M24VoiceManager.Core;

namespace F1M24VoiceManager.App.ViewModels;

public enum StaffFilter
{
    MyTeam,
    F1RaceSeats,
    AllF1,
    Everyone,
}

public sealed record FilterOption(StaffFilter Value, string Label);

public sealed class MainViewModel : ObservableObject
{
    private static readonly string PayloadDirectory = Path.Combine(AppContext.BaseDirectory, "payload", ModInstaller.ModFolderName);

    private readonly BackupManager _backups = new();
    private GameInstall? _game;
    private ModInstaller? _installer;
    private CareerData? _career;
    private CareerSave? _selectedSave;
    private Backup? _selectedBackup;
    private FilterOption _selectedFilter;
    private string _searchText = "";
    private string _status = "";
    private bool _statusIsError;
    private bool _isBusy;

    /// <summary>Mappings for staff not present in the loaded save are preserved on Apply.</summary>
    private Dictionary<int, VoiceMapping> _otherMappings = new();

    public MainViewModel()
    {
        Filters =
        [
            new(StaffFilter.MyTeam, "Mon équipe"),
            new(StaffFilter.F1RaceSeats, "Titulaires F1 et ingénieurs"),
            new(StaffFilter.AllF1, "Tout le staff F1"),
            new(StaffFilter.Everyone, "Tout le monde"),
        ];
        _selectedFilter = Filters[0];

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => o is StaffRow row && Matches(row);

        RefreshCommand = new RelayCommand(Refresh);
        LoadSaveCommand = new RelayCommand(LoadSelectedSave, () => SelectedSave is not null && !IsBusy);
        ApplyCommand = new RelayCommand(Apply, () => _career is not null && _installer is not null && !IsBusy);
        ResetAllCommand = new RelayCommand(ResetAll, () => Rows.Any(r => r.SelectedDonor.StaffId is not null));
        RevertCommand = new RelayCommand(Revert, () => Rows.Any(r => r.IsChanged));
        RestoreCommand = new RelayCommand(RestoreBackup, () => SelectedBackup is not null && _installer?.MappingsPath is not null);
        OpenModFolderCommand = new RelayCommand(OpenModFolder, () => _installer?.ModDirectory is { } d && Directory.Exists(d));

        Refresh();
    }

    public IReadOnlyList<FilterOption> Filters { get; }
    public ObservableCollection<StaffRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }
    public ObservableCollection<CareerSave> Saves { get; } = new();
    public ObservableCollection<Backup> Backups { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand LoadSaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ResetAllCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand OpenModFolderCommand { get; }

    // ---- Environment status -------------------------------------------------------------

    public string GameStatus => _game is null ? "Introuvable" : "Détecté";
    public bool GameOk => _game is not null;
    public string GamePath => _game?.Win64Directory ?? "Choisissez le dossier du jeu.";

    public string Ue4ssStatus => _game?.Ue4ss switch
    {
        Ue4ssLayout.Subfolder => "Installé (ue4ss\\)",
        Ue4ssLayout.Root => "Installé (ancienne disposition)",
        _ => "Absent",
    };
    public bool Ue4ssOk => _game is not null && _game.Ue4ss != Ue4ssLayout.Missing;

    public string ModStatus => _installer?.State switch
    {
        ModState.UpToDate => "Installé, à jour",
        ModState.Outdated => "Mise à jour disponible",
        ModState.NotInstalled => "Pas encore installé",
        _ => "—",
    };
    public bool ModOk => _installer?.State == ModState.UpToDate;

    public string GameRunningStatus => GameInstall.IsGameRunning()
        ? "Ouvert — les changements s'appliquent au prochain message radio"
        : "Fermé";

    // ---- Selection ---------------------------------------------------------------------------

    public CareerSave? SelectedSave
    {
        get => _selectedSave;
        set => Set(ref _selectedSave, value);
    }

    public Backup? SelectedBackup
    {
        get => _selectedBackup;
        set => Set(ref _selectedBackup, value);
    }

    public FilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (Set(ref _selectedFilter, value))
            {
                RowsView.Refresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                RowsView.Refresh();
            }
        }
    }

    public string CareerSummary => _career is null
        ? "Aucune carrière chargée."
        : $"{SelectedSave?.FileName} — {_career.Staff.Count(s => s.Role == VoiceRole.Driver)} pilotes, " +
          $"{_career.Staff.Count(s => s.Role == VoiceRole.Engineer)} ingénieurs.";

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => Set(ref _statusIsError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    // ---- Actions ---------------------------------------------------------------------------

    public void SetGameDirectory(string directory)
    {
        GameInstall? game = GameInstall.FromDirectory(directory);
        if (game is null)
        {
            Report("Ce dossier ne contient pas F1 Manager 2024 (F1Manager24\\Binaries\\Win64\\F1Manager24.exe).", error: true);
            return;
        }
        UseGame(game);
        Report("Jeu trouvé : " + game.Win64Directory);
    }

    private void Refresh()
    {
        UseGame(_game ?? GameInstall.Locate());

        string? previous = SelectedSave?.Path;
        Saves.Clear();
        foreach (CareerSave save in SaveReader.FindSaves())
        {
            Saves.Add(save);
        }
        SelectedSave = Saves.FirstOrDefault(s => s.Path == previous) ?? Saves.FirstOrDefault();

        RefreshBackups();

        if (_game is null)
        {
            Report("F1 Manager 2024 n'a pas été trouvé automatiquement. Cliquez sur « Choisir… » pour indiquer son dossier.", error: true);
        }
        else if (_game.Ue4ss == Ue4ssLayout.Missing)
        {
            Report("Le jeu a été trouvé, mais UE4SS n'est pas installé. Il est nécessaire pour charger le mod de voix.", error: true);
        }
        else if (Saves.Count == 0)
        {
            Report("Aucune sauvegarde de carrière trouvée. Lancez une carrière dans le jeu puis cliquez sur Actualiser.", error: true);
        }
        else if (_career is null)
        {
            Report("Choisissez une sauvegarde puis cliquez sur « Charger ».");
        }
    }

    private void UseGame(GameInstall? game)
    {
        _game = game;
        _installer = game is null ? null : new ModInstaller(game, PayloadDirectory);
        OnPropertyChanged(nameof(GameStatus));
        OnPropertyChanged(nameof(GameOk));
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(Ue4ssStatus));
        OnPropertyChanged(nameof(Ue4ssOk));
        OnPropertyChanged(nameof(ModStatus));
        OnPropertyChanged(nameof(ModOk));
        OnPropertyChanged(nameof(GameRunningStatus));
    }

    private async void LoadSelectedSave()
    {
        if (SelectedSave is not { } save)
        {
            return;
        }
        IsBusy = true;
        Report("Lecture de la sauvegarde…");
        try
        {
            _career = await Task.Run(() => SaveReader.Read(save.Path));
            BuildRows();
            OnPropertyChanged(nameof(CareerSummary));
            Report("Carrière chargée. Choisissez une voix dans la colonne « Voix utilisée », puis cliquez sur Appliquer.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DllNotFoundException)
        {
            Report("Impossible de lire cette sauvegarde : " + ex.Message, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildRows()
    {
        if (_career is null)
        {
            return;
        }
        var current = _installer?.MappingsPath is { } path
            ? new Dictionary<int, VoiceMapping>(MappingFile.Load(path))
            : new Dictionary<int, VoiceMapping>();

        var savedNames = _career.Staff.GroupBy(s => s.StaffId).ToDictionary(g => g.Key, g => g.First());
        var driverOfEngineer = _career.EngineerOfDriver.GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

        IReadOnlyList<DonorOption> OptionsFor(VoiceRole role) =>
            new[] { DonorOption.Original }
                .Concat(VoiceCatalog.For(role)
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(d => new DonorOption(d.StaffId,
                        savedNames.TryGetValue(d.StaffId, out var s) && !string.IsNullOrEmpty(s.TeamName)
                            ? $"{d.Name} ({s.TeamName})"
                            : d.Name)))
                .ToList();

        var driverOptions = OptionsFor(VoiceRole.Driver);
        var engineerOptions = OptionsFor(VoiceRole.Engineer);

        Rows.Clear();
        var ordered = _career.Staff
            .OrderByDescending(s => s.TeamId == _career.PlayerTeamId && s.TeamId is not null)
            .ThenBy(s => s.Formula ?? 99)
            .ThenBy(s => s.TeamId ?? int.MaxValue)
            .ThenBy(s => s.Role)
            .ThenBy(s => s.PosInTeam ?? 99)
            .ThenBy(s => s.LastName, StringComparer.CurrentCultureIgnoreCase);

        foreach (StaffMember member in ordered)
        {
            int? applied = current.TryGetValue(member.StaffId, out var m) && m.Role == member.Role ? m.DonorStaffId : null;
            current.Remove(member.StaffId);
            Rows.Add(new StaffRow(
                member,
                isPlayerTeam: member.TeamId is not null && member.TeamId == _career.PlayerTeamId,
                position: PositionLabel(member, driverOfEngineer, savedNames),
                donorOptions: member.Role == VoiceRole.Driver ? driverOptions : engineerOptions,
                appliedDonorId: applied));
        }
        _otherMappings = current;
        RowsView.Refresh();
    }

    private static string PositionLabel(StaffMember s, Dictionary<int, List<int>> driverOfEngineer, Dictionary<int, StaffMember> byId)
    {
        if (s.TeamId is null)
        {
            return "Sans contrat";
        }
        if (s.Role == VoiceRole.Engineer)
        {
            if (driverOfEngineer.TryGetValue(s.StaffId, out var drivers))
            {
                var names = drivers.Select(d => byId.TryGetValue(d, out var p) ? p.LastName : d.ToString());
                return "Ingénieur de " + string.Join(", ", names);
            }
            return "Ingénieur de course";
        }
        return s.PosInTeam switch
        {
            1 or 2 => $"Titulaire {s.PosInTeam}",
            3 => "Réserviste",
            _ => "Académie",
        } + (s.Formula is > 1 ? $" (F{s.Formula})" : "");
    }

    private bool Matches(StaffRow row)
    {
        bool filter = SelectedFilter.Value switch
        {
            StaffFilter.MyTeam => row.IsPlayerTeam,
            StaffFilter.F1RaceSeats => row.Member.Formula == 1 && (row.Member.Role == VoiceRole.Engineer || row.Member.IsRaceSeat),
            StaffFilter.AllF1 => row.Member.Formula == 1,
            _ => true,
        };
        if (!filter)
        {
            return false;
        }
        string q = SearchText.Trim();
        return q.Length == 0
               || row.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || row.Team.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || row.StaffId.ToString() == q;
    }

    private void Apply()
    {
        if (_installer?.MappingsPath is not { } mappingsPath || _career is null)
        {
            return;
        }
        try
        {
            ModState before = _installer.State;
            if (before is ModState.NotInstalled or ModState.Outdated)
            {
                _installer.Install();
            }

            Backup backup = _backups.Create(mappingsPath);

            var mappings = _otherMappings.Values.ToList();
            mappings.AddRange(Rows
                .Where(r => r.SelectedDonor.StaffId is not null)
                .Select(r => new VoiceMapping(r.Member.Role, r.StaffId, r.SelectedDonor.StaffId!.Value)));

            var names = _career.Staff.GroupBy(s => s.StaffId).ToDictionary(g => g.Key, g => g.First().Name);
            foreach (var d in VoiceCatalog.Drivers.Concat(VoiceCatalog.Engineers))
            {
                names.TryAdd(d.StaffId, d.Name);
            }
            MappingFile.Save(mappingsPath, mappings, id => names.GetValueOrDefault(id));

            foreach (StaffRow row in Rows)
            {
                row.MarkApplied();
            }
            RefreshBackups();
            UseGame(_game);

            int count = Rows.Count(r => r.SelectedDonor.StaffId is not null);
            string installed = before is ModState.NotInstalled ? "Mod installé. " : before is ModState.Outdated ? "Mod mis à jour. " : "";
            string when = GameInstall.IsGameRunning()
                ? "Le jeu est ouvert : les nouvelles voix s'entendront au prochain message radio."
                : "Lancez le jeu : les nouvelles voix seront actives en course.";
            Report($"{installed}{count} réattribution(s) enregistrée(s). {when} (Sauvegarde : {backup.Label})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Report(ex.Message, error: true);
        }
    }

    private void ResetAll()
    {
        foreach (StaffRow row in Rows)
        {
            row.SelectedDonor = DonorOption.Original;
        }
        Report("Toutes les voix d'origine sont sélectionnées. Cliquez sur Appliquer pour confirmer.");
    }

    private void Revert()
    {
        var current = _installer?.MappingsPath is { } path ? MappingFile.Load(path) : new Dictionary<int, VoiceMapping>();
        foreach (StaffRow row in Rows)
        {
            row.ResetTo(current.TryGetValue(row.StaffId, out var m) && m.Role == row.Member.Role ? m.DonorStaffId : null);
        }
        Report("Modifications non appliquées annulées.");
    }

    private void RestoreBackup()
    {
        if (SelectedBackup is not { } backup || _installer?.MappingsPath is not { } mappingsPath)
        {
            return;
        }
        try
        {
            Backup safety = _backups.Create(mappingsPath);
            _backups.Restore(backup, mappingsPath);
            BuildRows();
            RefreshBackups();
            Report($"Configuration du {backup.Label} restaurée. (L'état précédent a été sauvegardé : {safety.Label})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("La restauration a échoué : " + ex.Message, error: true);
        }
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (Backup b in _backups.List())
        {
            Backups.Add(b);
        }
        SelectedBackup = Backups.FirstOrDefault();
    }

    private void OpenModFolder()
    {
        if (_installer?.ModDirectory is { } dir && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
    }

    private void Report(string message, bool error = false)
    {
        Status = message;
        StatusIsError = error;
    }
}
