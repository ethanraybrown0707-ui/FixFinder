using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using FixFinder.Core.Checking;

namespace FixFinder.Gui;

/// <summary>Where one program of a folder has got to.</summary>
public enum ProgramState
{
    Waiting,
    Checking,
    Clean,
    HasProblems,
    CouldNotCheck,
}

/// <summary>One program of a scanned folder, as the list beside the report shows it.</summary>
public sealed class ProgramRow(ScannedProgram program) : INotifyPropertyChanged
{
    private ProgramState _state = ProgramState.Waiting;
    private int _problems;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScannedProgram Program { get; } = program;

    public string Entry => Program.Entry;

    public string Name => Program.IsSeveralFiles ? $"{Program.Name}  (+{Program.Files.Count - 1})" : Program.Name;

    /// <summary>The findings of this program, kept so going back to it does not check it again.</summary>
    public IReadOnlyList<Finding>? Findings { get; private set; }

    public ProgramState State => _state;

    public string StatusText => _state switch
    {
        ProgramState.Waiting => "waiting",
        ProgramState.Checking => "checking…",
        ProgramState.Clean => "✓",
        ProgramState.CouldNotCheck => "not checked",
        _ => _problems == 1 ? "1 problem" : $"{_problems} problems",
    };

    public Brush StatusBrush => (Brush)Application.Current.FindResource(_state switch
    {
        ProgramState.Clean => "SuccessBrush",
        ProgramState.HasProblems => "ErrorBrush",
        ProgramState.Checking => "AccentBrush",
        _ => "HintBrush",
    });

    public void Starting() => MoveTo(ProgramState.Checking, 0, null);

    public void Finished(IReadOnlyList<Finding> findings)
    {
        var problems = findings.Count(f => f.Severity != Severity.Suggestion);
        MoveTo(problems > 0 ? ProgramState.HasProblems : ProgramState.Clean, problems, findings);
    }

    public void CouldNotCheck() => MoveTo(ProgramState.CouldNotCheck, 0, []);

    private void MoveTo(ProgramState state, int problems, IReadOnlyList<Finding>? findings)
    {
        _state = state;
        _problems = problems;
        if (findings is not null) Findings = findings;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
    }
}
