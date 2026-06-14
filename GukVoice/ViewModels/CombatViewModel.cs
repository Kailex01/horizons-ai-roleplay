using GukVoice.Models;

namespace GukVoice.ViewModels;

public class CombatViewModel : INotifyPropertyChanged
{
    // ── Active fight state ─────────────────────────────────────────────────────

    private bool     _inCombat;
    private string   _currentTarget = "";
    private DateTime _fightStart;
    private DateTime _lastHit;
    private int      _meleeDmgDealt, _spellDmgDealt;
    private int      _meleeDmgTaken, _spellDmgTaken;

    private const double CombatTimeoutSeconds = 15;

    private readonly System.Timers.Timer _tickTimer;

    // ── Bound properties ───────────────────────────────────────────────────────

    public ObservableCollection<FightRecord> FightHistory { get; } = new();

    private bool _inCombatProp;
    public bool InCombat { get => _inCombatProp; private set { _inCombatProp = value; OnPropertyChanged(); } }

    private string _statusText = "No active fight";
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }

    private string _targetText = "—";
    public string TargetText  { get => _targetText; private set { _targetText = value; OnPropertyChanged(); } }

    private string _durationText = "—";
    public string DurationText { get => _durationText; private set { _durationText = value; OnPropertyChanged(); } }

    private string _dmgDealtText = "—";
    public string DmgDealtText  { get => _dmgDealtText; private set { _dmgDealtText = value; OnPropertyChanged(); } }

    private string _dmgTakenText = "—";
    public string DmgTakenText  { get => _dmgTakenText; private set { _dmgTakenText = value; OnPropertyChanged(); } }

    private string _dpsOutText = "—";
    public string DpsOutText { get => _dpsOutText; private set { _dpsOutText = value; OnPropertyChanged(); } }

    private string _dpsInText = "—";
    public string DpsInText  { get => _dpsInText;  private set { _dpsInText = value; OnPropertyChanged(); } }

    // ── Constructor ────────────────────────────────────────────────────────────

    public ICommand ClearCommand { get; }

    public CombatViewModel()
    {
        ClearCommand = new RelayCommand(_ => Clear());
        _tickTimer   = new System.Timers.Timer(500) { AutoReset = true };
        _tickTimer.Elapsed += (_, _) => OnTick();
        _tickTimer.Start();
    }

    public void Clear()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _inCombat  = false;
            InCombat   = false;
            StatusText = "No active fight";
            TargetText = "—";
            ClearLiveStats();
            FightHistory.Clear();
        });
    }

    // ── Public input ───────────────────────────────────────────────────────────

    public void ProcessEvent(CombatEvent ev)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (ev.Type)
            {
                case CombatEventType.DamageDealt:
                    EnsureFightStarted(ev.Target, ev.Time);
                    if (ev.Source == DamageSource.Melee) _meleeDmgDealt += ev.Damage;
                    else                                  _spellDmgDealt += ev.Damage;
                    _lastHit = ev.Time;
                    RefreshDisplay();
                    break;

                case CombatEventType.DamageTaken:
                    EnsureFightStarted(ev.Actor, ev.Time);
                    if (ev.Source == DamageSource.Melee) _meleeDmgTaken += ev.Damage;
                    else                                  _spellDmgTaken += ev.Damage;
                    _lastHit = ev.Time;
                    RefreshDisplay();
                    break;

                case CombatEventType.MobDeath:
                    if (_inCombat) EndFight(won: true, ev.Time);
                    break;

                case CombatEventType.PlayerDeath:
                    if (_inCombat) EndFight(won: false, ev.Time);
                    break;
            }
        });
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void EnsureFightStarted(string target, DateTime time)
    {
        if (_inCombat) return;
        _inCombat      = true;
        _currentTarget = target;
        _fightStart    = time;
        _lastHit       = time;
        _meleeDmgDealt = _spellDmgDealt = 0;
        _meleeDmgTaken = _spellDmgTaken = 0;
        InCombat       = true;
        TargetText     = target;
    }

    private void EndFight(bool won, DateTime endTime)
    {
        var record = new FightRecord
        {
            Target        = _currentTarget,
            StartTime     = _fightStart,
            EndTime       = endTime,
            MeleeDmgDealt = _meleeDmgDealt,
            SpellDmgDealt = _spellDmgDealt,
            MeleeDmgTaken = _meleeDmgTaken,
            SpellDmgTaken = _spellDmgTaken,
            Won           = won,
        };

        FightHistory.Insert(0, record);
        if (FightHistory.Count > 100) FightHistory.RemoveAt(FightHistory.Count - 1);

        _inCombat  = false;
        InCombat   = false;
        StatusText = $"Last: {record.Summary}";
        TargetText = "—";
        ClearLiveStats();
    }

    private void OnTick()
    {
        if (!_inCombat) return;

        // Auto-end if no hits for CombatTimeoutSeconds
        if ((DateTime.Now - _lastHit).TotalSeconds > CombatTimeoutSeconds)
        {
            Application.Current.Dispatcher.Invoke(() => EndFight(won: false, _lastHit));
            return;
        }

        Application.Current.Dispatcher.Invoke(RefreshDisplay);
    }

    private void RefreshDisplay()
    {
        if (!_inCombat) return;

        var totalDealt = _meleeDmgDealt + _spellDmgDealt;
        var totalTaken = _meleeDmgTaken + _spellDmgTaken;
        var elapsed    = Math.Max(1, (DateTime.Now - _fightStart).TotalSeconds);

        StatusText    = "In Combat";
        DurationText  = $"{(int)elapsed}s";
        DmgDealtText  = $"{totalDealt:N0}  (melee {_meleeDmgDealt:N0} / spell {_spellDmgDealt:N0})";
        DmgTakenText  = $"{totalTaken:N0}  (melee {_meleeDmgTaken:N0} / spell {_spellDmgTaken:N0})";
        DpsOutText    = $"{(int)(totalDealt / elapsed)}";
        DpsInText     = $"{(int)(totalTaken / elapsed)}";
    }

    private void ClearLiveStats()
    {
        DurationText = DmgDealtText = DmgTakenText = DpsOutText = DpsInText = "—";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
