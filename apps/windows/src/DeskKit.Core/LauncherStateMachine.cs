namespace DeskKit.Core;

/// <summary>
/// 浮层状态机。只允许 ui-state.json 里写明的转移，Esc 每次只退一层。
/// </summary>
public sealed class LauncherStateMachine
{
    readonly Dictionary<string, string[]> transitions;
    readonly Dictionary<string, string[]> esc;
    readonly Action<string> log;

    public string State { get; private set; } = "HIDDEN";

    public LauncherStateMachine(ContractStore store, Action<string>? log = null)
    {
        transitions = new Dictionary<string, string[]>(store.Transitions(), StringComparer.Ordinal);
        esc = new Dictionary<string, string[]>(store.EscMap(), StringComparer.Ordinal);
        this.log = log ?? (_ => { });
    }

    LauncherStateMachine(Dictionary<string, string[]> transitions, Dictionary<string, string[]> esc, string state, Action<string> log)
    {
        this.transitions = transitions;
        this.esc = esc;
        State = state;
        this.log = log;
    }

    /// <summary>
    /// 沿契约允许的边走到目标。A1 不能直接进 A4，会经过 A2。
    /// </summary>
    public bool Reach(string next)
    {
        if (State == next)
        {
            return true;
        }

        var path = ShortestPath(next);
        if (path == null)
        {
            log($"未能进入 {next}，当前 {State}");
            return false;
        }

        foreach (var step in path)
        {
            Transition(step);
        }

        return State == next;
    }

    public DkResult Transition(string next)
    {
        var allowed = transitions.TryGetValue(State, out var list) ? list : Array.Empty<string>();
        if (!allowed.Contains(next))
        {
            log($"拒绝状态转移 {State} -> {next}");
            return DkResult.Fail("INVALID_ARG", $"不能从 {State} 进入 {next}");
        }

        var from = State;
        State = next;
        log($"状态转移 {from} -> {next}");
        return DkResult.Success(State);
    }

    public DkResult Escape()
    {
        if (!esc.TryGetValue(State, out var nexts) || nexts.Length == 0)
        {
            log($"Esc 在 {State}，保持隐藏");
            return DkResult.Success(State);
        }

        log($"Esc 回退 {State} -> {nexts[0]}");
        return Transition(nexts[0]);
    }

    public IReadOnlyList<string> EscapeSequence()
    {
        var copy = new LauncherStateMachine(transitions, esc, State, _ => { });
        var seen = new List<string>();
        while (copy.State != "HIDDEN" && seen.Count < 8)
        {
            if (!copy.Escape().Ok)
            {
                break;
            }

            seen.Add(copy.State);
        }

        return seen;
    }

    List<string>? ShortestPath(string next)
    {
        var queue = new Queue<string[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { State };
        queue.Enqueue([State]);
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            if (!transitions.TryGetValue(path[^1], out var nexts))
            {
                continue;
            }

            foreach (var candidate in nexts)
            {
                if (!seen.Add(candidate))
                {
                    continue;
                }

                var longer = path.Append(candidate).ToArray();
                if (candidate == next)
                {
                    return longer.Skip(1).ToList();
                }

                queue.Enqueue(longer);
            }
        }

        return null;
    }
}
