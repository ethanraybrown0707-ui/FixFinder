namespace FixFinder.Core.Analysis.Solver;

/// <summary>c + k·δ for an infinitely small δ &gt; 0, so a strict x &lt; b can be solved as x ≤ b - δ.</summary>
internal readonly record struct DeltaRational(Rational Value, Rational Delta)
{
    public static DeltaRational Of(Rational value) => new(value, Rational.Zero);

    public static DeltaRational operator +(DeltaRational a, DeltaRational b) => new(a.Value + b.Value, a.Delta + b.Delta);
    public static DeltaRational operator -(DeltaRational a, DeltaRational b) => new(a.Value - b.Value, a.Delta - b.Delta);
    public static DeltaRational operator *(DeltaRational a, Rational factor) => new(a.Value * factor, a.Delta * factor);

    public int CompareTo(DeltaRational other)
    {
        var byValue = Value.CompareTo(other.Value);
        return byValue != 0 ? byValue : Delta.CompareTo(other.Delta);
    }

    public static bool operator <(DeltaRational a, DeltaRational b) => a.CompareTo(b) < 0;
    public static bool operator >(DeltaRational a, DeltaRational b) => a.CompareTo(b) > 0;
}

/// <summary>
/// The general simplex method used inside SMT solvers (Dutertre and de Moura, 2006): each constraint becomes a bound on a
/// slack variable, and pivots move values between variables until every bound holds or one provably cannot.
/// Bland's rule - always the lowest-numbered variable - keeps it from cycling.
/// </summary>
internal sealed class Simplex
{
    private readonly Dictionary<int, int> _column = [];
    private readonly List<int> _symbolOf = [];
    private readonly Dictionary<int, Dictionary<int, Rational>> _rows = [];
    private readonly List<DeltaRational> _value = [];
    private readonly List<DeltaRational?> _lower = [];
    private readonly List<DeltaRational?> _upper = [];
    private readonly Dictionary<LinearTerm, int> _slackFor = [];

    public int Pivots { get; private set; }

    private int NewVariable()
    {
        _value.Add(DeltaRational.Of(Rational.Zero));
        _lower.Add(null);
        _upper.Add(null);
        return _value.Count - 1;
    }

    private int VariableFor(int symbol)
    {
        if (_column.TryGetValue(symbol, out var variable)) return variable;

        variable = NewVariable();
        _column[symbol] = variable;
        while (_symbolOf.Count <= variable) _symbolOf.Add(-1);
        _symbolOf[variable] = symbol;
        return variable;
    }

    /// <summary>Adds a constraint that is not ≠. Returns false when it contradicts a bound already there.</summary>
    public bool Add(Constraint constraint)
    {
        var term = constraint.Term;
        var constant = term.Constant;
        var form = term - LinearTerm.Of(constant);

        int variable;
        Rational scale;

        if (form.Coefficients.Count == 1)
        {
            var (symbol, coefficient) = form.Coefficients.First();
            variable = VariableFor(symbol);
            scale = coefficient;
        }
        else
        {
            variable = SlackFor(form);
            scale = Rational.One;
        }

        var bound = -constant / scale;
        var strict = constraint.Relation == Relation.Less;
        var upper = scale.Sign > 0;

        return constraint.Relation == Relation.Equal
            ? Bound(variable, DeltaRational.Of(bound), upper: true) && Bound(variable, DeltaRational.Of(bound), upper: false)
            : Bound(variable, strict ? new DeltaRational(bound, upper ? -Rational.One : Rational.One) : DeltaRational.Of(bound), upper);
    }

    private int SlackFor(LinearTerm form)
    {
        if (_slackFor.TryGetValue(form, out var existing)) return existing;

        var row = new Dictionary<int, Rational>();
        foreach (var (symbol, coefficient) in form.Coefficients)
        {
            var variable = VariableFor(symbol);
            if (_rows.TryGetValue(variable, out var basicRow))
            {
                foreach (var (other, weight) in basicRow) AddTo(row, other, weight * coefficient);
            }
            else
            {
                AddTo(row, variable, coefficient);
            }
        }

        var slack = NewVariable();
        _rows[slack] = row;
        _value[slack] = row.Aggregate(DeltaRational.Of(Rational.Zero), (sum, pair) => sum + _value[pair.Key] * pair.Value);
        _slackFor[form] = slack;
        return slack;
    }

    private static void AddTo(Dictionary<int, Rational> row, int variable, Rational amount)
    {
        var total = row.GetValueOrDefault(variable, Rational.Zero) + amount;
        if (total.IsZero) row.Remove(variable);
        else row[variable] = total;
    }

    private bool Bound(int variable, DeltaRational bound, bool upper)
    {
        if (upper)
        {
            if (_upper[variable] is { } existing && !(bound < existing)) return true;
            if (_lower[variable] is { } lower && bound < lower) return false;
            _upper[variable] = bound;
        }
        else
        {
            if (_lower[variable] is { } existing && !(bound > existing)) return true;
            if (_upper[variable] is { } upperBound && bound > upperBound) return false;
            _lower[variable] = bound;
        }

        if (!_rows.ContainsKey(variable) && OutOfBounds(variable) is { } target) Update(variable, target);
        return true;
    }

    private DeltaRational? OutOfBounds(int variable) =>
        _lower[variable] is { } lower && _value[variable] < lower ? lower
        : _upper[variable] is { } upper && _value[variable] > upper ? upper
        : null;

    /// <summary>Moves a non-basic variable to a new value and every basic variable with it.</summary>
    private void Update(int nonBasic, DeltaRational target)
    {
        var change = target - _value[nonBasic];
        foreach (var (basic, row) in _rows)
            if (row.TryGetValue(nonBasic, out var coefficient)) _value[basic] += change * coefficient;
        _value[nonBasic] = target;
    }

    /// <summary>Whether every bound can hold at once; false is a proof they cannot. Null when the pivot budget ran out.</summary>
    public bool? Check(int mostPivots)
    {
        while (true)
        {
            var broken = _rows.Keys.Where(b => OutOfBounds(b) is not null).DefaultIfEmpty(-1).Min();
            if (broken < 0) return true;
            if (++Pivots > mostPivots) return null;

            var row = _rows[broken];
            var raise = _lower[broken] is { } lower && _value[broken] < lower;

            var entering = row
                .Where(pair => raise
                    ? pair.Value.Sign > 0 && CanRise(pair.Key) || pair.Value.Sign < 0 && CanFall(pair.Key)
                    : pair.Value.Sign < 0 && CanRise(pair.Key) || pair.Value.Sign > 0 && CanFall(pair.Key))
                .Select(pair => pair.Key)
                .DefaultIfEmpty(-1)
                .Min();

            if (entering < 0) return false;

            PivotAndUpdate(broken, entering, OutOfBounds(broken)!.Value);
        }
    }

    private bool CanRise(int variable) => _upper[variable] is not { } upper || _value[variable] < upper;

    private bool CanFall(int variable) => _lower[variable] is not { } lower || _value[variable] > lower;

    private void PivotAndUpdate(int basic, int entering, DeltaRational target)
    {
        var row = _rows[basic];
        var coefficient = row[entering];
        var theta = (target - _value[basic]) * (Rational.One / coefficient);

        _value[basic] = target;
        _value[entering] += theta;
        foreach (var (other, otherRow) in _rows)
            if (other != basic && otherRow.TryGetValue(entering, out var weight)) _value[other] += theta * weight;

        _rows.Remove(basic);
        var solved = new Dictionary<int, Rational> { [basic] = Rational.One / coefficient };
        foreach (var (variable, weight) in row)
            if (variable != entering) solved[variable] = -weight / coefficient;

        foreach (var otherRow in _rows.Values)
        {
            if (!otherRow.Remove(entering, out var weight)) continue;
            foreach (var (variable, amount) in solved) AddTo(otherRow, variable, weight * amount);
        }

        _rows[entering] = solved;
    }

    /// <summary>The values found, with δ made small enough that every strict bound still holds.</summary>
    public Dictionary<int, Rational> Model()
    {
        var delta = Rational.One;

        for (var variable = 0; variable < _value.Count; variable++)
        {
            var value = _value[variable];
            if (_lower[variable] is { } lower) delta = Shrink(delta, lower, value);
            if (_upper[variable] is { } upper) delta = Shrink(delta, value, upper);
        }

        var model = new Dictionary<int, Rational>();
        foreach (var (symbol, variable) in _column) model[symbol] = _value[variable].Value + _value[variable].Delta * delta;
        return model;
    }

    /// <summary>For low ≤ high in δ-form, the largest δ that keeps it true once δ is a real number.</summary>
    private static Rational Shrink(Rational delta, DeltaRational low, DeltaRational high)
    {
        if (low.Value < high.Value && low.Delta > high.Delta)
            return Rational.Min(delta, (high.Value - low.Value) / (low.Delta - high.Delta) / 2);
        return delta;
    }
}
